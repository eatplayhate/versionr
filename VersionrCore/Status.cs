using System;
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

            public bool DataEquals(Record x)
            {
                if (FilesystemEntry != null)
                {
                    if (FilesystemEntry.IsDirectory)
                        return x.Fingerprint == FilesystemEntry.CanonicalName;
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
        public List<Objects.Record> BaseRecords { get; set; }
        public List<Objects.Alteration> Alterations { get; set; }
        public Objects.Version CurrentVersion { get; set; }
        public Branch Branch { get; set; }
        public List<StatusEntry> Elements { get; set; }
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
        internal Status(Area workspace, WorkspaceDB db, LocalDB ldb, FileStatus currentSnapshot, string restrictedPath = null)
        {
            RestrictedPath = restrictedPath;
            Workspace = workspace;
            CurrentVersion = db.Version;
            Branch = db.Branch;
            Elements = new List<StatusEntry>();
            var tasks = new List<Task<StatusEntry>>();
            Dictionary<string, Entry> snapshotData = new Dictionary<string, Entry>();
            foreach (var x in currentSnapshot.Entries)
                snapshotData[x.CanonicalName] = x;
            HashSet<Entry> foundEntries = new HashSet<Entry>();
            List<Objects.Record> baserecs;
            List<Objects.Alteration> alterations;
            var records = db.GetRecords(CurrentVersion, out baserecs, out alterations);
            BaseRecords = baserecs;
            Alterations = alterations;
            var stage = ldb.StageOperations;
            Stage = stage;
            VersionControlRecords = records;
            HashSet<string> recordCanonicalNames = new HashSet<string>();
            foreach (var x in records)
                recordCanonicalNames.Add(x.CanonicalName);
            Dictionary<string, StageFlags> stageInformation = new Dictionary<string, StageFlags>();
            foreach (var x in stage)
            {
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
            foreach (var x in records)
            {
                tasks.Add(Task.Run<StatusEntry>(() =>
                {
                    StageFlags objectFlags;
                    stageInformation.TryGetValue(x.CanonicalName, out objectFlags);
                    Entry snapshotRecord = null;
                    if (restrictedPath != null && !x.CanonicalName.StartsWith(restrictedPath))
                        return new StatusEntry() { Code = StatusCode.Ignored, FilesystemEntry = snapshotRecord, VersionControlRecord = x, Staged = objectFlags.HasFlag(StageFlags.Recorded) };

                    if (snapshotData.TryGetValue(x.CanonicalName, out snapshotRecord))
                    {
                        lock (foundEntries)
                            foundEntries.Add(snapshotRecord);

                        if (objectFlags.HasFlag(StageFlags.Removed))
                            Printer.PrintWarning("Removed object `{0}` still in filesystem!", x.CanonicalName);

                        if (objectFlags.HasFlag(StageFlags.Renamed))
                            Printer.PrintWarning("Renamed object `{0}` still in filesystem!", x.CanonicalName);

                        if (objectFlags.HasFlag(StageFlags.Conflicted))
                            return new StatusEntry() { Code = StatusCode.Conflict, FilesystemEntry = snapshotRecord, VersionControlRecord = x, Staged = objectFlags.HasFlag(StageFlags.Recorded) };

						if (!snapshotRecord.Ignored && (snapshotRecord.Length != x.Size || ((!snapshotRecord.IsDirectory && (snapshotRecord.ModificationTime > Workspace.ReferenceTime)) && snapshotRecord.Hash != x.Fingerprint)))
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
            Elements.AddRange(tasks.Where(x => x != null).Select(x => x.Result));
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
                    foreach (var y in records)
                    {
                        if (y.Size != 0 && x.Value.Length == y.Size && x.Value.Hash == y.Fingerprint)
                        {
                            StageFlags otherFlags;
                            stageInformation.TryGetValue(y.CanonicalName, out otherFlags);
                            if (otherFlags.HasFlag(StageFlags.Removed))
                                Elements.Add(new StatusEntry() { Code = StatusCode.Renamed, FilesystemEntry = x.Value, Staged = objectFlags.HasFlag(StageFlags.Recorded), VersionControlRecord = y });
                            else
                            {
                                if (objectFlags.HasFlag(StageFlags.Recorded))
                                {
                                    Elements.Add(new StatusEntry() { Code = StatusCode.Copied, FilesystemEntry = x.Value, Staged = true, VersionControlRecord = y });
                                }
                                else if (!snapshotData.ContainsKey(y.CanonicalName))
                                {
                                    Elements.Add(new StatusEntry() { Code = StatusCode.Renamed, FilesystemEntry = x.Value, Staged = false, VersionControlRecord = y });
                                }
                                else
                                {
                                    Elements.Add(new StatusEntry() { Code = StatusCode.Copied, FilesystemEntry = x.Value, Staged = false, VersionControlRecord = y });
                                }
                                goto Next;
                            }
                        }
                    }
                    if (objectFlags.HasFlag(StageFlags.Recorded))
                    {
                        Elements.Add(new StatusEntry() { Code = StatusCode.Added, FilesystemEntry = x.Value, Staged = true, VersionControlRecord = null });
                        goto Next;
                    }
                    if (objectFlags.HasFlag(StageFlags.Conflicted))
                    {
                        Elements.Add(new StatusEntry() { Code = StatusCode.Conflict, FilesystemEntry = x.Value, Staged = objectFlags.HasFlag(StageFlags.Recorded), VersionControlRecord = null });
                        goto Next;
                    }
                    Elements.Add(new StatusEntry() { Code = StatusCode.Unversioned, FilesystemEntry = x.Value, Staged = false, VersionControlRecord = null });
                    Next:;
                }
            }

			Map = new Dictionary<string, StatusEntry>();
			foreach (var x in Elements)
				Map[x.CanonicalName] = x;
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
					foreach (var y in canonicalPaths)
					{
						if ((filenames && (string.Equals(x.Name, y, comparisonOptions) || string.Equals(x.Name, y + "/", comparisonOptions))) ||
							(!filenames && (string.Equals(x.CanonicalName, y, comparisonOptions) || string.Equals(x.CanonicalName, y + "/", comparisonOptions))))
						{
							results.Add(x);
							break;
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
