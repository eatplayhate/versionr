﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;
using System.IO;
using System.Text.RegularExpressions;

namespace Versionr
{
    public enum StatusCode
    {
        Unversioned,
        Unchanged,
        Added,
        Modified,
        Missing,
        Deleted,
        Renamed,
        Copied,
        Conflict,
		Ignored,

        Count
    }
    public class Status
    {
        public class StatusEntry
        {
            public StatusCode Code { get; set; }
            public bool Staged { get; set; }
            public Entry FilesystemEntry { get; set; }
            public Record VersionControlRecord { get; set; }
            public string CanonicalName
            {
                get
                {
                    if (FilesystemEntry != null)
                        return FilesystemEntry.CanonicalName;
                    return VersionControlRecord.CanonicalName;
                }
            }
            public string Name
            {
                get
                {
                    if (FilesystemEntry != null)
                        return FilesystemEntry.Name;
                    return VersionControlRecord.Name;
                }
            }
            public bool IsDirectory
            {
                get
                {
                    return FilesystemEntry != null ? FilesystemEntry.IsDirectory : VersionControlRecord.IsDirectory;
                }
            }

			public bool IsSymlink
			{
				get
				{
					return FilesystemEntry != null ? FilesystemEntry.Attributes.HasFlag(Objects.Attributes.Symlink) : VersionControlRecord.Attributes.HasFlag(Objects.Attributes.Symlink);
				}
			}

            public bool DataEquals(Record x)
            {
                if (FilesystemEntry != null)
                {
                    if (FilesystemEntry.IsDirectory)
                        return x.Fingerprint == FilesystemEntry.CanonicalName;
					if (FilesystemEntry.IsSymlink)
						return x.Fingerprint == FilesystemEntry.SymlinkTarget;
                    if (Code == StatusCode.Unchanged)
                        return x.DataEquals(VersionControlRecord);
                    return FilesystemEntry.DataEquals(x.Fingerprint, x.Size);
                }
                return false;
            }

            public string Hash
            {
                get
                {
                    if (Code == StatusCode.Unchanged)
                        return VersionControlRecord.Fingerprint;
                    return FilesystemEntry.Hash;
                }
            }

            public long Length
            {
                get
                {
                    return FilesystemEntry != null ? FilesystemEntry.Length : 0;
                }
            }

            public bool Removed
            {
                get
                {
                    return FilesystemEntry == null;
                }
            }
        }
        public List<Objects.Record> VersionControlRecords { get; set; }
        public Objects.Version CurrentVersion { get; set; }
        public Branch Branch { get; set; }
        public List<StatusEntry> Elements { get; set; }
        public List<Objects.Version> MergeInputs { get; set; }
		public Dictionary<string, StatusEntry> Map { get; set; }
		public List<LocalState.StageOperation> Stage { get; set; }
        public Area Workspace { get; set; }
        public string RestrictedPath { get; set; }
        public int Files { get; set; }
        public int Directories { get; set; }
        public int IgnoredObjects { get; set; }
        public bool HasData
        {
            get
            {
                foreach (var x in Elements)
                {
                    if (x.Staged)
                        return true;
                }
                return false;
            }
        }
        public bool HasModifications(bool requireStaging)
        {
            HashSet<string> addedFiles = new HashSet<string>(Stage.Where(x => x.Type == LocalState.StageOperationType.Add).Select(x => x.Operand1));
            foreach (var x in Elements)
            {
                if (x.Staged == true)
                    return true;
                else if (x.Code == StatusCode.Modified && !requireStaging)
                {
                    return true;
                }
            }
            return false;
        }
        [Flags]
        internal enum StageFlags
        {
            Recorded = 1,
            Removed = 2,
            Renamed = 4,
            Conflicted = 8,
        }
        int UpdatedFileTimeCount = 0;

        internal Status(Area workspace, WorkspaceDB db, LocalDB ldb, FileStatus currentSnapshot, string restrictedPath = null)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            RestrictedPath = restrictedPath;
            if (!string.IsNullOrEmpty(workspace.PartialPath))
                RestrictedPath = workspace.PartialPath + restrictedPath;
            Workspace = workspace;
            CurrentVersion = db.Version;
            Branch = db.Branch;
            Elements = new List<StatusEntry>();
            var tasks = new List<Task<StatusEntry>>();
            Dictionary<string, Entry> snapshotData = new Dictionary<string, Entry>();
            foreach (var x in currentSnapshot.Entries)
                snapshotData[x.CanonicalName] = x;
            HashSet<Entry> foundEntries = new HashSet<Entry>();
            var records = db.GetCachedRecords(CurrentVersion);
            var stage = ldb.StageOperations;
            Stage = stage;
            VersionControlRecords = records;
            HashSet<string> recordCanonicalNames = new HashSet<string>();
            foreach (var x in records)
                recordCanonicalNames.Add(x.CanonicalName);
            MergeInputs = new List<Objects.Version>();
            Dictionary<string, StageFlags> stageInformation = new Dictionary<string, StageFlags>();
			Dictionary<Record, StatusEntry> statusMap = new Dictionary<Record, StatusEntry>();
            foreach (var x in stage)
            {
                if (x.Type == LocalState.StageOperationType.Merge)
                    MergeInputs.Add(Workspace.GetVersion(new Guid(x.Operand1)));
                if (!x.IsFileOperation)
                    continue;
                StageFlags ops;
                if (!stageInformation.TryGetValue(x.Operand1, out ops))
                    stageInformation[x.Operand1] = ops;
                if (x.Type == LocalState.StageOperationType.Add)
                    ops |= StageFlags.Recorded;
                if (x.Type == LocalState.StageOperationType.Conflict)
                    ops |= StageFlags.Conflicted;
                if (x.Type == LocalState.StageOperationType.Remove)
                    ops |= StageFlags.Removed;
                if (x.Type == LocalState.StageOperationType.Rename)
                    ops |= StageFlags.Renamed;
                stageInformation[x.Operand1] = ops;
            }
            try
            {
                foreach (var x in records)
                {
                    tasks.Add(Utilities.LimitedTaskDispatcher.Factory.StartNew<StatusEntry>(() =>
                    {
                        StageFlags objectFlags;
                        stageInformation.TryGetValue(x.CanonicalName, out objectFlags);
                        Entry snapshotRecord = null;
                        if (RestrictedPath != null)
                        {
                            if (!x.CanonicalName.StartsWith(RestrictedPath) || x.CanonicalName == RestrictedPath)
                            {
                                if (x.CanonicalName == ".vrmeta" && !string.IsNullOrEmpty(Workspace.PartialPath))
                                {
                                    if (snapshotData.TryGetValue(x.CanonicalName, out snapshotRecord))
                                    {
                                        lock (foundEntries)
                                            foundEntries.Add(snapshotRecord);
                                    }
                                }
                                return new StatusEntry() { Code = StatusCode.Ignored, FilesystemEntry = snapshotRecord, VersionControlRecord = x, Staged = objectFlags.HasFlag(StageFlags.Recorded) };
                            }
                        }

                        if (snapshotData.TryGetValue(x.CanonicalName, out snapshotRecord) && snapshotRecord.Ignored == false)
                        {
                            lock (foundEntries)
                                foundEntries.Add(snapshotRecord);

                            if (objectFlags.HasFlag(StageFlags.Removed))
                                Printer.PrintWarning("Removed object `{0}` still in filesystem!", x.CanonicalName);

                            if (objectFlags.HasFlag(StageFlags.Renamed))
                                Printer.PrintWarning("Renamed object `{0}` still in filesystem!", x.CanonicalName);

                            if (objectFlags.HasFlag(StageFlags.Conflicted))
                                return new StatusEntry() { Code = StatusCode.Conflict, FilesystemEntry = snapshotRecord, VersionControlRecord = x, Staged = objectFlags.HasFlag(StageFlags.Recorded) };

                            bool changed = false;
                            if (snapshotRecord.Length != x.Size)
                                changed = true;
                            if (!changed && snapshotRecord.IsSymlink && snapshotRecord.SymlinkTarget != x.Fingerprint)
                                changed = true;
                            if (!changed && !snapshotRecord.IsDirectory && !snapshotRecord.IsSymlink)
                            {
                                LocalState.FileTimestamp fst = Workspace.GetReferenceTime(x.CanonicalName);
                                if (snapshotRecord.ModificationTime == x.ModificationTime || (fst.DataIdentifier == x.DataIdentifier && snapshotRecord.ModificationTime == fst.LastSeenTime))
                                    changed = false;
                                else
                                {
                                    Printer.PrintDiagnostics("Computing hash for: " + x.CanonicalName);
                                    if (snapshotRecord.Hash != x.Fingerprint)
                                        changed = true;
                                    else
                                    {
                                        System.Threading.Interlocked.Increment(ref this.UpdatedFileTimeCount);
                                        Workspace.UpdateFileTimeCache(x.CanonicalName, x, snapshotRecord.ModificationTime, false);
                                    }
                                }
                            }
                            if (changed == true)
                                return new StatusEntry() { Code = StatusCode.Modified, FilesystemEntry = snapshotRecord, VersionControlRecord = x, Staged = objectFlags.HasFlag(StageFlags.Recorded) };
                            else
                            {
                                if (objectFlags.HasFlag(StageFlags.Recorded))
                                    Printer.PrintWarning("Unchanged object `{0}` still marked as recorded in commit!", x.CanonicalName);
                                return new StatusEntry() { Code = StatusCode.Unchanged, FilesystemEntry = snapshotRecord, VersionControlRecord = x, Staged = objectFlags.HasFlag(StageFlags.Recorded) };
                            }
                        }
                        else
                        {
                            if (objectFlags.HasFlag(StageFlags.Removed))
                                return new StatusEntry() { Code = StatusCode.Deleted, FilesystemEntry = null, VersionControlRecord = x, Staged = true };
                            return new StatusEntry() { Code = StatusCode.Missing, FilesystemEntry = null, VersionControlRecord = x, Staged = false };
                        }
                    }));
                }
                Task.WaitAll(tasks.ToArray());
                Printer.PrintDiagnostics("Status record checking: {0}", sw.ElapsedTicks);
                sw.Restart();
            }
            finally
            {
                if (UpdatedFileTimeCount > 0)
                    Workspace.ReplaceFileTimes();
            }
            Printer.PrintDiagnostics("Status update file times: {0}", sw.ElapsedTicks);
            sw.Restart();
			foreach (var x in tasks)
			{
				if (x == null)
					continue;

				Elements.Add(x.Result);
				if (x.Result.VersionControlRecord != null)
					statusMap[x.Result.VersionControlRecord] = x.Result;
			}
            tasks.Clear();
            Dictionary<long, Dictionary<string, Record>> recordSizeMap = new Dictionary<long, Dictionary<string, Record>>();
            foreach (var x in records)
            {
                if (x.Size == 0 || !x.IsFile)
                    continue;
                Dictionary<string, Record> hashes = null;
                if (!recordSizeMap.TryGetValue(x.Size, out hashes))
                {
                    hashes = new Dictionary<string, Record>();
                    recordSizeMap[x.Size] = hashes;
                }
                hashes[x.Fingerprint] = x;
            }
            Printer.PrintDiagnostics("Status create size map: {0}", sw.ElapsedTicks);
            sw.Restart();
            List<StatusEntry> pendingRenames = new List<StatusEntry>();
            foreach (var x in snapshotData)
            {
                if (x.Value.IsDirectory)
                    Directories++;
                else
                    Files++;
                if (x.Value.Ignored)
                {
                    IgnoredObjects++;
                    continue;
                }

                StageFlags objectFlags;
                stageInformation.TryGetValue(x.Value.CanonicalName, out objectFlags);
                if (!foundEntries.Contains(x.Value))
                {
                    tasks.Add(Utilities.LimitedTaskDispatcher.Factory.StartNew<StatusEntry>(() =>
                    {
                        Record possibleRename = null;
                        Dictionary<string, Record> hashes = null;
                        lock (recordSizeMap)
                            recordSizeMap.TryGetValue(x.Value.Length, out hashes);
                        if (hashes != null)
                        {
                            Printer.PrintDiagnostics("Hashing unversioned file: {0}", x.Key);
                            hashes.TryGetValue(x.Value.Hash, out possibleRename);
                        }
                        if (possibleRename != null)
                        {
                            StageFlags otherFlags;
                            stageInformation.TryGetValue(possibleRename.CanonicalName, out otherFlags);
                            if (otherFlags.HasFlag(StageFlags.Removed))
                            {
                                lock (pendingRenames)
                                    pendingRenames.Add(new StatusEntry() { Code = StatusCode.Renamed, FilesystemEntry = x.Value, Staged = objectFlags.HasFlag(StageFlags.Recorded), VersionControlRecord = possibleRename });
                                return null;
                            }
                            else
                            {
                                if (objectFlags.HasFlag(StageFlags.Recorded))
                                {
                                    return new StatusEntry() { Code = StatusCode.Copied, FilesystemEntry = x.Value, Staged = true, VersionControlRecord = possibleRename };
                                }
                                else
                                {
                                    return new StatusEntry() { Code = StatusCode.Copied, FilesystemEntry = x.Value, Staged = false, VersionControlRecord = possibleRename };
                                }
                            }
                        }
                        if (objectFlags.HasFlag(StageFlags.Recorded))
                        {
                            return new StatusEntry() { Code = StatusCode.Added, FilesystemEntry = x.Value, Staged = true, VersionControlRecord = null };
                        }
                        if (objectFlags.HasFlag(StageFlags.Conflicted))
                        {
                            return new StatusEntry() { Code = StatusCode.Conflict, FilesystemEntry = x.Value, Staged = objectFlags.HasFlag(StageFlags.Recorded), VersionControlRecord = null };
                        }
                        return new StatusEntry() { Code = StatusCode.Unversioned, FilesystemEntry = x.Value, Staged = false, VersionControlRecord = null };
                    }));
                }
            }
            Task.WaitAll(tasks.ToArray());
            foreach (var x in tasks)
            {
                if (x.Result != null)
                    Elements.Add(x.Result);
            }
            Dictionary<Record, bool> allowedRenames = new Dictionary<Record, bool>();
            foreach (var x in pendingRenames)
            {
                if (allowedRenames.ContainsKey(x.VersionControlRecord))
                    allowedRenames[x.VersionControlRecord] = false;
                else
                    allowedRenames[x.VersionControlRecord] = true;
            }
            foreach (var x in pendingRenames)
            {
                if (allowedRenames[x.VersionControlRecord])
                {
                    Elements.Add(x);
                    statusMap[x.VersionControlRecord].Code = StatusCode.Ignored;
                }
                else
                {
                    x.Code = StatusCode.Copied;
                    Elements.Add(x);
                }
            }
            Printer.PrintDiagnostics("Status new file reconciliation: {0}", sw.ElapsedTicks);
            sw.Restart();

            Map = new Dictionary<string, StatusEntry>();
			foreach (var x in Elements)
				Map[x.CanonicalName] = x;
            Printer.PrintDiagnostics("Status finalization: {0}", sw.ElapsedTicks);
        }

		public List<StatusEntry> GetElements(IList<string> files, bool regex, bool filenames, bool caseInsensitive)
		{
			List<StatusEntry> results = new List<Status.StatusEntry>();

			bool globMatching = false;
			if (!regex)
			{
				foreach (var x in files)
				{
					if (x.Contains("*") || x.Contains("?"))
					{
						globMatching = true;
						break;
					}
				}
			}

			if (regex || globMatching)
			{
				List<System.Text.RegularExpressions.Regex> regexes = new List<System.Text.RegularExpressions.Regex>();
				RegexOptions caseOption = caseInsensitive ? RegexOptions.IgnoreCase : RegexOptions.None;
				if (globMatching)
				{
					foreach (var x in files)
					{
						string pattern = "^" + Regex.Escape(x).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
						regexes.Add(new Regex(pattern, RegexOptions.Singleline | caseOption));
					}
				}
				else
				{
					foreach (var x in files)
						regexes.Add(new Regex(x, RegexOptions.Singleline | caseOption));
				}

				foreach (var x in Elements)
				{
					foreach (var y in regexes)
					{
						if ((!filenames && y.IsMatch(x.CanonicalName)) || (filenames && x.FilesystemEntry?.Info != null && y.IsMatch(x.FilesystemEntry.Info.Name)))
						{
							results.Add(x);
							break;
						}
					}
				}
			}
			else
			{
				List<string> canonicalPaths = new List<string>();
				foreach (var x in files)
					canonicalPaths.Add(Workspace.GetLocalPath(Path.GetFullPath(x)));

				StringComparison comparisonOptions = caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

				foreach (var x in Elements)
				{
                    if (filenames)
                    {
                        foreach (var y in files)
                        {
                            if (string.Equals(x.Name, y, comparisonOptions) || string.Equals(x.Name, y + "/", comparisonOptions))
                            {
                                results.Add(x);
                                break;
                            }
                        }
                    }
                    else
                    {
                        foreach (var y in canonicalPaths)
                        {
                            if (string.Equals(x.CanonicalName, y, comparisonOptions) || string.Equals(x.CanonicalName, y + "/", comparisonOptions))
                            {
                                results.Add(x);
                                break;
                            }
                        }
                    }
				}
			}
			return results;
		}

        public class NameMatcher : IComparer<StatusEntry>
        {
            public int Compare(StatusEntry x, StatusEntry y)
            {
                return x.CanonicalName.CompareTo(y.CanonicalName);
            }
        }

        public void AddRecursiveElements(List<StatusEntry> entries)
        {
            var entrySet = new HashSet<StatusEntry>();
            foreach (var x in entries)
                entrySet.Add(x);
            List<StatusEntry> sortedList = Elements.Where(x => !entrySet.Contains(x)).OrderBy(x => x.CanonicalName).ToList();
            var skipSet = new HashSet<StatusEntry>();
            foreach (var x in entries.ToArray())
			{
				if (x.IsDirectory)
				{
                    if (skipSet.Contains(x))
                        continue;
                    int index = sortedList.BinarySearch(x, new NameMatcher());
                    if (index < 0)
                    {
                        index = ~index;
                        if (index == sortedList.Count)
                            continue;
                    }
                    skipSet.Add(sortedList[index]);
                    for (; index < sortedList.Count; index++)
                    {
                        if (entrySet.Contains(sortedList[index]))
                            continue;
                        if (sortedList[index].CanonicalName.StartsWith(x.CanonicalName))
                        {
                            entrySet.Add(sortedList[index]);
                            entries.Add(sortedList[index]);
                            skipSet.Add(sortedList[index]);
                        }
                        else
                            break;
                    }
				}
			}
		}
	}
}
