//#define MERGE_DIAGNOSTICS

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Versionr.LocalState;
using Versionr.Network;
using Versionr.Objects;
using Versionr.Utilities;

namespace Versionr
{
    public class VersionrException : Exception
    {

    }
    public static class IndexedSelect
    {
        public static IEnumerable<Tuple<int, T>> SelectIndexed<T>(this IEnumerable<T> input)
        {
            int count = 0;
            foreach (var x in input)
                yield return new Tuple<int, T>(count++, x);
            yield break;
        }
    }
    public class Area : IDisposable
    {
        public ObjectStore.ObjectStoreBase ObjectStore { get; private set; }
        public DirectoryInfo AdministrationFolder { get; private set; }
        public DirectoryInfo RootDirectory { get; private set; }

        private WorkspaceDB Database { get; set; }
        private LocalDB LocalData { get; set; }
        public Directives Directives { get; set; }
        public DateTime ReferenceTime { get; set; }
        public Newtonsoft.Json.Linq.JObject Configuration { get; set; }

        public Dictionary<string, FileTimestamp> FileTimeCache { get; set; }

        public List<Objects.Version> GetMergeList(Guid iD)
        {
            List<Objects.MergeInfo> merges = Database.GetMergeInfoFromSource(iD);
            return merges.Select(x => GetVersion(x.DestinationVersion)).ToList();
        }

        public string Username
        {
            get
            {
                return (Directives != null) ? Directives.UserName : Environment.UserName;
            }
        }

        public void RunConsistencyCheck()
        {
            Database.ConsistencyCheck();
        }
        public void RunVacuum()
        {
            Database.Vacuum();
        }

        [System.Runtime.InteropServices.DllImport("XDiffEngine", EntryPoint = "GeneratePatch", CharSet = System.Runtime.InteropServices.CharSet.Ansi, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern int GeneratePatch(string file1, string file2, string output);

        [System.Runtime.InteropServices.DllImport("XDiffEngine", EntryPoint = "GenerateBinaryPatch", CharSet = System.Runtime.InteropServices.CharSet.Ansi, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern int GenerateBinaryPatch(string file1, string file2, string output);
        [System.Runtime.InteropServices.DllImport("XDiffEngine", EntryPoint = "ApplyPatch", CharSet = System.Runtime.InteropServices.CharSet.Ansi, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern int ApplyPatch(string file1, string file2, string output, string errorOutput, int reversed);

        [System.Runtime.InteropServices.DllImport("XDiffEngine", EntryPoint = "ApplyBinaryPatch", CharSet = System.Runtime.InteropServices.CharSet.Ansi, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern int ApplyBinaryPatch(string file1, string file2, string output);

        [System.Runtime.InteropServices.DllImport("XDiffEngine", EntryPoint = "Merge3Way", CharSet = System.Runtime.InteropServices.CharSet.Ansi, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern int XDiffMerge3Way(string basefile, string file1, string file2, string output);

        public class StashInfo
        {
            public string Author { get; set; }
            public DateTime Time { get; set; }
            public string Name { get; set; }
            public string Key { get; set; }
            public Guid GUID { get; set; }
            public FileInfo File { get; set; }
            public int Version { get; set; }
            public Guid OriginatingVersion { get; set; }
            public long? LocalDBIndex { get; set; }

            internal static StashInfo Create(Area ws, string name, Guid originalVersion)
            {
                return new StashInfo()
                {
                    GUID = Guid.NewGuid(),
                    Name = name,
                    Key = string.Empty,
                    Author = ws.Username,
                    Time = DateTime.UtcNow,
                    Version = 2,
                    OriginatingVersion = originalVersion
                };
            }

            internal static StashInfo FromFile(string filename)
            {
                using (FileStream fs = System.IO.File.Open(filename, FileMode.Open, FileAccess.Read))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    StashInfo info = StashInfo.Read(br);
                    if (info != null)
                        info.File = new FileInfo(filename);
                    return info;
                }
            }

            internal static StashInfo Read(BinaryReader br)
            {
                string s1 = br.ReadString();
                if (s1 == "STASH2")
                {
                    return new StashInfo()
                    {
                        GUID = new Guid(br.ReadString()),
                        Name = br.ReadString(),
                        Author = br.ReadString(),
                        Time = new DateTime(br.ReadInt64()),
                        Key = br.ReadString(),
                        Version = 2,
                        OriginatingVersion = new Guid(br.ReadString())
                    };
                }
                return null;
            }

            internal void Write(BinaryWriter bw)
            {
                if (Version == 2)
                    bw.Write("STASH2");
                else
                    throw new Exception();
                bw.Write(GUID.ToString());
                bw.Write(Name);
                bw.Write(Author);
                bw.Write(Time.Ticks);
                bw.Write(Key);
                bw.Write(OriginatingVersion.ToString());
            }
        }

        public RemoteConfig FindRemoteFromURL(string remote)
        {
            foreach (var x in LocalData.Table<RemoteConfig>())
            {
                if (Network.Client.ToVersionrURL(x) == remote)
                    return x;
            }
            return null;
        }

        internal void RecordLock(Guid lockID, Guid? branchID, string lockedPath, string versionrURL, IEnumerable<Guid> locksToClean)
        {
            LocalData.BeginTransaction(true);
            LocalData.InsertSafe(new RemoteLock() { ID = lockID, LockedBranch = branchID, LockingPath = lockedPath, RemoteHost = versionrURL });
            ReleaseLocksInternal(locksToClean);
            LocalData.Commit();
        }

        public void ReleaseLocks(IEnumerable<Guid> locksToClean)
        {
            LocalData.BeginTransaction(true);
            ReleaseLocksInternal(locksToClean);
            LocalData.Commit();
        }

        private void ReleaseLocksInternal(IEnumerable<Guid> locksToClean)
        {
            if (locksToClean != null)
            {
                foreach (var x in locksToClean)
                {
                    var localLock = LocalData.Find<RemoteLock>(x);
                    if (localLock != null)
                        LocalData.DeleteSafe(localLock);
                }
            }
        }

        public bool FindStashExact(string guidString)
        {
            Guid guid = new Guid(guidString);
            SavedStash ss = LocalData.Find<SavedStash>(x => x.GUID == guid);
            return ss != null;
        }

        public List<StashInfo> ListStashes()
        {
            DirectoryInfo stashDir = new DirectoryInfo(Path.Combine(AdministrationFolder.FullName, "Stashes"));
            stashDir.Create();
            List<StashInfo> stashes = new List<StashInfo>();
            HashSet<string> guids = new HashSet<string>();
            foreach (var x in LocalData.Table<LocalState.SavedStash>().ToList())
            {
                string fname = x.GUID + ".stash";
                StashInfo info = new StashInfo()
                {
                    Author = x.Author,
                    File = new FileInfo(Path.Combine(stashDir.FullName, fname)),
                    Key = x.StashCode,
                    GUID = x.GUID,
                    Name = x.Name,
                    Time = x.Timestamp,
                    LocalDBIndex = x.Id
                };
                if (info.File.Exists)
                {
                    guids.Add(fname);
                    stashes.Add(info);
                }
                else
                    LocalData.Delete(x);
            }
            foreach (var x in stashDir.GetFiles())
            {
                if (x.Extension == ".stash")
                {
                    if (!guids.Contains(x.Name))
                    {
                        StashInfo stashInfo = StashInfo.FromFile(x.FullName);
                        if (stashInfo != null)
                        {
                            stashes.Add(stashInfo);
                            LocalData.RecordStash(stashInfo);
                        }
                    }
                }
            }
            return stashes.OrderByDescending(x => x.Time).ToList();
        }

        public List<StashInfo> FindStash(string name)
        {
            List<StashInfo> results = new List<StashInfo>();
            var stashes = ListStashes();
            foreach (var x in stashes)
            {
                if (string.Compare(name, (x.Author + "-" + x.Key), true) == 0
                    || string.Compare(x.Key, name, true) == 0
                    || string.Compare(x.Name, name, true) == 0
                    || x.GUID.ToString().ToLower().StartsWith(name.ToLower()))
                {
                    results.Add(x);
                }
            }
            return results;
        }

        internal string GenerateTempPath()
        {
            return Path.Combine(AdministrationFolder.CreateSubdirectory("Temp").FullName, Path.GetRandomFileName());
        }

        internal bool ImportStash(string filename)
        {
            StashInfo info = StashInfo.FromFile(filename);
            if (info != null)
            {
                info.File.MoveTo(Path.Combine(AdministrationFolder.CreateSubdirectory("Stashes").FullName, info.GUID + ".stash"));
                LocalData.RecordStash(info);
                return true;
            }
            else
                Printer.PrintMessage("Couldn't import stash object - unable to read stash file!");
            return false;
        }

        public class ApplyStashOptions
        {
            public bool StageOperations { get; set; } = true;
            public bool DisallowMoves { get; set; } = false;
            public bool DisallowDeletes { get; set; } = false;
            public bool Reverse { get; set; } = false;
            public bool AllowUncleanPatches { get; set; } = false;
            public bool AttemptThreeWayMergeOnPatchFailure { get; set; } = false;
        }

        public void Unstash(StashInfo stashInfo, ApplyStashOptions options, bool deleteAfterApply)
        {
            ApplyStash(stashInfo, options);
            if (deleteAfterApply)
            {
                DeleteStash(stashInfo);
            }
        }

        public void DeleteStash(StashInfo stashInfo)
        {
            if (stashInfo.LocalDBIndex.HasValue)
                LocalData.Delete<SavedStash>(stashInfo.LocalDBIndex.Value);
            stashInfo.File.Delete();
        }

        public void StashToPatch(StreamWriter result, StashInfo stash)
        {
            using (FileStream fs = stash.File.OpenRead())
            using (BinaryReader br = new BinaryReader(fs))
            {
                StashInfo info;
                List<StashEntry> entries;
                long[] indexTable;

                ReadStashHeader(br, out info, out entries, out indexTable);

                for (int i = 0; i < entries.Count; i++)
                {
                    var x = entries[i];
                    if (x.Alteration == AlterationType.Update)
                    {
                        if (!x.Flags.HasFlag(StashFlags.Binary))
                        {
                            br.BaseStream.Position = indexTable[i * 2 + 0];
                            long patchSize = br.ReadInt64();

                            result.Write("--- " + GetLocalCanonicalName(x.CanonicalName) + "\n");
                            result.Write("+++ " + GetLocalCanonicalName(x.CanonicalName) + "\n");
                            result.Flush();

                            Versionr.ObjectStore.LZHAMReaderStream reader = new Versionr.ObjectStore.LZHAMReaderStream(patchSize, fs);
                            reader.CopyTo(result.BaseStream);

                            result.Write("\n");
                            result.Flush();
                        }
                    }
                }
            }
        }

        private void ApplyStash(StashInfo infoOriginal, ApplyStashOptions options)
        {
            var tempFolder = AdministrationFolder.CreateSubdirectory("Temp");
            Status st = new Status(this, Database, LocalData, FileSnapshot, null, false);

            ResolveType? resolveAllText = null;
            ResolveType? resolveAllBinary = null;
            ResolveType? mergeResolve = null;
            bool? resolveDeleted = null;

            using (FileStream fs = infoOriginal.File.OpenRead())
            using (BinaryReader br = new BinaryReader(fs))
            {
                StashInfo info;
                List<StashEntry> entries;
                long[] indexTable;

                ReadStashHeader(br, out info, out entries, out indexTable);

                Printer.PrintMessage("Applying stash #b#{0}##.", info.GUID);

                bool enableStaging = options.StageOperations;
                bool moveAsCopies = options.DisallowMoves;
                bool allowDeletes = !options.DisallowDeletes;

                int increment = 1;
                int start = 0;
                if (options.Reverse)
                {
                    start = entries.Count - 1;
                    increment = -1;
                }
                for (int i = start; i < entries.Count && i >= 0; i += increment)
                {
                    var x = entries[i];
                    Printer.PrintMessage(" [{0}]: #b#{3}{1}## - {2}", i, x.Alteration, x.CanonicalName, options.Reverse ? "Reverse " : "");

                    Status.StatusEntry ws = null;
                    st.Map.TryGetValue(x.CanonicalName, out ws);

                    string rpath = GetRecordPath(x.CanonicalName);

                    if (options.Reverse && (x.Alteration == AlterationType.Add || x.Alteration == AlterationType.Copy || (x.Alteration == AlterationType.Move && moveAsCopies)))
                    {
                        if (ws == null)
                            Printer.PrintMessage("  - Skipped, object already missing.");
                        else
                        {
                            if (x.Flags.HasFlag(StashFlags.Directory))
                            {
                                try
                                {
                                    System.IO.Directory.Delete(rpath);
                                    Printer.PrintMessage("  - Deleted.");
                                }
                                catch
                                {
                                    Printer.PrintMessage("  - Couldn't delete directory (not empty?)");
                                }
                            }
                            else
                            {
                                bool deletionResolution = false;
                                if (ws.Hash != x.NewHash || ws.Length != x.NewSize)
                                    deletionResolution = GetStashResolutionDeletion(ref resolveDeleted);

                                if (deletionResolution == false)
                                {
                                    System.IO.FileInfo fi = new FileInfo(rpath);
                                    fi.IsReadOnly = false;
                                    fi.Delete();
                                    Printer.PrintMessage("  - Deleted.");
                                }
                                else
                                {
                                    Printer.PrintMessage("  - Skipped, file contents is not what is expected.");
                                }
                            }
                        }
                    }
                    else if (options.Reverse && x.Alteration == AlterationType.Delete)
                    {
                        if (ws != null && ws.Removed)
                        {
                            RestoreRecord(ws.VersionControlRecord, DateTime.Now);
                            Printer.PrintMessage("  - Undeleted.");
                        }
                        else
                            Printer.PrintMessage("  - Skipped, not deleted.");
                    }
                    else if (options.Reverse && x.Alteration == AlterationType.Move)
                    {
                        Printer.PrintMessage("  - Skipped, too complex!");
                    }
                    else if (options.Reverse && x.Alteration == AlterationType.Update)
                    {
                        if (ws == null)
                            Printer.PrintMessage("  - Skipped, object deleted.");
                        else if (ws.Hash == x.OriginalHash && ws.Length == x.OriginalSize)
                            Printer.PrintMessage("  - Skipped, object does not need to be reverted.");
                        else
                        {
                            if (x.Flags.HasFlag(StashFlags.Binary))
                            {
                                if (x.NewHash != ws.Hash || x.NewSize != ws.Length)
                                    Printer.PrintError("#e# - Can't un-apply binary patch - result file does not match!##");
                                else
                                {
                                    RestoreRecord(GetRecordFromIdentifier(x.OriginalHash + "-" + x.OriginalSize.ToString()), DateTime.Now, rpath);
                                    Printer.PrintMessage("  - Reverted (binary).");
                                }
                            }
                            else
                            {
                                ApplyPatchEntry(rpath, ws.FilesystemEntry.Info.FullName, x.Flags.HasFlag(StashFlags.Binary), indexTable[i * 2 + 0], br.BaseStream, options, x);
                            }
                        }
                    }
                    else if (x.Alteration == AlterationType.Add)
                    {
                        if (x.Flags.HasFlag(StashFlags.Directory))
                        {
                            if (ws == null)
                                System.IO.Directory.CreateDirectory(rpath);
                            else
                                Printer.PrintMessage("  - Skipped, directory already added.");
                        }
                        else
                        {
                            ApplyStashCreateFile(x, ws, rpath, enableStaging, ref resolveAllBinary, ref resolveAllText, ref mergeResolve, (string path) =>
                            {
                                br.BaseStream.Position = indexTable[i * 2 + 0];
                                Versionr.ObjectStore.LZHAMReaderStream reader = new Versionr.ObjectStore.LZHAMReaderStream(x.NewSize, br.BaseStream);
                                using (FileStream fout = File.Open(path, FileMode.Create))
                                    reader.CopyTo(fout);
                            });
                        }
                    }
                    else if (x.Alteration == AlterationType.Copy || (x.Alteration == AlterationType.Move && moveAsCopies))
                    {
                        ApplyStashCreateFile(x, ws, rpath, enableStaging, ref resolveAllBinary, ref resolveAllText, ref mergeResolve, (string path) =>
                        {
                            if (x.Alteration == AlterationType.Copy || (x.NewHash == x.OriginalHash && x.NewSize == x.OriginalSize))
                            {
                                Record dataRecord = GetRecordFromIdentifier(x.OriginalHash + "-" + x.OriginalSize.ToString());
                                GetMissingRecords(new Record[] { dataRecord });
                                RestoreRecord(dataRecord, DateTime.Now, path);
                            }
                            else
                            {
                                Record oldRecord = GetRecordFromIdentifier(x.OriginalHash + "-" + x.OriginalSize.ToString());
                                string oldPath = x.OriginalCanonicalName;

                                Status.StatusEntry oldEntry = null;
                                st.Map.TryGetValue(oldPath, out oldEntry);

                                if (oldEntry == null || oldEntry.Removed)
                                {
                                    GetMissingRecords(new Record[] { oldRecord });
                                    string tempFile = Path.Combine(tempFolder.FullName, Path.GetRandomFileName());

                                    RestoreRecord(oldRecord, DateTime.Now, tempFile);

                                    ApplyPatchEntry(path, tempFile, x.Flags.HasFlag(StashFlags.Binary), indexTable[i * 2 + 0], br.BaseStream, options, x);

                                    FileInfo tfi = new FileInfo(tempFile);
                                    tfi.IsReadOnly = false;
                                    tfi.Delete();
                                }
                                else
                                {
                                    if (x.Flags.HasFlag(StashFlags.Binary) && (x.OriginalHash != oldEntry.Hash || x.OriginalSize != oldEntry.Length))
                                    {
                                        Printer.PrintError("#e# - Can't apply binary patch - source file does not match!##");
                                        throw new Exception();
                                    }
                                    ApplyPatchEntry(path, oldEntry.FilesystemEntry.Info.FullName, x.Flags.HasFlag(StashFlags.Binary), indexTable[i * 2 + 0], br.BaseStream, options, x);
                                }
                            }
                        });
                    }
                    else if (x.Alteration == AlterationType.Move)
                    {
                        if (ws == null)
                        {
                            string oldPath = x.OriginalCanonicalName;

                            Status.StatusEntry oldEntry = null;
                            st.Map.TryGetValue(oldPath, out oldEntry);

                            if (oldEntry == null || oldEntry.Removed)
                                Printer.PrintMessage("  - Skipped, object removed.");
                            else
                            {
                                if (x.NewHash == x.OriginalHash && x.NewSize == x.OriginalSize)
                                {
                                    FileInfo tfi = new FileInfo(oldPath);
                                    tfi.IsReadOnly = false;
                                    tfi.MoveTo(rpath);

                                    ApplyAttributes(tfi, DateTime.Now, x.ObjectAttributes);
                                    Printer.PrintMessage("  - Moved {0} => {1}.", oldPath, x.CanonicalName);

                                    if (enableStaging)
                                    {
                                        LocalData.AddStageOperation(new StageOperation() { Operand1 = oldPath, Type = StageOperationType.Remove });
                                        LocalData.AddStageOperation(new StageOperation() { Operand1 = x.CanonicalName, Type = StageOperationType.Add });
                                    }
                                }
                                else
                                {
                                    FileInfo tfi = new FileInfo(oldPath);

                                    if (x.Flags.HasFlag(StashFlags.Binary) && (x.OriginalHash != oldEntry.Hash || x.OriginalSize != oldEntry.Length))
                                    {
                                        Printer.PrintError("#e# - Can't apply binary patch - source file does not match!##");
                                    }
                                    else
                                    {
                                        ApplyPatchEntry(rpath, tfi.FullName, x.Flags.HasFlag(StashFlags.Binary), indexTable[i * 2 + 0], br.BaseStream, options, x);
                                        tfi.IsReadOnly = false;
                                        tfi.Delete();

                                        Printer.PrintMessage("  - Moved and patched {0} => {1}.", oldPath, x.CanonicalName);

                                        if (enableStaging)
                                        {
                                            LocalData.AddStageOperation(new StageOperation() { Operand1 = oldPath, Type = StageOperationType.Remove });
                                            LocalData.AddStageOperation(new StageOperation() { Operand1 = x.CanonicalName, Type = StageOperationType.Add });
                                        }

                                        ApplyAttributes(new FileInfo(rpath), DateTime.Now, x.ObjectAttributes);
                                    }
                                }
                            }
                        }
                        else if (ws.Hash == x.NewHash && ws.Length == x.NewSize)
                            Printer.PrintMessage("  - Skipped, object already present.");
                        else
                            Printer.PrintMessage("  - Skipped, conflict.");
                    }
                    else if (x.Alteration == AlterationType.Update)
                    {
                        if (ws == null)
                            Printer.PrintMessage("  - Skipped, object deleted.");
                        else if (ws.Hash == x.NewHash && ws.Length == x.NewSize)
                            Printer.PrintMessage("  - Skipped, already up to date.");
                        else
                        {
                            if (x.Flags.HasFlag(StashFlags.Binary) && (x.OriginalHash != ws.Hash || x.OriginalSize != ws.Length))
                            {
                                Printer.PrintError("#e# - Can't apply binary patch - source file does not match!##");
                            }
                            else
                            {
                                ApplyPatchEntry(rpath, ws.FilesystemEntry.Info.FullName, x.Flags.HasFlag(StashFlags.Binary), indexTable[i * 2 + 0], br.BaseStream, options, x);
                                if (enableStaging)
                                {
                                    FileInfo resultInfo = new FileInfo(rpath);
                                    if (resultInfo.Length != ws.VersionControlRecord.Size || Entry.CheckHash(resultInfo) != ws.Hash)
                                        LocalData.AddStageOperation(new StageOperation() { Operand1 = x.CanonicalName, Type = StageOperationType.Add });
                                }
                            }
                        }
                    }
                    else if (x.Alteration == AlterationType.Delete && allowDeletes)
                    {
                        if (ws == null)
                            Printer.PrintMessage("  - Skipped, already deleted.");
                        else if (ws.Hash == x.OriginalHash && ws.Length == x.OriginalSize)
                        {
                            FileInfo tfi = new FileInfo(rpath);
                            tfi.IsReadOnly = false;
                            tfi.Delete();
                            Printer.PrintMessage("  - Deleted {0}.", x.CanonicalName);

                            if (enableStaging)
                            {
                                LocalData.AddStageOperation(new StageOperation() { Operand1 = x.CanonicalName, Type = StageOperationType.Remove });
                            }
                        }
                        else
                        {
                            bool deletionResolution = GetStashResolutionDeletion(ref resolveDeleted);
                            if (deletionResolution == true)
                                Printer.PrintMessage("  - Skipped, conflict.");
                            else
                            {
                                FileInfo tfi = new FileInfo(rpath);
                                tfi.IsReadOnly = false;
                                tfi.Delete();
                                Printer.PrintMessage("  - Deleted {0}.", x.CanonicalName);

                                if (enableStaging)
                                {
                                    LocalData.AddStageOperation(new StageOperation() { Operand1 = x.CanonicalName, Type = StageOperationType.Remove });
                                }
                            }
                        }
                    }
                    else
                        Printer.PrintMessage("  - Skipped");
                }
                End:;
            }
        }

        private bool GetStashResolutionDeletion(ref bool? resolveDeleted)
        {
            bool deletionResolution = resolveDeleted.HasValue ? resolveDeleted.Value : true;
            while (!resolveDeleted.HasValue)
            {
                Printer.PrintMessage("Stash deletes a file which has been modified, #s#(k)eep## or #e#(r)emove##? (Use #b#*## for all)");
                string resolution = System.Console.ReadLine();
                if (resolution.StartsWith("k"))
                {
                    if (resolution.Contains("*"))
                        resolveDeleted = true;
                    deletionResolution = true;
                    break;
                }
                if (resolution.StartsWith("r"))
                {
                    if (resolution.Contains("*"))
                        resolveDeleted = false;
                    deletionResolution = false;
                    break;
                }
            }
            return deletionResolution;
        }

        private void ReadStashHeader(BinaryReader br, out StashInfo info, out List<StashEntry> entries, out long[] indexTable)
        {
            info = StashInfo.Read(br);
            if (info == null)
                throw new Exception("Couldn't read stash header!");
            int stashedActions = br.ReadInt32();
            entries = new List<StashEntry>();
            for (int i = 0; i < stashedActions; i++)
                entries.Add(StashEntry.Read(br));

            indexTable = new long[stashedActions * 2];
            for (int i = 0; i < indexTable.Length; i++)
                indexTable[i] = br.ReadInt64();
        }

        private void ApplyStashCreateFile(StashEntry x, Status.StatusEntry ws, string rpath, bool enableStaging, ref ResolveType? resolveAllBinary, ref ResolveType? resolveAllText, ref ResolveType? mergeResolve, Action<string> extractor)
        {
            var tempFolder = AdministrationFolder.CreateSubdirectory("Temp");

            bool stage = true;
            bool extract = false;
            ResolveType rtype = ResolveType.Replace;
            string xpath = rpath;
            if (ws != null)
            {
                if (ws.Hash == x.NewHash && ws.Length == x.NewSize)
                    Printer.PrintMessage("  - Skipped, object already added.");
                else
                {
                    Printer.PrintMessage("  - File already exists!");
                    if (x.Flags.HasFlag(StashFlags.Binary))
                        rtype = GetStashResolutionBinary(ref resolveAllBinary);
                    else
                        rtype = GetStashResolutionText(ref resolveAllText);
                }
            }

            if (rtype == ResolveType.Conflict)
            {
                Printer.PrintMessage("  - Saving stashed file to {0}", x.CanonicalName + ".stashed");
                rpath = rpath + ".stashed";
                extract = true;
                stage = false;
            }
            else if (rtype == ResolveType.Replace)
            {
                extract = true;
            }
            else if (rtype == ResolveType.Skip)
            {
                Printer.PrintMessage("  - Skipping conflicted file.");
                extract = false;
                stage = false;
            }
            else if (rtype == ResolveType.Merge)
            {
                Printer.PrintMessage("  - Attempting two-way merge.");
                xpath = Path.Combine(tempFolder.FullName, Path.GetRandomFileName()) + ".stash";
                extract = true;
                stage = false;
            }

            if (extract)
            {
                extractor(xpath);

                if (rtype == ResolveType.Merge)
                {
                    string tfile = Path.Combine(tempFolder.FullName, Path.GetRandomFileName()) + ".result";
                    var mf = new FileInfo(xpath);
                    var ml = new FileInfo(rpath);
                    var mr = new FileInfo(tfile);
                    FileInfo result = Merge2Way(null, mf, null, ml, mr, true, ref mergeResolve);
                    if (result != null)
                    {
                        if (result != ml)
                        {
                            if (ml.IsReadOnly)
                                ml.IsReadOnly = false;
                            ml.Delete();
                        }
                        if (result != mr)
                        {
                            if (mr.IsReadOnly)
                                mr.IsReadOnly = false;
                            mr.Delete();
                        }
                        if (result != mf)
                        {
                            if (mf.IsReadOnly)
                                mf.IsReadOnly = false;
                            mf.Delete();
                        }
                        result.MoveTo(ml.FullName);
                    }
                }

                ApplyAttributes(new FileInfo(rpath), DateTime.Now, x.ObjectAttributes);

                if (stage && enableStaging)
                    LocalData.AddStageOperation(new StageOperation() { Operand1 = x.CanonicalName, Type = StageOperationType.Add });
            }
        }

        private bool ApplyPatchEntry(string resultPath, string original, bool binary, long patchDataOffset, Stream baseStream, ApplyStashOptions options, StashEntry e)
        {
            var tempFolder = AdministrationFolder.CreateSubdirectory("Temp");
            string patchFile = Path.Combine(tempFolder.FullName, Path.GetRandomFileName());
            string tempFile = Path.Combine(tempFolder.FullName, Path.GetRandomFileName());
            string rejectionFile = Path.Combine(tempFolder.FullName, Path.GetRandomFileName());

            baseStream.Position = patchDataOffset;
            BinaryReader br = new BinaryReader(baseStream);
            long patchSize = br.ReadInt64();

            Versionr.ObjectStore.LZHAMReaderStream reader = new Versionr.ObjectStore.LZHAMReaderStream(patchSize, baseStream);
            using (FileStream fout = File.Open(patchFile, FileMode.Create))
                reader.CopyTo(fout);

            int result;
            if (binary)
                result = ApplyBinaryPatch(Path.GetFullPath(original), patchFile, tempFile);
            else
            {
                result = ApplyPatch(Path.GetFullPath(original), patchFile, tempFile, rejectionFile, options.Reverse ? 1 : 0);

                bool hasRejectedHunks = false;
                using (FileStream fs = File.Open(rejectionFile, FileMode.Open))
                using (TextReader tr = new StreamReader(fs))
                {
                    if (fs.Length != 0)
                    {
                        hasRejectedHunks = true;
                        string[] errorLines = tr.ReadToEnd().Split('\n');
                        Printer.PrintError("#e#Error:#b# Couldn't apply all patch hunks!##");
                        foreach (var x in errorLines)
                        {
                            if (x.StartsWith("@@"))
                                Printer.PrintMessage("#c#{0}##", Printer.Escape(x));
                            else if (x.StartsWith("-"))
                                Printer.PrintMessage("#e#{0}##", Printer.Escape(x));
                            else if (x.StartsWith("+"))
                                Printer.PrintMessage("#s#{0}##", Printer.Escape(x));
                            else
                                Printer.PrintMessage(Printer.Escape(x));
                        }
                    }
                }

                if (hasRejectedHunks && !options.AllowUncleanPatches)
                {
                    Printer.PrintError("#e# - Not applied, patch not clean!");
                    File.Delete(rejectionFile);
                    File.Delete(patchFile);
                    File.Delete(tempFile);
                    return false;
                }
                else if (hasRejectedHunks)
                {
                    Printer.PrintError("#w# - Patch not clean, generating rejection file!");
                    File.Move(rejectionFile, Path.GetFullPath(resultPath) + ".rejected");
                }
            }
            if (result != 0)
                throw new Exception("Error in XDiff while applying patch!");
            else
            {
                FileInfo fi = new FileInfo(Path.GetFullPath(resultPath));
                fi.IsReadOnly = false;
                fi.Delete();
                File.Move(tempFile, fi.FullName);
            }

            File.Delete(patchFile);

            return true;
        }

        [Flags]
        public enum StashFlags
        {
            None = 0,
            Directory = 1,
            Binary = 2,
        }
        public class StashEntry
        {
            public string CanonicalName { get; set; }
            public AlterationType Alteration { get; set; }
            public string OriginalCanonicalName { get; set; }
            public string OriginalHash { get; set; }
            public string NewHash { get; set; }
            public long OriginalSize { get; set; }
            public long NewSize { get; set; }
            public Attributes ObjectAttributes { get; set; }
            public StashFlags Flags { get; set; }

            internal void Write(BinaryWriter bw)
            {
                bw.Write(CanonicalName);
                bw.Write((uint)Alteration);
                bw.Write(OriginalCanonicalName);
                bw.Write(OriginalHash);
                bw.Write(NewHash);
                bw.Write(OriginalSize);
                bw.Write(NewSize);
                bw.Write((uint)ObjectAttributes);
                bw.Write((uint)Flags);
            }

            static internal StashEntry Read(BinaryReader br)
            {
                return new StashEntry()
                {
                    CanonicalName = br.ReadString(),
                    Alteration = (AlterationType)br.ReadUInt32(),
                    OriginalCanonicalName = br.ReadString(),
                    OriginalHash = br.ReadString(),
                    NewHash = br.ReadString(),
                    OriginalSize = br.ReadInt64(),
                    NewSize = br.ReadInt64(),
                    ObjectAttributes = (Attributes)br.ReadUInt32(),
                    Flags = (StashFlags)br.ReadUInt32()
                };
            }
        }

        public void Cherrypick(Objects.Version version, bool relaxed, bool reverse)
        {
            var tempFolder = AdministrationFolder.CreateSubdirectory("Temp");
            var alterations = Database.GetAlterationsForVersion(version);
            List<Status.StatusEntry> stashTargets = new List<Status.StatusEntry>();

            StashInfo header = StashInfo.Create(this, string.Empty, Version.ID);
            List<Tuple<StashEntry, Func<Stream, long>>> stashWriters = new List<Tuple<StashEntry, Func<Stream, long>>>();

            foreach (var x in alterations)
            {
                if (x.Type == AlterationType.Add)
                {
                    Record newRecord = GetRecord(x.NewRecord.Value);
                    if (newRecord.IsDirectory)
                    {
                        StashEntry entry = new StashEntry()
                        {
                            Alteration = AlterationType.Add,
                            CanonicalName = newRecord.CanonicalName,
                            OriginalCanonicalName = string.Empty,
                            NewHash = string.Empty,
                            NewSize = -1,
                            ObjectAttributes = newRecord.Attributes,
                            OriginalHash = string.Empty,
                            OriginalSize = -1,
                            Flags = StashFlags.Directory
                        };

                        stashWriters.Add(new Tuple<StashEntry, Func<Stream, long>>(entry, (s) => { return (long)0; }));
                    }
                    else
                    {
                        GetMissingRecords(new Record[] { newRecord });

                        var tempFile = GetTemporaryFile(newRecord);
                        RestoreRecord(newRecord, DateTime.Now, tempFile.FullName);

                        tempFile = new FileInfo(tempFile.FullName);
                        bool binary = FileClassifier.Classify(tempFile) == FileEncoding.Binary;

                        StashEntry entry = new StashEntry()
                        {
                            Alteration = AlterationType.Add,
                            CanonicalName = newRecord.CanonicalName,
                            OriginalCanonicalName = string.Empty,
                            NewHash = newRecord.Fingerprint,
                            NewSize = newRecord.Size,
                            ObjectAttributes = newRecord.Attributes,
                            OriginalHash = string.Empty,
                            OriginalSize = -1,
                            Flags = binary ? StashFlags.Binary : StashFlags.None
                        };

                        stashWriters.Add(new Tuple<StashEntry, Func<Stream, long>>(entry, (s) =>
                        {
                            long resultSize;
                            using (FileStream input = File.Open(tempFile.FullName, FileMode.Open, FileAccess.Read))
                            {
                                Versionr.ObjectStore.LZHAMWriter.CompressToStream(tempFile.Length, 16 * 1024 * 1024, out resultSize, input, s);
                            }
                            tempFile.IsReadOnly = false;
                            tempFile.Delete();
                            return resultSize;
                        }));
                    }
                }
                else if (x.Type == AlterationType.Update)
                {
                    Record newRecord = GetRecord(x.NewRecord.Value);
                    Record oldRecord = GetRecord(x.PriorRecord.Value);

                    GetMissingRecords(new Record[] { newRecord, oldRecord });

                    var tempFileNew = GetTemporaryFile(newRecord);
                    RestoreRecord(newRecord, DateTime.Now, tempFileNew.FullName);
                    var tempFileOld = GetTemporaryFile(oldRecord);
                    RestoreRecord(oldRecord, DateTime.Now, tempFileOld.FullName);

                    tempFileNew = new FileInfo(tempFileNew.FullName);
                    tempFileOld = new FileInfo(tempFileOld.FullName);

                    bool binary = FileClassifier.Classify(tempFileNew) == FileEncoding.Binary;

                    StashEntry entry = new StashEntry()
                    {
                        Alteration = AlterationType.Update,
                        CanonicalName = newRecord.CanonicalName,
                        OriginalCanonicalName = string.Empty,
                        NewHash = newRecord.Fingerprint,
                        NewSize = newRecord.Size,
                        ObjectAttributes = newRecord.Attributes,
                        OriginalHash = oldRecord.Fingerprint,
                        OriginalSize = oldRecord.Size,
                        Flags = binary ? StashFlags.Binary : StashFlags.None
                    };

                    stashWriters.Add(new Tuple<StashEntry, Func<Stream, long>>(entry, (s) =>
                    {
                        long resultSize;

                        string patchFile = Path.Combine(tempFolder.FullName, Path.GetRandomFileName());

                        int xdiffres = 0;
                        if (binary)
                            xdiffres = GenerateBinaryPatch(tempFileOld.FullName, tempFileNew.FullName, patchFile);
                        else
                            xdiffres = GeneratePatch(tempFileOld.FullName, tempFileNew.FullName, patchFile);

                        if (xdiffres != 0)
                            throw new Exception("Error during xdiff!");

                        BinaryWriter bw = new BinaryWriter(s);
                        long patchSize = new FileInfo(patchFile).Length;
                        bw.Write(patchSize);

                        using (FileStream input = File.Open(patchFile, FileMode.Open, FileAccess.Read))
                        {
                            Versionr.ObjectStore.LZHAMWriter.CompressToStream(patchSize, 16 * 1024 * 1024, out resultSize, input, s);
                        }

                        File.Delete(patchFile);

                        tempFileNew.IsReadOnly = false;
                        tempFileOld.IsReadOnly = false;
                        tempFileNew.Delete();
                        tempFileOld.Delete();

                        return resultSize + 8;
                    }));
                }
                else if (x.Type == AlterationType.Copy || x.Type == AlterationType.Move)
                {
                    Record newRecord = GetRecord(x.NewRecord.Value);
                    Record oldRecord = GetRecord(x.PriorRecord.Value);
                    bool binary = newRecord.Attributes.HasFlag(Attributes.Binary);

                    StashEntry entry = new StashEntry()
                    {
                        Alteration = x.Type,
                        CanonicalName = newRecord.CanonicalName,
                        OriginalCanonicalName = oldRecord.CanonicalName,
                        NewHash = newRecord.Fingerprint,
                        NewSize = newRecord.Size,
                        ObjectAttributes = newRecord.Attributes,
                        OriginalHash = oldRecord.Fingerprint,
                        OriginalSize = oldRecord.Size,
                        Flags = binary ? StashFlags.Binary : StashFlags.None
                    };

                    stashWriters.Add(new Tuple<StashEntry, Func<Stream, long>>(entry, (s) => { return (long)0; }));
                }
                else if (x.Type == AlterationType.Delete)
                {
                    Record oldRecord = GetRecord(x.PriorRecord.Value);
                    bool binary = oldRecord.Attributes.HasFlag(Attributes.Binary);

                    StashEntry entry = new StashEntry()
                    {
                        Alteration = AlterationType.Delete,
                        CanonicalName = oldRecord.CanonicalName,
                        OriginalCanonicalName = string.Empty,
                        NewHash = string.Empty,
                        NewSize = -1,
                        ObjectAttributes = Attributes.None,
                        OriginalHash = oldRecord.Fingerprint,
                        OriginalSize = oldRecord.Size,
                        Flags = binary ? StashFlags.Binary : StashFlags.None
                    };

                    stashWriters.Add(new Tuple<StashEntry, Func<Stream, long>>(entry, (s) => { return (long)0; }));
                }
                else
                    Printer.PrintError("Cherrypick currently doesn't support a {0} alteration.", x.Type);
            }

            string fn = WriteStash(header, stashWriters, true);

            ApplyStashOptions opts = new ApplyStashOptions();
            opts.AllowUncleanPatches = relaxed;
            opts.Reverse = reverse;
            Unstash(header, opts, true);
        }

        internal Guid GrantLock(string path, Guid? branch, string author)
        {
            VaultLock vl = new VaultLock()
            {
                Branch = branch,
                Path = path,
                User = author,
                ID = Guid.NewGuid()
            };
            Database.InsertSafe(vl);
            return vl.ID;
        }

        internal List<Guid> BreakLocks(List<Guid> lockConflicts)
        {
            List<Guid> brokenLocks = new List<Guid>();
            foreach (var x in lockConflicts)
            {
                var lk = Database.Find<VaultLock>(x);
                if (lk != null)
                {
                    Database.DeleteSafe(lk);
                    brokenLocks.Add(x);
                }
            }
            return brokenLocks;
        }

        internal void BreakLocks(List<VaultLock> lockConflicts)
        {
            foreach (var x in lockConflicts)
                Database.DeleteSafe(x);
        }

        internal void CheckLocks(string path, Guid? branch, HashSet<Guid> locks, out List<VaultLock> lockConflicts)
        {
            lockConflicts = new List<VaultLock>();

            // Full
            if (string.IsNullOrEmpty(path))
                path = "/";
            else if (path[0] != '/')
                path = "/" + path;

            bool requestingDirectory = path.EndsWith("/");
            foreach (var x in Database.Table<VaultLock>().ToList())
            {
                if (locks.Contains(x.ID))
                    continue;
                if (branch == null || x.Branch == null || branch.Value == x.Branch.Value)
                {
                    if (string.IsNullOrEmpty(x.Path))
                        lockConflicts.Add(x);
                    else
                    {
                        bool lockIsDirectory = x.Path.EndsWith("/");
                        if ((lockIsDirectory && (path.StartsWith(x.Path, StringComparison.OrdinalIgnoreCase) || x.Path.StartsWith(path, StringComparison.OrdinalIgnoreCase))) ||
                            (!lockIsDirectory && !requestingDirectory && path.Equals(x.Path, StringComparison.OrdinalIgnoreCase)))
                        {
                            lockConflicts.Add(x);
                        }
                    }
                }
            }
        }

        public void Stash(string name, bool revert, Action<Status.StatusEntry, StatusCode> revertFeedback = null)
        {
            Status st = new Status(this, Database, LocalData, FileSnapshot, null, true);
            List<Status.StatusEntry> stashTargets = new List<Status.StatusEntry>();
            Dictionary<string, long> mergeRecords = new Dictionary<string, long>();
            foreach (var x in LocalData.StageOperations)
            {
                if (x.Type == StageOperationType.Add || x.Type == StageOperationType.Remove)
                    stashTargets.Add(st.Map[x.Operand1]);
                else if (x.Type == StageOperationType.MergeRecord)
                    mergeRecords[x.Operand1] = x.ReferenceObject;
            }

            var tempFolder = AdministrationFolder.CreateSubdirectory("Temp");

            if (stashTargets.Count == 0)
            {
                Printer.PrintMessage("Nothing to stash.");
                return;
            }

            if (name == null)
                name = string.Empty;
            StashInfo header = StashInfo.Create(this, name, Version.ID);
            List<Tuple<StashEntry, Func<Stream, long>>> stashWriters = new List<Tuple<StashEntry, Func<Stream, long>>>();
            List<Status.StatusEntry> reverters = new List<Status.StatusEntry>();

            bool includeDeletes = true;
            bool includeDirectories = true;
            bool includeRenames = true;
            bool renamesAsAdds = true;

            if (includeDirectories)
            {
                foreach (var x in stashTargets.Where(x => x.IsDirectory && x.Code == StatusCode.Added))
                {
                    StashEntry entry = new StashEntry()
                    {
                        Alteration = AlterationType.Add,
                        CanonicalName = x.CanonicalName,
                        OriginalCanonicalName = string.Empty,
                        NewHash = string.Empty,
                        NewSize = -1,
                        ObjectAttributes = x.FilesystemEntry.Attributes,
                        OriginalHash = string.Empty,
                        OriginalSize = -1,
                        Flags = StashFlags.Directory
                    };

                    stashWriters.Add(new Tuple<StashEntry, Func<Stream, long>>(entry, (s) => { return (long)0; }));

                    reverters.Add(x);
                }
            }

            foreach (var x in stashTargets.Where(x => !x.IsDirectory))
            {
                long mr;
                if (!mergeRecords.TryGetValue(x.CanonicalName, out mr))
                    mr = -1;

                if (x.Code == StatusCode.Added ||
                    (renamesAsAdds && x.Code == StatusCode.Renamed && x.Hash != x.VersionControlRecord.Fingerprint))
                {
                    bool binary = FileClassifier.Classify(x.FilesystemEntry.Info) == FileEncoding.Binary;

                    StashEntry entry = new StashEntry()
                    {
                        Alteration = AlterationType.Add,
                        CanonicalName = x.CanonicalName,
                        OriginalCanonicalName = string.Empty,
                        NewHash = x.Hash,
                        NewSize = x.Length,
                        ObjectAttributes = x.FilesystemEntry.Attributes,
                        OriginalHash = string.Empty,
                        OriginalSize = -1,
                        Flags = binary ? StashFlags.Binary : StashFlags.None
                    };

                    stashWriters.Add(new Tuple<StashEntry, Func<Stream, long>>(entry, (s) =>
                    {
                        long resultSize;
                        using (FileStream input = File.Open(GetRecordPath(x.CanonicalName), FileMode.Open, FileAccess.Read))
                        {
                            Versionr.ObjectStore.LZHAMWriter.CompressToStream(x.Length, 16 * 1024 * 1024, out resultSize, input, s);
                        }
                        return resultSize;
                    }));
                    reverters.Add(x);
                }
                else if (x.Code == StatusCode.Copied ||
                    (renamesAsAdds && x.Code == StatusCode.Renamed && x.Hash == x.VersionControlRecord.Fingerprint))
                {
                    bool binary = FileClassifier.Classify(x.FilesystemEntry.Info) == FileEncoding.Binary;

                    StashEntry entry = new StashEntry()
                    {
                        Alteration = AlterationType.Copy,
                        CanonicalName = x.CanonicalName,
                        OriginalCanonicalName = string.Empty,
                        NewHash = x.Hash,
                        NewSize = x.Length,
                        ObjectAttributes = x.FilesystemEntry.Attributes,
                        OriginalHash = x.Hash,
                        OriginalSize = x.Length,
                        Flags = binary ? StashFlags.Binary : StashFlags.None
                    };

                    stashWriters.Add(new Tuple<StashEntry, Func<Stream, long>>(entry, (s) => { return (long)0; }));
                    reverters.Add(x);
                }
                else if (x.Code == StatusCode.Deleted && includeDeletes)
                {
                    StashEntry entry = new StashEntry()
                    {
                        Alteration = AlterationType.Delete,
                        CanonicalName = x.CanonicalName,
                        OriginalCanonicalName = string.Empty,
                        NewHash = string.Empty,
                        NewSize = -1,
                        ObjectAttributes = Attributes.None,
                        OriginalHash = x.VersionControlRecord.Fingerprint,
                        OriginalSize = x.VersionControlRecord.Size,
                        Flags = StashFlags.None,
                    };

                    stashWriters.Add(new Tuple<StashEntry, Func<Stream, long>>(entry, (s) => { return (long)0; }));
                    reverters.Add(x);
                }
                else if ((x.Code == StatusCode.Renamed && includeRenames) || x.Code == StatusCode.Modified)
                {
                    bool binary = FileClassifier.Classify(x.FilesystemEntry.Info) == FileEncoding.Binary;

                    StashEntry entry = new StashEntry()
                    {
                        Alteration = x.Code == StatusCode.Modified ? AlterationType.Update : AlterationType.Move,
                        CanonicalName = x.CanonicalName,
                        OriginalCanonicalName = x.Code == StatusCode.Modified ? string.Empty : x.VersionControlRecord.CanonicalName,
                        NewHash = x.Hash,
                        NewSize = x.Length,
                        ObjectAttributes = x.FilesystemEntry.Attributes,
                        OriginalHash = x.VersionControlRecord.Fingerprint,
                        OriginalSize = x.VersionControlRecord.Size,
                        Flags = binary ? StashFlags.Binary : StashFlags.None
                    };
                    reverters.Add(x);

                    stashWriters.Add(new Tuple<StashEntry, Func<Stream, long>>(entry, (s) =>
                    {
                        if (x.Code == StatusCode.Modified || (entry.NewHash == entry.OriginalHash || entry.NewSize != entry.OriginalSize))
                        {
                            long resultSize;

                            string priorRecord = Path.Combine(tempFolder.FullName, Path.GetRandomFileName());
                            string patchFile = Path.Combine(tempFolder.FullName, Path.GetRandomFileName());
                            RestoreRecord(x.VersionControlRecord, DateTime.Now, priorRecord);

                            int xdiffres = 0;
                            if (binary)
                                xdiffres = GenerateBinaryPatch(priorRecord, x.FilesystemEntry.Info.FullName, patchFile);
                            else
                                xdiffres = GeneratePatch(priorRecord, x.FilesystemEntry.Info.FullName, patchFile);

                            if (xdiffres != 0)
                                throw new Exception("Error during xdiff!");

                            BinaryWriter bw = new BinaryWriter(s);
                            long patchSize = new FileInfo(patchFile).Length;
                            bw.Write(patchSize);

                            using (FileStream input = File.Open(patchFile, FileMode.Open, FileAccess.Read))
                            {
                                Versionr.ObjectStore.LZHAMWriter.CompressToStream(patchSize, 16 * 1024 * 1024, out resultSize, input, s);
                            }

                            var oldrec = new FileInfo(priorRecord);
                            oldrec.IsReadOnly = false;
                            File.Delete(priorRecord);
                            File.Delete(patchFile);

                            return resultSize + 8;
                        }
                        return 0;
                    }));
                }
            }
            if (includeDirectories && includeDeletes)
            {
                foreach (var x in stashTargets.Where(x => x.IsDirectory && x.Code == StatusCode.Deleted))
                {
                    StashEntry entry = new StashEntry()
                    {
                        Alteration = AlterationType.Delete,
                        CanonicalName = x.CanonicalName,
                        OriginalCanonicalName = string.Empty,
                        NewHash = string.Empty,
                        NewSize = -1,
                        ObjectAttributes = x.FilesystemEntry.Attributes,
                        OriginalHash = string.Empty,
                        OriginalSize = -1,
                        Flags = StashFlags.Directory
                    };
                    reverters.Add(x);

                    stashWriters.Add(new Tuple<StashEntry, Func<Stream, long>>(entry, (s) => { return (long)0; }));
                }
            }

            try
            {
                LocalData.BeginTransaction();
                header.Key = LocalData.GetStashCode();
                string fn = WriteStash(header, stashWriters, false);
                header.File = new FileInfo(fn);
                LocalData.RecordStash(header);
                LocalData.Commit();
            }
            catch
            {
                LocalData.Rollback();
                throw;
            }
            reverters.Reverse();
            if (revert)
            {
                Revert(reverters, true, false, true, revertFeedback);
            }
        }

        private string WriteStash(StashInfo header, List<Tuple<StashEntry, Func<Stream, long>>> stashWriters, bool cherrypickMode)
        {
            var stashFolder = AdministrationFolder.CreateSubdirectory("Stashes");
            string stashfn = header.GUID + ".stash";
            string filename = Path.Combine(stashFolder.FullName, stashfn);
            if (cherrypickMode)
                filename = Path.Combine(AdministrationFolder.CreateSubdirectory("Temp").FullName, stashfn);
            try
            {
                if (!cherrypickMode)
                    Printer.PrintMessage("Creating stash: #b#{0}## {1}\n #q#<{2}>##", header.Key, header.Name.Length == 0 ? "(no name)" : ("- " + header.Name), header.GUID);
                using (FileStream fs = File.Open(filename, FileMode.Create, FileAccess.Write))
                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    header.Write(bw);
                    if (!cherrypickMode)
                        Printer.PrintMessage(" - Stashing {0} changes.", stashWriters.Count);
                    bw.Write(stashWriters.Count);
                    for (int i = 0; i < stashWriters.Count; i++)
                        stashWriters[i].Item1.Write(bw);
                    long mainIndexPos = fs.Position;
                    long[] indexTable = new long[stashWriters.Count * 2];
                    fs.Seek(indexTable.Length * 8, SeekOrigin.Current);
                    for (int i = 0; i < stashWriters.Count; i++)
                    {
                        if (!cherrypickMode)
                            Printer.PrintMessage(" [{0}]: #b#{1}## - {2}", i, stashWriters[i].Item1.Alteration, stashWriters[i].Item1.CanonicalName);
                        long currentFilePos = fs.Position;
                        indexTable[i * 2 + 0] = currentFilePos;
                        long packedSize = stashWriters[i].Item2(fs);
                        if (currentFilePos + packedSize != fs.Position)
                            throw new Exception();
                        indexTable[i * 2 + 1] = packedSize;
                    }
                    if (!cherrypickMode)
                        Printer.PrintMessage("Packed stash file size is: {0} bytes.", fs.Position);
                    fs.Seek(mainIndexPos, SeekOrigin.Begin);
                    for (int i = 0; i < indexTable.Length; i++)
                        bw.Write(indexTable[i]);
                }

                header.File = new FileInfo(filename);
            }
            catch
            {
                if (System.IO.File.Exists(filename))
                    System.IO.File.Delete(filename);
                throw;
            }
            return stashfn;
        }

        public Guid Domain
        {
            get
            {
                return Database.Domain;
            }
        }

        public void PrintStats(string objectname = null)
        {
            if (objectname != null && !objectname.Contains("*"))
            {
                var nameObject = Database.Table<ObjectName>().ToList().Where(x => x.CanonicalName.Equals(objectname, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                if (nameObject == null)
                {
                    Printer.PrintMessage("Unknown object #e#{0}##!", objectname);
                    return;
                }
                PrintObjectStats(nameObject);
                return;
            }
            Regex matchedObjects = null;
            if (objectname != null)
            {
                string pattern = "^" + Regex.Escape(objectname).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                matchedObjects = new Regex(pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            }
            long vcount = Database.Table<Objects.Version>().Count();
            Printer.PrintMessage("#b#Core Metadata Stats:##");
            Printer.PrintMessage("  #b#{0}## Versions", vcount);
            Printer.PrintMessage("  #b#{0}## Branches", Database.Table<Objects.Branch>().Count());
            Printer.PrintMessage("  #b#{0}## Records", Database.Table<Objects.Record>().Count());
            Printer.PrintMessage("  #b#{0}## Alterations", Database.Table<Objects.Alteration>().Count());
            Printer.PrintMessage("  #b#{0}## Branch Journal Entries", Database.Table<Objects.BranchJournal>().Count());

            List<long> churnCount = new List<long>();
            List<Record> records = new List<Record>();
            Dictionary<long, Record> recordMap = new Dictionary<long, Record>();
            Dictionary<long, string> nameMap = new Dictionary<long, string>();
            foreach (var x in Database.Table<Objects.Record>())
            {
                recordMap[x.Id] = x;
                if (!nameMap.ContainsKey(x.CanonicalNameId))
                    nameMap[x.CanonicalNameId] = Database.Get<ObjectName>(x.CanonicalNameId).CanonicalName;
                if (matchedObjects == null || matchedObjects.IsMatch(nameMap[x.CanonicalNameId]))
                {
                    records.Add(x);
                    while (x.CanonicalNameId >= churnCount.Count)
                        churnCount.Add(-1);
                    churnCount[(int)x.CanonicalNameId]++;
                }
            }

            long additions = 0;
            long updates = 0;
            long deletions = 0;
            Dictionary<Guid, long> versionSize = new Dictionary<Guid, long>();
            foreach (var x in Database.Table<Objects.Version>())
            {
                long totalSize = x.Parent.HasValue ? versionSize[x.Parent.Value] : 0;
                var alterations = GetAlterations(x);
                foreach (var y in alterations)
                {
                    if (y.Type == AlterationType.Add || y.Type == AlterationType.Copy)
                    {
                        totalSize += recordMap[y.NewRecord.Value].Size;
                        additions++;
                    }
                    else if (y.Type == AlterationType.Move || y.Type == AlterationType.Update)
                    {
                        totalSize -= recordMap[y.PriorRecord.Value].Size;
                        updates++;
                        totalSize += recordMap[y.NewRecord.Value].Size;
                    }
                    else if (y.Type == AlterationType.Delete)
                    {
                        totalSize -= recordMap[y.PriorRecord.Value].Size;
                        deletions++;
                    }
                }
                versionSize[x.ID] = totalSize;
            }
            Printer.PrintMessage("\nAn #c#average## commit has:");
            Printer.PrintMessage("  #b#{0:N2}## Updates", updates / (double)vcount);
            Printer.PrintMessage("  #s#{0:N2}## Additions", additions / (double)vcount);
            Printer.PrintMessage("  #e#{0:N2}## Deletions", deletions / (double)vcount);
            Printer.PrintMessage("And requires #c#{0}## of space.", Versionr.Utilities.Misc.FormatSizeFriendly((long)versionSize.Values.Average()));

            var top = churnCount.SelectIndexed().Where(x => x.Item2 != -1).OrderByDescending(x => x.Item2).Take(20);
            Printer.PrintMessage("\nFiles with the #b#most## churn:");
            foreach (var x in top)
            {
                Printer.PrintMessage("  #b#{0}##: #c#{1}## stored revisions.", Database.Get<Objects.ObjectName>(x.Item1).CanonicalName, x.Item2 + 1);
            }
            HashSet<long> ids = new HashSet<long>();
            var largest = records.OrderByDescending(x => x.Size).Where(x => !ids.Contains(x.CanonicalNameId)).Take(10);
            Printer.PrintMessage("\n#b#Largest## files:");
            foreach (var x in largest)
            {
                ids.Add(x.CanonicalNameId);
                Printer.PrintMessage("  #b#{0}##: {1}", Database.Get<Objects.ObjectName>(x.CanonicalNameId).CanonicalName, Versionr.Utilities.Misc.FormatSizeFriendly(x.Size));
            }
            List<long> objectSize = new List<long>();
            foreach (var x in records)
            {
                while (x.CanonicalNameId >= objectSize.Count)
                    objectSize.Add(-1);
                if (objectSize[(int)x.CanonicalNameId] == -1)
                    objectSize[(int)x.CanonicalNameId] = x.Size;
                else
                    objectSize[(int)x.CanonicalNameId] += x.Size;
            }
            top = objectSize.SelectIndexed().Where(x => x.Item2 != -1).OrderByDescending(x => x.Item2).Take(10);
            Printer.PrintMessage("\n#b#Largest## committed size:");
            foreach (var x in top)
            {
                Printer.PrintMessage("  #b#{0}##: {1} total over {2} revisions", Database.Get<Objects.ObjectName>(x.Item1).CanonicalName, Versionr.Utilities.Misc.FormatSizeFriendly(x.Item2), churnCount[x.Item1] + 1);
            }
            List<long> allocatedSize = new List<long>();
            Dictionary<Record, ObjectStore.RecordInfo> recordInfoMap = new Dictionary<Record, Versionr.ObjectStore.RecordInfo>();
            long missingData = 0;
            List<long> allocObjectSize = new List<long>();
            foreach (var x in records)
            {
                while (x.CanonicalNameId >= allocatedSize.Count)
                    allocatedSize.Add(-1);
                while (x.CanonicalNameId >= allocObjectSize.Count)
                    allocObjectSize.Add(-1);
                var info = ObjectStore.GetInfo(x);
                if (info == null && x.HasData)
                    missingData++;
                recordInfoMap[x] = info;
                if (info != null)
                {
                    if (allocatedSize[(int)x.CanonicalNameId] == -1)
                    {
                        allocObjectSize[(int)x.CanonicalNameId] = x.Size;
                        allocatedSize[(int)x.CanonicalNameId] = info.AllocatedSize;
                    }
                    else
                    {
                        allocObjectSize[(int)x.CanonicalNameId] += x.Size;
                        allocatedSize[(int)x.CanonicalNameId] += info.AllocatedSize;
                    }
                }
            }
            long objectEntries = 0;
            long snapCount = 0;
            long snapSize = 0;
            long deltaCount = 0;
            long deltaSize = 0;
            long storedObjectUnpackedSize = 0;
            HashSet<long> objectIDs = new HashSet<long>();
            foreach (var x in recordInfoMap)
            {
                if (x.Value != null)
                {
                    if (objectIDs.Contains(x.Value.ID))
                        continue;
                    objectEntries++;
                    storedObjectUnpackedSize += x.Key.Size;
                    objectIDs.Add(x.Value.ID);
                    if (x.Value.DeltaCompressed)
                    {
                        deltaCount++;
                        deltaSize += x.Value.AllocatedSize;
                    }
                    else
                    {
                        snapCount++;
                        snapSize += x.Value.AllocatedSize;
                    }
                }
            }
            Printer.PrintMessage("\n#b#Core Object Store Stats:##");
            Printer.PrintMessage("  Missing data for #b#{0} ({1:N2}%)## records.", missingData, 100.0 * missingData / (double)recordMap.Count);
            Printer.PrintMessage("  #b#{0}/{2}## Entries ({1:N2}% of records)", objectEntries, 100.0 * objectEntries / (double)recordMap.Count, ObjectStore.GetEntryCount());
            Printer.PrintMessage("  #b#{0} ({1})## Snapshots", snapCount, Versionr.Utilities.Misc.FormatSizeFriendly(snapSize));
            Printer.PrintMessage("  #b#{0} ({1})## Deltas", deltaCount, Versionr.Utilities.Misc.FormatSizeFriendly(deltaSize));
            Printer.PrintMessage("  Unpacked size of all objects: #b#{0}##", Versionr.Utilities.Misc.FormatSizeFriendly(storedObjectUnpackedSize));
            Printer.PrintMessage("  Total size of all records: #b#{0}##", Versionr.Utilities.Misc.FormatSizeFriendly(records.Select(x => x.Size).Sum()));
            Printer.PrintMessage("  Total size of all versions: #b#{0}##", Versionr.Utilities.Misc.FormatSizeFriendly(versionSize.Values.Sum()));

            top = allocatedSize.SelectIndexed().Where(x => x.Item2 != -1).OrderByDescending(x => x.Item2).Take(10);
            Printer.PrintMessage("\nMost #b#allocated object size##:");
            foreach (var x in top)
            {
                Printer.PrintMessage("  #b#{0}##: {1} total", Database.Get<Objects.ObjectName>(x.Item1).CanonicalName, Versionr.Utilities.Misc.FormatSizeFriendly(x.Item2));
            }
            var ratios = allocatedSize.SelectIndexed().Where(x => x.Item2 > 0).Select(x => new Tuple<int, double>(x.Item1, x.Item2 / (double)allocObjectSize[x.Item1]));
            Printer.PrintMessage("\n#s#Best## compression:");
            foreach (var x in ratios.OrderBy(x => x.Item2).Take(10))
            {
                Printer.PrintMessage("  #b#{0}##: {1} -> {2} ({3:N2}% over {4} versions)", Database.Get<Objects.ObjectName>(x.Item1).CanonicalName,
                    Versionr.Utilities.Misc.FormatSizeFriendly(allocObjectSize[x.Item1]),
                    Versionr.Utilities.Misc.FormatSizeFriendly(allocatedSize[x.Item1]),
                    x.Item2 * 100.0,
                    churnCount[x.Item1] + 1);
            }
            Printer.PrintMessage("\n#e#Worst## compression:");
            foreach (var x in ratios.Where(x => allocatedSize[x.Item1] > 1024).OrderByDescending(x => x.Item2).Take(10))
            {
                Printer.PrintMessage("  #b#{0}##: {1} -> {2} ({3:N2}% over {4} versions)", Database.Get<Objects.ObjectName>(x.Item1).CanonicalName,
                    Versionr.Utilities.Misc.FormatSizeFriendly(allocObjectSize[x.Item1]),
                    Versionr.Utilities.Misc.FormatSizeFriendly(allocatedSize[x.Item1]),
                    x.Item2 * 100.0,
                    churnCount[x.Item1] + 1);
            }
        }

        public bool RebaseCollapse(Objects.Version currentVersion, Objects.Version parentVersion, string message)
        {
            List<Objects.Version> rebaseOperations = new List<Objects.Version>();
            bool success = false;
            foreach (var x in GetHistory(currentVersion))
            {
                if (x.ID == parentVersion.ID)
                {
                    success = true;
                    break;
                }
                rebaseOperations.Add(x);
            }
            if (!success)
            {
                Printer.PrintError("#e#Error: Rebase parent #b#{0}#e# is not part of the local version's history.", parentVersion.ID);
                return false;
            }
            rebaseOperations.Reverse();
            Dictionary<long, Objects.Alteration> alterationKeys = new Dictionary<long, Objects.Alteration>();
            int totalAlterationCount = 0;
            foreach (var x in rebaseOperations)
            {
                var alterations = GetAlterations(x);
                foreach (var y in alterations)
                {
                    totalAlterationCount++;
                    Objects.Alteration priorAlteration = null;
                    if (y.PriorRecord.HasValue)
                        alterationKeys.TryGetValue(y.PriorRecord.Value, out priorAlteration);

                    if (y.Type == Objects.AlterationType.Add)
                    {
                        alterationKeys[y.NewRecord.Value] = y;
                        if (priorAlteration != null)
                            throw new Exception("Unable to reconcile alterations for rebase.");
                    }
                    else if (y.Type == Objects.AlterationType.Copy)
                    {
                        // if the object was added or updated in the prior ops, this should be an "add"
                        alterationKeys[y.NewRecord.Value] = y;
                        if (priorAlteration != null)
                        {
                            if (priorAlteration.Type == Objects.AlterationType.Add || priorAlteration.Type == Objects.AlterationType.Update)
                            {
                                y.Type = Objects.AlterationType.Add;
                                y.PriorRecord = null;
                            }
                            else if (priorAlteration.Type == Objects.AlterationType.Copy)
                            {
                                y.PriorRecord = priorAlteration.PriorRecord.Value;
                            }
                            else if (priorAlteration.Type == Objects.AlterationType.Move)
                            {
                                y.Type = Objects.AlterationType.Add;
                                y.PriorRecord = null;
                            }
                            else if (priorAlteration.Type == Objects.AlterationType.Delete)
                                throw new Exception("Unable to reconcile alterations for rebase.");
                        }
                    }
                    else if (y.Type == Objects.AlterationType.Move)
                    {
                        alterationKeys[y.NewRecord.Value] = y;
                        if (priorAlteration != null)
                        {
                            if (priorAlteration.Type == Objects.AlterationType.Add || priorAlteration.Type == Objects.AlterationType.Update || priorAlteration.Type == Objects.AlterationType.Copy)
                            {
                                alterationKeys.Remove(y.PriorRecord.Value);
                                y.Type = Objects.AlterationType.Add;
                                y.PriorRecord = null;
                            }
                            else if (priorAlteration.Type == Objects.AlterationType.Move)
                            {
                                alterationKeys.Remove(y.PriorRecord.Value);
                                y.PriorRecord = priorAlteration.PriorRecord.Value;
                            }
                            else if (priorAlteration.Type == Objects.AlterationType.Delete)
                                throw new Exception("Unable to reconcile alterations for rebase.");
                        }
                    }
                    else if (y.Type == Objects.AlterationType.Delete)
                    {
                        // no prior ops - normal, update/move - alter prior record, add/copy - remove all
                        if (priorAlteration != null)
                        {
                            if (priorAlteration.Type == Objects.AlterationType.Add || priorAlteration.Type == Objects.AlterationType.Copy)
                                alterationKeys.Remove(y.PriorRecord.Value);
                            else if (priorAlteration.Type == Objects.AlterationType.Update || priorAlteration.Type == Objects.AlterationType.Move)
                            {
                                alterationKeys.Remove(y.PriorRecord.Value);
                                y.PriorRecord = priorAlteration.PriorRecord.Value;
                                alterationKeys[y.PriorRecord.Value] = y;
                            }
                            else if (priorAlteration.Type == Objects.AlterationType.Delete)
                                throw new Exception("Unable to reconcile alterations for rebase.");
                        }
                        else
                            alterationKeys[y.PriorRecord.Value] = y;
                    }
                    else if (y.Type == Objects.AlterationType.Update)
                    {
                        // no prior ops - normal, update - alter prior record, move - becomes add/delete, add/copy - becomes add
                        if (priorAlteration != null)
                        {
                            if (priorAlteration.Type == Objects.AlterationType.Add || priorAlteration.Type == Objects.AlterationType.Copy)
                            {
                                alterationKeys.Remove(y.PriorRecord.Value);
                                y.PriorRecord = null;
                                y.Type = Objects.AlterationType.Add;
                                alterationKeys[y.NewRecord.Value] = y;
                            }
                            else if (priorAlteration.Type == Objects.AlterationType.Update)
                            {
                                alterationKeys.Remove(y.PriorRecord.Value);
                                y.PriorRecord = priorAlteration.PriorRecord.Value;
                                alterationKeys[y.NewRecord.Value] = y;
                            }
                            else if (priorAlteration.Type == Objects.AlterationType.Move)
                            {
                                priorAlteration.NewRecord = null;
                                priorAlteration.Type = Objects.AlterationType.Delete;
                                y.PriorRecord = null;
                                y.Type = Objects.AlterationType.Add;
                                alterationKeys[y.NewRecord.Value] = y;
                            }
                            else if (priorAlteration.Type == Objects.AlterationType.Delete)
                                throw new Exception("Unable to reconcile alterations for rebase.");
                        }
                        else
                            alterationKeys[y.NewRecord.Value] = y;
                    }
                }
            }
            Objects.Version rebaseVersion = Objects.Version.Create();
            rebaseVersion.Message = message;
            rebaseVersion.Parent = parentVersion.ID;
            rebaseVersion.Published = false;
            rebaseVersion.Author = Username;
            rebaseVersion.Branch = parentVersion.Branch;
            rebaseVersion.Timestamp = DateTime.UtcNow;
            MergeInfo mergeInfo = new MergeInfo();
            mergeInfo.DestinationVersion = rebaseVersion.ID;
            mergeInfo.SourceVersion = currentVersion.ID;
            mergeInfo.Type = MergeType.Rebase;
            Database.BeginExclusive();
            try
            {
                List<Objects.Alteration> mergedAlterations = alterationKeys.Values.ToList();
                Objects.Snapshot snapshot = new Snapshot();
                Database.Insert(snapshot);
                Printer.PrintMessage("Rebasing commit with #b#{0}## operations ({1} source operations).", mergedAlterations.Count, totalAlterationCount);
                foreach (var x in mergedAlterations)
                {
                    x.Owner = snapshot.Id;
                    Database.Insert(x);
                }
                rebaseVersion.AlterationList = snapshot.Id;
                Database.Insert(rebaseVersion);
                Database.Insert(mergeInfo);
                var otherHeads = GetHeads(currentVersion.ID).Concat(GetHeads(parentVersion.ID));
                foreach (var x in otherHeads)
                {
                    if (x.Branch == rebaseVersion.Branch)
                    {
                        Database.Delete(x);
                        Printer.PrintMessage(" - Deleted prior head record: #b#{0}##.", x.Version);
                    }
                }
                Database.Insert(new Head() { Branch = rebaseVersion.Branch, Version = rebaseVersion.ID });
                LocalData.BeginTransaction();
                try
                {
                    var ws = LocalData.Workspace;
                    ws.Branch = rebaseVersion.Branch;
                    ws.Tip = rebaseVersion.ID;
                    LocalData.Update(ws);
                    LocalData.Commit();
                    Database.Commit();
                    Printer.PrintMessage("Rebase complete. Now on #b#{0}## in branch #b#\"{1}\"##.", rebaseVersion.ID, GetBranch(rebaseVersion.Branch).Name);
                    return true;
                }
                catch
                {
                    LocalData.Rollback();
                    throw;
                }
            }
            catch
            {
                Database.Rollback();
            }
            return false;
        }

        public IEnumerable<Branch> GetBranches(bool deleted)
        {
            if (deleted)
                return Database.Table<Branch>();
            else
                return Database.Table<Branch>().Where(x => x.Terminus == null);
        }

        public bool ExpungeVersion(Objects.Version version)
        {
            try
            {
                Database.BeginExclusive();
                if (!version.Parent.HasValue)
                    throw new Exception("Can't remove first version!");
                if (Database.Table<Objects.Version>().Where(x => x.Parent == version.ID).Count() > 0)
                    throw new Exception("Can't a version which is not at the tip of the history!");
                Objects.Version parent = GetVersion(version.Parent.Value);
                Database.Delete(version);
                Printer.PrintMessage("Deleted version #b#{0}##.", version.ID);
                var heads = GetHeads(version.ID);
                foreach (var x in heads)
                {
                    x.Version = parent.ID;
                    Database.Update(x);
                }
                Printer.PrintMessage(" - Updated {0} heads.", heads.Count);
                var terminators = Database.Table<Objects.Branch>().Where(x => x.Terminus == version.ID).ToList();
                foreach (var x in terminators)
                {
                    x.Terminus = parent.ID;
                    Database.Update(x);
                }
                Printer.PrintMessage(" - Updated {0} branch terminuses.", terminators.Count);

                var ws = LocalData.Workspace;
                if (ws.Tip == version.ID)
                {
                    ws.Tip = parent.ID;
                    ws.Branch = parent.Branch;
                    LocalData.Update(ws);
                    Printer.PrintMessage("Moved tip to version #b#{0}## on branch \"#b#{1}##\"", parent.ID, GetBranch(parent.Branch).Name);
                }
                Database.Commit();
                return true;
            }
            catch
            {
                Database.Rollback();
                throw;
            }
        }

		internal List<Record> GetCurrentRecords()
		{
			return Database.Records;
		}
		
        public bool PathContains(string possibleparent, string location)
        {
            string outerpath = GetLocalPath(Path.Combine(Root.FullName, possibleparent));
            if (!outerpath.EndsWith("/"))
                outerpath += "/";
            if (GetLocalPath(location).ToLower().StartsWith(outerpath.ToLower()))
                return true;
            return false;
        }

        internal bool InExtern(DirectoryInfo info)
        {
            foreach (var x in Externs)
            {
                string externPath = GetLocalPath(Path.Combine(Root.FullName, x.Value.Location));
                if (!externPath.EndsWith("/"))
                    externPath += "/";
                if (GetLocalPath(info.FullName).ToLower().StartsWith(externPath.ToLower()))
                    return true;
            }
            return false;
        }

        private void PrintObjectStats(ObjectName nameObject)
        {
            Printer.PrintMessage("Stats for #b#{0}##:", nameObject.CanonicalName);
            List<Objects.Record> records = Database.Table<Objects.Record>().Where(x => x.CanonicalNameId == nameObject.NameId).ToList();
            HashSet<long> revisions = new HashSet<long>();
            foreach (var x in records)
                revisions.Add(x.Id);

            foreach (var x in records)
            {
                if (x.Parent == null)
                {
                    var alteration = Database.Table<Objects.Alteration>().Where(y => y.NewRecord == x.Id).First();
                    var version = Database.Table<Objects.Version>().Where(y => y.AlterationList == alteration.Owner).First();
                    Printer.PrintMessage("Object initially added in version #c#{0}##.", version.ShortName);
                    break;
                }
                else if (!revisions.Contains(x.Parent.Value))
                {
                    var alteration = Database.Table<Objects.Alteration>().Where(y => y.NewRecord == x.Id).First();
                    var version = Database.Table<Objects.Version>().Where(y => y.AlterationList == alteration.Owner).First();
                    var prior = Database.Table<Objects.Record>().Where(y => y.Id == x.Parent.Value).First();
                    Printer.PrintMessage("Object initially copied from #b#{1}## in version #c#{0}##.", version.ShortName, Database.Get<ObjectName>(prior.CanonicalNameId).CanonicalName);
                }
            }

            Printer.PrintMessage("#b#{0}## records in vault.", records.Count);
            Printer.PrintMessage("  #b#Earliest:## {0}", new DateTime(records.Min(x => x.ModificationTime.ToLocalTime().Ticks)));
            Printer.PrintMessage("  #b#Latest:## {0}", new DateTime(records.Max(x => x.ModificationTime.ToLocalTime().Ticks)));
            Printer.PrintMessage("  #b#Size:## Min {0}, Max: {1}, Av: {2}",
                Versionr.Utilities.Misc.FormatSizeFriendly(records.Min(x => x.Size)),
                Versionr.Utilities.Misc.FormatSizeFriendly(records.Max(x => x.Size)),
                Versionr.Utilities.Misc.FormatSizeFriendly((long)records.Average(x => x.Size)));
            Printer.PrintMessage("  #b#Total Bytes:## {0}", Versionr.Utilities.Misc.FormatSizeFriendly(records.Sum(x => x.Size)));
            var objstoreinfo = records.Select(x => new Tuple<Record, ObjectStore.RecordInfo>(x, ObjectStore.GetInfo(x))).Where(x => x.Item2 != null).ToList();
            Printer.PrintMessage("\n#b#{0}## objects stored.", objstoreinfo.Count);
            Printer.PrintMessage("  #b#Total Allocated:## {0}", Versionr.Utilities.Misc.FormatSizeFriendly(objstoreinfo.Sum(x => x.Item2.AllocatedSize)));
            Printer.PrintMessage("  #b#Compression Ratio:## {0:N2}%", 100.0 * objstoreinfo.Average(x => (double)x.Item2.AllocatedSize / x.Item1.Size));

            int deltas = objstoreinfo.Count(x => x.Item2.DeltaCompressed);
            Printer.PrintMessage("  #b#Delta Count:## {0} ({1:N2}%)", deltas, 100.0 * deltas / (double)objstoreinfo.Count);

            if (deltas != objstoreinfo.Count)
                Printer.PrintMessage("  #b#Average snapshot size:## {0}", Versionr.Utilities.Misc.FormatSizeFriendly((long)objstoreinfo.Where(x => !x.Item2.DeltaCompressed).Average(x => x.Item2.AllocatedSize)));
            if (deltas != 0)
                Printer.PrintMessage("  #b#Average delta size:## {0}", Versionr.Utilities.Misc.FormatSizeFriendly((long)objstoreinfo.Where(x => x.Item2.DeltaCompressed).Average(x => x.Item2.AllocatedSize)));
        }

        public FileInfo MetadataFile
        {
            get
            {
                return new FileInfo(Path.Combine(AdministrationFolder.FullName, "metadata.db"));
            }
        }

        public void Update(MergeSpecialOptions options, string updateTarget = null)
        {
            Merge(string.IsNullOrEmpty(updateTarget) ? CurrentBranch.ID.ToString() : updateTarget, true, options);
            ProcessExterns(true);
        }

        public FileInfo LocalMetadataFile
        {
            get
            {
                return new FileInfo(Path.Combine(AdministrationFolder.FullName, "config.db"));
            }
        }

        public List<Objects.Branch> GetBranches(string name, bool deleted, bool partialNames)
        {
            if (string.IsNullOrEmpty(name))
            {
                if (deleted)
                    return Database.Table<Branch>().ToList();
                else
                    return Database.Table<Branch>().Where(x => x.Terminus == null).ToList();
            }
            string query = "SELECT rowid, * FROM Branch";
            if (partialNames)
                query += string.Format(" WHERE Name LIKE '%{0}%'", name);
            else
                query += string.Format(" WHERE Name IS '{0}'", name);
            if (deleted)
                return Database.Query<Branch>(query);
            else
                return Database.Query<Branch>(query).Where(x => x.Terminus == null).ToList();
        }

        public bool DeleteBranch(Branch branch)
        {
            return RunLocked(() =>
            {
                BranchJournal journal = GetBranchJournalTip();

                BranchJournal change = new BranchJournal();
                change.Branch = branch.ID;
                change.ID = Guid.NewGuid();
                change.Operand = GetBranchHead(branch).Version.ToString();
                change.Type = BranchAlterationType.Terminate;
                return InsertBranchJournalChange(journal, change);
            }, true);
        }

        public void DeleteBranchNoTransaction(Branch branch)
        {
            BranchJournal journal = GetBranchJournalTip();

            BranchJournal change = new BranchJournal();
            change.Branch = branch.ID;
            change.ID = Guid.NewGuid();
            change.Operand = GetBranchHead(branch).Version.ToString();
            change.Type = BranchAlterationType.Terminate;
            InsertBranchJournalChangeNoTransaction(journal, change, true);
        }

        private bool InsertBranchJournalChange(BranchJournal journal, BranchJournal change)
        {
            try
            {
                Database.BeginTransaction();
                InsertBranchJournalChangeNoTransaction(journal, change, false);
                Database.Commit();
                return true;
            }
            catch
            {
                Database.Rollback();
                return false;
            }
        }

        private void InsertBranchJournalChangeNoTransaction(BranchJournal journal, BranchJournal change, bool interactive)
        {
            Database.InsertSafe(change);
            if (journal != null)
            {
                BranchJournalLink link = new BranchJournalLink()
                {
                    Link = change.ID,
                    Parent = journal.ID
                };
                Database.InsertSafe(link);
            }

            ReplayBranchJournal(change, false, null);

            Database.BranchJournalTip = change.ID;
        }

        public bool SetPartialPath(string path)
        {
            if (!string.IsNullOrEmpty(path) && !path.EndsWith("/"))
                path += "/";
            if (LocalData.PartialPath != path)
            {
                try
                {
                    Printer.PrintMessage("Workspace internal path set to: #b#{0}##.", path);
                    LocalData.BeginTransaction();
                    var ws = LocalData.Workspace;
                    ws.PartialPath = path;
                    LocalData.UpdateSafe(ws);
                    LocalData.Commit();
                    LocalData.RefreshPartialPath();
                    return true;
                }
                catch
                {
                    LocalData.Rollback();
                    return false;
                }
            }
            return true;
        }

        internal List<Record> GetAllMissingRecords()
        {
            return FindMissingRecords(Database.GetAllRecords());
        }

        public Branch GetBranchByPartialName(string v, out bool multipleBranches)
        {
            var branches = GetBranchByName(v);
            multipleBranches = false;
            if (branches.Count == 0)
            {
                Objects.Branch branch = Database.Find<Objects.Branch>(v);
                if (branch != null)
                    return branch;
                bool postfix = false;
                if (v.StartsWith("..."))
                {
                    postfix = true;
                    branches = Database.Query<Objects.Branch>(string.Format("SELECT * FROM Branch WHERE Branch.ID LIKE '%{0}'", v.Substring(3)));
                }
                else
                    branches = Database.Query<Objects.Branch>(string.Format("SELECT * FROM Branch WHERE Branch.ID LIKE '{0}%'", v));
            }
            if (branches.Count == 1)
                return branches[0];
            if (branches.Count > 1)
            {
                multipleBranches = true;
                Printer.PrintError("Can't find a unique branch with pattern: {0}\nCould be:", v);
                foreach (var x in branches)
                    Printer.PrintMessage("\t{0} - name: \"#b#{1}##\"", x.ID, x.Name);
            }
            return null;
        }

        public bool RenameBranch(Branch branch, string name)
        {
            return RunLocked(() =>
            {
                BranchJournal journal = GetBranchJournalTip();

                BranchJournal change = new BranchJournal();
                change.Branch = branch.ID;
                change.ID = Guid.NewGuid();
                change.Operand = name;
                change.Type = BranchAlterationType.Rename;
                return InsertBranchJournalChange(journal, change);
            }, true);
        }

        internal bool ReplayBranchJournal(BranchJournal change, bool interactive, List<BranchJournal> conflicts, SharedNetwork.SharedNetworkInfo sharedInfo = null)
        {
            Objects.Branch branch = Database.Find<Objects.Branch>(change.Branch);
            if (branch == null)
                return true;
            if (change.Type == BranchAlterationType.Rename)
            {
                if (interactive)
                    Printer.PrintMessage("Renamed branch \"#b#{0}##\" to \"#b#{1}##\".", branch.Name, change.Operand);

                branch.Name = change.Operand;
                Database.UpdateSafe(branch);
                return true;
            }
            else if (change.Type == BranchAlterationType.Terminate)
            {
                if (string.IsNullOrEmpty(change.Operand) && branch.Terminus.HasValue)
                {
                    if (interactive)
                        Printer.PrintMessage("Undeleted branch \"#b#{0}##\" (#c#{1}##)", branch.Name, branch.ID);
                    else
                        Printer.PrintDiagnostics("Undeleting branch");
                    Guid id = branch.Terminus.Value;
                    Printer.PrintDiagnostics("Prior terminus: {0}", id);
                    branch.Terminus = null;
                    Database.UpdateSafe(branch);
                    Objects.Version v = sharedInfo == null ? GetVersion(id) : GetLocalOrRemoteVersion(id, sharedInfo);
                    if (v != null)
                    {
                        Head head = new Head()
                        {
                            Branch = branch.ID,
                            Version = id
                        };
                        Database.InsertSafe(head);
                    }
                }
                else
                {
                    if (interactive)
                        Printer.PrintMessage("Deleting branch \"#b#{0}##\" (#c#{1}##)", branch.Name, branch.ID);
                    var targetID = new Guid(change.Operand);
                    Objects.Version v = sharedInfo == null ? GetVersion(targetID) : GetLocalOrRemoteVersion(targetID, sharedInfo);
                    if (v == null)
                    {
                        branch.Terminus = targetID;
                        Database.UpdateSafe(branch);
                        return true;
                    }
                    if (branch.Terminus.HasValue)
                    {
                        // we only care about receiving exciting terminuses
                        if (sharedInfo != null)
                        {
                            if (!SharedNetwork.IsAncestor(branch.Terminus.Value, targetID, sharedInfo))
                            {
                                // we received an older ancestor, just return
                                if (interactive)
                                    Printer.PrintMessage("#w#Received an outdated branch deletion.##");
                                conflicts.Add(new BranchJournal()
                                {
                                    Branch = change.Branch,
                                    Operand = branch.Terminus.Value.ToString(),
                                    Type = BranchAlterationType.Terminate
                                });
                                return true;
                            }
                        }
                    }
                    var heads = Database.GetHeads(branch);
                    foreach (var x in heads)
                    {
                        if (sharedInfo != null)
                        {
                            if (targetID != x.Version && SharedNetwork.IsAncestor(targetID, x.Version, sharedInfo))
                            {
                                if (interactive)
                                {
                                    Printer.PrintMessage("#w#Received a branch deletion to a version older than the current head.##\n  Branch: #c#{0}## \"#b#{1}##\"\n  Head: #b#{2}##\n  Incoming terminus: #b#{3}##.", branch.ID, branch.Name, x.Version, targetID);
                                    bool resolved = false;
                                    while (!resolved)
                                    {
                                        Printer.PrintMessage("(U)ndelete branch, (m)ove terminus, or (a)ccept incoming delete? ");
                                        string resolution = System.Console.ReadLine();
                                        if (resolution.StartsWith("u", StringComparison.OrdinalIgnoreCase))
                                        {
                                            conflicts.Add(new BranchJournal()
                                            {
                                                Branch = change.Branch,
                                                Operand = x.Version.ToString(),
                                                Type = BranchAlterationType.Terminate
                                            });
                                            conflicts.Add(new BranchJournal()
                                            {
                                                Branch = change.Branch,
                                                Operand = null,
                                                Type = BranchAlterationType.Terminate
                                            });
                                            resolved = true;
                                        }
                                        if (resolution.StartsWith("m", StringComparison.OrdinalIgnoreCase))
                                        {
                                            targetID = x.Version;
                                            conflicts.Add(new BranchJournal()
                                            {
                                                Branch = change.Branch,
                                                Operand = targetID.ToString(),
                                                Type = BranchAlterationType.Terminate
                                            });
                                            resolved = true;
                                        }
                                        if (resolution.StartsWith("a", StringComparison.OrdinalIgnoreCase))
                                        {
                                            resolved = true;
                                        }
                                    }
                                }
                                else
                                {
                                    Printer.PrintDiagnostics("Branch terminus set before head - manually updating.");
                                    targetID = x.Version;
                                    conflicts.Add(new BranchJournal()
                                    {
                                        Branch = change.Branch,
                                        Operand = x.Version.ToString(),
                                        Type = BranchAlterationType.Terminate
                                    });
                                }
                            }
                        }
                        Database.DeleteSafe(x);
                    }
                    branch.Terminus = targetID;
                    Database.UpdateSafe(branch);
                }
                return true;
            }
            else if (change.Type == BranchAlterationType.Merge)
                return true;
            else
                throw new Exception();
        }

        internal void ReplaceHeads(Guid key, List<Head> value)
        {
            Objects.Branch branch = Database.Get<Branch>(key);
            var heads = GetBranchHeads(branch);

            for (int i = value.Count; i < heads.Count; i++)
                Database.DeleteSafe(heads[i]);
            while (heads.Count > value.Count)
                heads.RemoveAt(heads.Count - 1);

            for (int i = 0; i < heads.Count; i++)
            {
                if (heads[i].Version != value[i].Version)
                {
                    Printer.PrintDiagnostics("Updating head of branch {0} to version {1}", branch.Name, value[i].Version);
                    heads[i].Version = value[i].Version;
                    Database.UpdateSafe(heads[i]);
                }
            }
            for (int i = heads.Count; i < value.Count; i++)
            {
                Printer.PrintDiagnostics("Adding new head of branch {0}, version {1}", branch.Name, value[i].Version);
                Database.InsertSafe(value[i]);
            }
        }

        public DirectoryInfo Root
        {
            get
            {
                return RootDirectory;
            }
        }

        public FileStatus FileSnapshot
        {
            get
            {
                return new FileStatus(this, Root);
            }
        }

        public bool IsHead(Objects.Version v)
        {
            return Database.Table<Objects.Head>().Where(x => x.Version == v.ID).Any();
        }

        public List<Branch> MapVersionToHeads(Guid versionID)
        {
            var heads = Database.Table<Objects.Head>().Where(x => x.Version == versionID).ToList();
            var branches = heads.Select(x => Database.Get<Objects.Branch>(x.Branch)).ToList();
            return branches;
        }

        public bool ForceBehead(string target)
        {
            var heads = Database.Query<Objects.Head>(String.Format("SELECT * FROM Head WHERE Head.Version LIKE \"{0}%\"", target));
            if (heads.Count == 0)
                return false;

            Database.BeginTransaction();
            try
            {
                foreach (var x in heads)
                    Database.Delete(x);
                Database.Commit();
                return true;
            }
            catch
            {
                Database.Rollback();
                return false;
            }
        }

        public void UpdateRemoteTimestamp(RemoteConfig config)
        {
            config.LastPull = DateTime.UtcNow;
            LocalData.UpdateSafe(config);
        }

        public bool FindVersion(string target, out Objects.Version version)
        {
            version = GetPartialVersion(target);
            if (version == null)
                return false;
            return true;
        }

        public Versionr.Status GetStatus(DirectoryInfo activeDirectory)
        {
            if (activeDirectory.FullName == Root.FullName && PartialPath == null)
                return Status;
            return new Status(this, Database, LocalData, new FileStatus(this, activeDirectory), GetLocalPath(activeDirectory.GetFullNameWithCorrectCase()));
        }

        class LocalRefreshState
        {
            public int RecordsTotal;
            public int RecordsProcessed;
        }

        internal void RefreshLocalTimes()
        {
            var records = Database.Records;
            LocalRefreshState lrs = new LocalRefreshState() { RecordsTotal = records.Count };
            var printer = Printer.CreateProgressBarPrinter("Updating local timestamp cache", "Record",
                (obj) => { return string.Empty; },
                (obj) => { return (float)System.Math.Round(100.0 * lrs.RecordsProcessed / (float)lrs.RecordsTotal); },
                (pct, obj) => { return string.Format("{0}/{1}", lrs.RecordsProcessed, lrs.RecordsTotal); },
                70);
            Dictionary<string, FileTimestamp> filetimes = new Dictionary<string, FileTimestamp>();
            foreach (var x in records)
            {
                printer.Update(null);
                FileInfo dest = new FileInfo(Path.Combine(Root.FullName, x.CanonicalName));
                if (dest.Exists)
                {
                    if (dest.Length == x.Size)
                    {
                        if (Entry.CheckHash(dest) == x.Fingerprint)
                        {
                            filetimes[x.CanonicalName] = new FileTimestamp() { CanonicalName = x.CanonicalName, LastSeenTime = dest.LastWriteTimeUtc, DataIdentifier = x.DataIdentifier };
                        }
                    }
                }
                lrs.RecordsProcessed++;
            }
            printer.End(null);
            LocalData.ReplaceFileTimes(filetimes);
        }

        internal bool HasBranchJournal(Guid id)
        {
            return Database.Find<BranchJournal>(id) != null;
        }

        internal List<BranchJournal> GetBranchJournalParents(BranchJournal journal)
        {
            var links = Database.Table<BranchJournalLink>().Where(x => x.Link == journal.ID).ToList();
            var parents = new List<BranchJournal>();
            foreach (var x in links)
                parents.Add(Database.Get<BranchJournal>(x.Parent));
            return parents;
        }

        internal Guid BranchJournalTipID
        {
            get
            {
                Guid? branchJournalTip = Database.BranchJournalTip;
                if (branchJournalTip.HasValue)
                    return branchJournalTip.Value;
                return Guid.Empty;
            }
        }

        internal BranchJournal GetBranchJournalTip()
        {
            Guid? branchJournalTip = Database.BranchJournalTip;
            BranchJournal journal = null;
            if (branchJournalTip.HasValue)
                journal = Database.Get<BranchJournal>(branchJournalTip);
            return journal;
        }

        public void ReplaceFileTimes()
        {
            LocalData.ReplaceFileTimes(FileTimeCache);
        }

        public List<Objects.Record> GetAllRecords()
        {
            return Database.GetAllRecords();
        }

        public List<Head> GetHeads(Guid versionID)
        {
            return Database.Table<Objects.Head>().Where(x => x.Version == versionID).ToList();
        }

        public Status Status
        {
            get
            {
                return new Status(this, Database, LocalData, FileSnapshot);
            }
		}
		
		public bool SetRemote(string url, string name)
        {
            Regex validNames = new Regex("^[A-Za-z0-9-_]+$");
            if (!validNames.IsMatch(name))
            {
                Printer.PrintError("#e#Name \"{0}\" invalid for remote. Only alphanumeric characters, underscores and dashes are allowed.", name);
                return false;
            }

			// Try to parse Versionr URL so we can store host/port/module in RemoteConfig
			string host;
			int port;
			string module;
			if (Client.TryParseVersionrURL(url, out host, out port, out module))
			{
				// Ok, parsed Versionr URL
			}
			else
			{
				// Store URL in module, leave host null
				module = url;
			}

            LocalData.BeginTransaction();
            try
            {
                RemoteConfig config = LocalData.Find<RemoteConfig>(x => x.Name == name);
                if (config == null)
                {
                    config = new RemoteConfig() { Name = name };
                    config.Host = host;
                    config.Port = port;
                    config.Module = module;
                    LocalData.InsertSafe(config);
                }
                else
                {
                    config.Host = host;
                    config.Port = port;
                    config.Module = module;
                    LocalData.UpdateSafe(config);
                }

				Printer.PrintDiagnostics("Updating remote \"{0}\" to {1}", url);
                LocalData.Commit();

                return true;
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                Printer.PrintError("Couldn't set remote: {0}", e);
                return false;
            }
        }

        internal static Area InitRemote(DirectoryInfo workingDir, ClonePayload clonePack, bool skipContainmentCheck = false)
        {
            Area ws = CreateWorkspace(workingDir, skipContainmentCheck);
            if (!ws.Init(null, clonePack))
                throw new Exception("Couldn't initialize versionr.");
            return ws;
        }

        public List<RemoteConfig> GetRemotes()
        {
            return LocalData.Query<RemoteConfig>("SELECT * FROM RemoteConfig");
        }
        public RemoteConfig GetRemote(string name)
        {
            Printer.PrintDiagnostics("Trying to find remote with name \"{0}\"", name);
            return LocalData.Find<RemoteConfig>(x => x.Name == name);
        }
        public void ClearRemotes()
        {
            LocalData.DeleteAll<RemoteConfig>();
        }

        public List<Branch> Branches
        {
            get
            {
                return Database.Table<Objects.Branch>().ToList();
            }
        }

        public Objects.Version Version
        {
            get
            {
                try
                {
                    return Database.Version;
                }
                catch
                {
                    Printer.PrintError("#e#Error:## Unable to locate version {0}, switching local copy to initial revision.", LocalData.Workspace.Tip);
                    var ws = LocalData.Workspace;
                    ws.Tip = Database.Domain;
                    LocalData.Update(ws);
                    return Database.Version;
                }
            }
        }

        public List<Objects.Version> History
        {
            get
            {
                return Database.History;
            }
        }

        IEnumerable<Objects.Version> GetHistoryChunked(Objects.Version version, int? limit)
        {
            int remaining = limit.HasValue ? limit.Value : int.MaxValue;
            int chunkCount = 256;
            Objects.Version top = version;
            bool first = true;
            while (remaining > 0)
            {
                int inChunk = Math.Min(chunkCount, remaining);
                List<Objects.Version> block = Database.GetHistory(top, inChunk);
                if (block.Count == 0 || (block.Count == 1 && !first))
                    yield break;
                for (int i = first ? 0 : 1; i < block.Count; i++)
                {
                    yield return block[i];
                }
                first = false;
                top = block[block.Count - 1];
                remaining -= inChunk;
            }
        }

        public List<Tuple<Objects.Version, int>> GetLogicalHistorySequenced(Objects.Version version, bool followBranches, bool showMerges, int? limit = null, HashSet<Guid> excludes = null)
        {
            int sequence = 0;
            return GetLogicalHistory(version, followBranches, showMerges, limit, excludes, ref sequence);
        }

        public List<Objects.Version> GetLogicalHistory(Objects.Version version, bool followBranches, bool showMerges, int? limit = null, HashSet<Guid> excludes = null)
        {
            int sequence = 0;
            return GetLogicalHistory(version, followBranches, showMerges, limit, excludes, ref sequence).Select(x => x.Item1).ToList();
        }

        internal List<Tuple<Objects.Version, int>> GetLogicalHistory(Objects.Version version, bool followBranches, bool showMerges, int? limit, HashSet<Guid> excludes, ref int sequence)
        {
            var versions = GetHistoryChunked(version, limit);
            List<Objects.Version> versionsToCheck = new List<Objects.Version>();
            List<Tuple<Objects.Version, int>> results = new List<Tuple<Objects.Version, int>>();
            HashSet<Guid> primaryLine = new HashSet<Guid>();
            HashSet<Guid> addedLine = new HashSet<Guid>();
            foreach (var x in versions)
            {
                if (excludes == null || !excludes.Contains(x.ID))
                {
                    versionsToCheck.Add(x);
                    primaryLine.Add(x.ID);
                }
                else
                    break;
            }
            int? seq = null;
            foreach (var x in versionsToCheck)
            {
                if (excludes != null && excludes.Contains(x.ID))
                    continue;
                var merges = Database.GetMergeInfo(x.ID);
                bool rebased = false;
                bool automerged = false;
                bool merged = false;
                bool added = false;
                if (excludes != null)
                    excludes.Add(x.ID);
                foreach (var y in merges)
                {
                    if (y.Type == MergeType.Rebase)
                        rebased = true;
                    if (y.Type == MergeType.Automatic)
                        automerged = true;
                    merged = true;
                    if ((showMerges || (!rebased && !followBranches)) && !automerged)
                    {
                        if (!added)
                        {
                            addedLine.Add(x.ID);
                            if (seq == null)
                                seq = sequence++;
                            results.Add(new Tuple<Objects.Version, int>(x, seq.Value));
                        }
                        added = true;
                    }
                    var mergedVersion = GetVersion(y.SourceVersion);
                    if ((mergedVersion.Branch == x.Branch || followBranches) && !rebased)
                    {
                        // automerge or manual reconcile
                        var mergedHistory = GetLogicalHistory(mergedVersion, followBranches, showMerges, limit, excludes != null ? excludes : primaryLine, ref sequence);
                        foreach (var z in mergedHistory)
                        {
                            if (!addedLine.Contains(z.Item1.ID))
                            {
                                addedLine.Add(z.Item1.ID);
                                primaryLine.Add(z.Item1.ID);
                                if (excludes != null)
                                    excludes.Add(z.Item1.ID);
                                results.Add(z);
                            }
                            else
                                break;
                        }
                    }
                }
                if (!merged)
                {
                    addedLine.Add(x.ID);
                    if (seq == null)
                        seq = sequence++;
                    results.Add(new Tuple<Objects.Version, int>(x, seq.Value));
                }
            }
            var ordered = results.OrderByDescending(x => x.Item1.Timestamp);
            if (limit == null)
                return ordered.ToList();
            else return ordered.Take(limit.Value).ToList();
        }

        public List<Objects.Version> GetHistory(Objects.Version version, int? limit = null)
        {
            return Database.GetHistory(version, limit);
        }

        public static string CoreVersion
        {
            get
            {
                return "v1.2.1";
            }
        }

        public static Tuple<string, string>[] ComponentVersions
        {
            get
            {
                return new Tuple<string, string>[]
                {
                    WorkspaceDB.ComponentVersionInfo,
                    LocalDB.ComponentVersionInfo,
                    Versionr.ObjectStore.StandardObjectStore.ComponentVersionInfo,
                    SharedNetwork.ComponentVersionInfo,
                };
            }
        }

        public bool RemoveHead(Head x)
        {
            Database.BeginTransaction();
            try
            {
                Database.Table<Objects.Head>().Delete(y => y.Id == x.Id);
                Database.Commit();
                return true;
            }
            catch (Exception e)
            {
                Printer.PrintError(e.ToString());
                Database.Rollback();
                return false;
            }
        }

        internal FileTimestamp GetReferenceTime(string canonicalName)
        {
            lock (FileTimeCache)
            {
                FileTimestamp result;
                if (FileTimeCache.TryGetValue(canonicalName, out result))
                    return result;
                return new FileTimestamp() { CanonicalName = canonicalName, DataIdentifier = string.Empty, LastSeenTime = DateTime.MinValue };
            }
        }

        public void UpdateReferenceTime(DateTime utcNow)
        {
            LocalData.WorkspaceReferenceTime = utcNow;
        }

        public List<Objects.Branch> GetBranchByName(string name)
        {
            return Database.Table<Objects.Branch>().Where(x => x.Terminus == null).ToList().Where(x => x.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)).ToList();
        }

        public Objects.Branch CurrentBranch
        {
            get
            {
                return Database.Branch;
            }
        }

        public int DatabaseVersion
        {
            get
            {
                return Database.Format.InternalFormat;
            }
        }

        public Area(DirectoryInfo adminFolder)
        {
            Utilities.MultiArchPInvoke.BindDLLs();
            AdministrationFolder = adminFolder;
            RootDirectory = new System.IO.DirectoryInfo(AdministrationFolder.Parent.GetFullNameWithCorrectCase());
            AdministrationFolder.Create();
            AdministrationFolder.Attributes = FileAttributes.Directory | FileAttributes.Hidden;
        }

        public bool ImportDB()
        {
            try
            {
                LocalData = LocalDB.Create(LocalMetadataFile.FullName);
                Database = WorkspaceDB.Create(LocalData, MetadataFile.FullName);
                ObjectStore = new ObjectStore.StandardObjectStore();
                ObjectStore.Create(this);
                ImportRoot();
                return true;
            }
            catch (Exception e)
            {
                Printer.PrintError(e.ToString());
                return false;
            }
        }

        private bool Init(string branchName = null, ClonePayload remote = null)
        {
            try
            {
                if (branchName != null && remote != null)
                    throw new Exception("Can't initialize a repository with a specific root branch name and a clone payload.");
                AdministrationFolder.Create();
                AdministrationFolder.Attributes = FileAttributes.Directory | FileAttributes.Hidden;
                LocalData = LocalDB.Create(LocalMetadataFile.FullName);
                Database = WorkspaceDB.Create(LocalData, MetadataFile.FullName);
                ObjectStore = new ObjectStore.StandardObjectStore();
                ObjectStore.Create(this);
                if (remote == null)
                    PopulateDefaults(branchName);
                else
                    PopulateRemoteRoot(remote);
                return true;
            }
            catch (Exception e)
            {
                Printer.PrintError(e.ToString());
                return false;
            }
        }

        private void ImportRoot()
        {
            Printer.PrintDiagnostics("Importing root from database...");
            LocalState.Configuration config = LocalData.Configuration;

            LocalState.Workspace ws = LocalState.Workspace.Create();

            Guid initialRevision = Database.Domain;

            Objects.Version version = GetVersion(initialRevision);
            Objects.Branch branch = GetBranch(version.Branch);

            ws.Name = Username;
            ws.Branch = branch.ID;
            ws.Tip = version.ID;
            config.WorkspaceID = ws.ID;
            ws.Domain = initialRevision;

            Printer.PrintDiagnostics("Starting DB transaction.");
            LocalData.BeginTransaction();
            try
            {
                LocalData.InsertSafe(ws);
                LocalData.UpdateSafe(config);
                LocalData.Commit();
                Printer.PrintDiagnostics("Finished.");
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Couldn't initialize repository!", e);
            }
        }

        private void PopulateRemoteRoot(ClonePayload remote)
        {
            Printer.PrintDiagnostics("Cloning root state...");
            LocalState.Configuration config = LocalData.Configuration;

            LocalState.Workspace ws = LocalState.Workspace.Create();

            Objects.Branch branch = remote.InitialBranch;
            Objects.Version version = remote.RootVersion;
            Objects.Snapshot snapshot = new Objects.Snapshot();
            Objects.Head head = new Objects.Head();
            Objects.Domain domain = new Objects.Domain();

            Printer.PrintDiagnostics("Imported branch \"{0}\", ID: {1}.", branch.Name, branch.ID);
            Printer.PrintDiagnostics("Imported version {0}", version.ID);

            domain.InitialRevision = version.ID;
            ws.Name = Username;

            head.Branch = branch.ID;
            head.Version = version.ID;
            ws.Branch = branch.ID;
            ws.Tip = version.ID;
            config.WorkspaceID = ws.ID;
            ws.Domain = domain.InitialRevision;

            Printer.PrintDiagnostics("Starting DB transaction.");
            LocalData.BeginTransaction();
            try
            {
                Database.BeginTransaction();
                try
                {
                    Database.InsertSafe(snapshot);
                    version.AlterationList = snapshot.Id;
                    version.Snapshot = snapshot.Id;
                    Database.InsertSafe(version);
                    Database.InsertSafe(head);
                    Database.InsertSafe(domain);
                    Database.InsertSafe(branch);
                    Database.InsertSafe(snapshot);
                    Database.Commit();
                }
                catch (Exception e)
                {
                    Database.Rollback();
                    throw new Exception("Couldn't initialize repository!", e);
                }
                LocalData.InsertSafe(ws);
                LocalData.UpdateSafe(config);
                LocalData.Commit();
                Printer.PrintDiagnostics("Finished.");
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Couldn't initialize repository!", e);
            }
        }

        internal bool ImportBranchJournal(SharedNetwork.SharedNetworkInfo info, bool interactive)
        {
            List<BranchJournalPack> receivedBranchJournals = info.ReceivedBranchJournals;
            if (receivedBranchJournals.Count == 0)
                return true;
            BranchJournal localTip = GetBranchJournalTip();
            int count = receivedBranchJournals.Count;
            HashSet<Guid> allParents = new HashSet<Guid>();
            foreach (var x in receivedBranchJournals)
            {
                if (x.Parents == null)
                    x.Parents = new List<Guid>();
                foreach (var y in x.Parents)
                    allParents.Add(y);
            }
            List<BranchJournal> localChanges = new List<BranchJournal>();
            Stack<BranchJournal> openList = new Stack<BranchJournal>();
            bool needsMerge = false;
            if (localTip != null)
            {
                needsMerge = !allParents.Contains(localTip.ID);
                openList.Push(localTip);
                while (openList.Count > 0)
                {
                    BranchJournal journal = openList.Pop();
                    if (allParents.Contains(journal.ID))
                        continue;
                    else
                    {
                        localChanges.Add(journal);
                        foreach (var y in GetBranchJournalParents(journal))
                            openList.Push(y);
                    }
                }
            }
            HashSet<Guid> processedList = new HashSet<Guid>();
            HashSet<Guid> missingList = new HashSet<Guid>();
            BranchJournal end = null;
            if (interactive)
                Printer.PrintMessage("Received #b#{0}## branch journal updates.", count);
            else
                Printer.PrintDiagnostics("Received {0} branch journal updates.", count);
            List<BranchJournal> conflicts = new List<BranchJournal>();
            bool passed = false;
            bool debug = false;
            while (count > 0)
            {
                passed = true;
                foreach (var x in receivedBranchJournals)
                {
                    if (processedList.Contains(x.Payload.ID))
                        continue;
                    if (debug)
                        Printer.PrintDiagnostics("Processing {0}, {1} parents", x.Payload.ID.ToString().Substring(0, 8), x.Parents.Count);
                    bool accept = true;
                    foreach (var y in x.Parents)
                    {
                        if (!processedList.Contains(y))
                        {
                            if (missingList.Contains(y))
                            {
                                if (debug)
                                    Printer.PrintDiagnostics("Parent {0} in missing entry list.", y.ToString().Substring(0, 8));
                                accept = false;
                                break;
                            }
                            else
                            {
                                if (HasBranchJournal(y))
                                {
                                    if (debug)
                                        Printer.PrintDiagnostics("Parent {0} found!", y.ToString().Substring(0, 8));
                                    processedList.Add(y);
                                }
                                else
                                {
                                    if (debug)
                                        Printer.PrintDiagnostics("Parent {0} not found.", y.ToString().Substring(0, 8));
                                    missingList.Add(y);
                                    accept = false;
                                    break;
                                }
                            }
                        }
                    }
                    if (accept)
                    {
                        Printer.PrintDiagnostics("Accepted [{3}] {0}: {1}, {2}", x.Payload.Type, x.Payload.Branch.ToString().Substring(0, 8), x.Payload.Operand, x.Payload.ID.ToString().Substring(0, 8));
                        count--;
                        missingList.Remove(x.Payload.ID);
                        processedList.Add(x.Payload.ID);
                        if (!ReplayBranchJournal(x.Payload, interactive, conflicts, info))
                            return false;
                        Database.Insert(x.Payload);

                        foreach (var y in x.Parents)
                        {
                            BranchJournalLink link = new BranchJournalLink();
                            link.Link = x.Payload.ID;
                            link.Parent = y;
                            Database.Insert(link);
                        }

                        end = x.Payload;
                        passed = false;
                    }
                }
                if (passed)
                {
                    throw new Exception("Error while importing branch journal!");
                }
            }
            if (conflicts.Count > 0)
                needsMerge = true;
            else if (needsMerge)
                conflicts.Add(new BranchJournal() { Type = BranchAlterationType.Merge, Branch = Guid.Empty });
            if (needsMerge && end != null)
            {
                foreach (var x in conflicts)
                {
                    BranchJournal merge = x;
                    merge.ID = Guid.NewGuid();

                    ReplayBranchJournal(merge, false, null, info);

                    Database.InsertSafe(merge);

                    BranchJournalLink link = new BranchJournalLink();
                    link.Link = merge.ID;
                    link.Parent = end.ID;
                    Database.InsertSafe(link);

                    if (localTip != null)
                    {
                        link = new BranchJournalLink();
                        link.Link = merge.ID;
                        link.Parent = localTip.ID;
                        Database.InsertSafe(link);
                        localTip = null;
                    }

                    end = merge;
                }
            }

            Database.BranchJournalTip = end.ID;

            return true;
        }

        private void PopulateDefaults(string branchName)
        {
            Printer.PrintDiagnostics("Creating initial state...");
            LocalState.Configuration config = LocalData.Configuration;

            LocalState.Workspace ws = LocalState.Workspace.Create();

            Objects.Branch branch = Objects.Branch.Create(branchName, null, null);
            Objects.Version version = Objects.Version.Create();
            Objects.Snapshot snapshot = new Objects.Snapshot();
            Objects.Head head = new Objects.Head();
            Objects.Domain domain = new Objects.Domain();

            Printer.PrintDiagnostics("Created branch \"{0}\", ID: {1}.", branch.Name, branch.ID);

            domain.InitialRevision = version.ID;
            ws.Name = Username;
            version.Parent = null;
            version.Timestamp = DateTime.UtcNow;
            version.Author = ws.Name;
            version.Message = "Autogenerated by Versionr.";

            head.Branch = branch.ID;
            head.Version = version.ID;
            ws.Branch = branch.ID;
            ws.Tip = version.ID;
            config.WorkspaceID = ws.ID;
            version.Branch = branch.ID;
            ws.Domain = domain.InitialRevision;

            Printer.PrintDiagnostics("Created initial state version {0}, message: \"{1}\".", version.ID, version.Message);
            Printer.PrintDiagnostics("Created head node to track branch {0} with version {1}.", branch.ID, version.ID);

            Printer.PrintDiagnostics("Starting DB transaction.");
            LocalData.BeginTransaction();
            try
            {
                Database.BeginTransaction();
                try
                {
                    Database.InsertSafe(snapshot);
                    version.AlterationList = snapshot.Id;
                    version.Snapshot = snapshot.Id;
                    Database.InsertSafe(version);
                    Database.InsertSafe(head);
                    Database.InsertSafe(domain);
                    Database.InsertSafe(branch);
                    Database.InsertSafe(snapshot);
                    Database.Commit();
                }
                catch (Exception e)
                {
                    Database.Rollback();
                    throw new Exception("Couldn't initialize repository!", e);
                }
                LocalData.InsertSafe(ws);
                LocalData.UpdateSafe(config);
                LocalData.Commit();
                Printer.PrintDiagnostics("Finished.");
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Couldn't initialize repository!", e);
            }
        }

        internal bool BackupDB(FileInfo fsInfo)
        {
            Printer.PrintDiagnostics("Running backup...");
            return Database.Backup(fsInfo, (int pages, int total) =>
            {
                Printer.PrintDiagnostics("Backup progress: ({0}/{1}) pages remaining.", pages, total);
            });
        }

        public List<Objects.Alteration> GetAlterations(Objects.Version x)
        {
            return Database.GetAlterationsForVersion(x);
        }

        public Objects.Record GetRecord(long id)
        {
            Objects.Record rec = Database.Find<Objects.Record>(id);
            if (rec != null)
                rec.CanonicalName = Database.Get<Objects.ObjectName>(rec.CanonicalNameId).CanonicalName;
            return rec;
        }

        internal void AddHeadNoCommit(Head x)
        {
            Database.InsertSafe(x);
        }

        internal long GetTransmissionLength(Record record)
        {
            return ObjectStore.GetTransmissionLength(record);
        }

        internal bool TransmitRecordData(Record record, Func<byte[], int, bool, bool> sender, byte[] scratchBuffer, Action beginTransmission = null)
        {
            return ObjectStore.TransmitRecordData(record, sender, scratchBuffer, beginTransmission);
        }

        Dictionary<string, long?> KnownCanonicalNames = new Dictionary<string, long?>();

        internal Record LocateRecord(Record newRecord)
        {
            long? present = null;
            if (!KnownCanonicalNames.TryGetValue(newRecord.CanonicalName, out present))
            {
                ObjectName canonicalNameId = Database.Query<ObjectName>("SELECT * FROM ObjectName WHERE CanonicalName IS ?", newRecord.CanonicalName).FirstOrDefault();
                if (canonicalNameId == null)
                {
                    KnownCanonicalNames[newRecord.CanonicalName] = null;
                    return null;
                }
                KnownCanonicalNames[newRecord.CanonicalName] = canonicalNameId.NameId;
                present = canonicalNameId.NameId;
            }
            if (!present.HasValue)
                return null;
            var results = Database.Table<Objects.Record>().Where(x => x.Fingerprint == newRecord.Fingerprint && x.Size == newRecord.Size && x.ModificationTime == newRecord.ModificationTime && x.CanonicalNameId == present.Value).ToList();
            foreach (var x in results)
            {
                if (newRecord.UniqueIdentifier == x.UniqueIdentifier)
                    return x;
            }
            return null;
        }

        internal bool ImportBranch(Branch x)
        {
            lock (this)
            {
                Database.BeginTransaction();
                try
                {
                    try
                    {
                        ImportBranchNoCommit(x);
                        Database.Commit();
                        return true;
                    }
                    catch
                    {
                        if (GetBranch(x.ID) == null)
                            throw;
                        Printer.PrintDiagnostics("Warning - branch {0} has already been imported!", x.ID);
                        Database.Rollback();
                        return false;
                    }
                }
                catch
                {
                    Database.Rollback();
                    throw;
                }
            }
        }

        public void ImportBranchNoCommit(Branch x)
        {
            Database.InsertSafe(x);
        }

        internal void ImportVersionNoCommit(SharedNetwork.SharedNetworkInfo clientInfo, VersionInfo x, bool mapRecords)
        {
            Printer.PrintDiagnostics("Importing version {0}", x.Version.ID);
            Objects.Snapshot alterationLink = new Snapshot();
            Database.InsertSafe(alterationLink);
            x.Version.Published = true;
            x.Version.AlterationList = alterationLink.Id;
            x.Version.Snapshot = null;

            Database.InsertSafe(x.Version);
            if (x.MergeInfos != null)
            {
                foreach (var y in x.MergeInfos)
                    Database.InsertSafe(y);
            }
            if (x.Alterations != null)
            {
                foreach (var z in x.Alterations)
                {
                    var alteration = new Objects.Alteration();
                    alteration.Owner = alterationLink.Id;
                    alteration.Type = z.Alteration;
                    if (z.NewRecord != null)
                        alteration.NewRecord = mapRecords ? clientInfo.LocalRecordMap[z.NewRecord.Id].Id : z.NewRecord.Id;
                    if (z.PriorRecord != null)
                        alteration.PriorRecord = mapRecords ? clientInfo.LocalRecordMap[z.PriorRecord.Id].Id : z.PriorRecord.Id;

                    Database.InsertSafe(alteration);
                }
            }
        }

        internal bool HasObjectDataDirect(string x)
        {
            List<string> ignored;
            return ObjectStore.HasDataDirect(x, out ignored);
        }

        public void CommitDatabaseTransaction()
        {
            Database.Commit();
        }

        public int ExecuteDatabaseSQL(string sql)
        {
            return Database.Execute(sql);
        }

        internal void ImportRecordNoCommit(Record rec, bool checkduplicates = true)
        {
            if (checkduplicates)
            {
                Record r1 = LocateRecord(rec);
                if (r1 != null)
                {
                    rec.Id = r1.Id;
                    return;
                }
            }
            long? cnId = null;
            KnownCanonicalNames.TryGetValue(rec.CanonicalName, out cnId);
            if (!cnId.HasValue)
            {
                Retry:
                ObjectName canonicalNameId = Database.Query<ObjectName>("SELECT * FROM ObjectName WHERE CanonicalName IS ?", rec.CanonicalName).FirstOrDefault();
                if (canonicalNameId == null)
                {
                    canonicalNameId = new ObjectName() { CanonicalName = rec.CanonicalName };
                    if (!Database.InsertSafe(canonicalNameId))
                        goto Retry;
                }
                KnownCanonicalNames[rec.CanonicalName] = canonicalNameId.NameId;
                cnId = canonicalNameId.NameId;
            }
            rec.CanonicalNameId = cnId.Value;

            Database.InsertSafe(rec);
        }

        public void RollbackDatabaseTransaction()
        {
            Database.Rollback();
        }

        public void BeginDatabaseTransaction()
        {
            Database.BeginTransaction();
        }

        internal void ImportRecordData(Versionr.ObjectStore.ObjectStoreTransaction transaction, string directName, Stream data, out string dependency)
        {
            if (!ObjectStore.ReceiveRecordData(transaction, directName, data, out dependency))
                throw new Exception();
        }

        internal bool HasObjectData(Record rec)
        {
            List<string> ignored;
            return HasObjectData(rec, out ignored);
        }

        internal bool HasObjectData(Record rec, out List<string> requestedDataIdentifiers)
        {
            return ObjectStore.HasData(rec, out requestedDataIdentifiers);
        }

        internal void ImportHeadNoCommit(KeyValuePair<Guid, Head> x)
        {
            Objects.Branch branch = Database.Get<Branch>(x.Key);
            var heads = GetBranchHeads(branch);
            if (heads.Count > 1)
                throw new Exception("Multiple branch heads");
            if (heads.Count == 0 || heads[0].Version != x.Value.Version)
            {
                Printer.PrintDiagnostics("Updating head of branch {0} to version {1}", branch.Name, x.Value.Version);
                if (heads.Count == 0)
                    Database.InsertSafe(x.Value);
                else
                    Database.UpdateSafe(x.Value);
            }
        }

        public bool RecordChanges(Status status, IList<Status.StatusEntry> files, bool missing, bool interactive, Action<Status.StatusEntry, StatusCode, bool> callback = null)
        {
            List<LocalState.StageOperation> stageOps = new List<StageOperation>();

            HashSet<string> stagedPaths = new HashSet<string>();
            HashSet<string> removals = new HashSet<string>();
            foreach (var x in files)
            {
                if (x.Staged == false && (
                    x.Code == StatusCode.Unversioned ||
                    x.Code == StatusCode.Renamed ||
                    x.Code == StatusCode.Modified ||
                    x.Code == StatusCode.Copied ||
                    ((x.Code == StatusCode.Masked || x.Code == StatusCode.Missing) && missing)))
                {
                    stagedPaths.Add(x.CanonicalName);

                    if (x.Code == StatusCode.Masked || x.Code == StatusCode.Missing)
                    {
                        if (interactive)
                        {
                            Printer.PrintMessageSingleLine("Record #e#deletion## of #b#{0}##", x.VersionControlRecord.CanonicalName);
                            bool skip = false;
                            while (true)
                            {
                                Printer.PrintMessageSingleLine(" [(y)es, (n)o, (s)top]? ");
                                string input = System.Console.ReadLine();
                                if (input.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                                    break;
                                if (input.StartsWith("s", StringComparison.OrdinalIgnoreCase))
                                    goto End;
                                if (input.StartsWith("n", StringComparison.OrdinalIgnoreCase))
                                {
                                    skip = true;
                                    break;
                                }
                            }
                            if (skip)
                                continue;
                        }

                        if (callback != null)
                            callback(x, StatusCode.Deleted, false);
                        //Printer.PrintMessage("Recorded deletion: #b#{0}##", x.VersionControlRecord.CanonicalName);
                        stageOps.Add(new StageOperation() { Operand1 = x.VersionControlRecord.CanonicalName, Type = StageOperationType.Remove });
                        removals.Add(x.VersionControlRecord.CanonicalName);
                    }
                    else
                    {
                        if (interactive)
                        {
                            Printer.PrintMessageSingleLine("Record {1} of #b#{0}##", x.FilesystemEntry.CanonicalName, x.Code == StatusCode.Modified ? "#s#update##" : "#w#addition##");
                            bool skip = false;
                            while (true)
                            {
                                Printer.PrintMessageSingleLine(" [(y)es, (n)o, (s)top]? ");
                                string input = System.Console.ReadLine();
                                if (input.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                                    break;
                                if (input.StartsWith("s", StringComparison.OrdinalIgnoreCase))
                                    goto End;
                                if (input.StartsWith("n", StringComparison.OrdinalIgnoreCase))
                                {
                                    skip = true;
                                    break;
                                }
                            }
                            if (skip)
                                continue;
                        }

                        if (callback != null)
                            callback(x, x.Code == StatusCode.Unversioned ? StatusCode.Added : x.Code, false);
                        //Printer.PrintMessage("Recorded: #b#{0}##", x.FilesystemEntry.CanonicalName);
                        stageOps.Add(new StageOperation() { Operand1 = x.FilesystemEntry.CanonicalName, Type = StageOperationType.Add });
                    }
                }
            }
            End:
            // add parent directories
            foreach (var x in stageOps.ToArray())
            {
                if (x.Type == StageOperationType.Add)
                {
                    Status.StatusEntry entry = status.Map[x.Operand1];
                    while (entry.FilesystemEntry != null && entry.FilesystemEntry.Parent != null)
                    {
                        entry = status.Map[entry.FilesystemEntry.Parent.CanonicalName];
                        if (entry.Staged == false && (
                            entry.Code == StatusCode.Added ||
                            entry.Code == StatusCode.Unversioned))
                        {
                            if (!stagedPaths.Contains(entry.CanonicalName))
                            {
                                if (callback != null)
                                    callback(entry, entry.Code == StatusCode.Unversioned ? StatusCode.Added : entry.Code, true);
                                //Printer.PrintMessage("#q#Recorded (auto): #b#{0}##", entry.CanonicalName);
                                stageOps.Add(new StageOperation() { Operand1 = entry.CanonicalName, Type = StageOperationType.Add });
                                stagedPaths.Add(entry.CanonicalName);
                            }
                        }
                    }
                }
                else if (x.Type == StageOperationType.Remove)
                {
                    Status.StatusEntry entry = status.Map[x.Operand1];
                    if (entry.IsDirectory)
                    {
                        foreach (var y in status.Elements)
                        {
                            if (y.CanonicalName.StartsWith(entry.CanonicalName))
                            {
                                if (y.Code != StatusCode.Deleted && !removals.Contains(y.CanonicalName))
                                {
                                    if (callback != null)
                                        callback(entry, StatusCode.Deleted, true);
                                    Printer.PrintMessage("#x#Error:##\n  Can't stage removal of \"#b#{0}##\", obstructed by object \"#b#{1}##\". Remove contained objects first.", x.Operand1, y.CanonicalName);
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            if (stageOps.Count == 0)
            {
                Printer.PrintMessage("#w#Warning:##\n  No changes found to record.");
                return false;
            }
            Printer.PrintMessage("");
            Printer.PrintMessage("Recorded #b#{0}## objects.", stageOps.Count);
            LocalData.BeginTransaction();
            try
            {
                foreach (var x in stageOps)
                    LocalData.InsertSafe(x);
                LocalData.Commit();
                return true;
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Couldn't record changes to stage!", e);
            }
        }

        internal Record GetRecordFromIdentifier(string id)
        {
            int hyphen = id.LastIndexOf('-');
            string fingerprint = id.Substring(0, hyphen);
            long size = long.Parse(id.Substring(hyphen + 1));
            return Database.Table<Objects.Record>().Where(x => x.Fingerprint == fingerprint && x.Size == size).FirstOrDefault();
        }

        public IEnumerable<Objects.MergeInfo> GetMergeInfo(Guid iD)
        {
            return Database.GetMergeInfo(iD);
        }
        
		public void LoadDirectives()
		{
            string error;

            // Load .vrmeta
            Directives directives = DirectivesUtils.LoadVRMeta(this, out error);
            Directives = (directives != null) ? directives : new Directives();

            // Load global .vruser
            directives = DirectivesUtils.LoadGlobalVRUser(out error);
            if (directives != null)
                Directives.Merge(directives);

            // Load .vruser
            directives = DirectivesUtils.LoadVRUser(this, out error);
            if (directives != null)
                Directives.Merge(directives);
		}
        public T LoadConfigurationElement<T>(string v)
            where T : new()
        {
			if (Configuration == null)
				return new T();
            var element = Configuration[v];
            if (element == null)
                return new T();
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(element.ToString());
        }
        private bool Load(bool headless = false)
        {
            try
            {
                if (!MetadataFile.Exists)
                    return false;
                LocalData = LocalDB.Open(LocalMetadataFile.FullName);
                // Load metadata DB
                if (!LocalData.Valid)
                    return false;
                Database = WorkspaceDB.Open(LocalData, MetadataFile.FullName);
                if (!Database.Valid)
                    return false;
                if (LocalData.Domain != Database.Domain)
                    return false;

                if (LocalData.RefreshLocalTimes)
                    RefreshLocalTimes();

                if (!headless)
                    FileTimeCache = LocalData.LoadFileTimes();

                ReferenceTime = LocalData.WorkspaceReferenceTime;

                if (!headless)
                    LoadDirectives();

                ObjectStore = new ObjectStore.StandardObjectStore();
                if (!ObjectStore.Open(this))
                    return false;

                return true;
            }
            catch (Exception e)
            {
                Printer.PrintError(e.ToString());
                return false;
            }
        }

        public Objects.Version GetVersion(Guid value)
        {
            return Database.Find<Objects.Version>(value);
        }

        internal VersionInfo MergeRemote(Objects.Version localVersion, Guid remoteVersionID, SharedNetwork.SharedNetworkInfo clientInfo, out string error, bool clientMode = false)
        {
            Objects.Version remoteVersion = GetLocalOrRemoteVersion(remoteVersionID, clientInfo);
            Objects.Version parent = GetCommonParentForRemote(localVersion, remoteVersionID, clientInfo);

            var records = Database.GetRecords(localVersion);
            var foreignRecords = GetRemoteRecords(remoteVersionID, clientInfo);
            var parentRecords = Database.GetRecords(parent);
            List<FusedAlteration> alterations = new List<FusedAlteration>();

            Dictionary<string, Record> foreignLookup = new Dictionary<string, Record>();
            foreach (var x in foreignRecords)
                foreignLookup[x.Item1.CanonicalName] = x.Item1;
            Dictionary<string, Record> localLookup = new Dictionary<string, Record>();
            foreach (var x in records)
                localLookup[x.CanonicalName] = x;
            Dictionary<string, Record> parentLookup = new Dictionary<string, Record>();
            foreach (var x in parentRecords)
                parentLookup[x.CanonicalName] = x;

            foreach (var x in foreignRecords)
            {
                Objects.Record parentRecord = null;
                Objects.Record localRecord = null;
                parentLookup.TryGetValue(x.Item1.CanonicalName, out parentRecord);
                localLookup.TryGetValue(x.Item1.CanonicalName, out localRecord);

                if (localRecord == null)
                {
                    if (parentRecord == null)
                    {
                        alterations.Add(new FusedAlteration() { Alteration = AlterationType.Add, NewRecord = x.Item1 });
                    }
                    else
                    {
                        // Removed locally
                        if (parentRecord.DataEquals(x.Item1))
                        {
                            // this is fine, we removed it in our branch
                        }
                        else
                        {
                            error = string.Format("Object \"{0}\" changed in pushed branch and removed from remote - requires full merge.", x.Item1.CanonicalName);
                            return null;
                        }
                    }
                }
                else
                {
                    if (localRecord.DataEquals(x.Item1))
                    {
                        // all good, same data in both places
                    }
                    else if (parentRecord == null)
                    {
                        // two additions = conflict
                        error = string.Format("Object \"{0}\" added in pushed branch and remote - requires full merge.", x.Item1.CanonicalName);
                        Printer.PrintWarning("Object \"{0}\" requires full merge.", x.Item1.CanonicalName);
                        return null;
                    }
                    else
                    {
                        if (localRecord.DataEquals(parentRecord))
                        {
                            alterations.Add(new FusedAlteration() { Alteration = AlterationType.Update, NewRecord = x.Item1, PriorRecord = localRecord });
                        }
                        else if (parentRecord.DataEquals(x.Item1))
                        {
                            // modified locally
                        }
                        else
                        {
                            error = string.Format("Object \"{0}\" changed on pushed branch and remote - requires full merge.", x.Item1.CanonicalName);
                            Printer.PrintWarning("Object \"{0}\" requires full merge.", x.Item1.CanonicalName);
                            return null;
                        }
                    }
                }
            }
            foreach (var x in parentRecords)
            {
                Objects.Record foreignRecord = null;
                Objects.Record localRecord = null;
                foreignLookup.TryGetValue(x.CanonicalName, out foreignRecord);
                localLookup.TryGetValue(x.CanonicalName, out localRecord);

                if (foreignRecord == null)
                {
                    // deleted by branch
                    if (localRecord != null)
                    {
                        if (localRecord.DataEquals(x))
                        {
                            alterations.Add(new FusedAlteration() { Alteration = AlterationType.Delete, NewRecord = null, PriorRecord = localRecord });
                        }
                        else
                        {
                            error = string.Format("Object \"{0}\" removed in pushed branch and changed on server head - requires full merge.", x.CanonicalName);
                            Printer.PrintWarning("Object \"{0}\" removed remotely and changed locally - requires full merge.", x.CanonicalName);
                            return null;
                        }
                    }
                }
            }
            error = string.Empty;
            Objects.Version resultVersion = new Objects.Version()
            {
                ID = Guid.NewGuid(),
                Author = clientMode ? localVersion.Author : remoteVersion.Author,
                Branch = localVersion.Branch,
                Message = string.Format("Automatic merge of {0}.", remoteVersion.ID),
                Parent = localVersion.ID,
                Published = true,
                Timestamp = DateTime.UtcNow
            };
            return new VersionInfo()
            {
                Alterations = alterations.ToArray(),
                MergeInfos = new MergeInfo[1] { new MergeInfo() { DestinationVersion = resultVersion.ID, SourceVersion = remoteVersionID, Type = MergeType.Automatic } },
                Version = resultVersion
            };
        }

        private List<Tuple<Objects.Record, bool>> GetRemoteRecords(Guid remoteVersionID, SharedNetwork.SharedNetworkInfo clientInfo)
        {
            Stack<Network.FusedAlteration> remoteAlterations = new Stack<Network.FusedAlteration>();
            while (true)
            {
                VersionInfo info = clientInfo.PushedVersions.Where(x => x.Version.ID == remoteVersionID).FirstOrDefault();
                if (info == null)
                {
                    Objects.Version localVersion = GetVersion(remoteVersionID);
                    var recordsBase = Database.GetRecords(localVersion);
                    List<Tuple<Objects.Record, bool>> records = new List<Tuple<Record, bool>>(recordsBase.Select(x => new Tuple<Objects.Record, bool>(x, false)));
                    while (remoteAlterations.Count > 0)
                    {
                        Network.FusedAlteration alteration = remoteAlterations.Pop();
                        switch (alteration.Alteration)
                        {
                            case AlterationType.Add:
                            case AlterationType.Copy:
                                records.Add(new Tuple<Record, bool>(clientInfo.LocalRecordMap[alteration.NewRecord.Id], true));
                                break;
                            case AlterationType.Delete:
                                {
                                    long removedID = clientInfo.LocalRecordMap[alteration.PriorRecord.Id].Id;
                                    bool found = false;
                                    for (int i = 0; i < records.Count; i++)
                                    {
                                        if (records[i].Item1.Id == removedID)
                                        {
                                            records.RemoveAt(i);
                                            found = true;
                                            break;
                                        }
                                    }
                                    if (!found)
                                        throw new Exception("Couldn't consolidate changelists.");
                                    break;
                                }
                            case AlterationType.Move:
                            case AlterationType.Update:
                                {
                                    long removedID = clientInfo.LocalRecordMap[alteration.PriorRecord.Id].Id;
                                    bool found = false;
                                    for (int i = 0; i < records.Count; i++)
                                    {
                                        if (records[i].Item1.Id == removedID)
                                        {
                                            records.RemoveAt(i);
                                            found = true;
                                            break;
                                        }
                                    }
                                    if (!found)
                                        throw new Exception("Couldn't consolidate changelists.");
                                    records.Add(new Tuple<Record, bool>(clientInfo.LocalRecordMap[alteration.NewRecord.Id], true));
                                    break;
                                }
                        }
                    }
                    return records;
                }
                else
                {
                    foreach (var x in info.Alterations)
                        remoteAlterations.Push(x);
                }
                remoteVersionID = info.Version.Parent.Value;
            }
        }

        private Objects.Version GetCommonParentForRemote(Objects.Version localVersion, Guid remoteVersionID, SharedNetwork.SharedNetworkInfo clientInfo)
        {
            Dictionary<Guid, int> foreignGraph = GetParentGraphRemote(remoteVersionID, clientInfo);
            Dictionary<Guid, int> localGraph = GetParentGraphRemote(localVersion.ID, clientInfo);
            var shared = new List<KeyValuePair<Guid, int>>(foreignGraph.Where(x => localGraph.ContainsKey(x.Key)).OrderBy(x => x.Value));
            if (shared.Count == 0)
                return null;
            return Database.Find<Objects.Version>(shared[0].Key);
        }

        private Dictionary<Guid, int> GetParentGraphRemote(Guid versionID, SharedNetwork.SharedNetworkInfo clientInfo)
        {
            Printer.PrintDiagnostics("Getting parent graph for version {0}", versionID);
            Stack<Tuple<Objects.Version, int>> openNodes = new Stack<Tuple<Objects.Version, int>>();
            Objects.Version mergeVersion = GetLocalOrRemoteVersion(versionID, clientInfo);
            openNodes.Push(new Tuple<Objects.Version, int>(mergeVersion, 0));
            Dictionary<Guid, int> result = new Dictionary<Guid, int>();
            while (openNodes.Count > 0)
            {
                var currentNodeData = openNodes.Pop();
                Objects.Version currentNode = currentNodeData.Item1;

                int distance = 0;
                if (result.TryGetValue(currentNode.ID, out distance))
                {
                    if (distance > currentNodeData.Item2)
                        result[currentNode.ID] = currentNodeData.Item2;
                    continue;
                }
                result[currentNode.ID] = currentNodeData.Item2;

                if (currentNode.Parent.HasValue && !result.ContainsKey(currentNode.Parent.Value))
                    openNodes.Push(new Tuple<Objects.Version, int>(GetLocalOrRemoteVersion(currentNode.Parent.Value, clientInfo), currentNodeData.Item2 + 1));
                foreach (var x in Database.GetMergeInfo(currentNode.ID))
                {
                    if (!result.ContainsKey(x.SourceVersion))
                        openNodes.Push(new Tuple<Objects.Version, int>(GetLocalOrRemoteVersion(x.SourceVersion, clientInfo), currentNodeData.Item2 + 1));
                }
            }
            return result;
        }

        internal Objects.Version GetLocalOrRemoteVersion(Guid versionID, SharedNetwork.SharedNetworkInfo clientInfo)
        {
            Objects.Version v = Database.Find<Objects.Version>(x => x.ID == versionID);
            if (v == null)
                v = clientInfo.PushedVersions.Where(x => x.Version.ID == versionID).Select(x => x.Version).FirstOrDefault();
            return v;
        }

        class TransientMergeObject
        {
            public Record Record { get; set; }
            public FileInfo TemporaryFile { get; set; }
            public string Fingerprint
            {
                get
                {
                    if (m_Fingerprint == null)
                    {
                        if (TemporaryFile != null)
                        {
                            m_Fingerprint = Entry.CheckHash(TemporaryFile);
                        }
                        else
                            m_Fingerprint = Record.Fingerprint;
                    }
                    return m_Fingerprint;
                }
            }
            public long Length
            {
                get
                {
                    if (!m_Length.HasValue)
                    {
                        if (TemporaryFile != null)
                        {
                            while (true)
                            {
                                try
                                {
                                    m_Length = TemporaryFile.Length;
                                    break;
                                }
                                catch
                                {
                                    if (System.IO.File.Exists(TemporaryFile.FullName))
                                        TemporaryFile = new System.IO.FileInfo(TemporaryFile.FullName);
                                    else
                                        throw;
                                }
                            }
                        }
                        else
                            m_Length = Record.Size;
                    }
                    return m_Length.Value;
                }
            }
            public string CanonicalName { get; set; }
            string m_Fingerprint;
            long? m_Length;

            public bool DataEquals(Record r)
            {
                return r.Size == Length && Fingerprint == r.Fingerprint;
            }

            internal bool DataEquals(Status.StatusEntry localObject)
            {
                if (localObject.FilesystemEntry != null)
                {
                    if (localObject.FilesystemEntry.IsDirectory && Fingerprint == localObject.CanonicalName)
                        return true;
                    return localObject.Length == Length && localObject.Hash == Fingerprint;
                }
                return false;
            }
        }

        public class MergeSpecialOptions
        {
            public bool MetadataOnly { get; set; }
            public bool IgnoreMergeParents { get; set; }
            public bool Reintegrate { get; set; }
            public bool AllowRecursiveMerge { get; set; }
            public enum ResolutionSystem
            {
                Normal,
                Theirs,
                Mine
            };

            public ResolutionSystem ResolutionStrategy { get; set; }

            public MergeSpecialOptions()
            {
                MetadataOnly = false;
                IgnoreMergeParents = false;
                Reintegrate = false;
                AllowRecursiveMerge = true;
                ResolutionStrategy = ResolutionSystem.Normal;
            }

            internal void Validate()
            {
                if (ResolutionStrategy != ResolutionSystem.Normal)
                    AllowRecursiveMerge = false;
                if (MetadataOnly)
                    AllowRecursiveMerge = false;
            }
        }
        public void Merge(string v, bool updateMode, MergeSpecialOptions options)
        {
            options.Validate();
            var conflicts = LocalData.StageOperations.Where(x => x.Type == StageOperationType.Conflict).ToList();
            if (conflicts.Count > 0)
            {
                Printer.PrintMessage("#e#Error:## Can't merge while pending conflicts are still present.\n#b#Conflicts:##");
                foreach (var x in conflicts)
                    Printer.PrintMessage(" {0}", x.Operand1);
                Printer.PrintMessage("\nResolve these conflicts and run the operation again.");
                return;
            }
            Objects.Version mergeVersion = null;
            Objects.Version parentVersion = null;
            Versionr.Status status = new Status(this, Database, LocalData, FileSnapshot, null, false, false);
            List<TransientMergeObject> parentData;
            string postUpdateMerge = null;
            Objects.Branch possibleBranch = null;
            if (!updateMode)
            {
                bool multiple;
                possibleBranch = GetBranchByPartialName(v, out multiple);
                if (possibleBranch != null && !multiple)
                {
                    Head head = GetBranchHead(possibleBranch);
                    mergeVersion = Database.Find<Objects.Version>(head.Version);
                }
                else
                {
                    mergeVersion = GetPartialVersion(v);
                }
                if (mergeVersion == null)
                    throw new Exception("Couldn't find version to merge from!");
                if (possibleBranch == null && options.Reintegrate)
                    throw new Exception("Can't reintegrate when merging a version and not a branch.");

                var parents = GetCommonParents(null, mergeVersion, options.IgnoreMergeParents);
                if (parents == null || parents.Count == 0)
                    throw new Exception("No common parent!");

                Objects.Version parent = null;
                Printer.PrintMessage("Starting merge:");
                Printer.PrintMessage(" - Local: {0} #b#\"{1}\"##", Database.Version.ID, GetBranch(Database.Version.Branch).Name);
                Printer.PrintMessage(" - Remote: {0} #b#\"{1}\"##", mergeVersion.ID, GetBranch(mergeVersion.Branch).Name);
                if (false && parents.Count == 2 && options.AllowRecursiveMerge)
                {
                    parents = GetCommonParents(GetVersion(parents[0].Key), GetVersion(parents[1].Key), options.IgnoreMergeParents);
                }
                if (parents.Count == 1 || !options.AllowRecursiveMerge)
                {
                    parent = GetVersion(parents[0].Key);
                    if (parent.ID == mergeVersion.ID)
                    {
                        Printer.PrintMessage("Merge information is already up to date.");
                        return;
                    }
                    Printer.PrintMessage(" - Parent: {0} #b#\"{1}\"##", parent.ID, GetBranch(parent.Branch).Name);
                    parentData = Database.GetRecords(parent).Select(x => new TransientMergeObject() { Record = x, CanonicalName = x.CanonicalName }).ToList();
                }
                else if (parents.Count == 2)
                {
                    Printer.PrintMessage(" - Parent: <virtual>");
                    // recursive merge
                    parentData = MergeCoreRecursive(GetVersion(parents[0].Key), GetVersion(parents[1].Key));
                }
                else
                {
                    Printer.PrintMessage("Recursive merge is sad, do a normal merge instead :(");
                    // complicated recursive merge
                    throw new Exception();
                }
            }
            else
            {
                parentVersion = Version;
                parentData = Database.GetRecords(parentVersion).Select(x => new TransientMergeObject() { Record = x, CanonicalName = x.CanonicalName }).ToList();
                List<Objects.Version> updateHeads = new List<Objects.Version>();
                foreach (var x in GetBranchHeads(CurrentBranch))
                {
                    updateHeads.Add(GetVersion(x.Version));
                }

                if (updateHeads.Count == 1)
                    mergeVersion = updateHeads[0];
                else if (updateHeads.Count == 0)
                    mergeVersion = GetVersion(CurrentBranch.Terminus.Value);
                else
                {
                    if (updateHeads.Count != 2)
                    {
                        Printer.PrintMessage("Branch has {0} heads. Merge must be done in several phases.", updateHeads.Count);
                    }
                    // choose the best base version
                    if (updateHeads.Any(x => x.ID == parentVersion.ID))
                    {
                        // merge extra head into current version
                        Merge(updateHeads.First(x => x.ID != parentVersion.ID).ID.ToString(), false, options);
                        return;
                    }
                    mergeVersion = null;
                    Objects.Version backup = null;
                    foreach (var x in updateHeads)
                    {
                        if (GetHistory(x).Any(y => y.ID == parentVersion.ID))
                        {
                            if (mergeVersion != null && GetHistory(x).Any(y => y.ID == mergeVersion.ID))
                                mergeVersion = x;
                            break;
                        }
                        if (x.Author == Username)
                        {
                            backup = x;
                        }
                    }
                    if (mergeVersion == null)
                        mergeVersion = backup;
                    if (mergeVersion == null)
                        mergeVersion = updateHeads[0];
                    foreach (var x in updateHeads)
                    {
                        if (x != mergeVersion)
                        {
                            postUpdateMerge = x.ID.ToString();
                            break;
                        }
                    }
                }

                if (mergeVersion.ID == parentVersion.ID)
                {
                    Printer.PrintMessage("Already up-to-date.");
                    return;
                }

                Printer.PrintMessage("Updating current vault:");
                Printer.PrintMessage(" - Old version: {0}", parentVersion.ID);
                Printer.PrintMessage(" - New version: {0}", mergeVersion.ID);
            }

            if (options.MetadataOnly)
            {
                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Merge, Operand1 = mergeVersion.ID.ToString() });
                return;
            }

            var foreignRecords = Database.GetRecords(mergeVersion);
            DateTime newRefTime = DateTime.UtcNow;
            ResolveType? resolveAll = null;

            if (!GetMissingRecords(parentData.Select(x => x.Record).Concat(foreignRecords).ToList()))
            {
                Printer.PrintError("Missing record data!");
                throw new Exception();
            }

            Dictionary<string, TransientMergeObject> parentDataLookup = new Dictionary<string, TransientMergeObject>();
            foreach (var x in parentData)
                parentDataLookup[x.CanonicalName] = x;
            Dictionary<string, Record> foreignLookup = new Dictionary<string, Record>();
            foreach (var x in foreignRecords)
                foreignLookup[x.CanonicalName] = x;

            List<StageOperation> delayedStageOperations = new List<StageOperation>();
            Dictionary<string, bool> parentIgnoredList = new Dictionary<string, bool>();

#if MERGE_DIAGNOSTICS
            Printer.PrintDiagnostics("Merge phase 1 - processing foreign records.");
#endif

            HashSet<string> caseInsensitiveSet = new HashSet<string>();
            foreach (var x in CheckoutOrder(foreignRecords))
            {
#if MERGE_DIAGNOSTICS
                Printer.PrintDiagnostics("(F) Record: {0} (\\#{1})", x.CanonicalName, x.Id);
                Printer.PrintDiagnostics(" > Fingerprint: {0}", x.Fingerprint);
#endif
                TransientMergeObject parentObject = null;
                parentDataLookup.TryGetValue(x.CanonicalName, out parentObject);
                Status.StatusEntry localObject = null;
                status.Map.TryGetValue(x.CanonicalName, out localObject);

#if MERGE_DIAGNOSTICS
                if (parentObject != null)
                {
                    Printer.PrintDiagnostics(" > Found parent: {0} (\\#{1})", parentObject.CanonicalName, parentObject.Record.Id);
                    if (parentObject.TemporaryFile != null)
                        Printer.PrintDiagnostics("   > Parent is a virtual merge result.");
                    else
                        Printer.PrintDiagnostics("   > Parent fingerprint: {0}", parentObject.Record.Fingerprint);
                }
                if (localObject != null)
                {
                    Printer.PrintDiagnostics(" > Found local object: {0} (status: {1})", localObject.CanonicalName, localObject.Code);
                    if (localObject.VersionControlRecord != null)
                        Printer.PrintDiagnostics("   > Local object has a VC record \\#{0}", localObject.VersionControlRecord.Id);
                    else
                        Printer.PrintDiagnostics("   > Local object is not in version control");
                    Printer.PrintDiagnostics("   > Local fingerprint: {0}", localObject.Hash);
                }
#endif

                bool included = Included(x.CanonicalName);
#if MERGE_DIAGNOSTICS
                if (included)
                    Printer.PrintDiagnostics(" > Object is part of the masked area of the checkout graph.");
                else
                    Printer.PrintDiagnostics(" > Object is NOT included in the masked graph.");
#endif

                if (localObject == null || localObject.Removed)
                {
#if MERGE_DIAGNOSTICS
                    Printer.PrintDiagnostics(" > Local object is NOT PRESENT.");
#endif
                    if (localObject != null && localObject.Staged == false && localObject.IsDirectory)
                    {
                        if (included && !System.IO.Directory.Exists(GetRecordPath(x)))
                        {
#if MERGE_DIAGNOSTICS
                            Printer.PrintDiagnostics(" > #c#MR:## Local directory does not exist. Creating.");
#endif
                            Printer.PrintMessage("Recreating locally missing directory: #b#{0}##.", localObject.CanonicalName);
                            RestoreRecord(x, newRefTime);
                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                        }
                    }
                    else if (parentObject == null)
                    {
#if MERGE_DIAGNOSTICS
                        Printer.PrintDiagnostics(" > No parent object exists for this record.");
                        Printer.PrintDiagnostics(" > #c#MR:## Remote object was added in remote branch.");
#endif
                        // Added
                        if (included)
                        {
                            caseInsensitiveSet.Add(x.CanonicalName.ToLower());
                            RestoreRecord(x, newRefTime);
                        }
                        if (!updateMode)
                        {
                            if (!included)
                            {
                                Printer.PrintError("#x#Error:##\n  Merge results in a tree change outside the current restricted path. Aborting.");
                                return;
                            }
                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                        }
                    }
                    else
                    {
#if MERGE_DIAGNOSTICS
                        Printer.PrintDiagnostics(" > A parent object exists for this record.");
#endif
                        // Removed locally
                        if (parentObject.DataEquals(x))
                        {
#if MERGE_DIAGNOSTICS
                            Printer.PrintDiagnostics(" > #c#MR:## Remote object is the same as parent, it was locally removed.");
#endif
                            // this is fine, we removed it in our branch
                        }
                        else
                        {
#if MERGE_DIAGNOSTICS
                            Printer.PrintDiagnostics(" > Remote object is different to common parent.");
#endif
                            if (!included && !updateMode)
                            {
                                Printer.PrintError("#x#Error:##\n  Merge results in a tree change outside the current restricted path. Aborting.");
                                return;
                            }
                            // less fine
                            if (included)
                            {
#if MERGE_DIAGNOSTICS
                                Printer.PrintDiagnostics(" > Checking to see if this is in an ignored location and can be discarded.");
#endif
                                // check to see if we've ignored this whole thing
                                string parentName = x.CanonicalName;
                                bool resolved = false;
                                bool directoryRemoved = false;
                                while (true)
                                {
                                    if (!parentName.Contains('/'))
                                        break;
                                    if (parentName.EndsWith("/"))
                                        parentName = parentName.Substring(0, parentName.Length - 1);
                                    parentName = parentName.Substring(0, parentName.LastIndexOf('/') + 1);
                                    bool ignoredInParentList = false;
                                    if (parentIgnoredList.TryGetValue(parentName, out ignoredInParentList))
                                    {
                                        if (ignoredInParentList)
                                            resolved = true;
                                        break;
                                    }
                                    else
                                    {
                                        Status.StatusEntry parentObjectEntry = null;
                                        status.Map.TryGetValue(parentName, out parentObjectEntry);
                                        directoryRemoved = Directory.Exists(GetRecordPath(parentName)) && parentObjectEntry == null;
                                        if (directoryRemoved ||
                                            (parentObjectEntry != null && parentObjectEntry.Code == StatusCode.Masked))
                                        {
                                            parentIgnoredList[parentName] = true;
                                            resolved = true;
                                            break;
                                        }
                                    }
                                }
                                if (resolved)
                                {
#if MERGE_DIAGNOSTICS
                                    Printer.PrintDiagnostics(" > #c#MR:## Remote object is locally ignored.");
#endif
                                    Printer.PrintMessage("#q#Merged (Ignored)##: #b#{0}##", x.CanonicalName);
                                    if (!directoryRemoved)
                                        delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                                }
                                else
                                {
#if MERGE_DIAGNOSTICS
                                    Printer.PrintDiagnostics(" > #c#MR:## Unreconcilable changes, tree conflict.");
#endif
                                    if (options.ResolutionStrategy == MergeSpecialOptions.ResolutionSystem.Normal)
                                    {
                                        Printer.PrintWarning("Object \"{0}\" removed locally but changed in target version.", x.CanonicalName);
                                        RestoreRecord(x, newRefTime);
                                        LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
                                        if (!updateMode)
                                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                                    }
                                    else if (options.ResolutionStrategy == MergeSpecialOptions.ResolutionSystem.Theirs)
                                    {
                                        caseInsensitiveSet.Add(x.CanonicalName.ToLower());
                                        RestoreRecord(x, newRefTime);
                                        LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
#if MERGE_DIAGNOSTICS
                    Printer.PrintDiagnostics(" > There is local data for this object.");
#endif
                    if (localObject.DataEquals(x))
                    {
#if MERGE_DIAGNOSTICS
                        Printer.PrintDiagnostics(" > Both local and remote data is the same.");
#endif
                        // all good, same data in both places
                        if (localObject.Code == StatusCode.Unversioned)
                        {
#if MERGE_DIAGNOSTICS
                            Printer.PrintDiagnostics(" > #c#MR:## Local data is unversioned, adding to stage.");
#endif
                            caseInsensitiveSet.Add(x.CanonicalName.ToLower());
                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                        }
                        else
                        {
#if MERGE_DIAGNOSTICS
                            Printer.PrintDiagnostics(" > #c#MR:## No operation required.");
#endif
                        }
                    }
                    else
                    {
#if MERGE_DIAGNOSTICS
                        Printer.PrintDiagnostics(" > Local data is different to remote data.");
#endif
                        if (localObject.Code == StatusCode.Masked)
                        {
#if MERGE_DIAGNOSTICS
                            Printer.PrintDiagnostics(" > #c#MR:## Local changes at that path are not important.");
#endif
                            // don't care, update the merge info because someone else does care
                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                        }
                        else if (parentObject != null && parentObject.DataEquals(localObject))
                        {
#if MERGE_DIAGNOSTICS
                            Printer.PrintDiagnostics(" > Parent data is the same as local data.");
#endif
                            // modified in foreign branch
                            if (included)
                            {
#if MERGE_DIAGNOSTICS
                                Printer.PrintDiagnostics(" > #c#MR:## Updating to remote data as there are no conflicting changes in this branch.");
#endif
                                caseInsensitiveSet.Add(x.CanonicalName.ToLower());
                                RestoreRecord(x, newRefTime);
                                if (!updateMode)
                                {
                                    delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                                    delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                                }
                            }
                            else if (!updateMode)
                            {
                                Printer.PrintError("#x#Error:##\n  Merge results in a tree change outside the current restricted path. Aborting.");
                                return;
                            }
                        }
                        else if (parentObject != null && parentObject.DataEquals(x))
                        {
#if MERGE_DIAGNOSTICS
                            Printer.PrintDiagnostics(" > #c#MR:## Remote data and shared parent are the same, local data is modified.");
#endif
                            // modified locally
                        }
                        else if (parentObject == null)
                        {
#if MERGE_DIAGNOSTICS
                            Printer.PrintDiagnostics(" > No shared parent, requires two way merge.");
#endif
                            if (included)
                            {
#if MERGE_DIAGNOSTICS
                                Printer.PrintDiagnostics(" > #c#MR:## Running two way merge.");
#endif
                                caseInsensitiveSet.Add(x.CanonicalName.ToLower());
                                if (options.ResolutionStrategy == MergeSpecialOptions.ResolutionSystem.Theirs)
                                {
                                    RestoreRecord(x, newRefTime);
                                }
                                else if (options.ResolutionStrategy == MergeSpecialOptions.ResolutionSystem.Normal)
                                {
                                    // added in both places
                                    var mf = GetTemporaryFile(x, "-theirs");
                                    var ml = localObject.FilesystemEntry.Info;
                                    var mr = GetTemporaryFile(x, "-result");

                                    RestoreRecord(x, newRefTime, mf.FullName);

                                    mf = new FileInfo(mf.FullName);

                                    FileInfo result = Merge2Way(x, mf, localObject.VersionControlRecord, ml, mr, true, ref resolveAll);
                                    if (result != null)
                                    {
#if MERGE_DIAGNOSTICS
                                        Printer.PrintDiagnostics(" > #c#MR:## Two way merge success.");
#endif
                                        if (result != ml)
                                        {
                                            if (ml.IsReadOnly)
                                                ml.IsReadOnly = false;
                                            ml.Delete();
                                        }
                                        if (result != mr)
                                        {
                                            if (mr.IsReadOnly)
                                                mr.IsReadOnly = false;
                                            mr.Delete();
                                        }
                                        if (result != mf)
                                        {
                                            if (mf.IsReadOnly)
                                                mf.IsReadOnly = false;
                                            mf.Delete();
                                        }
                                        result.MoveTo(ml.FullName);
                                        if (!updateMode)
                                        {
                                            TransientMergeObject tmo = new TransientMergeObject() { CanonicalName = x.CanonicalName, Record = x, TemporaryFile = new FileInfo(ml.FullName) };
                                            if (!tmo.DataEquals(localObject))
                                            {
                                                delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                                                delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                                            }
                                        }
                                    }
                                    else
                                    {
#if MERGE_DIAGNOSTICS
                                        Printer.PrintDiagnostics(" > #c#MR:## Two way merge failed, conflict.");
#endif
                                        if (mr.IsReadOnly)
                                            mr.IsReadOnly = false;
                                        mr.Delete();
                                        LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
                                        if (!updateMode)
                                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                                    }
                                }
                            }
                            else if (!updateMode)
                            {
                                Printer.PrintError("#x#Error:##\n  Merge results in a tree change outside the current restricted path. Aborting.");
                                return;
                            }
                        }
                        else
                        {
#if MERGE_DIAGNOSTICS
                            Printer.PrintDiagnostics(" > Parent exists, running three-way merge.");
#endif
                            if (included)
                            {
                                caseInsensitiveSet.Add(x.CanonicalName.ToLower());
                                if (options.ResolutionStrategy == MergeSpecialOptions.ResolutionSystem.Theirs)
                                {
                                    RestoreRecord(x, newRefTime);
                                }
                                else if (options.ResolutionStrategy == MergeSpecialOptions.ResolutionSystem.Normal)
                                {
                                    var mf = GetTemporaryFile(x, "-theirs");
                                    FileInfo mb;
                                    var ml = localObject.FilesystemEntry.Info;
                                    var mr = GetTemporaryFile(x, "-result");

                                    if (parentObject.TemporaryFile == null)
                                    {
                                        mb = GetTemporaryFile(parentObject.Record, "-base");
                                        RestoreRecord(parentObject.Record, newRefTime, mb.FullName);
                                        mb = new FileInfo(mb.FullName);
                                    }
                                    else
                                        mb = parentObject.TemporaryFile;

                                    RestoreRecord(x, newRefTime, mf.FullName);

                                    mf = new FileInfo(mf.FullName);

                                    FileInfo result = Merge3Way(x, mf, localObject.VersionControlRecord, ml, parentObject.Record, mb, mr, true, ref resolveAll);
                                    if (result != null)
                                    {
#if MERGE_DIAGNOSTICS
                                    Printer.PrintDiagnostics(" > #c#MR:## Three way merge success.");
#endif
                                        if (result != ml)
                                        {
                                            if (ml.IsReadOnly)
                                                ml.IsReadOnly = false;
                                            ml.Delete();
                                        }
                                        if (result != mr)
                                        {
                                            if (mr.IsReadOnly)
                                                mr.IsReadOnly = false;
                                            mr.Delete();
                                        }
                                        if (result != mf)
                                        {
                                            if (mf.IsReadOnly)
                                                mf.IsReadOnly = false;
                                            mf.Delete();
                                        }
                                        if (result != mb)
                                        {
                                            if (mb.IsReadOnly)
                                                mb.IsReadOnly = false;
                                            mb.Delete();
                                        }
                                        result.MoveTo(ml.FullName);
                                        if (!updateMode)
                                        {
                                            TransientMergeObject tmo = new TransientMergeObject() { CanonicalName = x.CanonicalName, Record = x, TemporaryFile = new FileInfo(ml.FullName) };
                                            if (!tmo.DataEquals(localObject))
                                            {
                                                delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                                                delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                                            }
                                        }
                                    }
                                    else
                                    {
#if MERGE_DIAGNOSTICS
                                    Printer.PrintDiagnostics(" > #c#MR:## Three way merge failed. Conflict.");
#endif
                                        LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
                                        if (!updateMode)
                                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                                    }
                                }
                            }
                            else if (!updateMode)
                            {
                                Printer.PrintError("#x#Error:##\n  Merge results in a tree change outside the current restricted path. Aborting.");
                                return;
                            }
                        }
                    }
                }

                if (x.IsDirective)
                    LoadDirectives();
            }
#if MERGE_DIAGNOSTICS
            Printer.PrintDiagnostics(" > Merge phase 2, checking parent data.");
#endif
            List<Record> deletionList = new List<Record>();
            foreach (var x in DeletionOrder(parentData))
            {
#if MERGE_DIAGNOSTICS
                Printer.PrintDiagnostics("(P) Record: {0} (\\#{1})", x.CanonicalName, x.Record.Id);
#endif
                Objects.Record foreignRecord = null;
                foreignLookup.TryGetValue(x.CanonicalName, out foreignRecord);
                Status.StatusEntry localObject = null;
                status.Map.TryGetValue(x.CanonicalName, out localObject);
#if MERGE_DIAGNOSTICS
                if (foreignRecord != null)
                {
                    Printer.PrintDiagnostics(" > Found foreign record: {0} (\\#{1})", foreignRecord.CanonicalName, foreignRecord.Id);
                }
                if (localObject != null)
                {
                    Printer.PrintDiagnostics(" > Found local object: {0} (status: {1})", localObject.CanonicalName, localObject.Code);
                }
#endif
                if (foreignRecord == null)
                {
#if MERGE_DIAGNOSTICS
                    Printer.PrintDiagnostics(" > Parent object was deleted in foreign branch.");
#endif
                    // deleted by branch
                    if (localObject != null)
                    {
#if MERGE_DIAGNOSTICS
                        Printer.PrintDiagnostics(" > Object still exists in local filesystem.");
#endif
                        if (localObject.Code == StatusCode.Masked)
                        {
#if MERGE_DIAGNOSTICS
                            Printer.PrintDiagnostics(" > #c#MR:## Object exists locally but is ignored, removing record info.");
#endif
                            Printer.PrintMessage("#q#Removing (Ignored):## #b#{0}##", x.CanonicalName);
                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = -1 });
                        }
                        else if (!localObject.Removed && !caseInsensitiveSet.Contains(x.CanonicalName.ToLower()))
                        {
#if MERGE_DIAGNOSTICS
                            Printer.PrintDiagnostics(" > Local object is still active.");
#endif
                            string path = System.IO.Path.Combine(Root.FullName, x.CanonicalName);
                            if (x.DataEquals(localObject) || options.ResolutionStrategy == MergeSpecialOptions.ResolutionSystem.Theirs)
                            {
#if MERGE_DIAGNOSTICS
                                Printer.PrintDiagnostics(" > #c#MR:## Local object matches parent, removing.");
#endif
                                Printer.PrintMessage("#c#Removing:## {0}", x.CanonicalName);
                                deletionList.Add(x.Record);
                            }
                            else if (options.ResolutionStrategy == MergeSpecialOptions.ResolutionSystem.Mine)
                            {
#if MERGE_DIAGNOSTICS
                                Printer.PrintDiagnostics(" > #c#MR:## Local object doesn't match parent!");
#endif
                                Printer.PrintError("Can't remove object \"{0}\", tree confict!", x.CanonicalName);
                                Printer.PrintMessage("Resolve conflict by: (r)emoving file, (k)eeping local or (c)onflict?");
                                string resolution = System.Console.ReadLine();
                                if (resolution.StartsWith("k"))
                                    continue;
                                if (resolution.StartsWith("r"))
                                {
                                    deletionList.Add(x.Record);
                                }
                                if (resolution.StartsWith("c"))
                                    delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
                            }
                        }
                    }
                }
            }
            List<string> filetimesToRemove = new List<string>();
#if MERGE_DIAGNOSTICS
            Printer.PrintDiagnostics(" > Merge phase 3, removing files marked for delete.");
#endif
            foreach (var x in deletionList)
            {
#if MERGE_DIAGNOSTICS
                Printer.PrintDiagnostics(" > #w#Note:## Attempting to delete {0}", x.CanonicalName);
#endif
                if (!Included(x.CanonicalName))
                {
                    if (!updateMode)
                    {
                        Printer.PrintError("#x#Error:##\n  Merge results in a tree change outside the current restricted path. Aborting.");
                        return;
                    }
                }
                string path = GetRecordPath(x);
                if (x.IsFile)
                {
                    try
                    {
                        System.IO.FileInfo fi = new FileInfo(path);
                        if (fi.IsReadOnly)
                            fi.IsReadOnly = false;

                        System.IO.File.Delete(path);
                    }
                    catch
                    {
                        Printer.PrintError("#x#Can't remove object \"{0}\"!", path);
                    }
                }
                else if (x.IsSymlink)
                {
                    try
                    {
                        Utilities.Symlink.Delete(path);
                    }
                    catch (Exception)
                    {
                        Printer.PrintError("#x#Can't remove object \"{0}\"!", x.CanonicalName);
                    }
                }
                else if (x.IsDirectory)
                {
                    try
                    {
                        System.IO.Directory.Delete(path.Substring(0, path.Length - 1));
                    }
                    catch
                    {
                        Printer.PrintError("#x#Can't remove directory \"{0}\"!", x.CanonicalName);
                    }
                }
                filetimesToRemove.Add(x.CanonicalName);
                if (!updateMode)
                    delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.Remove, Operand1 = x.CanonicalName });
            }
            LocalData.AddStageOperations(delayedStageOperations);
            foreach (var x in parentData)
            {
                if (x.TemporaryFile != null)
                {
                    if (x.TemporaryFile.IsReadOnly)
                        x.TemporaryFile.IsReadOnly = false;
                    x.TemporaryFile.Delete();
                }
            }
            LocalData.BeginTransaction();
            foreach (var x in filetimesToRemove)
                RemoveFileTimeCache(x);
            LocalData.Commit();
            if (options.Reintegrate)
                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Reintegrate, Operand1 = possibleBranch.ID.ToString() });
            if (!updateMode)
            {
                Dictionary<Guid, int> mergeVersionGraph = null;
                foreach (var x in LocalData.StageOperations)
                {
                    if (x.Type == StageOperationType.Merge)
                    {
                        if (mergeVersionGraph == null)
                            mergeVersionGraph = GetParentGraph(mergeVersion, options.IgnoreMergeParents);
                        Objects.Version stagedMergeVersion = GetVersion(new Guid(x.Operand1));
                        if (mergeVersionGraph.ContainsKey(stagedMergeVersion.ID))
                            LocalData.RemoveStageOperation(x);
                    }
                }
                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Merge, Operand1 = mergeVersion.ID.ToString() });
            }
            else
            {
                LocalData.BeginTransaction();
                try
                {
                    var ws = LocalData.Workspace;
                    ws.Tip = mergeVersion.ID;
                    LocalData.Update(ws);
                    LocalData.Commit();
                    Printer.PrintMessage("Updated - at version {0}", mergeVersion.ID);
                }
                catch
                {
                    LocalData.Rollback();
                }
            }

            if (updateMode && !string.IsNullOrEmpty(postUpdateMerge))
            {
                Merge(postUpdateMerge, false, new MergeSpecialOptions());
            }
        }

        private void RemoveFileTimeCache(string item2, bool updateDB = true)
        {
            FileTimeCache.Remove(item2);
            if (updateDB)
                LocalData.RemoveFileTime(item2);
        }

        class MergeResult
        {
            public List<TransientMergeObject> Contents { get; set; }
            public List<Objects.Version> Inputs { get; set; }
        }

        private List<TransientMergeObject> MergeCoreRecursive(Objects.Version v1, Objects.Version v2)
        {
            var parents = GetCommonParents(v1, v2, false);
            if (parents == null || parents.Count == 0)
                throw new Exception("No common parent!");

            List<TransientMergeObject> parentData = null;

            Objects.Version parent = null;
            if (parents.Count == 1)
            {
                parent = GetVersion(parents[0].Key);
                if (parent.ID == v1.ID || parent.ID == v2.ID)
                {
                    Printer.PrintMessage("Merge information is already up to date.");
                    return Database.GetRecords(parent.ID == v1.ID ? v2 : v1).Select(x => new TransientMergeObject() { Record = x, CanonicalName = x.CanonicalName }).ToList();
                }
                Printer.PrintMessage("Starting recursive merge:");
                Printer.PrintMessage(" - Left: {0} #b#\"{1}\"##", v1.ID, GetBranch(v1.Branch).Name);
                Printer.PrintMessage(" - Right: {0} #b#\"{1}\"##", v2.ID, GetBranch(v2.Branch).Name);
                Printer.PrintMessage(" - Parent: {0} #b#\"{1}\"##", parent.ID, GetBranch(parent.Branch).Name);
                parentData = Database.GetRecords(parent).Select(x => new TransientMergeObject() { Record = x, CanonicalName = x.CanonicalName }).ToList();
            }
            else if (parents.Count == 2)
            {
                // recursive merge
                parentData = MergeCoreRecursive(GetVersion(parents[0].Key), GetVersion(parents[1].Key));
            }
            else
            {
                // complicated recursive merge
                throw new Exception();
            }

            var localRecords = Database.GetRecords(v1);
            var foreignRecords = Database.GetRecords(v2);

            if (!GetMissingRecords(parentData.Select(x => x.Record).Concat(localRecords.Concat(foreignRecords)).ToList()))
            {
                Printer.PrintError("Missing record data!");
                throw new Exception();
            }

            DateTime newRefTime = DateTime.UtcNow;

            List<TransientMergeObject> results = new List<TransientMergeObject>();

            Dictionary<string, TransientMergeObject> parentDataLookup = new Dictionary<string, TransientMergeObject>();
            foreach (var x in parentData)
                parentDataLookup[x.CanonicalName] = x;
            Dictionary<string, Record> foreignLookup = new Dictionary<string, Record>();
            foreach (var x in foreignRecords)
                foreignLookup[x.CanonicalName] = x;
            Dictionary<string, Record> localLookup = new Dictionary<string, Record>();
            foreach (var x in localRecords)
                localLookup[x.CanonicalName] = x;

            ResolveType? resolveAll = null;
            foreach (var x in foreignRecords)
            {
                TransientMergeObject parentObject = null;
                parentDataLookup.TryGetValue(x.CanonicalName, out parentObject);
                Record localRecord = null;
                localLookup.TryGetValue(x.CanonicalName, out localRecord);

                if (localRecord == null)
                {
                    if (parentObject == null)
                    {
                        var transientResult = new TransientMergeObject() { Record = x, CanonicalName = x.CanonicalName };
                        if (x.HasData)
                        {
                            transientResult.TemporaryFile = GetTemporaryFile(transientResult.Record);
                            RestoreRecord(x, newRefTime, transientResult.TemporaryFile.FullName);
                            transientResult.TemporaryFile = new FileInfo(transientResult.TemporaryFile.FullName);
                        }
                        results.Add(transientResult);
                    }
                    else
                    {
                        // Removed locally
                        if (parentObject.DataEquals(x))
                        {
                            // this is fine, we removed it in our branch
                        }
                        else
                        {
                            Printer.PrintMessage("Remote file #w#{0}## has been removed.", x.CanonicalName);
                            while (true)
                            {
                                Printer.PrintMessage("This file has been modified on the other branch. Resolve by (k)eeping local or (d)eleting file?");
                                Printer.PrintMessage("Specifiying (c) for conflict will abort the merge.");
                                string resolution = System.Console.ReadLine();
                                if (resolution.StartsWith("k"))
                                {
                                    var transientResult = new TransientMergeObject() { Record = localRecord, CanonicalName = localRecord.CanonicalName };
                                    results.Add(transientResult);
                                    break;
                                }
                                if (resolution.StartsWith("d"))
                                {
                                    // do nothing
                                    break;
                                }
                                if (resolution.StartsWith("c"))
                                {
                                    Printer.PrintMessage("Tree conflicts cannot be recursively resolved.");
                                    throw new Exception();
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (localRecord.DataEquals(x))
                    {
                        var transientResult = new TransientMergeObject() { Record = localRecord, CanonicalName = x.CanonicalName };
                        results.Add(transientResult);
                    }
                    else
                    {
                        if (parentObject != null && parentObject.DataEquals(localRecord))
                        {
                            // modified in foreign branch
                            var transientResult = new TransientMergeObject() { Record = x, CanonicalName = x.CanonicalName };
                            if (x.HasData)
                            {
                                transientResult.TemporaryFile = GetTemporaryFile(transientResult.Record);
                                RestoreRecord(x, newRefTime, transientResult.TemporaryFile.FullName);
                                transientResult.TemporaryFile = new FileInfo(transientResult.TemporaryFile.FullName);
                            }
                            results.Add(transientResult);
                        }
                        else if (parentObject != null && parentObject.DataEquals(x))
                        {
                            // modified locally
                            var transientResult = new TransientMergeObject() { Record = localRecord, CanonicalName = x.CanonicalName };
                            results.Add(transientResult);
                        }
                        else if (parentObject == null)
                        {
                            var transientResult = new TransientMergeObject() { Record = x, CanonicalName = x.CanonicalName };
                            transientResult.TemporaryFile = GetTemporaryFile(transientResult.Record);

                            var foreign = GetTemporaryFile(x);
                            var local = GetTemporaryFile(localRecord);

                            RestoreRecord(x, newRefTime, foreign.FullName);
                            RestoreRecord(localRecord, newRefTime, local.FullName);

                            foreign = new FileInfo(foreign.FullName);
                            local = new FileInfo(local.FullName);

                            FileInfo info = Merge2Way(x, foreign, localRecord, local, transientResult.TemporaryFile, false, ref resolveAll);
                            if (info != transientResult.TemporaryFile)
                            {
                                if (transientResult.TemporaryFile.IsReadOnly)
                                    transientResult.TemporaryFile.IsReadOnly = false;
                                transientResult.TemporaryFile.Delete();
                                System.IO.File.Move(info.FullName, transientResult.TemporaryFile.FullName);
                            }
                            if (foreign.IsReadOnly)
                                foreign.IsReadOnly = false;
                            if (local.IsReadOnly)
                                local.IsReadOnly = false;
                            foreign.Delete();
                            local.Delete();
                            transientResult.TemporaryFile = new FileInfo(transientResult.TemporaryFile.FullName);
                            results.Add(transientResult);
                        }
                        else
                        {
                            var transientResult = new TransientMergeObject() { Record = x, CanonicalName = x.CanonicalName };
                            transientResult.TemporaryFile = GetTemporaryFile(transientResult.Record);

                            var foreign = GetTemporaryFile(x);
                            var local = GetTemporaryFile(localRecord);
                            FileInfo parentFile = null;
                            if (parentObject.TemporaryFile == null)
                            {
                                parentFile = GetTemporaryFile(parentObject.Record);
                                RestoreRecord(parentObject.Record, newRefTime, parentFile.FullName);
                                parentFile = new FileInfo(parentFile.FullName);
                            }
                            else
                                parentFile = parentObject.TemporaryFile;

                            RestoreRecord(x, newRefTime, foreign.FullName);
                            RestoreRecord(localRecord, newRefTime, local.FullName);

                            foreign = new FileInfo(foreign.FullName);
                            local = new FileInfo(local.FullName);

                            FileInfo info = Merge3Way(x, foreign, localRecord, local, parentObject.Record, parentFile, transientResult.TemporaryFile, false, ref resolveAll);
                            if (info != transientResult.TemporaryFile)
                            {
                                if (transientResult.TemporaryFile.IsReadOnly)
                                    transientResult.TemporaryFile.IsReadOnly = false;
                                transientResult.TemporaryFile.Delete();
                                System.IO.File.Move(info.FullName, transientResult.TemporaryFile.FullName);
                            }
                            if (foreign.IsReadOnly)
                                foreign.IsReadOnly = false;
                            if (local.IsReadOnly)
                                local.IsReadOnly = false;
                            foreign.Delete();
                            local.Delete();
                            if (parentObject.TemporaryFile == null)
                            {
                                if (parentFile.IsReadOnly)
                                    parentFile.IsReadOnly = false;
                                parentFile.Delete();
                            }
                            transientResult.TemporaryFile = new FileInfo(transientResult.TemporaryFile.FullName);
                            results.Add(transientResult);
                        }
                    }
                }
            }
            foreach (var x in parentData)
            {
                Objects.Record foreignRecord = null;
                foreignLookup.TryGetValue(x.CanonicalName, out foreignRecord);
                Objects.Record localRecord = null;
                localLookup.TryGetValue(x.CanonicalName, out localRecord);
                if (foreignRecord == null)
                {
                    // deleted by branch
                    if (localRecord != null)
                    {
                        string path = System.IO.Path.Combine(Root.FullName, x.CanonicalName);
                        if (x.DataEquals(localRecord))
                        {
                            // all good, do nothing
                        }
                        else
                        {
                            Printer.PrintError("Can't remove object \"{0}\", tree confict!", x.CanonicalName);
                            Printer.PrintMessage("Resolve conflict by: (r)emoving file, (k)eeping local or (c)onflict?");
                            string resolution = System.Console.ReadLine();
                            if (resolution.StartsWith("k"))
                            {
                                var transientResult = new TransientMergeObject() { Record = localRecord, CanonicalName = localRecord.CanonicalName };
                                results.Add(transientResult);
                            }
                            if (resolution.StartsWith("r"))
                            {
                                // do nothing
                            }
                            if (resolution.StartsWith("c"))
                                throw new Exception();
                        }
                    }
                }
            }
            foreach (var x in localLookup)
            {
                if (foreignLookup.ContainsKey(x.Key))
                    continue;
                if (parentDataLookup.ContainsKey(x.Key))
                    continue;
                var transientResult = new TransientMergeObject() { Record = x.Value, CanonicalName = x.Value.CanonicalName };
                results.Add(transientResult);
            }
            foreach (var x in parentData)
            {
                if (x.TemporaryFile != null)
                {
                    if (x.TemporaryFile.IsReadOnly)
                        x.TemporaryFile.IsReadOnly = false;
                    x.TemporaryFile.Delete();
                }
            }
            return results;
        }

        private FileInfo Merge3Way(Record x, FileInfo foreign, Record localRecord, FileInfo local, Record record, FileInfo parentFile, FileInfo temporaryFile, bool allowConflict, ref ResolveType? resolveAll)
        {
            Printer.PrintMessage("#w#Merging:## {0}", x.CanonicalName);
            // modified in both places
            string mf = foreign.FullName;
            string mb = parentFile.FullName;
            string ml = local.FullName;
            string mr = temporaryFile.FullName;
            bool isBinary = FileClassifier.Classify(foreign) == FileEncoding.Binary ||
                FileClassifier.Classify(local) == FileEncoding.Binary ||
                FileClassifier.Classify(parentFile) == FileEncoding.Binary;

            System.IO.File.Copy(ml, ml + ".mine", true);
            if (!isBinary)
            {
                bool xdiffSuccess = XDiffMerge3Way(mb, ml, mf, mr) == 0;
                if (xdiffSuccess || Utilities.DiffTool.Merge3Way(mb, ml, mf, mr, Directives.ExternalMerge))
                {
                    FileInfo fi = new FileInfo(ml + ".mine");
                    if (fi.IsReadOnly)
                        fi.IsReadOnly = false;
                    fi.Delete();
                    if (!xdiffSuccess)
                        Printer.PrintMessage("#s# - Resolved.##");
                    return temporaryFile;
                }
            }

            ResolveType resolution = GetResolution(isBinary, ref resolveAll);
            if (resolution == ResolveType.Mine)
            {
                Printer.PrintMessage("#s# - Resolved (mine).##");
                FileInfo fi = new FileInfo(ml + ".mine");
                if (fi.IsReadOnly)
                    fi.IsReadOnly = false;
                fi.Delete();
                return local;
            }
            if (resolution == ResolveType.Theirs)
            {
                Printer.PrintMessage("#s# - Resolved (theirs).##");
                FileInfo fi = new FileInfo(ml + ".mine");
                if (fi.IsReadOnly)
                    fi.IsReadOnly = false;
                fi.Delete();
                return foreign;
            }
            else
            {
                if (!allowConflict)
                    throw new Exception();
                System.IO.File.Move(mf, ml + ".theirs");
                System.IO.File.Move(mb, ml + ".base");
                Printer.PrintMessage("#e# - File not resolved. Please manually merge and then mark as resolved.##");
                return null;
            }
        }

        enum ResolveType
        {
            Mine,
            Theirs,
            Conflict,

            // Stash resolve types
            Replace,
            Skip,
            Merge,
        }

        private FileInfo Merge2Way(Record x, FileInfo foreign, Record localRecord, FileInfo local, FileInfo temporaryFile, bool allowConflict, ref ResolveType? resolveAll)
        {
            if (x != null)
                Printer.PrintMessage("#w#Merging:## {0}", x.CanonicalName);
            string mf = foreign.FullName;
            string ml = local.FullName;
            string mr = temporaryFile.FullName;

            bool isBinary = FileClassifier.Classify(foreign) == FileEncoding.Binary ||
                FileClassifier.Classify(local) == FileEncoding.Binary;

            System.IO.File.Copy(ml, ml + ".mine", true);
            if (!isBinary && Utilities.DiffTool.Merge(ml, mf, mr, Directives.ExternalMerge2Way))
            {
                FileInfo fi = new FileInfo(ml + ".mine");
                if (fi.IsReadOnly)
                    fi.IsReadOnly = false;
                fi.Delete();
                Printer.PrintMessage("#s# - Resolved.##");
                return temporaryFile;
            }
            else
            {
                ResolveType resolution = GetResolution(isBinary, ref resolveAll);
                if (resolution == ResolveType.Mine)
                {
                    Printer.PrintMessage("#s# - Resolved (mine).##");
                    FileInfo fi = new FileInfo(ml + ".mine");
                    if (fi.IsReadOnly)
                        fi.IsReadOnly = false;
                    fi.Delete();
                    return local;
                }
                if (resolution == ResolveType.Theirs)
                {
                    Printer.PrintMessage("#s# - Resolved (theirs).##");
                    FileInfo fi = new FileInfo(ml + ".mine");
                    if (fi.IsReadOnly)
                        fi.IsReadOnly = false;
                    fi.Delete();
                    return foreign;
                }
                else
                {
                    if (!allowConflict)
                        throw new Exception();
                    System.IO.File.Move(mf, ml + ".theirs");
                    Printer.PrintMessage("#e# - File not resolved. Please manually merge and then mark as resolved.##");
                    return null;
                }
            }
        }

        private ResolveType GetResolution(bool binary, ref ResolveType? resolveAll)
        {
            if (resolveAll.HasValue)
            {
                Printer.PrintMessage(" - Auto-resolving using #b#{0}##.", resolveAll.Value);
                return resolveAll.Value;
            }
            while (true)
            {
                if (!binary)
                    Printer.PrintMessage("Reconcile file, use #s#(m)ine##, #c#(t)heirs## or #e#(c)onflict##? (Use #b#*## for all)");
                else
                    Printer.PrintMessage("Reconcile binary file, use #s#(m)ine##, #c#(t)heirs## or #e#(c)onflict##? (Use #b#*## for all)");
                string resolution = System.Console.ReadLine();
                if (resolution.StartsWith("m"))
                {
                    if (resolution.Contains("*"))
                        resolveAll = ResolveType.Mine;
                    return ResolveType.Mine;
                }
                if (resolution.StartsWith("t"))
                {
                    if (resolution.Contains("*"))
                        resolveAll = ResolveType.Theirs;
                    return ResolveType.Theirs;
                }
                if (resolution.StartsWith("c"))
                {
                    if (resolution.Contains("*"))
                        resolveAll = ResolveType.Conflict;
                    return ResolveType.Conflict;
                }
            }
        }

        private ResolveType GetStashResolutionText(ref ResolveType? resolveAll)
        {
            if (resolveAll.HasValue)
            {
                Printer.PrintMessage(" - Auto-resolving using #b#{0}##.", resolveAll.Value);
                return resolveAll.Value;
            }
            while (true)
            {
                Printer.PrintMessage("Reconcile stashed file, use #s#(r)eplace##, #c#(s)kip##, #w#(b)oth## or #b#attempt to (m)erge##? (Use #b#*## for all)");

                string resolution = System.Console.ReadLine();
                if (resolution.StartsWith("r"))
                {
                    if (resolution.Contains("*"))
                        resolveAll = ResolveType.Replace;
                    return ResolveType.Replace;
                }
                if (resolution.StartsWith("s"))
                {
                    if (resolution.Contains("*"))
                        resolveAll = ResolveType.Skip;
                    return ResolveType.Skip;
                }
                if (resolution.StartsWith("b"))
                {
                    if (resolution.Contains("*"))
                        resolveAll = ResolveType.Conflict;
                    return ResolveType.Conflict;
                }
                if (resolution.StartsWith("m"))
                {
                    if (resolution.Contains("*"))
                        resolveAll = ResolveType.Merge;
                    return ResolveType.Merge;
                }
            }
        }

        private ResolveType GetStashResolutionBinary(ref ResolveType? resolveAll)
        {
            if (resolveAll.HasValue)
            {
                Printer.PrintMessage(" - Auto-resolving using #b#{0}##.", resolveAll.Value);
                return resolveAll.Value;
            }
            while (true)
            {
                Printer.PrintMessage("Reconcile stashed binary file, use #s#(r)eplace##, #c#(s)kip## or #w#(b)oth##? (Use #b#*## for all)");

                string resolution = System.Console.ReadLine();
                if (resolution.StartsWith("r"))
                {
                    if (resolution.Contains("*"))
                        resolveAll = ResolveType.Replace;
                    return ResolveType.Replace;
                }
                if (resolution.StartsWith("s"))
                {
                    if (resolution.Contains("*"))
                        resolveAll = ResolveType.Skip;
                    return ResolveType.Skip;
                }
                if (resolution.StartsWith("b"))
                {
                    if (resolution.Contains("*"))
                        resolveAll = ResolveType.Conflict;
                    return ResolveType.Conflict;
                }
            }
        }

        int m_TempFileIndex = 0;
        private FileInfo GetTemporaryFile(Record rec, string name = "")
        {
            DirectoryInfo info = new DirectoryInfo(Path.Combine(AdministrationFolder.FullName, "temp"));
            info.Create();
            lock (this)
            {
                while (true)
                {
                    string fn = rec.Name + name + m_TempFileIndex++.ToString() + ".tmp";
                    var x = new FileInfo(Path.Combine(info.FullName, fn));
                    if (!x.Exists)
                    {
                        using (var t = x.Create())
                        {

                        }
                        return x;
                    }
                }
            }
        }

        private List<KeyValuePair<Guid, int>> GetCommonParents(Objects.Version version, Objects.Version mergeVersion, bool ignoreMergeParents)
        {
            Dictionary<Guid, int> foreignGraph = GetParentGraph(mergeVersion, ignoreMergeParents);
            List<KeyValuePair<Guid, int>> shared = GetSharedParentGraphMinimal(version, foreignGraph, ignoreMergeParents);
            shared = shared.OrderBy(x => x.Value).ToList();
            if (shared.Count == 0)
                return null;
            HashSet<Guid> ignored = new HashSet<Guid>();
            var pruned = new List<KeyValuePair<Guid, int>>();
            for (int i = 0; i < shared.Count; i++)
            {
                if (ignored.Contains(shared[i].Key))
                    continue;
                pruned.Add(shared[i]);
                var parents = GetParentGraph(GetVersion(shared[i].Key), ignoreMergeParents);
                for (int j = 0; j < shared.Count; j++)
                {
                    if (j == i)
                        continue;
                    if (parents.ContainsKey(shared[j].Key))
                        ignored.Add(shared[j].Key);
                }
            }
            return pruned.Where(x => !ignored.Contains(x.Key)).ToList();
        }

        private List<KeyValuePair<Guid, int>> GetSharedParentGraphMinimal(Objects.Version version, Dictionary<Guid, int> foreignGraph, bool ignoreMergeParents)
        {
            bool includePendingMerge = false;
            if (version == null)
            {
                includePendingMerge = true;
                version = Version;
            }
            Printer.PrintDiagnostics("Getting minimal parent graph for version {0}", version.ID);
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            Stack<Tuple<Objects.Version, int>> openNodes = new Stack<Tuple<Objects.Version, int>>();
            openNodes.Push(new Tuple<Objects.Version, int>(version, 0));
            if (includePendingMerge)
            {
                foreach (var x in LocalData.StageOperations)
                {
                    if (x.Type == StageOperationType.Merge)
                        openNodes.Push(new Tuple<Objects.Version, int>(GetVersion(new Guid(x.Operand1)), 1));
                }
            }
            Dictionary<Guid, int> visited = new Dictionary<Guid, int>();
            HashSet<Guid> sharedNodes = new HashSet<Guid>();
            while (openNodes.Count > 0)
            {
                var currentNodeData = openNodes.Pop();
                Objects.Version currentNode = currentNodeData.Item1;

                int distance = 0;
                if (visited.TryGetValue(currentNode.ID, out distance))
                {
                    if (distance > currentNodeData.Item2)
                        visited[currentNode.ID] = currentNodeData.Item2;
                    continue;
                }

                visited[currentNode.ID] = currentNodeData.Item2;
                if (foreignGraph.ContainsKey(currentNode.ID))
                {
                    sharedNodes.Add(currentNode.ID);
                }
                else
                {
                    if (currentNode.Parent.HasValue && !visited.ContainsKey(currentNode.Parent.Value))
                        openNodes.Push(new Tuple<Objects.Version, int>(Database.Get<Objects.Version>(currentNode.Parent), currentNodeData.Item2 + 1));

                    if (!ignoreMergeParents)
                    {
                        foreach (var x in Database.GetMergeInfo(currentNode.ID))
                        {
                            if (!visited.ContainsKey(x.SourceVersion))
                                openNodes.Push(new Tuple<Objects.Version, int>(Database.Get<Objects.Version>(x.SourceVersion), currentNodeData.Item2 + 1));
                        }
                    }
                }
            }

            List<KeyValuePair<Guid, int>> shared = new List<KeyValuePair<Guid, int>>();
            foreach (var x in sharedNodes)
            {
                int distance = System.Math.Max(visited[x], foreignGraph[x]);
                shared.Add(new KeyValuePair<Guid, int>(x, distance));
            }
            shared = shared.OrderBy(x => x.Value).ToList();
            sw.Stop();
            Printer.PrintDiagnostics("Determined shared node hierarchy in {0} ms.", sw.ElapsedMilliseconds);
            return shared;
        }

        public Dictionary<Guid, int> GetParentGraph(Objects.Version mergeVersion, bool ignoreMergeParents)
        {
            Printer.PrintDiagnostics("Getting parent graph for version {0}", mergeVersion.ID);
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            Stack<Tuple<Objects.Version, int>> openNodes = new Stack<Tuple<Objects.Version, int>>();
            openNodes.Push(new Tuple<Objects.Version, int>(mergeVersion, 0));
            Dictionary<Guid, int> result = new Dictionary<Guid, int>();
            while (openNodes.Count > 0)
            {
                var currentNodeData = openNodes.Pop();
                Objects.Version currentNode = currentNodeData.Item1;

                int distance = 0;
                if (result.TryGetValue(currentNode.ID, out distance))
                {
                    if (distance > currentNodeData.Item2)
                        result[currentNode.ID] = currentNodeData.Item2;
                    continue;
                }
                result[currentNode.ID] = currentNodeData.Item2;

                if (currentNode.Parent.HasValue && !result.ContainsKey(currentNode.Parent.Value))
                    openNodes.Push(new Tuple<Objects.Version, int>(Database.Get<Objects.Version>(currentNode.Parent), currentNodeData.Item2 + 1));

                if (!ignoreMergeParents)
                {
                    foreach (var x in Database.GetMergeInfo(currentNode.ID))
                    {
                        if (!result.ContainsKey(x.SourceVersion))
                            openNodes.Push(new Tuple<Objects.Version, int>(Database.Get<Objects.Version>(x.SourceVersion), currentNodeData.Item2 + 1));
                    }
                }
            }
            sw.Stop();
            Printer.PrintDiagnostics("Determined node hierarchy in {0} ms.", sw.ElapsedMilliseconds);
            return result;
        }

        public Objects.Version GetBranchHeadVersion(Branch branch)
        {
            if (branch.Terminus.HasValue)
                return Database.Get<Objects.Version>(branch.Terminus.Value);
            Head head = GetBranchHead(branch);
            return Database.Get<Objects.Version>(head.Version);
        }

        public Head GetBranchHead(Branch branch)
        {
            var heads = Database.GetHeads(branch);
            if (heads.Count > 1)
            {
                Printer.PrintError("Can't access branch head - {0} heads on record in branch!", heads.Count);
                foreach (var x in heads)
                    Printer.PrintError(" - Version {0} marked as head.", x.Version);
                throw new Exception("Can't access branch - multiple heads!");
            }
            else if (heads.Count == 0)
                throw new Exception("Can't access branch - no head!");
            return heads[0];
        }

        public Objects.Branch GetBranch(Guid branch)
        {
            return Database.Find<Objects.Branch>(branch);
        }

        public List<Objects.Head> GetBranchHeads(Branch x)
        {
            return Database.GetHeads(x);
        }

        public void Branch(string v)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(v, "^([-]|\\w)+$"))
                throw new Exception("Invalid branch name.");
            Printer.PrintDiagnostics("Checking for existing branch \"{0}\".", v);
            var branch = GetBranchByName(v).FirstOrDefault();
            if (branch != null)
                throw new Exception(string.Format("Branch \"{0}\" already exists!", v));

            Objects.Version currentVer = Database.Version;
            branch = Objects.Branch.Create(v, currentVer.ID, currentVer.Branch);
            Printer.PrintDiagnostics("Created new branch \"{0}\", ID: {1}.", v, branch.ID);
            var ws = LocalData.Workspace;
            ws.Branch = branch.ID;
            Objects.Head head = new Head();
            head.Branch = branch.ID;
            head.Version = currentVer.ID;
            Printer.PrintDiagnostics("Created head node to track branch {0} with version {1}.", branch.ID, currentVer.ID);
            Printer.PrintDiagnostics("Starting DB transaction.");
            LocalData.BeginTransaction();
            try
            {
                Database.BeginTransaction();
                try
                {
                    Database.InsertSafe(head);
                    Database.InsertSafe(branch);
                    Database.Commit();
                    Printer.PrintDiagnostics("Finished.");
                }
                catch (Exception e)
                {
                    Database.Rollback();
                    throw new Exception("Couldn't branch!", e);
                }
                LocalData.UpdateSafe(ws);
                LocalData.Commit();
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Couldn't branch!", e);
            }
        }

        public void Checkout(string v, bool purge, bool verbose, bool metadataOnly = false)
        {
            Objects.Version target = null;
            if (!string.IsNullOrEmpty(v))
            {
                if (!Switch(v))
                {
                    target = GetPartialVersion(v);
                    if (target == null)
                    {
                        Printer.PrintError("Can't find version or branch with name: {0}", v);
                        return;
                    }
                    if (target.Branch != CurrentBranch.ID)
                    {
                        Objects.Branch branch = GetBranch(target.Branch);
                        Printer.PrintMessage("Switching branch to \"{0}\".", branch.Name);
                        SwitchBranch(branch);
                    }
                }
            }
            if (target == null)
                target = Database.Get<Objects.Version>(GetBranchHead(Database.Branch).Version);
            CleanStage();
            if (metadataOnly)
            {
                LocalData.BeginTransaction();
                try
                {
                    var ws = LocalData.Workspace;
                    ws.Tip = target.ID;
                    ws.LocalCheckoutTime = DateTime.Now;
                    LocalData.Update(ws);
                    LocalData.Commit();
                }
                catch (Exception e)
                {
                    throw new Exception("Couldn't update local information!", e);
                }
            }
            else
                CheckoutInternal(target, verbose);
            Database.GetCachedRecords(Version);

            if (purge)
                Purge();

            Printer.PrintMessage("At version #b#{0}## on branch \"#b#{1}##\"", Database.Version.ID, Database.Branch.Name);
        }

        private void Purge()
        {
            var status = Status;
            HashSet<Entry> deletionList = new HashSet<Entry>();
            foreach (var x in status.Elements)
            {
                if (x.Code == StatusCode.Unversioned)
                {
                    try
                    {
                        if (x.FilesystemEntry.IsDirectory)
                            deletionList.Add(x.FilesystemEntry);
                        else
                        {
                            if (x.FilesystemEntry.Info.IsReadOnly)
                                x.FilesystemEntry.Info.IsReadOnly = false;
                            x.FilesystemEntry.Info.Delete();
                            RemoveFileTimeCache(x.CanonicalName);
                            Printer.PrintMessage("Purging unversioned file {0}", x.CanonicalName);
                        }
                    }
                    catch
                    {
                        Printer.PrintMessage("#x#Couldn't delete {0}", x.CanonicalName);
                    }
                }
                else if (x.Code == StatusCode.Copied)
                {
                    try
                    {
                        if (x.FilesystemEntry.Info.IsReadOnly)
                            x.FilesystemEntry.Info.IsReadOnly = false;

                        x.FilesystemEntry.Info.Delete();
                        RemoveFileTimeCache(x.CanonicalName);
                        Printer.PrintMessage("Purging copied file {0}", x.CanonicalName);
                    }
                    catch
                    {
                        Printer.PrintMessage("#x#Couldn't delete {0}", x.CanonicalName);
                    }
                }
            }
            foreach (var x in deletionList.OrderByDescending(x => x.CanonicalName.Length))
            {
                try
                {
                    x.DirectoryInfo.Delete();
                }
                catch
                {
                    Printer.PrintMessage("#x#Couldn't delete directory {0}", x.CanonicalName);
                }
            }
        }

        public bool ExportRecord(string cannonicalPath, Objects.Version version, string outputPath)
        {
            List<Record> records = Database.GetRecords(version);
            foreach (var x in records)
            {
                if (x.CanonicalName == cannonicalPath)
                {
                    GetMissingRecords(new Record[] { x }.ToList());
                    RestoreRecord(x, DateTime.UtcNow, outputPath);
                    return true;
                }
            }
            return false;
        }

        public class DAG<T, U>
        {
            public class Link
            {
                public U Source { get; set; }
                public bool Merge { get; set; }
            }
            public class ObjectAndLinks
            {
                public T Object { get; set; }
                public List<Link> Links { get; set; }

                public ObjectAndLinks(T obj)
                {
                    Object = obj;
                    Links = new List<Link>();
                }
            }
            public List<ObjectAndLinks> Objects { get; set; }
            public Dictionary<U, Tuple<T, int>> Lookup { get; set; }

            public DAG()
            {
                Objects = new List<ObjectAndLinks>();
                Lookup = new Dictionary<U, Tuple<T, int>>();
            }
        }
        public DAG<Objects.Version, Guid> GetDAG(int? limit)
        {
            var allVersionsList = Database.Table<Objects.Version>().ToList();
            List<Objects.Version> allVersions = null;
            if (limit.HasValue && limit.Value > 0)
                allVersions = allVersionsList.Reverse<Objects.Version>().Take(limit.Value).ToList();
            else
                allVersions = allVersionsList;
            DAG<Objects.Version, Guid> result = new DAG<Objects.Version, Guid>();
            foreach (var x in allVersions)
            {
                result.Lookup[x.ID] = new Tuple<Objects.Version, int>(x, result.Objects.Count);
                var initialLink = new DAG<Objects.Version, Guid>.ObjectAndLinks(x);
                result.Objects.Add(initialLink);
                if (x.Parent.HasValue)
                    initialLink.Links.Add(new DAG<Objects.Version, Guid>.Link() { Source = x.Parent.Value, Merge = false });

                var mergeInfo = GetMergeInfo(x.ID);
                foreach (var y in mergeInfo)
                    initialLink.Links.Add(new DAG<Objects.Version, Guid>.Link() { Source = y.SourceVersion, Merge = true });
            }
            return result;
        }

        public Objects.Version GetPartialVersion(string v)
        {
            Objects.Version version = Database.Find<Objects.Version>(v);
            if (version != null)
                return version;
            List<Objects.Version> potentials;
            string searchmode = "ID prefix";
            if (v.StartsWith("%"))
            {
                searchmode = "revnumber";
                potentials = Database.Query<Objects.Version>("SELECT rowid, * FROM Version WHERE Version.rowid IS ?", int.Parse(v.Substring(1)));
            }
            else if (v.StartsWith("..."))
            {
                searchmode = "ID suffix";
                potentials = Database.Query<Objects.Version>(string.Format("SELECT rowid, * FROM Version WHERE Version.ID LIKE '%{0}'", v.Substring(3)));
            }
            else
                potentials = Database.Query<Objects.Version>(string.Format("SELECT rowid, * FROM Version WHERE Version.ID LIKE '{0}%'", v));
            if (potentials.Count > 1)
            {
                Printer.PrintError("Can't find a unique version with {1}: {0}##\nCould be:", v, searchmode);
                foreach (var x in potentials)
                    Printer.PrintMessage("\t#b#{0}## - branch: \"#b#{1}##\", {2}", x.ID, Database.Get<Objects.Branch>(x.Branch).Name, x.Timestamp.ToLocalTime());
            }
            if (potentials.Count == 1)
                return potentials[0];
            return null;
        }

        private void CleanStage(bool createTransaction = true)
        {
            Printer.PrintDiagnostics("Clearing stage.");
            try
            {
                if (createTransaction)
                    LocalData.BeginTransaction();
                LocalData.DeleteAll<LocalState.StageOperation>();
                if (createTransaction)
                    LocalData.Commit();
            }
            catch
            {
                if (createTransaction)
                    LocalData.Rollback();
                throw;
            }
        }

        public List<Record> GetRecords(Objects.Version v)
        {
            return Database.GetRecords(v);
        }

        private static IEnumerable<T> CheckoutOrder<T>(IList<T> targetRecords)
            where T : ICheckoutOrderable
        {
            // TODO: .vrmeta first.
            foreach (var x in targetRecords.Where(x => x.IsDirective))
            {
                yield return x;
            }
            foreach (var x in targetRecords.Where(x => x.IsDirectory).OrderBy(x => x.CanonicalName.Length))
            {
                yield return x;
            }
            foreach (var x in targetRecords.Where(x => x.IsFile && !x.IsDirective))
            {
                yield return x;
            }
            foreach (var x in targetRecords.Where(x => x.IsSymlink))
            {
                yield return x;
            }
        }

        private static IEnumerable<Record> DeletionOrder(List<Record> records)
        {
            foreach (var x in records.Where(x => !x.IsDirectory))
            {
                yield return x;
            }
            foreach (var x in records.Where(x => x.IsDirectory).OrderByDescending(x => x.CanonicalName.Length))
            {
                yield return x;
            }
        }
        private static IEnumerable<TransientMergeObject> DeletionOrder(List<TransientMergeObject> records)
        {
            foreach (var x in records.Where(x => !x.Record.IsDirectory))
            {
                yield return x;
            }
            foreach (var x in records.Where(x => x.Record.IsDirectory).OrderByDescending(x => x.CanonicalName.Length))
            {
                yield return x;
            }
        }

        public enum RecordUpdateType
        {
            Created,
            Updated,
            AlreadyPresent,
            Deleted
        }

        private void CheckoutInternal(Objects.Version tipVersion, bool verbose)
        {
            List<Record> records = Database.Records;

            List<Record> targetRecords = Database.GetRecords(tipVersion).Where(x => Included(x.CanonicalName)).ToList();

            DateTime newRefTime = DateTime.UtcNow;

            if (!GetMissingRecords(targetRecords))
            {
                Printer.PrintError("Missing record data!");
                return;
            }

            HashSet<string> canonicalNames = new HashSet<string>();
            List<Task> tasks = new List<Task>();
            List<Record> pendingSymlinks = new List<Record>();

            Printer.InteractivePrinter printer = null;
            long totalSize = targetRecords.Sum(x => x.Size);
            int updates = 0;
            int deletions = 0;
            int additions = 0;
            Object completeMarker = false;
            if (targetRecords.Count > 0 && totalSize > 0)
            {
                printer = Printer.CreateProgressBarPrinter(
                string.Empty,
                "Unpacking",
                (obj) =>
                {
                    return Misc.FormatSizeFriendly((long)obj) + " total";
                },
                (obj) =>
                {
                    return (float)(100.0f * (long)obj / (double)totalSize);
                },
                (pct, obj) =>
                {
                    return string.Format("{0:N2}%", pct);
                },
                49);
            }
            long count = 0;
            Action<RecordUpdateType, string, Objects.Record> feedback = (type, name, rec) =>
            {
                if (type == RecordUpdateType.Created)
                    additions++;
                else if (type == RecordUpdateType.Updated)
                    updates++;
                if (verbose && type != RecordUpdateType.AlreadyPresent)
                    Printer.PrintMessage("#b#{0}{2}##: {1}", type == RecordUpdateType.Created ? "Created" : "Updated", name, rec.IsDirectory ? " directory" : "");
                if (printer != null)
                    printer.Update(System.Threading.Interlocked.Add(ref count, rec.Size));
            };
            ConcurrentQueue<FileTimestamp> updatedTimestamps = new ConcurrentQueue<FileTimestamp>();
            HashSet<string> signatures = new HashSet<string>();

            foreach (var x in targetRecords)
                signatures.Add(x.CanonicalName);

            List<Record> recordsToDelete = new List<Record>();
            foreach (var x in records)
            {
                if (signatures.Contains(x.CanonicalName))
                    continue;
                recordsToDelete.Add(x);
            }

            Printer.InteractivePrinter spinner = null;
            int deletionCount = 0;
            foreach (var x in DeletionOrder(recordsToDelete))
            {
                if (!Included(x.CanonicalName))
                    continue;
                if (canonicalNames.Contains(x.CanonicalName))
                    continue;

                if (spinner == null)
                {
                    spinner = Printer.CreateSpinnerPrinter("Deleting", (obj) => { return string.Format("{0} objects.", (int)obj); });
                }
                if ((deletionCount++ & 15) == 0)
                    spinner.Update(deletionCount);

                if (x.IsFile)
                {
                    string path = Path.Combine(Root.FullName, x.CanonicalName);
                    if (System.IO.File.Exists(path))
                    {
                        try
                        {
                            RemoveFileTimeCache(x.CanonicalName, false);
                            System.IO.FileInfo fi = new FileInfo(path);
                            if (fi.IsReadOnly)
                                fi.IsReadOnly = false;
                            fi.Delete();
                            if (verbose)
                                Printer.PrintMessage("#b#Deleted## {0}", x.CanonicalName);
                            deletions++;
                        }
                        catch
                        {
                            Printer.PrintMessage("Couldn't delete `{0}`!", x.CanonicalName);
                        }
                    }
                }
                else if (x.IsSymlink)
                {
                    string path = Path.Combine(Root.FullName, x.CanonicalName);
                    if (Utilities.Symlink.Exists(path))
                    {
                        try
                        {
                            Utilities.Symlink.Delete(path);
                            if (verbose)
                                Printer.PrintMessage("Deleted symlink {0}", x.CanonicalName);
                            deletions++;
                        }
                        catch (Exception e)
                        {
                            Printer.PrintMessage("Couldn't delete symlink `{0}`!\n{1}", x.CanonicalName, e.ToString());
                        }
                    }
                }
                else if (x.IsDirectory)
                {
                    string path = Path.Combine(Root.FullName, x.CanonicalName.Substring(0, x.CanonicalName.Length - 1));
                    if (System.IO.Directory.Exists(path))
                    {
                        try
                        {
                            RemoveFileTimeCache(x.CanonicalName, false);
                            System.IO.Directory.Delete(path);
                            deletions++;
                        }
                        catch
                        {
                            Printer.PrintMessage("Couldn't delete `{0}`, files still present!", x.CanonicalName);
                        }
                    }
                }
            }
            if (spinner != null)
                spinner.End(deletionCount);
            foreach (var x in CheckoutOrder(targetRecords))
            {
                canonicalNames.Add(x.CanonicalName);
                if (x.IsDirectory)
                {
                    System.Threading.Interlocked.Increment(ref count);
                    RestoreRecord(x, newRefTime, null, null, feedback);
                }
                else if (x.IsFile)
                {
                    if (x.IsDirective)
                    {
                        System.Threading.Interlocked.Increment(ref count);
                        RestoreRecord(x, newRefTime, null, null, feedback);
                        LoadDirectives();
                    }
                    else
                    {
                        //RestoreRecord(x, newRefTime);
                        tasks.Add(LimitedTaskDispatcher.Factory.StartNew(() => {
                            RestoreRecord(x, newRefTime, null, updatedTimestamps, feedback);
                        }));
                    }

                }
                else if (x.IsSymlink)
                {
                    try
                    {
                        RestoreRecord(x, newRefTime, null, null, feedback);
                        System.Threading.Interlocked.Increment(ref count);
                    }
                    catch (Utilities.Symlink.TargetNotFoundException)
                    {
                        Printer.PrintDiagnostics("Couldn't resolve symlink {0}, will try later", x.CanonicalName);
                        pendingSymlinks.Add(x);
                    }
                }
            }
            FileTimestamp fst = null;
            try
            {
                while (!Task.WaitAll(tasks.ToArray(), 10000))
                {
                    while (updatedTimestamps.TryDequeue(out fst))
                        UpdateFileTimeCache(fst, false);
                }
                while (updatedTimestamps.TryDequeue(out fst))
                    UpdateFileTimeCache(fst, false);
                LocalData.ReplaceFileTimes(FileTimeCache);
            }
            catch (Exception e)
            {
                throw new Exception("Checkout failed!", e);
            }
            int attempts = 5;
            while (attempts > 0)
            {
                attempts--;
                List<Record> done = new List<Record>();
                foreach (var x in pendingSymlinks)
                {
                    try
                    {
                        RestoreRecord(x, newRefTime, null, null, feedback);
                        done.Add(x);
                        Printer.PrintDiagnostics("Pending symlink {0} resolved with {1} attempts remaining", x.CanonicalName, attempts);
                    }
                    catch (Utilities.Symlink.TargetNotFoundException)
                    {
                        // do nothing...
                        if (attempts == 0)
                            Printer.PrintError("Could not create symlink {0}, because {1} could not be resolved", x.CanonicalName, x.Fingerprint);
                    }
                }
                foreach (var x in done)
                    pendingSymlinks.Remove(x);
            }
            if (printer != null)
                printer.End(totalSize);
            Printer.PrintMessage("#b#{0}## updates, #b#{1}## additions, #b#{2}## deletions.", updates, additions, deletions);

            ReferenceTime = newRefTime;
            LocalData.BeginTransaction();
            try
            {
                var ws = LocalData.Workspace;
                ws.Tip = tipVersion.ID;
                ws.LocalCheckoutTime = ReferenceTime;
                LocalData.Update(ws);
                LocalData.Commit();
            }
            catch (Exception e)
            {
                throw new Exception("Couldn't update local information!", e);
            }

            ProcessExterns(verbose);
        }

        private void ProcessExterns(bool verbose)
        {
            foreach (var x in Directives.Externals)
            {
                string cleanLocation = x.Value.Location.Replace('\\', '/');
                if (Included(cleanLocation))
                {
                    Printer.PrintMessage("Processing extern #c#{0}## at location #b#{1}##", x.Key, cleanLocation);
                    System.IO.DirectoryInfo directory = new System.IO.DirectoryInfo(GetRecordPath(cleanLocation));
                    if (!directory.Exists)
                        directory.Create();
					string url = x.Value.Host;
                    Client client = null;
                    Area external = LoadWorkspace(directory, false, true);
                    bool fresh = false;
                    if (external == null)
                    {
                        client = new Client(directory, true);
                        fresh = true;
                    }
                    else
                        client = new Client(external);

                    if (!client.Connect(url))
                    {
                        Printer.PrintError("#x#Error:##\n  Couldn't connect to remote \"#b#{0}##\" while processing extern \"#b#{1}##\"!", x.Value.Host, x.Key);
                        if (external == null)
                            continue;
                        client = null;
                    }
                    if (external == null)
                    {
                        if (!client.Clone(true))
                        {
                            Printer.PrintError("#x#Error:##\n  Couldn't clone remote repository while processing extern \"#b#{0}##\"!", x.Key);
                            continue;
                        }
                    }
                    if (client != null)
                    {
                        client.Workspace.SetPartialPath(x.Value.PartialPath);
                        client.Workspace.SetRemote(url, "default");
                        if (!fresh && !client.Pull(false, x.Value.Branch))
                        {
                            client.Close();
                            Printer.PrintError("#x#Error:##\n  Couldn't pull remote branch \"#b#{0}##\" while processing extern \"#b#{1}##\"", x.Value.Branch, x.Key);
                            continue;
                        }

                        client.Close();
                        if (!client.ReceivedData && !fresh)
                            continue;
                    }

                    if (external == null)
                        external = client.Workspace;
                    external.SetPartialPath(x.Value.PartialPath);
                    external.SetRemote(url, "default");
                    if (fresh)
                    {
                        if (!String.IsNullOrEmpty(x.Value.Branch))
                        {
                            bool multiple;
                            Objects.Branch externBranch = external.GetBranchByPartialName(string.IsNullOrEmpty(x.Value.Branch) ? external.GetVersion(external.Domain).Branch.ToString() : x.Value.Branch, out multiple);
                            external.Checkout(externBranch.ID.ToString(), false, verbose);
                        }
                        else
                        {
                            external.Checkout(x.Value.Target, false, false);
                        }
                    }
                    else
                    {
                        bool multiple;
                        Objects.Branch externBranch = external.GetBranchByPartialName(string.IsNullOrEmpty(x.Value.Branch) ? external.GetVersion(external.Domain).Branch.ToString() : x.Value.Branch, out multiple);
                        if (x.Value.Target == null)
                        {
                            if (external.CurrentBranch.ID == externBranch.ID)
                                external.Update(new MergeSpecialOptions());
                            else
                            {
                                if (external.Status.HasModifications(false))
                                    Printer.PrintError("#x#Error:##\n  Extern #c#{0}## can't switch to branch \"#b#{1}##\", due to local modifications.", x.Key, externBranch.Name);
                                else
                                    external.Checkout(externBranch.ID.ToString(), false, verbose);
                            }
                        }
                        else
                        {
                            Objects.Version externVersion = external.GetPartialVersion(x.Value.Target);
                            if (externVersion == null)
                                Printer.PrintError("#x#Error:##\n  Extern #c#{0}## can't locate target \"#b#{1}##\"", x.Key, x.Value.Target);
                            else
                            {
                                if (external.Version.ID != externVersion.ID)
                                {
                                    if (external.Status.HasModifications(false))
                                        Printer.PrintError("#x#Error:##\n  Extern #c#{0}## can't switch to version \"#b#{1}##\", due to local modifications.", x.Key, externVersion.ID);
                                    else
                                        external.Checkout(externVersion.ID.ToString(), false, verbose);
                                }
                            }
                        }
                    }
                }
            }
        }

        public List<KeyValuePair<string, Extern>> Externs
        {
            get
            {
                return Directives.Externals.ToList();
            }
        }

        public bool Included(string canonicalName)
        {
            if (canonicalName.Equals(".vrmeta", StringComparison.OrdinalIgnoreCase))
                return true;
            if (canonicalName.StartsWith(LocalData.PartialPath, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

		public IRemoteClient Connect(string url, bool requiresWriteAccess = false)
		{
			// Find a provider that can make this connection
			foreach (var clientProvider in PluginCache.GetImplementations<IRemoteClientProvider>())
			{
				var client = clientProvider.Connect(this, url, requiresWriteAccess);
				if (client != null)
					return client;
			}
			return null;
		}

        public bool GetMissingRecords(IEnumerable<Record> targetRecords)
        {
            List<Record> missingRecords = FindMissingRecords(targetRecords.Where(x => Included(x.CanonicalName)));
            if (missingRecords.Count > 0)
            {
                Printer.PrintMessage("Checking out this version requires {0} remote objects.", missingRecords.Count);
                var configs = LocalData.Table<LocalState.RemoteConfig>().OrderByDescending(x => x.LastPull).ToList();
                foreach (var x in configs)
                {
                    Printer.PrintMessage(" - Attempting to pull data from remote \"{2}\" ({0}:{1})", x.Host, x.Port, x.Name);

					IRemoteClient client = Connect(x.URL);
					if (client == null)
					{
						Printer.PrintMessage(" - Connection failed.");
					}
					else
					{
						try
						{
							List<string> retrievedRecords = client.GetRecordData(missingRecords);
							HashSet<string> retrievedData = new HashSet<string>();
							Printer.PrintMessage(" - Got {0} records from remote.", retrievedRecords.Count);
							foreach (var y in retrievedRecords)
								retrievedData.Add(y);
							missingRecords = missingRecords.Where(z => !retrievedData.Contains(z.DataIdentifier)).ToList();
							client.Close();
						}
						catch
						{
							client.Close();
						}
					}

					if (missingRecords.Count > 0)
                        Printer.PrintMessage("This checkout still requires {0} additional records.", missingRecords.Count);
                    else
                        return true;
                }
            }
            else
                return true;
            return false;
        }

        public List<Record> FindMissingRecords(IEnumerable<Record> targetRecords)
        {
            List<Record> missingRecords = new List<Record>();
            HashSet<string> requestedData = new HashSet<string>();
            foreach (var x in targetRecords)
            {
                List<string> dataRequests = null;
                if (x.Size == 0)
                    continue;
                if (x.IsDirectory)
                    continue;
                if (Included(x.CanonicalName) && !HasObjectData(x, out dataRequests))
                {
                    if (dataRequests != null)
                    {
                        foreach (var y in dataRequests)
                        {
                            if (!requestedData.Contains(y))
                            {
                                if (y == x.DataIdentifier)
                                    missingRecords.Add(x);
                                else
                                    missingRecords.Add(GetRecordFromIdentifier(y));
                            }
                        }
                    }
                }
            }
            return missingRecords;
        }

        private bool Switch(string v)
        {
            bool multiplebranches;
            var branch = GetBranchByPartialName(v, out multiplebranches);
            if (branch == null)
                return false;
            return SwitchBranch(branch);
        }

        private bool SwitchBranch(Objects.Branch branch)
        {
            LocalData.BeginTransaction();
            try
            {
                var ws = LocalData.Workspace;
                ws.Branch = branch.ID;
                LocalData.Update(ws);
                LocalData.Commit();
                return true;
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Couldn't switch!", e);
            }
        }

        public void Revert(IList<Status.StatusEntry> targets, bool revertRecord, bool interactive, bool deleteNewFiles, Action<Versionr.Status.StatusEntry, StatusCode> callback = null)
        {
            List<Status.StatusEntry> directoryDeletionList = new List<Status.StatusEntry>();
            List<Status.StatusEntry> deletionList = new List<Status.StatusEntry>();

            Dictionary<string, Record> recordMap = new Dictionary<string, Record>();
            foreach (var x in Database.Records)
                recordMap[x.CanonicalName] = x;

            Dictionary<string, List<StageOperation>> stageMap = new Dictionary<string, List<StageOperation>>();
            foreach (var y in LocalData.StageOperations)
            {
                List<StageOperation> s;
                if (!stageMap.TryGetValue(y.Operand1, out s))
                {
                    s = new List<StageOperation>();
                    stageMap[y.Operand1] = s;
                }
                s.Add(y);
            }

            LocalData.BeginTransaction();
            try
            {
                foreach (var x in CheckoutOrder(targets))
                {
                    if (!Included(x.CanonicalName))
                        continue;
                    if (interactive && (x.Staged || (revertRecord && x.Code != StatusCode.Unchanged)))
                    {
                        Printer.PrintMessageSingleLine("{1} object #b#{0}##", x.CanonicalName, (revertRecord && x.Code == StatusCode.Modified) ? "#e#Revert##" : "#b#Unrecord##");
                        bool skip = false;
                        bool stop = false;
                        while (true)
                        {
                            Printer.PrintMessageSingleLine(" [(y)es, (n)o, (s)top]? ");
                            string input = System.Console.ReadLine();
                            if (input.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                                break;
                            if (input.StartsWith("s", StringComparison.OrdinalIgnoreCase))
                            {
                                stop = true;
                                break;
                            }
                            if (input.StartsWith("n", StringComparison.OrdinalIgnoreCase))
                            {
                                skip = true;
                                break;
                            }
                        }
                        if (stop)
                            break;
                        if (skip)
                            continue;
                    }
                    List<StageOperation> ops;
                    if (stageMap.TryGetValue(x.CanonicalName, out ops))
                    {
                        foreach (var y in ops)
                            LocalData.Delete(y);
                        ops.Clear();
                    }
                    if (x.Staged == true)
                    {
                        if (callback != null)
                        {
                            callback(x,
                                (x.Code == StatusCode.Deleted ? StatusCode.Missing :
                                (x.Code == StatusCode.Added ? StatusCode.Unversioned :
                                x.Code)));
                        }
                    }
                    else if (x.Code == StatusCode.Conflict)
                    {
                        //Printer.PrintMessage("Marking {0} as resolved.", x.CanonicalName);
                        foreach (var y in LocalData.StageOperations)
                        {
                            if (y.Type == StageOperationType.Conflict && y.Operand1 == x.CanonicalName)
                            {
                                LocalData.Delete(y);
                            }
                        }
                        if (callback != null)
                            callback(x, StatusCode.Modified);
                    }

                    if (revertRecord && x.Code != StatusCode.Unchanged)
                    {
                        Record rec = null;
                        recordMap.TryGetValue(x.CanonicalName, out rec);
                        if (rec != null)
                        {
                            Printer.PrintMessage("Reverted: #b#{0}##", x.CanonicalName);
                            RestoreRecord(rec, DateTime.UtcNow);
                        }
                        if (deleteNewFiles &&
                            (x.Code == StatusCode.Unversioned || x.Code == StatusCode.Added || x.Code == StatusCode.Copied || x.Code == StatusCode.Renamed))
                        {
                            if (x.FilesystemEntry.IsDirectory)
                                directoryDeletionList.Add(x);
                            else
                                deletionList.Add(x);
                        }
                    }
                }
                LocalData.Commit();
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Unable to remove stage operations!", e);
            }
            foreach (var x in deletionList)
            {
                Printer.PrintMessage("#e#Deleted:## #b#{0}##", x.CanonicalName);
                if (x.FilesystemEntry.Info.IsReadOnly)
                    x.FilesystemEntry.Info.IsReadOnly = false;
                x.FilesystemEntry.Info.Delete();
            }
            foreach (var x in directoryDeletionList.OrderByDescending(x => x.CanonicalName.Length))
            {
                Printer.PrintMessage("#e#Removed:## #b#{0}##", x.CanonicalName);
                try
                {
                    x.FilesystemEntry.DirectoryInfo.Delete();
                }
                catch
                {
                    Printer.PrintMessage(" #q#(failed to delete folder - not empty?)##");
                }
            }
        }

        public void Resolve(IList<Status.StatusEntry> targets, Action<Versionr.Status.StatusEntry, StatusCode> callback = null)
        {
            List<Status.StatusEntry> directoryDeletionList = new List<Status.StatusEntry>();
            List<Status.StatusEntry> deletionList = new List<Status.StatusEntry>();

            Dictionary<string, Record> recordMap = new Dictionary<string, Record>();
            foreach (var x in Database.Records)
                recordMap[x.CanonicalName] = x;

            LocalData.BeginTransaction();
            try
            {
                foreach (var x in CheckoutOrder(targets))
                {
                    if (!Included(x.CanonicalName))
                        continue;
                    foreach (var y in LocalData.StageOperations)
                    {
                        if (y.Operand1 == x.CanonicalName && y.Type == StageOperationType.Conflict)
                        {
                            LocalData.Delete(y);
                            Printer.PrintMessage("#g#Resolved:## {0}", x.CanonicalName);
                            FileInfo mineFile = new FileInfo(GetRecordPath(x.CanonicalName + ".mine"));
                            FileInfo theirsFile = new FileInfo(GetRecordPath(x.CanonicalName + ".theirs"));
                            FileInfo baseFile = new FileInfo(GetRecordPath(x.CanonicalName + ".base"));
                            if (mineFile.Exists)
                            {
                                Printer.PrintMessage("#w#Deleting:## {0}", GetLocalCanonicalName(mineFile.FullName));
                                try
                                {
                                    if (mineFile.IsReadOnly)
                                        mineFile.IsReadOnly = false;
                                    mineFile.Delete();
                                }
                                catch (Exception)
                                {
                                    Printer.PrintMessage("Couldn't delete: {0}", mineFile.FullName);
                                }
                            }
                            if (theirsFile.Exists)
                            {
                                Printer.PrintMessage("#w#Deleting:## {0}", GetLocalCanonicalName(theirsFile.FullName));
                                try
                                {
                                    if (theirsFile.IsReadOnly)
                                        theirsFile.IsReadOnly = false;
                                    theirsFile.Delete();
                                }
                                catch (Exception)
                                {
                                    Printer.PrintMessage("Couldn't delete: {0}", theirsFile.FullName);
                                }
                            }
                            if (baseFile.Exists)
                            {
                                Printer.PrintMessage("#w#Deleting:## {0}", GetLocalCanonicalName(baseFile.FullName));
                                try
                                {
                                    if (baseFile.IsReadOnly)
                                        baseFile.IsReadOnly = false;
                                    baseFile.Delete();
                                }
                                catch (Exception)
                                {
                                    Printer.PrintMessage("Couldn't delete: {0}", baseFile.FullName);
                                }
                            }
                        }
                    }
                }
                LocalData.Commit();
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Unable to remove stage operations!", e);
            }
            foreach (var x in deletionList)
            {
                Printer.PrintMessage("#e#Deleted:## #b#{0}##", x.CanonicalName);
                if (x.FilesystemEntry.Info.IsReadOnly)
                    x.FilesystemEntry.Info.IsReadOnly = false;
                x.FilesystemEntry.Info.Delete();
            }
            foreach (var x in directoryDeletionList.OrderByDescending(x => x.CanonicalName.Length))
            {
                Printer.PrintMessage("#e#Removed:## #b#{0}##", x.CanonicalName);
                x.FilesystemEntry.DirectoryInfo.Delete();
            }
        }

        public bool Commit(string message = "", bool force = false)
        {
            List<Guid> mergeIDs = new List<Guid>();
            List<Guid> reintegrates = new List<Guid>();
            Printer.PrintDiagnostics("Checking stage info for pending conflicts...");
            foreach (var x in LocalData.StageOperations)
            {
                if (x.Type == StageOperationType.Conflict)
                {
                    Printer.PrintError("#x#Error:##\n  Can't commit while pending conflicts on file \"#b#{0}##\"!", x.Operand1);
                    return false;
                }
                if (x.Type == StageOperationType.Merge)
                    mergeIDs.Add(new Guid(x.Operand1));
                if (x.Type == StageOperationType.Reintegrate)
                    reintegrates.Add(new Guid(x.Operand1));
            }
            try
            {
                bool result = RunLocked(() =>
                {
                    Objects.Version parentVersion = Database.Version;
                    Printer.PrintDiagnostics("Getting status for commit.");
                    Status st = new Status(this, Database, LocalData, FileSnapshot, null, false);
                    if (st.HasModifications(true) || mergeIDs.Count > 0)
                    {
                        Printer.PrintMessage("Committing changes..");
                        Versionr.ObjectStore.ObjectStoreTransaction transaction = null;
                        try
                        {
                            Objects.Version vs = null;
                            vs = Objects.Version.Create();
                            vs.Author = Username;
                            vs.Parent = Database.Version.ID;
                            vs.Branch = Database.Branch.ID;
                            Printer.PrintDiagnostics("Created new version ID - {0}", vs.ID);
                            List<Objects.MergeInfo> mergeInfos = new List<MergeInfo>();
                            List<Objects.Head> mergeHeads = new List<Head>();
                            foreach (var guid in mergeIDs)
                            {
                                Objects.MergeInfo mergeInfo = new MergeInfo();
                                mergeInfo.SourceVersion = guid;
                                mergeInfo.DestinationVersion = vs.ID;
                                var mergeVersion = GetVersion(guid);

                                Printer.PrintMessage("Input merge: #b#{0}## on branch \"#b#{1}##\" (rev {2})", guid, GetBranch(mergeVersion.Branch).Name, mergeVersion.Revision);
                                Objects.Head mergeHead = Database.Table<Objects.Head>().Where(x => x.Version == guid).ToList().Where(x => x.Branch == Database.Branch.ID).FirstOrDefault();
                                if (mergeHead != null)
                                {
                                    Printer.PrintMessage("#q# - Deleting head reference.");
                                    mergeHeads.Add(mergeHead);
                                }
                                mergeInfos.Add(mergeInfo);
                            }
                            vs.Message = message;
                            vs.Timestamp = DateTime.UtcNow;

                            Objects.Branch branch = Database.Branch;
                            Objects.Head head = null;
                            bool newHead = false;
                            head = Database.Find<Objects.Head>(x => x.Version == vs.Parent && x.Branch == branch.ID);
                            if (head == null)
                            {
                                Printer.PrintDiagnostics("No branch head with prior version present. Inserting new head.");
                                head = Database.Find<Objects.Head>(x => x.Branch == branch.ID);
                                if (head != null && !force)
                                {
                                    Printer.PrintError("#x#Error:##\n   Branch already has head but current version is not a direct child.\nA new head has to be inserted, but this requires that the #b#`--force`## option is used.");
                                    return false;
                                }
                                else
                                    Printer.PrintWarning("#w#This branch has no previously recorded head, but a new head has to be inserted.");
                                head = new Head();
                                head.Branch = branch.ID;
                                newHead = true;
                            }
                            else
                                Printer.PrintDiagnostics("Existing head for current version found. Updating branch head.");
                            if (branch.Terminus.HasValue)
                            {
                                if (GetHistory(GetVersion(vs.Parent.Value)).Where(z => z.ID == branch.Terminus.Value).FirstOrDefault() == null)
                                {
                                    Printer.PrintError("#x#Error:##\n   Branch was deleted and parent revision is not a child of the branch terminus. Aborting commit.");
                                    return false;
                                }
                            }
                            head.Version = vs.ID;

                            List<Objects.Alteration> alterations = new List<Alteration>();
                            List<Objects.Record> records = new List<Record>();
                            HashSet<Objects.Record> finalRecords = new HashSet<Record>();
                            List<Tuple<Objects.Record, Objects.Alteration>> alterationLinkages = new List<Tuple<Record, Alteration>>();
                            HashSet<string> stagedChanges = new HashSet<string>(LocalData.StageOperations.Where(x => x.Type == StageOperationType.Add).Select(x => x.Operand1));

                            Dictionary<string, List<StageOperation>> fullStageInfo = LocalData.GetMappedStage();

                            Dictionary<string, ObjectName> canonicalNames = new Dictionary<string, ObjectName>();
                            foreach (var x in Database.Table<ObjectName>().ToList())
                                canonicalNames[x.CanonicalName] = x;
                            List<Tuple<Record, ObjectName>> canonicalNameInsertions = new List<Tuple<Record, ObjectName>>();

                            transaction = ObjectStore.BeginStorageTransaction();

                            foreach (var x in st.Elements)
                            {
                                List<StageOperation> stagedOps;
                                fullStageInfo.TryGetValue(x.FilesystemEntry != null ? x.FilesystemEntry.CanonicalName : x.VersionControlRecord.CanonicalName, out stagedOps);
                                switch (x.Code)
                                {
                                    case StatusCode.Deleted:
                                        {
                                            Printer.PrintMessage("Deleted: #b#{0}##", x.VersionControlRecord.CanonicalName);
                                            Printer.PrintDiagnostics("Recorded deletion: {0}, old record: {1}", x.VersionControlRecord.CanonicalName, x.VersionControlRecord.Id);
                                            Objects.Alteration alteration = new Alteration();
                                            alteration.PriorRecord = x.VersionControlRecord.Id;
                                            alteration.Type = AlterationType.Delete;
                                            alterations.Add(alteration);
                                        }
                                        break;
                                    case StatusCode.Masked:
                                    case StatusCode.Added:
                                    case StatusCode.Modified:
                                    case StatusCode.Renamed:
                                    case StatusCode.Copied:
                                        {
                                            try
                                            {
                                                if ((x.Code == StatusCode.Renamed || x.Code == StatusCode.Modified)
                                                    && !stagedChanges.Contains(x.FilesystemEntry.CanonicalName))
                                                {
                                                    finalRecords.Add(x.VersionControlRecord);
                                                    break;
                                                }
                                                if (x.Code == StatusCode.Copied)
                                                {
                                                    if (!stagedChanges.Contains(x.FilesystemEntry.CanonicalName))
                                                        break;
                                                }
                                                Objects.Record record = null;
                                                bool recordIsMerged = false;
                                                if (stagedOps != null)
                                                {
                                                    foreach (var op in stagedOps)
                                                    {
                                                        if (op.Type == StageOperationType.MergeRecord)
                                                        {
                                                            if (op.ReferenceObject == -1 && x.Code == StatusCode.Masked)
                                                            {
                                                                record = null;
                                                                recordIsMerged = true;
                                                                break;
                                                            }
                                                            else
                                                            {
                                                                Objects.Record mergedRecord = GetRecord(op.ReferenceObject);
                                                                if (x.Code == StatusCode.Masked || (mergedRecord.Size == x.FilesystemEntry.Length && mergedRecord.Fingerprint == x.FilesystemEntry.Hash))
                                                                {
                                                                    record = mergedRecord;
                                                                    recordIsMerged = true;
                                                                    break;
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                if (x.Code == StatusCode.Masked)
                                                {
                                                    if (recordIsMerged)
                                                    {
                                                        if (record == null)
                                                        {
                                                            if (x.VersionControlRecord != null)
                                                            {
                                                                Printer.PrintMessage("Removed (ignored locally): #b#{0}##", x.CanonicalName);
                                                                Alteration localAlteration = new Alteration();
                                                                localAlteration.Type = AlterationType.Delete;
                                                                localAlteration.PriorRecord = x.VersionControlRecord.Id;
                                                                alterations.Add(localAlteration);
                                                            }
                                                            break;
                                                        }
                                                        else
                                                        {
                                                            Printer.PrintMessage("Updated (ignored locally): #b#{0}##", x.CanonicalName);
                                                            finalRecords.Add(record);
                                                            Alteration localAlteration = new Alteration();
                                                            alterationLinkages.Add(new Tuple<Record, Alteration>(record, localAlteration));
                                                            if (x.VersionControlRecord != null)
                                                            {
                                                                localAlteration.Type = AlterationType.Update;
                                                                localAlteration.PriorRecord = x.VersionControlRecord.Id;
                                                            }
                                                            else
                                                                localAlteration.Type = AlterationType.Add;
                                                            alterations.Add(localAlteration);
                                                            break;
                                                        }
                                                    }
                                                    else
                                                    {
                                                        finalRecords.Add(x.VersionControlRecord);
                                                        break;
                                                    }
                                                }
                                                FileStream stream = null;
                                                Objects.Alteration alteration = new Alteration();
                                                try
                                                {
                                                    if (!x.IsDirectory && !x.IsSymlink && x.Code != StatusCode.Masked)
                                                    {
                                                        stream = x.FilesystemEntry.Info.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
                                                        FileInfo info = new FileInfo(x.FilesystemEntry.Info.FullName);
                                                        DateTime overrideDateTime = info.LastWriteTimeUtc;
                                                        if (overrideDateTime != x.FilesystemEntry.ModificationTime)
                                                        {
                                                            x.FilesystemEntry.ModificationTime = overrideDateTime;
                                                            x.FilesystemEntry.Hash = Entry.CheckHash(info);
                                                        }
                                                    }

                                                    if (record == null)
                                                    {
                                                        record = new Objects.Record();
                                                        record.CanonicalName = x.FilesystemEntry.CanonicalName;
                                                        record.Attributes = x.FilesystemEntry.Attributes;
                                                        if (record.IsFile && FileClassifier.Classify(x.FilesystemEntry.Info) == FileEncoding.Binary)
                                                            record.Attributes = (Attributes)((int)record.Attributes | (int)Attributes.Binary);
                                                        if (record.IsSymlink)
                                                            record.Fingerprint = x.FilesystemEntry.SymlinkTarget;
                                                        else if (record.IsDirectory)
                                                            record.Fingerprint = x.FilesystemEntry.CanonicalName;
                                                        else
                                                            record.Fingerprint = x.FilesystemEntry.Hash;
                                                        record.Size = x.FilesystemEntry.Length;
                                                        record.ModificationTime = x.FilesystemEntry.ModificationTime;
                                                        if (x.VersionControlRecord != null)
                                                            record.Parent = x.VersionControlRecord.Id;
                                                    }
                                                    Objects.Record possibleRecord = LocateRecord(record);
                                                    if (possibleRecord != null)
                                                        record = possibleRecord;

                                                    alterationLinkages.Add(new Tuple<Record, Alteration>(record, alteration));
                                                    if (x.Code == StatusCode.Added)
                                                    {
                                                        Printer.PrintMessage("Added: #b#{0}##", x.FilesystemEntry.CanonicalName);
                                                        Printer.PrintDiagnostics("Recorded addition: {0}", x.FilesystemEntry.CanonicalName);
                                                        alteration.Type = AlterationType.Add;
                                                    }
                                                    else if (x.Code == StatusCode.Modified)
                                                    {
                                                        Printer.PrintMessage("Updated: #b#{0}##", x.FilesystemEntry.CanonicalName);
                                                        Printer.PrintDiagnostics("Recorded update: {0}", x.FilesystemEntry.CanonicalName);
                                                        alteration.PriorRecord = x.VersionControlRecord.Id;
                                                        alteration.Type = AlterationType.Update;
                                                    }
                                                    else if (x.Code == StatusCode.Copied)
                                                    {
                                                        Printer.PrintMessage("Copied: #b#{0}##", x.FilesystemEntry.CanonicalName);
                                                        Printer.PrintDiagnostics("Recorded copy: {0}, from: {1}", x.FilesystemEntry.CanonicalName, x.VersionControlRecord.CanonicalName);
                                                        alteration.PriorRecord = x.VersionControlRecord.Id;
                                                        alteration.Type = AlterationType.Copy;
                                                    }
                                                    else if (x.Code == StatusCode.Renamed)
                                                    {
                                                        Printer.PrintMessage("Renamed: #b#{0}##", x.FilesystemEntry.CanonicalName);
                                                        Printer.PrintDiagnostics("Recorded rename: {0}, from: {1}", x.FilesystemEntry.CanonicalName, x.VersionControlRecord.CanonicalName);
                                                        alteration.PriorRecord = x.VersionControlRecord.Id;
                                                        alteration.Type = AlterationType.Move;
                                                    }
                                                    List<string> ignored;
                                                    if (!ObjectStore.HasData(record, out ignored))
                                                        ObjectStore.RecordData(transaction, record, x.VersionControlRecord, x.FilesystemEntry);

                                                    if (stream != null)
                                                        stream.Close();
                                                }
                                                catch (Exception e)
                                                {
                                                    Printer.PrintError("Error:#e#\n File operations on #b#{0}#e# did not succeed.\n\n#b#Internal error:##\n{1}.", x.CanonicalName, e);
                                                    throw new VersionrException();
                                                }

                                                ObjectName nameRecord = null;
                                                if (canonicalNames.TryGetValue(x.FilesystemEntry.CanonicalName, out nameRecord))
                                                {
                                                    record.CanonicalNameId = nameRecord.NameId;
                                                }
                                                else
                                                {
                                                    canonicalNameInsertions.Add(new Tuple<Record, ObjectName>(record, new ObjectName() { CanonicalName = x.FilesystemEntry.CanonicalName }));
                                                }

                                                if (x.Code != StatusCode.Masked)
                                                {
                                                    Printer.PrintDiagnostics("Created new object record: {0}", x.FilesystemEntry.CanonicalName);
                                                    Printer.PrintDiagnostics("Record fingerprint: {0}", record.Fingerprint);
                                                    if (record.Parent != null)
                                                        Printer.PrintDiagnostics("Record parent ID: {0}", record.Parent);
                                                }

                                                finalRecords.Add(record);
                                                alterations.Add(alteration);
                                                if (!recordIsMerged)
                                                    records.Add(record);
                                            }
                                            catch (Exception e)
                                            {
                                                Printer.PrintError("Failed to add {0}!", x.FilesystemEntry.CanonicalName);
                                                throw e;
                                            }
                                            break;
                                        }
                                    case StatusCode.Obstructed:
                                        if (x.VersionControlRecord != null && !x.Staged)
                                        {
                                            finalRecords.Add(x.VersionControlRecord);
                                            break;
                                        }
                                        else if (x.VersionControlRecord == null)
                                        {
                                            break;
                                        }
                                        else
                                        {
                                            Printer.PrintError("Error:#e#\n Aborting commit. Obstructed file #b#\"{0}\"#e# is included in record list.", x.CanonicalName);
                                            throw new VersionrException();
                                        }
                                    case StatusCode.Unchanged:
                                    case StatusCode.Missing:
                                        finalRecords.Add(x.VersionControlRecord);
                                        break;
                                    case StatusCode.Unversioned:
                                    default:
                                        break;
                                }
                            }

                            ObjectStore.EndStorageTransaction(transaction);
                            transaction = null;

                            Printer.PrintMessage("Updating internal state.");
                            Objects.Snapshot ss = new Snapshot();
                            Database.BeginTransaction();

                            foreach (var z in reintegrates)
                            {
                                Objects.Branch deletedBranch = Database.Get<Objects.Branch>(z);
                                DeleteBranchNoTransaction(deletedBranch);
                            }

                            if (branch.Terminus.HasValue)
                            {
                                Printer.PrintWarning("#w#Undeleting branch...");
                                BranchJournal journal = GetBranchJournalTip();
                                BranchJournal change = new BranchJournal();
                                change.Branch = branch.ID;
                                change.ID = Guid.NewGuid();
                                change.Operand = null;
                                change.Type = BranchAlterationType.Terminate;
                                InsertBranchJournalChangeNoTransaction(journal, change, false);

                                head = Database.Find<Objects.Head>(x => x.Version == branch.Terminus.Value && x.Branch == branch.ID);
                                head.Version = vs.ID;
                                newHead = false;
                            }
                            Database.InsertSafe(ss);
                            vs.AlterationList = ss.Id;
                            Printer.PrintDiagnostics("Adding {0} object records.", records.Count);
                            foreach (var x in canonicalNameInsertions)
                            {
                                if (!Database.InsertSafe(x.Item2))
                                {
                                    var name = Database.Get<ObjectName>(z => z.CanonicalName == x.Item2.CanonicalName);
                                    x.Item2.NameId = name.NameId;
                                }
                                x.Item1.CanonicalNameId = x.Item2.NameId;
                            }
                            foreach (var x in records)
                            {
                                UpdateFileTimeCache(x.CanonicalName, x, x.ModificationTime, true);
                                Database.InsertSafe(x);
                            }
                            foreach (var x in alterationLinkages)
                                x.Item2.NewRecord = x.Item1.Id;

                            Printer.PrintDiagnostics("Adding {0} alteration records.", alterations.Count);
                            foreach (var x in alterations)
                            {
                                x.Owner = ss.Id;
                                Database.InsertSafe(x);
                            }
                            foreach (var info in mergeInfos)
                                Database.InsertSafe(info);

                            if (newHead)
                                Database.InsertSafe(head);
                            else
                                Database.UpdateSafe(head);

                            foreach (var mergeHead in mergeHeads)
                            {
                                if (mergeHead != null)
                                    Database.DeleteSafe(mergeHead);
                            }
                            Database.InsertSafe(vs);

                            var ws = LocalData.Workspace;
                            ws.Tip = vs.ID;
                            LocalData.UpdateSafe(ws);

                            try
                            {
                                Database.GetCachedRecords(Version, true);
                            }
                            catch
                            {
                                throw new Exception("Critical: commit operation is generating an invalid set of alterations!");
                            }

                            Database.Commit();
                            Printer.PrintDiagnostics("Finished.");
                            CleanStage(false);
                            Printer.PrintMessage("At version #b#{0}## on branch \"#b#{1}##\" (rev {2})", Database.Version.ID, Database.Branch.Name, Database.Version.Revision);
                        }
                        catch (Exception e)
                        {
                            if (transaction != null)
                                ObjectStore.AbortStorageTransaction(transaction);
                            Database.Rollback();
                            if (e is VersionrException)
                                return false;
                            Printer.PrintError("Exception during commit: {0}", e.ToString());
                            throw;
                        }
                        finally
                        {
                            if (transaction != null)
                                ObjectStore.AbortStorageTransaction(transaction);
                        }
                    }
                    else
                    {
                        Printer.PrintWarning("#w#Warning:##\n  Nothing to do.");
                        return false;
                    }
                    return true;
                }, true);
                return result;
            }
            catch
            {
                Printer.PrintWarning("\n#x#Error:##\n  Error during commit. Rolling back.");
                return false;
            }
        }

        public bool RunLocked(Func<bool> lockedFunction, bool inform = false)
        {
            try
            {
                if (!LocalData.BeginImmediate(false))
                {
                    if (inform)
                        Printer.PrintWarning("Couldn't acquire lock. Waiting.");
                    LocalData.BeginImmediate(true);
                }
                while (!LocalData.AcquireLock())
                {
                    System.Threading.Thread.Yield();
                }
                return lockedFunction();
            }
            catch
            {
                LocalData.Rollback();
                throw;
            }
            finally
            {
                LocalData.Commit();
            }
        }

        public void RestoreRecord(Record rec, DateTime referenceTime, string overridePath = null, ConcurrentQueue<FileTimestamp> updatedTimestamps = null, Action<RecordUpdateType, string, Objects.Record> feedback = null)
        {
            if (rec.IsSymlink)
            {
                string path = GetRecordPath(rec);
                if (!Utilities.Symlink.Exists(path) || Utilities.Symlink.GetTarget(path) != rec.Fingerprint)
                {
                    if (Utilities.Symlink.Create(path, rec.Fingerprint, true))
                        Printer.PrintMessage("Created symlink {0} -> {1}", GetLocalCanonicalName(rec), rec.Fingerprint);
                }
                return;
            }
            // Otherwise, have to make sure we first get rid of the symlink to replace with the real file/dir
            else
                Utilities.Symlink.Delete(GetRecordPath(rec));

            if (rec.IsDirectory)
            {
                string recPath = GetRecordPath(rec);
                DirectoryInfo directory = new DirectoryInfo(recPath);
                string fullCasedPath = directory.GetFullNameWithCorrectCase();
                if (MultiArchPInvoke.RunningPlatform == Platform.Windows)
                    recPath = recPath.Replace('/', '\\');
                if (fullCasedPath != recPath)
                {
                    // Fix for possible parent renames being broken too
                    System.IO.DirectoryInfo casedInfo = new DirectoryInfo(fullCasedPath);
                    System.IO.DirectoryInfo currentDirectory = directory;
                    while (true)
                    {
                        if (string.Equals(casedInfo.FullName, Root.FullName, StringComparison.OrdinalIgnoreCase))
                            break;
                        if (casedInfo.Name != currentDirectory.Name)
                        {
                            string tempName = Path.Combine(casedInfo.Parent.FullName, currentDirectory.Name + "_" + System.IO.Path.GetRandomFileName());
                            System.IO.Directory.Move(casedInfo.FullName, tempName);
                            System.IO.Directory.Move(tempName, currentDirectory.FullName);
                        }
                        currentDirectory = currentDirectory.Parent;
                        casedInfo = casedInfo.Parent;
                    }
                }
                if (!directory.Exists)
                {
                    if (feedback == null)
                        Printer.PrintMessage("Creating directory {0}", GetLocalCanonicalName(rec));
                    else
                        feedback(RecordUpdateType.Created, GetLocalCanonicalName(rec), rec);
                    directory.Create();
                    ApplyAttributes(directory, referenceTime, rec);
                }
                return;
            }
            FileInfo dest = overridePath == null ? new FileInfo(GetRecordPath(rec)) : new FileInfo(overridePath);
            if (overridePath == null && dest.Exists)
            {
                FileInfo caseCheck = dest.GetCorrectCase();
                if (caseCheck.Name != dest.Name)
                {
                    caseCheck.MoveTo(Path.GetRandomFileName());
                    caseCheck.MoveTo(GetRecordPath(rec));
                    dest = caseCheck;
                }
                FileTimestamp fst = GetReferenceTime(rec.CanonicalName);

                if (dest.LastWriteTimeUtc == fst.LastSeenTime && dest.Length == rec.Size && rec.DataIdentifier == fst.DataIdentifier)
                {
                    if (feedback != null)
                        feedback(RecordUpdateType.AlreadyPresent, GetLocalCanonicalName(rec), rec);
                    return;
                }
                if (dest.Length == rec.Size)
                {
                    Printer.PrintDiagnostics("Hashing: " + rec.CanonicalName);
                    if (Entry.CheckHash(dest) == rec.Fingerprint)
                    {
                        if (updatedTimestamps == null)
                            UpdateFileTimeCache(rec.CanonicalName, rec, dest.LastWriteTimeUtc);
                        else
                            updatedTimestamps.Enqueue(new FileTimestamp() { CanonicalName = rec.CanonicalName, DataIdentifier = rec.DataIdentifier, LastSeenTime = dest.LastWriteTimeUtc });
                        if (feedback != null)
                            feedback(RecordUpdateType.AlreadyPresent, GetLocalCanonicalName(rec), rec);
                        return;
                    }
                }
                if (overridePath == null)
                {
                    if (feedback == null)
                        Printer.PrintMessage("#M#Updating:## {0}", GetLocalCanonicalName(rec));
                    else
                        feedback(RecordUpdateType.Updated, GetLocalCanonicalName(rec), rec);
                }
            }
            else if (overridePath == null)
            {
                if (feedback == null)
                    Printer.PrintMessage("#s#Creating:## {0}", GetLocalCanonicalName(rec));
                else
                    feedback(RecordUpdateType.Created, GetLocalCanonicalName(rec), rec);
            }
            int retries = 0;
            Retry:
            try
            {
                dest.Directory.Create();
                if (rec.Size == 0)
                {
                    using (var fs = dest.Create()) { }
                }
                else
                {
                    if (dest.Exists && dest.IsReadOnly)
                        dest.IsReadOnly = false;
                    using (var fsd = dest.Open(FileMode.Create))
                    {
                        ObjectStore.WriteRecordStream(rec, fsd);
                    }
                }
                ApplyAttributes(dest, referenceTime, rec);
                if (dest.Length != rec.Size)
                {
                    Printer.PrintError("Size mismatch after decoding record!");
                    Printer.PrintError(" - Expected: {0}", rec.Size);
                    Printer.PrintError(" - Actual: {0}", dest.Length);
                    throw new Exception();
                }
                string hash = Entry.CheckHash(dest);
                if (hash != rec.Fingerprint)
                {
                    Printer.PrintError("Hash mismatch after decoding record!");
                    Printer.PrintError(" - Expected: {0}", rec.Fingerprint);
                    Printer.PrintError(" - Found: {0}", hash);
                }
                else
                {
                    if (overridePath == null)
                    {
                        if (updatedTimestamps == null)
                            UpdateFileTimeCache(rec.CanonicalName, rec, dest.LastWriteTimeUtc);
                        else
                            updatedTimestamps.Enqueue(new FileTimestamp() { CanonicalName = rec.CanonicalName, DataIdentifier = rec.DataIdentifier, LastSeenTime = dest.LastWriteTimeUtc });
                    }
                }
            }
            catch (System.IO.IOException)
            {
                if (retries++ == 10)
                {
                    Printer.PrintError("Couldn't write file \"{0}\"!", rec.CanonicalName);
                    return;
                }
                System.Threading.Thread.Sleep(100);
                goto Retry;
            }
            catch (System.UnauthorizedAccessException)
            {
                if (retries++ == 10)
                {
                    Printer.PrintError("Couldn't write file \"{0}\"!", rec.CanonicalName);
                    return;
                }
                System.Threading.Thread.Sleep(100);
                goto Retry;
            }
        }

        public string PartialPath
        {
            get
            {
                return LocalData.PartialPath;
            }
        }

        public Branch RootBranch
        {
            get
            {
                return Database.Get<Objects.Branch>(GetVersion(Domain).Branch);
            }
        }

        public long LastVersion
        {
            get
            {
                return Database.ExecuteScalar<long>("SELECT rowid FROM Version ORDER BY rowid DESC LIMIT 1");
            }
        }

        public long LastBranch
        {
            get
            {
                return Database.ExecuteScalar<long>("SELECT rowid FROM Branch ORDER BY rowid DESC LIMIT 1");
            }
        }

        public List<Guid> LocalLockTokens
        {
            get
            {
                return LocalData.Table<LocalState.RemoteLock>().ToList().Select(x => x.ID).ToList();
            }
        }

        public List<RemoteLock> HeldLocks
        {
            get
            {
                return LocalData.Table<RemoteLock>().ToList();
            }
        }

        public bool HasPendingMerge
        {
            get
            {
                return LocalData.StageOperations.Any(x => x.Type == StageOperationType.Merge);
            }
        }

        public List<Guid> StagedMergeInputs
        {
            get
            {
                return LocalData.StageOperations.Where(x => x.Type == StageOperationType.Merge).Select(x => new Guid(x.Operand1)).ToList();
            }
        }

        public string GetLocalCanonicalName(Record rec)
        {
            return GetLocalCanonicalName(rec.CanonicalName);
        }

        public string GetLocalCanonicalName(string name)
        {
            string canonicalPath = name;
            if (canonicalPath.Equals(".vrmeta", StringComparison.OrdinalIgnoreCase))
                return canonicalPath;
#if DEBUG
            if (!name.StartsWith(LocalData.PartialPath))
                throw new Exception();
#endif
            return canonicalPath.Substring(LocalData.PartialPath.Length);
        }

        private string GetRecordPath(string name)
        {
            return Path.Combine(Root.FullName, GetLocalCanonicalName(name));
        }

        private string GetRecordPath(Record rec)
        {
            return Path.Combine(Root.FullName, GetLocalCanonicalName(rec.CanonicalName));
        }

        public void UpdateFileTimeCache(FileTimestamp fst, bool commit = true)
        {
            lock (FileTimeCache)
            {
                bool present = false;
                if (FileTimeCache.ContainsKey(fst.CanonicalName))
                    present = true;
                FileTimeCache[fst.CanonicalName] = fst;
                if (commit)
                    LocalData.UpdateFileTime(fst.CanonicalName, fst, present);
            }
        }

        public void UpdateFileTimeCache(string canonicalName, Record rec, DateTime lastAccessTimeUtc, bool commit = true)
        {
            if (string.IsNullOrEmpty(canonicalName))
                return;
            UpdateFileTimeCache(new FileTimestamp() { DataIdentifier = rec.DataIdentifier, LastSeenTime = lastAccessTimeUtc, CanonicalName = canonicalName }, commit);
        }

        private void ApplyAttributes(FileSystemInfo info, DateTime newRefTime, Attributes attrib)
        {
            info.LastWriteTimeUtc = newRefTime;
            if (attrib.HasFlag(Objects.Attributes.Hidden))
                info.Attributes = info.Attributes | FileAttributes.Hidden;
            if (attrib.HasFlag(Objects.Attributes.ReadOnly))
                info.Attributes = info.Attributes | FileAttributes.ReadOnly;
        }

        private void ApplyAttributes(FileSystemInfo info, DateTime newRefTime, Record rec)
        {
            ApplyAttributes(info, newRefTime, rec.Attributes);
        }

        private void RegexMatch(List<string> results, DirectoryInfo root, Regex regexPattern, bool recursive, bool fullpath, bool nodirs)
        {
            foreach (var x in root.GetDirectories())
            {
                if (x.FullName == AdministrationFolder.FullName)
                    continue;
                string localPath = GetLocalPath(x.FullName);
                if (!nodirs && (regexPattern.IsMatch(x.Name) || (fullpath && regexPattern.IsMatch(localPath))))
                {
                    localPath = localPath + "/";
                    results.Add(localPath);
                    AddRecursiveSimple(results, x);
                }
                else
                    RegexMatch(results, x, regexPattern, recursive, fullpath, nodirs);
            }
            foreach (var x in root.GetFiles())
            {
                if (x.Name == "." || x.Name == "..")
                    continue;
                string localPath = GetLocalPath(x.FullName);
                if (regexPattern.IsMatch(x.Name) || (fullpath && regexPattern.IsMatch(localPath)))
                {
                    results.Add(localPath);
                }
            }
        }

        private void AddRecursiveSimple(List<string> results, DirectoryInfo dirInfo)
        {
            if (dirInfo.FullName == AdministrationFolder.FullName)
                return;
            foreach (var x in dirInfo.GetDirectories())
            {
                if (x.FullName == AdministrationFolder.FullName || x.Name == ".svn")
                    continue;
                string localPath = GetLocalPath(x.FullName);
                localPath = localPath + "/";
                results.Add(localPath);
                AddRecursiveSimple(results, x);
            }
            foreach (var x in dirInfo.GetFiles())
            {
                if (x.Name == "." || x.Name == "..")
                    continue;
                string localPath = GetLocalPath(x.FullName);
                results.Add(localPath);
            }
        }

        public string GetLocalPath(string fullName)
        {
            string rootFolder = Root.FullName.Replace('\\', '/');
            string localFolder = fullName.Replace('\\', '/');
            if (!localFolder.StartsWith(rootFolder, StringComparison.OrdinalIgnoreCase))
                throw new Exception(string.Format("{0} doesn't start with {1}", localFolder, rootFolder));
            else
            {
                if (localFolder == rootFolder)
                    return PartialPath == null ? "" : PartialPath;
                string local = localFolder.Substring(rootFolder.Length + 1);
                if (local.Equals(".vrmeta", StringComparison.OrdinalIgnoreCase))
                    return local;
                return PartialPath + local;
            }
        }

        public void Prune()
        {
            HashSet<long> preservedRecords = new HashSet<long>();
            HashSet<Guid> processedVersions = new HashSet<Guid>();
            Action<Objects.Version> preserveRecords = (ver) =>
            {
                if (processedVersions.Contains(ver.ID))
                    return;
                var records = GetRecords(ver);
                foreach (var x in records)
                    preservedRecords.Add(x.Id);
                int count = 25;
                while (count > 0)
                {
                    if (!ver.Parent.HasValue)
                        break;
                    if (processedVersions.Contains(ver.Parent.Value))
                        break;
                    processedVersions.Add(ver.Parent.Value);
                    ver = GetVersion(ver.Parent.Value);
                    var alts = GetAlterations(ver);
                    foreach (var x in alts)
                    {
                        if (x.PriorRecord.HasValue)
                            preservedRecords.Add(x.PriorRecord.Value);
                    }
                    count--;
                }
            };
            var branches = Branches;
            for (int i = 0; i < branches.Count; i++)
            {
                foreach (var y in GetBranchHeads(branches[i]))
                    preserveRecords(GetVersion(y.Version));
                Printer.PrintMessage("Processed branch {1}/{2}: {0}", branches[i].Name, i + 1, branches.Count);
            }
            HashSet<string> dataIdentifiers = new HashSet<string>();
            long size = 0;
            var allRecords = Database.Table<Objects.Record>().ToList();
            foreach (var x in allRecords)
            {
                if (preservedRecords.Contains(x.Id))
                    continue;
                if (x.Size < 1024 * 512)
                    continue;
                var name = Database.Get<Objects.ObjectName>(x.CanonicalNameId).CanonicalName;
                if (name.EndsWith(".cpp") || name.EndsWith(".cs") || name.EndsWith(".h") || name.EndsWith(".hpp"))
                    continue;
                if (!dataIdentifiers.Contains(x.DataIdentifier))
                {
                    dataIdentifiers.Add(x.DataIdentifier);
                    size += x.Size;
                }
            }

            Printer.PrintMessage("Preserved {0} of {1} records.", preservedRecords.Count, allRecords.Count);
            Printer.PrintMessage("About to prune {0} objects with a combined unpacked size of {1}.", dataIdentifiers.Count, Utilities.Misc.FormatSizeFriendly(size));

            foreach (var x in dataIdentifiers)
                ObjectStore.EraseData(x);
        }

        public static Area Init(DirectoryInfo workingDir, string branchname = "master")
        {
            Area ws = CreateWorkspace(workingDir);
            if (ws == null)
                return null;
            if (!ws.Init(branchname))
                throw new Exception("Couldn't initialize versionr.");
            return ws;
        }

        private static Area CreateWorkspace(DirectoryInfo workingDir, bool skipContainmentCheck = false)
        {
            Area ws = LoadWorkspace(workingDir, false, skipContainmentCheck);
            if (ws != null)
            {
                Printer.Write(Printer.MessageType.Error, string.Format("#x#Error:#e# Vault Initialization Failed##\n  The current directory #b#`{0}`## is already part of a versionr vault located in #b#`{1}`##.\n", workingDir.FullName, ws.Root.FullName));
                return null;
            }
            DirectoryInfo adminFolder = GetAdminFolderForDirectory(workingDir);
            Printer.Write(Printer.MessageType.Message, string.Format("Initializing new vault in location `#b#{0}##`.\n", adminFolder.FullName));
            ws = new Area(adminFolder);
            return ws;
        }

        public static Area Load(DirectoryInfo workingDir, bool headless = false, bool skipContainment = false)
        {
            Area ws = LoadWorkspace(workingDir, headless, skipContainment);
            return ws;
        }

        private static Area LoadWorkspace(DirectoryInfo workingDir, bool headless = false, bool skipContainmentCheck = false)
        {
            DirectoryInfo adminFolder = FindAdministrationFolder(workingDir, skipContainmentCheck);
            if (adminFolder == null)
                return null;
            Area ws = new Area(adminFolder);
            if (!ws.Load(headless))
                return null;
            return ws;
        }

        private static DirectoryInfo FindAdministrationFolder(DirectoryInfo workingDir, bool skipContainmentCheck = false)
        {
            while (true)
            {
                DirectoryInfo adminFolder = GetAdminFolderForDirectory(workingDir);
                if (adminFolder.Exists)
                    return adminFolder;
                if (workingDir.Root.FullName == workingDir.FullName)
                    return null;
                if (skipContainmentCheck)
                    return null;
                if (workingDir.Parent != null)
                    workingDir = workingDir.Parent;
            }
        }

        public static DirectoryInfo GetAdminFolderForDirectory(DirectoryInfo workingDir)
        {
            return new DirectoryInfo(Path.Combine(workingDir.FullName, ".versionr"));
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Database.Dispose();
                    LocalData.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~Area() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
