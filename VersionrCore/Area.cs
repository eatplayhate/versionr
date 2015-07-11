using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Versionr.LocalState;
using Versionr.Network;
using Versionr.Objects;

namespace Versionr
{
    public class Area
    {
        public ObjectStore.ObjectStoreBase ObjectStore { get; private set; }
        public DirectoryInfo AdministrationFolder { get; private set; }
        private WorkspaceDB Database { get; set; }
        private LocalDB LocalData { get; set; }
        public Directives Directives { get; set; }
        public Guid Domain
        {
            get
            {
                return Database.Domain;
            }
        }
        public FileInfo MetadataFile
        {
            get
            {
                return new FileInfo(Path.Combine(AdministrationFolder.FullName, "metadata.db"));
            }
        }
        public FileInfo LocalMetadataFile
        {
            get
            {
                return new FileInfo(Path.Combine(AdministrationFolder.FullName, "config.db"));
            }
        }
        public DirectoryInfo Root
        {
            get
            {
                return AdministrationFolder.Parent;
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

        public List<Branch> MapVersionToHeads(Objects.Version v)
        {
            var heads = Database.Table<Objects.Head>().Where(x => x.Version == v.ID).ToList();
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
            LocalData.Update(config);
        }

        public bool FindVersion(string target, out Objects.Version version)
        {
            version = GetPartialVersion(target);
            if (version == null)
                return false;
            return true;
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

        public bool SetRemote(string host, int port, string name)
        {
            LocalData.BeginTransaction();
            try
            {
                RemoteConfig config = LocalData.Find<RemoteConfig>(x => x.Name == name);
                if (config == null)
                    config = new RemoteConfig() { Name = name };

                config.Host = host;
                config.Port = port;
                LocalData.Insert(config);

                Printer.PrintDiagnostics("Updating remote \"{0}\" to {1}:{2}", name, host, port);
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

        internal static Area InitRemote(DirectoryInfo workingDir, ClonePayload clonePack)
        {
            Area ws = CreateWorkspace(workingDir);
            if (!ws.Init(null, clonePack))
                throw new Exception("Couldn't initialize versionr.");
            return ws;
        }

        public RemoteConfig GetRemote(string name)
        {
            Printer.PrintDiagnostics("Trying to find remote with name \"{0}\"", name);
            return LocalData.Find<RemoteConfig>(x => x.Name == name);
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
                return Database.Version;
            }
        }

        public List<Objects.Version> History
        {
            get
            {
                return Database.History;
            }
        }

        public List<Objects.Version> GetHistory(Objects.Version version, int? limit = null)
        {
            return Database.GetHistory(version, limit);
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

        public List<Objects.Branch> GetBranchByName(string name)
        {
            return Database.Table<Objects.Branch>().Where(x => x.Name == name).ToList();
        }

        public Objects.Branch CurrentBranch
        {
            get
            {
                return Database.Branch;
            }
        }

        private Area(DirectoryInfo adminFolder)
        {
            Utilities.MultiArchPInvoke.BindDLLs();
            AdministrationFolder = adminFolder;
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
            ws.Name = Environment.UserName;

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
                    Database.Insert(snapshot);
                    version.AlterationList = snapshot.Id;
                    version.Snapshot = snapshot.Id;
                    Database.Insert(version);
                    Database.Insert(head);
                    Database.Insert(domain);
                    Database.Insert(branch);
                    Database.Insert(snapshot);
                    Database.Commit();
                }
                catch (Exception e)
                {
                    Database.Rollback();
                    throw new Exception("Couldn't initialize repository!", e);
                }
                LocalData.Insert(ws);
                LocalData.Update(config);
                LocalData.Commit();
                Printer.PrintDiagnostics("Finished.");
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Couldn't initialize repository!", e);
            }
        }

        private void PopulateDefaults(string branchName)
        {
            Printer.PrintDiagnostics("Creating initial state...");
            LocalState.Configuration config = LocalData.Configuration;

            LocalState.Workspace ws = LocalState.Workspace.Create();

            Objects.Branch branch = Objects.Branch.Create(branchName);
            Objects.Version version = Objects.Version.Create();
            Objects.Snapshot snapshot = new Objects.Snapshot();
            Objects.Head head = new Objects.Head();
            Objects.Domain domain = new Objects.Domain();

            Printer.PrintDiagnostics("Created branch \"{0}\", ID: {1}.", branch.Name, branch.ID);

            domain.InitialRevision = version.ID;
            ws.Name = Environment.UserName;
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
                    Database.Insert(snapshot);
                    version.AlterationList = snapshot.Id;
                    version.Snapshot = snapshot.Id;
                    Database.Insert(version);
                    Database.Insert(head);
                    Database.Insert(domain);
                    Database.Insert(branch);
                    Database.Insert(snapshot);
                    Database.Commit();
                }
                catch (Exception e)
                {
                    Database.Rollback();
                    throw new Exception("Couldn't initialize repository!", e);
                }
                LocalData.Insert(ws);
                LocalData.Update(config);
                LocalData.Commit();
                Printer.PrintDiagnostics("Finished.");
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Couldn't initialize repository!", e);
            }
        }

        internal List<Objects.Alteration> GetAlterations(Objects.Version x)
        {
            return Database.GetAlterationsForVersion(x);
        }

        internal Objects.Record GetRecord(long id)
        {
            Objects.Record rec = Database.Find<Objects.Record>(id);
            if (rec != null)
            {
                rec.CanonicalName = Database.Get<Objects.ObjectName>(rec.CanonicalNameId).CanonicalName;
            }
            return rec;
        }

        public bool RecordAllChanges(bool missing)
        {
            List<LocalState.StageOperation> stageOps = new List<StageOperation>();
            var stat = Status;
            foreach (var x in stat.Elements)
            {
                if (x.Staged == false && (
                    x.Code == StatusCode.Added ||
                    x.Code == StatusCode.Unversioned ||
                    x.Code == StatusCode.Renamed ||
                    x.Code == StatusCode.Modified ||
                    x.Code == StatusCode.Copied))
                {
                    stageOps.Add(new StageOperation() { Operand1 = x.FilesystemEntry.CanonicalName, Type = StageOperationType.Add });
                }
                else if (x.Code == StatusCode.Missing && missing)
                    stageOps.Add(new StageOperation() { Operand1 = x.VersionControlRecord.CanonicalName, Type = StageOperationType.Remove });
            }
            if (stageOps.Count == 0)
            {
                Printer.PrintMessage("No changes found to record.");
                return false;
            }
            Printer.PrintMessage("Recorded {0} changes.", stageOps.Count);
            LocalData.BeginTransaction();
            try
            {
                foreach (var x in stageOps)
                    LocalData.Insert(x);
                LocalData.Commit();
                return true;
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Couldn't record changes to stage!", e);
            }
        }

        internal void AddHeadNoCommit(Head x)
        {
            Database.Insert(x);
        }

        internal bool TransmitRecordData(Record record, Func<IEnumerable<byte>, bool, bool> sender)
        {
            FileInfo file = GetFileForCode(record.Fingerprint, record.Size);
            System.Console.Write("Progress: ");
            int left = System.Console.CursorLeft;
            using (var fss = file.OpenRead())
            {
                BinaryReader sw = new BinaryReader(fss);
                long length = fss.Length;
                sender(BitConverter.GetBytes(length), false); // send size data
                long currentPosition = 0;
                byte[] buffer = new byte[1024 * 1024];
                while (length > currentPosition)
                {
                    int bufferSize = buffer.Length;
                    long remainder = length - currentPosition;
                    if (remainder < bufferSize)
                    {
                        bufferSize = (int)remainder;
                        buffer = new byte[bufferSize];
                    }
                    sw.Read(buffer, 0, bufferSize);

                    int percent = (int)(100.0 * (double)currentPosition / (double)length);
                    System.Console.CursorLeft = left;
                    System.Console.Write("{0}%", percent);
                    currentPosition += bufferSize;

                    if (!sender(buffer, false))
                        return false;
                }
            }
            System.Console.CursorLeft = left;
            System.Console.WriteLine("100%");
            return true;
        }

        internal Record LocateRecord(Record newRecord)
        {
            var results = Database.Table<Objects.Record>().Where(x => x.Fingerprint == newRecord.Fingerprint);
            foreach (var x in results)
            {
                if (x.UniqueIdentifier == newRecord.UniqueIdentifier)
                {
                    if (newRecord.CanonicalName == Database.Table<ObjectName>().Where(y => y.Id == x.CanonicalNameId).First().CanonicalName)
                        return x;
                }
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
                        Database.Insert(x);
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

        internal void ImportVersionNoCommit(SharedNetwork.SharedNetworkInfo clientInfo, VersionInfo x, bool mapRecords)
        {
            Printer.PrintDiagnostics("Importing version {0}", x.Version.ID);
            Objects.Snapshot alterationLink = new Snapshot();
            Database.Insert(alterationLink);
            x.Version.Published = true;
            x.Version.AlterationList = alterationLink.Id;
            x.Version.Snapshot = null;

            Database.Insert(x.Version);
            if (x.MergeInfos != null)
            {
                foreach (var y in x.MergeInfos)
                    Database.Insert(y);
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
                        alteration.PriorRecord = mapRecords ? clientInfo.LocalRecordMap[z.PriorRecord.Id].Id : z.NewRecord.Id;

                    Database.Insert(alteration);
                }
            }

            List<Record> baseList;
            List<Alteration> alterationList;
            var records = Database.GetRecords(x.Version, out baseList, out alterationList);
            if (alterationList.Count > baseList.Count)
            {
                Objects.Snapshot snapshot = new Snapshot();
                Database.Insert(snapshot);
                foreach (var z in records)
                {
                    Objects.RecordRef rref = new RecordRef();
                    rref.RecordID = z.Id;
                    rref.SnapshotID = snapshot.Id;
                    Database.Insert(rref);
                }
                x.Version.Snapshot = snapshot.Id;
                Database.Update(x.Version);
            }
        }

        internal void CommitDatabaseTransaction()
        {
            Database.Commit();
        }

        internal void ImportRecordNoCommit(Record rec)
        {
            var result = Database.Find<Objects.ObjectName>(x => x.CanonicalName == rec.CanonicalName);
            if (result == null)
            {
                result = new ObjectName() { CanonicalName = rec.CanonicalName };
                Database.Insert(result);
            }
            rec.CanonicalNameId = result.Id;
            Database.Insert(rec);
        }

        internal void RollbackDatabaseTransaction()
        {
            Database.Rollback();
        }

        internal void BeginDatabaseTransaction()
        {
            Database.BeginTransaction();
        }

        internal void ImportRecordData(Record rec, Stream data)
        {
            DirectoryInfo tempDirectory = new DirectoryInfo(System.IO.Path.Combine(AdministrationFolder.FullName, "temp"));
            if (!tempDirectory.Exists)
                tempDirectory.Create();
            FileInfo temp;
            do
            {
                temp = new FileInfo(System.IO.Path.Combine(tempDirectory.FullName, System.IO.Path.GetRandomFileName()));
            } while (temp.Exists);
            try
            {
                FileInfo info = GetFileForCode(rec.Fingerprint, rec.Size);
                int bufferSize = 1024 * 1024 * 4;
                byte[] buffer = new byte[bufferSize];
                long remainder = data.Length;
                using (var tempStream = temp.OpenWrite())
                {
                    while (remainder > 0)
                    {
                        int readSize = remainder > bufferSize ? bufferSize : (int)remainder;
                        data.Read(buffer, 0, readSize);
                        tempStream.Write(buffer, 0, readSize);
                        remainder -= readSize;
                    }
                }
                lock (this)
                {
                    if (!info.Exists)
                    {
                        temp.MoveTo(info.FullName);
                        temp = null;
                    }
                }
            }
            finally
            {
                if (temp != null)
                    temp.Delete();
            }
        }

        internal bool HasObjectData(Record rec)
        {
            lock (this)
            {
                FileInfo info = GetFileForCode(rec.Fingerprint, rec.Size);
                return info.Exists;
            }
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
                    Database.Insert(x.Value);
                else
                    Database.Update(x.Value);
            }
        }

        public bool RecordChanges(IList<string> files, bool missing, bool recursive, bool regex, bool filenames, bool caseInsensitive)
        {
            List<LocalState.StageOperation> stageOps = new List<StageOperation>();
            var stat = Status;
            bool globMatching = false;
            Dictionary<string, Status.StatusEntry> statusMap = new Dictionary<string, Status.StatusEntry>();
            foreach (var x in stat.Elements)
            {
                statusMap[x.CanonicalName] = x;
            }
            if (!regex)
            {
                foreach (var x in files)
                {
                    if (x.Contains("*") || x.Contains("?"))
                        globMatching = true;
                }
                if (globMatching)
                    regex = true;
            }
            HashSet<string> stagedPaths = new HashSet<string>();
            if (regex)
            {
                List<System.Text.RegularExpressions.Regex> regexes = new List<System.Text.RegularExpressions.Regex>();
                if (globMatching)
                {
                    foreach (var x in files)
                    {
                        string pattern = "^" + Regex.Escape(x).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                        regexes.Add(new Regex(pattern, RegexOptions.Singleline | (caseInsensitive ? RegexOptions.IgnoreCase : RegexOptions.None)));
                    }
                }
                else
                {
                    foreach (var x in files)
                        regexes.Add(new Regex(x, RegexOptions.Singleline | (caseInsensitive ? RegexOptions.IgnoreCase : RegexOptions.None)));
                }
                foreach (var x in stat.Elements)
                {
                    if (x.Staged == false && (
                        x.Code == StatusCode.Added ||
                        x.Code == StatusCode.Unversioned ||
                        x.Code == StatusCode.Renamed ||
                        x.Code == StatusCode.Modified ||
                        x.Code == StatusCode.Copied ||
                        (x.Code == StatusCode.Missing && missing)))
                    {
                        foreach (var y in regexes)
                        {
                            if ((!filenames && y.IsMatch(x.CanonicalName)) || (filenames && x.FilesystemEntry?.Info != null && y.IsMatch(x.FilesystemEntry.Info.Name)))
                            {
                                stagedPaths.Add(x.CanonicalName);

                                if (x.Code == StatusCode.Missing)
                                {
                                    Printer.PrintMessage("Recorded deletion: {0}", x.VersionControlRecord.CanonicalName);
                                    stageOps.Add(new StageOperation() { Operand1 = x.VersionControlRecord.CanonicalName, Type = StageOperationType.Remove });
                                }
                                else
                                {
                                    Printer.PrintMessage("Recorded object: {0}", x.FilesystemEntry.CanonicalName);
                                    stageOps.Add(new StageOperation() { Operand1 = x.FilesystemEntry.CanonicalName, Type = StageOperationType.Add });
                                }
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                List<string> canonicalPaths = new List<string>();
                foreach (var x in files)
                    canonicalPaths.Add(GetLocalPath(Path.GetFullPath(x)));
                foreach (var x in stat.Elements)
                {
                    if (x.Staged == false && (
                        x.Code == StatusCode.Added ||
                        x.Code == StatusCode.Unversioned ||
                        x.Code == StatusCode.Renamed ||
                        x.Code == StatusCode.Modified ||
                        x.Code == StatusCode.Copied ||
                        x.Code == StatusCode.Missing && (!filenames || (missing && filenames))))
                    {
                        foreach (var y in canonicalPaths)
                        {
                            if ((filenames && (string.Equals(x.Name, y, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                                    string.Equals(x.Name, y + "/", caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))) ||
                                (!filenames && (string.Equals(x.CanonicalName, y, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                                    string.Equals(x.CanonicalName, y + "/", caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))))
                            {
                                stagedPaths.Add(x.CanonicalName);

                                if (x.Code == StatusCode.Missing)
                                {
                                    Printer.PrintMessage("Recorded deletion: {0}", x.VersionControlRecord.CanonicalName);
                                    stageOps.Add(new StageOperation() { Operand1 = x.VersionControlRecord.CanonicalName, Type = StageOperationType.Remove });
                                }
                                else
                                {
                                    Printer.PrintMessage("Recorded object: {0}", x.FilesystemEntry.CanonicalName);
                                    stageOps.Add(new StageOperation() { Operand1 = x.FilesystemEntry.CanonicalName, Type = StageOperationType.Add });
                                }
                                if (recursive && x.IsDirectory)
                                    RecordRecursive(stat.Elements, x, stageOps, stagedPaths);
                                break;
                            }
                        }
                    }
                    else if (recursive && x.IsDirectory)
                    {
                        foreach (var y in canonicalPaths)
                        {
                            if ((filenames && (string.Equals(x.Name, y, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                                    string.Equals(x.Name, y + "/", caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))) ||
                                (!filenames && (string.Equals(x.CanonicalName, y, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                                    string.Equals(x.CanonicalName, y + "/", caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))))
                            {
                                RecordRecursive(stat.Elements, x, stageOps, stagedPaths);
                                break;
                            }
                        }
                    }
                }
            }
            // add parent directories
            foreach (var x in stageOps.ToArray())
            {
                if (x.Type == StageOperationType.Add)
                {
                    Status.StatusEntry entry = statusMap[x.Operand1];
                    while (entry.FilesystemEntry.Parent != null)
                    {
                        entry = statusMap[entry.FilesystemEntry.Parent.CanonicalName];
                        if (entry.Staged == false && (
                            entry.Code == StatusCode.Added ||
                            entry.Code == StatusCode.Unversioned))
                        {
                            if (!stagedPaths.Contains(entry.CanonicalName))
                            {
                                Printer.PrintMessage("Recorded object (auto): {0}", entry.CanonicalName);
                                stageOps.Add(new StageOperation() { Operand1 = entry.CanonicalName, Type = StageOperationType.Add });
                                stagedPaths.Add(entry.CanonicalName);
                            }
                        }
                    }
                }
            }

            if (stageOps.Count == 0)
            {
                Printer.PrintMessage("No changes found to record.");
                return false;
            }
            Printer.PrintMessage("Recorded {0} changes.", stageOps.Count);
            LocalData.BeginTransaction();
            try
            {
                foreach (var x in stageOps)
                    LocalData.Insert(x);
                LocalData.Commit();
                return true;
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Couldn't record changes to stage!", e);
            }
        }
        private void RecordRecursive(List<Status.StatusEntry> elements, Status.StatusEntry parent, List<StageOperation> stageOps, HashSet<string> stagedPaths)
        {
            foreach (var x in elements)
            {
                if (stagedPaths.Contains(x.CanonicalName))
                    continue;
                if (x.CanonicalName.StartsWith(parent.CanonicalName))
                {
                    stagedPaths.Add(x.CanonicalName);
                    if (x.Code == StatusCode.Missing)
                    {
                        Printer.PrintMessage("Recorded deletion: {0}", x.VersionControlRecord.CanonicalName);
                        stageOps.Add(new StageOperation() { Operand1 = x.VersionControlRecord.CanonicalName, Type = StageOperationType.Remove });
                    }
                    else
                    {
                        Printer.PrintMessage("Recorded object: {0}", x.FilesystemEntry.CanonicalName);
                        stageOps.Add(new StageOperation() { Operand1 = x.FilesystemEntry.CanonicalName, Type = StageOperationType.Add });
                    }
                }
            }
        }

        public IEnumerable<Objects.MergeInfo> GetMergeInfo(Guid iD)
        {
            return Database.GetMergeInfo(iD);
        }

        private bool Load()
        {
            try
            {
                if (!MetadataFile.Exists)
                    return false;
                // Load metadata DB
                LocalData = LocalDB.Open(LocalMetadataFile.FullName);
                if (!LocalData.Valid)
                    return false;
                Database = WorkspaceDB.Open(LocalData, MetadataFile.FullName);
                if (!Database.Valid)
                    return false;
                if (LocalData.Domain != Database.Domain)
                    return false;
                ObjectStore = new ObjectStore.StandardObjectStore();
                if (!ObjectStore.Open(this))
                    return false;

                FileInfo info = new FileInfo(Path.Combine(Root.FullName, ".vrmeta"));
                if (info.Exists)
                {
                    string data = string.Empty;
                    using (var sr = info.OpenText())
                    {
                        data = sr.ReadToEnd();
                    }
                    Directives = Newtonsoft.Json.JsonConvert.DeserializeObject<Directives>(data);
                }
                else
                    Directives = Directives.Default();

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

        internal VersionInfo MergeRemote(Objects.Version localVersion, Guid remoteVersionID, SharedNetwork.SharedNetworkInfo clientInfo, out string error)
        {
            Objects.Version remoteVersion = GetLocalOrRemoteVersion(remoteVersionID, clientInfo);
            Objects.Version parent = GetCommonParentForRemote(localVersion, remoteVersionID, clientInfo);

            var records = Database.GetRecords(localVersion);
            var foreignRecords = GetRemoteRecords(remoteVersionID, clientInfo);
            var parentRecords = Database.GetRecords(parent);
            List<FusedAlteration> alterations = new List<FusedAlteration>();

            foreach (var x in foreignRecords)
            {
                Objects.Record parentRecord = parentRecords.Where(z => x.Item1.CanonicalName == z.CanonicalName).FirstOrDefault();
                Objects.Record localRecord = records.Where(z => x.Item1.CanonicalName == z.CanonicalName).FirstOrDefault();

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
                Objects.Record foreignRecord = foreignRecords.Where(z => x.CanonicalName == z.Item1.CanonicalName).Select(z => z.Item1).FirstOrDefault();
                Objects.Record localRecord = records.Where(z => x.CanonicalName == z.CanonicalName).FirstOrDefault();
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
                Author = remoteVersion.Author,
                Branch = localVersion.Branch,
                Message = string.Format("Automatic merge of {0}.", remoteVersion.ID),
                Parent = localVersion.ID,
                Published = true,
                Timestamp = DateTime.UtcNow
            };
            return new VersionInfo()
            {
                Alterations = alterations.ToArray(),
                MergeInfos = new MergeInfo[1] { new MergeInfo() { DestinationVersion = resultVersion.ID, SourceVersion = remoteVersionID } },
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

        private Objects.Version GetLocalOrRemoteVersion(Guid versionID, SharedNetwork.SharedNetworkInfo clientInfo)
        {
            Objects.Version v = Database.Find<Objects.Version>(x => x.ID == versionID);
            if (v == null)
                v = clientInfo.PushedVersions.Where(x => x.Version.ID == versionID).Select(x => x.Version).First();
            return v;
        }

        public void Merge(string v)
        {
            foreach (var x in LocalData.StageOperations)
            {
                if (x.Type == StageOperationType.Merge)
                {
                    throw new Exception("Please commit data before merging again.");
                }
            }

            var possibleBranch = Database.Table<Objects.Branch>().Where(x => x.Name == v).FirstOrDefault();
            Objects.Version mergeVersion = null;
            if (possibleBranch != null)
            {
                Head head = GetBranchHead(possibleBranch);
                mergeVersion = Database.Find<Objects.Version>(head.Version);
            }
            else
                mergeVersion = GetPartialVersion(v);
            if (mergeVersion == null)
                throw new Exception("Couldn't find version to merge from!");
            Objects.Version parent = GetCommonParent(Database.Version, mergeVersion);
            if (parent == null)
                throw new Exception("No common parent!");

            if (parent.ID == mergeVersion.ID)
            {
                Printer.PrintMessage("Merge information is already up to date.");
                return;
            }

            Printer.PrintMessage("Starting merge:");
            Printer.PrintMessage(" - Local: {0}", Database.Version.ID);
            Printer.PrintMessage(" - Remote: {0}", mergeVersion.ID);
            Printer.PrintMessage(" - Parent: {0}", parent.ID);

            var records = Database.Records;
            var foreignRecords = Database.GetRecords(mergeVersion);
            var parentRecords = Database.GetRecords(parent);

            foreach (var x in foreignRecords)
            {
                Objects.Record parentRecord = parentRecords.Where(z => x.CanonicalName == z.CanonicalName).FirstOrDefault();
                Objects.Record localRecord = records.Where(z => x.CanonicalName == z.CanonicalName).FirstOrDefault();

                if (localRecord == null)
                {
                    if (parentRecord == null)
                    {
                        // Added
                        RestoreRecord(x);
                        Add(x.CanonicalName);
                        LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                    }
                    else
                    {
                        // Removed locally
                        if (parentRecord.DataEquals(x))
                        {
                            // this is fine, we removed it in our branch
                        }
                        else
                        {
                            // less fine
                            Printer.PrintWarning("Object \"{0}\" removed locally but present in merge source.", x.CanonicalName);
                            RestoreRecord(x);
                            LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
                            LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                        }
                    }
                }
                else
                {
                    if (localRecord.DataEquals(x))
                    {
                        // all good, same data in both places
                    }
                    else
                    {
                        if (parentRecord != null && localRecord.DataEquals(parentRecord))
                        {
                            // modified in foreign branch
                            RestoreRecord(x);
                            Add(x.CanonicalName);
                            LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                        }
                        else if (parentRecord != null && parentRecord.DataEquals(x))
                        {
                            // modified locally
                        }
                        else if (parentRecord == null)
                        {
                            Printer.PrintMessage("Merging {0}", x.CanonicalName);
                            // modified in both places
                            string mf = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetTempFileName());
                            string ml = System.IO.Path.Combine(Root.FullName, x.CanonicalName);
                            string mr = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetTempFileName());
                            
                            RestoreRecord(x, mf);

                            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo()
                            {
                                FileName = "C:\\Program Files\\KDiff3\\kdiff3.exe",
                                Arguments = string.Format("\"{0}\" \"{1}\" -o \"{2}\" --auto", ml, mf, mr)
                            };
                            var proc = System.Diagnostics.Process.Start(psi);
                            proc.WaitForExit();
                            if (proc.ExitCode == 0)
                            {
                                System.IO.File.Delete(ml);
                                System.IO.File.Move(mr, ml);
                                Printer.PrintMessage(" - Resolved.");
                                System.IO.File.Delete(mf);
                                Add(x.CanonicalName);
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                            }
                            else
                            {
                                System.IO.File.Move(ml, ml + ".mine");
                                System.IO.File.Move(mf, ml + ".theirs");
                                Printer.PrintMessage(" - File not resolved. Please manually merge and then mark as resolved.");
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
                            }
                        }
                        else
                        {
                            Printer.PrintMessage("Merging {0}", x.CanonicalName);
                            // modified in both places
                            string mf = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetTempFileName());
                            string mb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetTempFileName());
                            string ml = System.IO.Path.Combine(Root.FullName, x.CanonicalName);
                            string mr = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetTempFileName());

                            RestoreRecord(parentRecord, mb);
                            RestoreRecord(x, mf);

                            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo()
                            {
                                FileName = "C:\\Program Files\\KDiff3\\kdiff3.exe",
                                Arguments = string.Format("\"{0}\" \"{1}\" \"{2}\" -o \"{3}\" --auto", mb, ml, mf, mr)
                            };
                            var proc = System.Diagnostics.Process.Start(psi);
                            proc.WaitForExit();
                            if (proc.ExitCode == 0)
                            {
                                System.IO.File.Delete(ml);
                                System.IO.File.Move(mr, ml);
                                Printer.PrintMessage(" - Resolved.");
                                System.IO.File.Delete(mf);
                                System.IO.File.Delete(mb);
                                Add(x.CanonicalName);
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                            }
                            else
                            {
                                System.IO.File.Move(ml, ml + ".mine");
                                System.IO.File.Move(mf, ml + ".theirs");
                                System.IO.File.Move(mb, ml + ".base");
                                Printer.PrintMessage(" - File not resolved. Please manually merge and then mark as resolved.");
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
                            }
                        }
                    }
                }
            }
            foreach (var x in parentRecords)
            {
                Objects.Record foreignRecord = foreignRecords.Where(z => x.CanonicalName == z.CanonicalName).FirstOrDefault();
                Objects.Record localRecord = records.Where(z => x.CanonicalName == z.CanonicalName).FirstOrDefault();
                if (foreignRecord == null)
                {
                    // deleted by branch
                    if (localRecord != null)
                    {
                        if (localRecord.DataEquals(x))
                        {
                            Remove(x.CanonicalName);
                        }
                        else
                        {
                            Printer.PrintError("Can't remove object \"{0}\", tree confict!", x.CanonicalName);
                            Printer.PrintMessage("Resolve conflict by: (r)emoving file, (k)eeping local or (c)onflict?");
                            string resolution = System.Console.ReadLine();
                            if (resolution.StartsWith("k"))
                                continue;
                            if (resolution.StartsWith("r"))
                                Remove(x.CanonicalName);
                            if (resolution.StartsWith("c"))
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
                        }
                    }
                }
            }
            LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Merge, Operand1 = mergeVersion.ID.ToString() });
        }

        private Objects.Version GetCommonParent(Objects.Version version, Objects.Version mergeVersion)
        {
            Dictionary<Guid, int> foreignGraph = GetParentGraph(mergeVersion);
            Dictionary<Guid, int> localGraph = GetParentGraph(version);
            var shared = new List<KeyValuePair<Guid, int>>(foreignGraph.Where(x => localGraph.ContainsKey(x.Key)).OrderBy(x => x.Value));
            if (shared.Count == 0)
                return null;
            return Database.Find<Objects.Version>(shared[0].Key);
        }

        private Dictionary<Guid, int> GetParentGraph(Objects.Version mergeVersion)
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
                foreach (var x in Database.GetMergeInfo(currentNode.ID))
                {
                    if (!result.ContainsKey(x.SourceVersion))
                        openNodes.Push(new Tuple<Objects.Version, int>(Database.Get<Objects.Version>(x.SourceVersion), currentNodeData.Item2 + 1));
                }
            }
            sw.Stop();
            Printer.PrintDiagnostics("Determined node hierarchy in {0} ms.", sw.ElapsedMilliseconds);
            return result;
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
            Printer.PrintDiagnostics("Checking for existing branch \"{0}\".", v);
            var branch = Database.Find<Branch>(x => x.Name == v);
            if (branch != null)
                throw new Exception(string.Format("Branch \"{0}\" already exists!", v));

            Objects.Version currentVer = Database.Version;
            branch = Objects.Branch.Create(v, currentVer.Branch);
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
                    Database.Insert(head);
                    Database.Insert(branch);
                    Database.Commit();
                    Printer.PrintDiagnostics("Finished.");
                }
                catch (Exception e)
                {
                    Database.Rollback();
                    throw new Exception("Couldn't branch!", e);
                }
                LocalData.Update(ws);
                LocalData.Commit();
            }
            catch (Exception e)
            {
                LocalData.Rollback();
                throw new Exception("Couldn't branch!", e);
            }
        }

        public void Checkout(string v)
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
            CheckoutInternal(target);

            Printer.PrintMessage("At version {0} on branch \"{1}\"", Database.Version.ID, Database.Branch.Name);
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
        public DAG<Objects.Version, Guid> GetDAG()
        {
            var allVersions = Database.Table<Objects.Version>().ToList();
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

        private Objects.Version GetPartialVersion(string v)
        {
            Objects.Version version = Database.Find<Objects.Version>(v);
            if (version != null)
                return version;
            List<Objects.Version> potentials;
            bool postfix = false;
            if (v.StartsWith("..."))
            {
                postfix = true;
                potentials = Database.Query<Objects.Version>(string.Format("SELECT Version.* FROM Version WHERE Version.ID LIKE '%{0}'", v.Substring(3)));
            }
            else
                potentials = Database.Query<Objects.Version>(string.Format("SELECT Version.* FROM Version WHERE Version.ID LIKE '{0}%'", v));
            if (potentials.Count > 1)
            {
                Printer.PrintError("Can't find a unique version with {1}: {0}\nCould be:", v, postfix ? "postfix" : "prefix");
                foreach (var x in potentials)
                    Printer.PrintMessage("\t{0} - branch: {1}, {2}", x.ID, Database.Get<Objects.Branch>(x.Branch).Name, x.Timestamp.ToLocalTime());
            }
            if (potentials.Count == 1)
                return potentials[0];
            return null;
        }

        private void CleanStage()
        {
            Printer.PrintDiagnostics("Clearing stage.");
            LocalData.BeginTransaction();
            LocalData.DeleteAll<LocalState.StageOperation>();
            LocalData.Commit();
        }

        private void CheckoutInternal(Objects.Version tipVersion)
        {
            List<Record> records = Database.Records;
            
            List<Record> targetRecords = Database.GetRecords(tipVersion);

            if (!GetMissingRecords(targetRecords))
            {
                Printer.PrintError("Missing record data!");
                return;
            }

            HashSet<string> canonicalNames = new HashSet<string>();
            foreach (var x in targetRecords.Where(x => x.IsDirectory).OrderBy(x => x.CanonicalName.Length))
            {
                RestoreRecord(x);
                canonicalNames.Add(x.CanonicalName);
            }
            List<Task> tasks = new List<Task>();
            foreach (var x in targetRecords.Where(x => !x.IsDirectory))
            {
                tasks.Add(Task.Run(() => { RestoreRecord(x); }));
                canonicalNames.Add(x.CanonicalName);
            }
            Task.WaitAll(tasks.ToArray());
            foreach (var x in records.Where(x => !x.IsDirectory))
            {
                if (!canonicalNames.Contains(x.CanonicalName))
                {
                    string path = Path.Combine(Root.FullName, x.CanonicalName);
                    if (System.IO.File.Exists(path))
                    {
                        try
                        {
                            System.IO.File.Delete(path);
                            Printer.PrintMessage("Deleted {0}", x.CanonicalName);
                        }
                        catch
                        {
                            Printer.PrintMessage("Couldn't delete `{0}`!", x.CanonicalName);
                        }
                    }
                }
            }
            foreach (var x in records.Where(x => x.IsDirectory).OrderByDescending(x => x.CanonicalName.Length))
            {
                if (!canonicalNames.Contains(x.CanonicalName))
                {
                    string path = Path.Combine(Root.FullName, x.CanonicalName.Substring(0, x.CanonicalName.Length - 1));
                    if (System.IO.Directory.Exists(path))
                    {
                        try
                        {
                            System.IO.Directory.Delete(path);
                        }
                        catch
                        {
                            Printer.PrintMessage("Couldn't delete `{0}`, files still present!", x.CanonicalName);
                        }
                    }
                }
            }

            LocalData.BeginTransaction();
            try
            {
                var ws = LocalData.Workspace;
                ws.Tip = tipVersion.ID;
                LocalData.Update(ws);
                LocalData.Commit();
            }
            catch (Exception e)
            {
                throw new Exception("Couldn't update local information!", e);
            }
        }

        private bool GetMissingRecords(List<Record> targetRecords)
        {
            List<Record> missingRecords = new List<Record>();
            HashSet<string> requestedData = new HashSet<string>();
            foreach (var x in targetRecords)
            {
                if (x.IsDirectory)
                    continue;
                if (!HasObjectData(x))
                {
                    if (!requestedData.Contains(x.DataIdentifier))
                    {
                        requestedData.Add(x.DataIdentifier);
                        missingRecords.Add(x);
                    }
                }
            }
            if (missingRecords.Count > 0)
            {
                Printer.PrintMessage("Checking out this version requires {0} remote objects.", missingRecords.Count);
                var configs = LocalData.Table<LocalState.RemoteConfig>().OrderByDescending(x => x.LastPull).ToList();
                foreach (var x in configs)
                {
                    Printer.PrintMessage(" - Attempting to pull data from remote \"{2}\" ({0}:{1})", x.Host, x.Port, x.Name);

                    Client client = new Client(this);
                    try
                    {
                        if (!client.Connect(x.Host, x.Port))
                            Printer.PrintMessage(" - Connection failed.");
                        List<Record> retrievedRecords = client.GetRecordData(missingRecords);
                        HashSet<string> retrievedData = new HashSet<string>();
                        Printer.PrintMessage(" - Got {0} records from remote.", retrievedRecords.Count);
                        foreach (var y in retrievedRecords)
                            retrievedData.Add(y.DataIdentifier);
                        missingRecords = missingRecords.Where(z => !retrievedData.Contains(z.DataIdentifier)).ToList();
                        client.Close();
                    }
                    catch
                    {
                        client.Close();
                    }
                    if (missingRecords.Count > 0)
                        Printer.PrintMessage("This checkout still requires {0} additional records.", missingRecords.Count);
                    else
                        return true;
                }
            }
            return false;
        }

        private bool Switch(string v)
        {
            var branch = Database.Find<Branch>(x => x.Name == v && x.Deleted == false);
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

        internal void Revert(string v)
        {
            string localPath = GetLocalPath(Path.GetFullPath(v));
            foreach (var x in LocalData.StageOperations)
            {
                if (x.Operand1 == localPath)
                {
                    Database.Delete(x);
                    goto Next;
                }
            }
            Next:
            Record rec = Database.Records.Where(x => x.CanonicalName == localPath).FirstOrDefault();
            if (rec != null)
            {
                RestoreRecord(rec);
            }
        }

        public bool Commit(string message = "", bool force = false, bool allModified = false)
        {
            Guid? mergeID = null;
            Printer.PrintDiagnostics("Checking stage info for pending conflicts...");
            foreach (var x in LocalData.StageOperations)
            {
                if (x.Type == StageOperationType.Conflict)
                {
                    throw new Exception(string.Format("Can't commit while pending conflicts on file \"{0}\"!", x.Operand1));
                }
                if (x.Type == StageOperationType.Merge)
                    mergeID = new Guid(x.Operand1);
            }
            Objects.Version parentVersion = Database.Version;
            Printer.PrintDiagnostics("Getting status for commit.");
            Status st = Status;
            if (st.HasModifications(!allModified) || mergeID != null)
            {
                try
                {
                    Objects.Version vs = null;
                    vs = Objects.Version.Create();
                    vs.Author = Environment.UserName;
                    vs.Parent = Database.Version.ID;
                    vs.Branch = Database.Branch.ID;
                    Printer.PrintDiagnostics("Created new version ID - {0}", vs.ID);
                    Objects.MergeInfo mergeInfo = null;
                    if (mergeID.HasValue)
                    {
                        mergeInfo = new MergeInfo();
                        mergeInfo.SourceVersion = mergeID.Value;
                        mergeInfo.DestinationVersion = vs.ID;
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
                            Printer.PrintError("Branch already has head but current version is not a direct child.\nA new head has to be inserted, but this requires that the force option is used.");
                            return false;
                        }
                        else
                            Printer.PrintDiagnostics("This branch has a previously recorded head, but a new head has to be inserted.");
                        head = new Head();
                        head.Branch = branch.ID;
                        newHead = true;
                    }
                    else
                        Printer.PrintDiagnostics("Existing head for current version found. Updating branch head.");
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
                    foreach (var x in st.Elements)
                    {
                        List<StageOperation> stagedOps;
                        fullStageInfo.TryGetValue(x.FilesystemEntry != null ? x.FilesystemEntry.CanonicalName : x.VersionControlRecord.CanonicalName, out stagedOps);
                        switch (x.Code)
                        {
                            case StatusCode.Deleted:
                                {
                                    Printer.PrintDiagnostics("Recorded deletion: {0}, old record: {1}", x.VersionControlRecord.CanonicalName, x.VersionControlRecord.Id);
                                    Objects.Alteration alteration = new Alteration();
                                    alteration.PriorRecord = x.VersionControlRecord.Id;
                                    alteration.Type = AlterationType.Delete;
                                    alterations.Add(alteration);
                                }
                                break;
                            case StatusCode.Added:
                            case StatusCode.Modified:
                            case StatusCode.Renamed:
                            case StatusCode.Copied:
                                {
                                    try
                                    {
                                        if (!allModified)
                                        {
                                            if ((x.Code == StatusCode.Renamed || x.Code == StatusCode.Modified)
                                                && !stagedChanges.Contains(x.FilesystemEntry.CanonicalName))
                                            {
                                                finalRecords.Add(x.VersionControlRecord);
                                                break;
                                            }
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
                                                    Objects.Record mergedRecord = GetRecord(op.ReferenceObject);
                                                    if (mergedRecord.Size == x.FilesystemEntry.Length && mergedRecord.Fingerprint == x.FilesystemEntry.Hash)
                                                    {
                                                        record = mergedRecord;
                                                        recordIsMerged = true;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        if (record == null)
                                        {
                                            record = new Objects.Record();
                                            record.CanonicalName = x.FilesystemEntry.CanonicalName;
                                            if (record.IsDirectory)
                                                record.Fingerprint = x.FilesystemEntry.CanonicalName;
                                            else
                                                record.Fingerprint = x.FilesystemEntry.Hash;
                                            record.Attributes = x.FilesystemEntry.Attributes;
                                            record.Size = x.FilesystemEntry.Length;
                                            record.ModificationTime = x.FilesystemEntry.ModificationTime;
                                            if (x.VersionControlRecord != null)
                                                record.Parent = x.VersionControlRecord.Id;
                                        }
                                        RecordData(x.FilesystemEntry, x.VersionControlRecord, record);

                                        ObjectName nameRecord = null;
                                        if (canonicalNames.TryGetValue(x.FilesystemEntry.CanonicalName, out nameRecord))
                                        {
                                            record.CanonicalNameId = nameRecord.Id;
                                        }
                                        else
                                        {
                                            canonicalNameInsertions.Add(new Tuple<Record, ObjectName>(record, new ObjectName() { CanonicalName = x.FilesystemEntry.CanonicalName }));
                                        }

                                        Printer.PrintDiagnostics("Created new object record: {0}", x.FilesystemEntry.CanonicalName);
                                        Printer.PrintDiagnostics("Record fingerprint: {0}", record.Fingerprint);
                                        if (record.Parent != null)
                                            Printer.PrintDiagnostics("Record parent ID: {0} ({1})", x.VersionControlRecord.Id, x.VersionControlRecord.CanonicalName);

                                        Objects.Alteration alteration = new Alteration();
                                        alterationLinkages.Add(new Tuple<Record, Alteration>(record, alteration));
                                        if (x.Code == StatusCode.Added)
                                        {
                                            Printer.PrintDiagnostics("Recorded addition: {0}", x.FilesystemEntry.CanonicalName);
                                            alteration.Type = AlterationType.Add;
                                        }
                                        else if (x.Code == StatusCode.Modified)
                                        {
                                            Printer.PrintDiagnostics("Recorded update: {0}", x.FilesystemEntry.CanonicalName);
                                            alteration.PriorRecord = x.VersionControlRecord.Id;
                                            alteration.Type = AlterationType.Update;
                                        }
                                        else if (x.Code == StatusCode.Copied)
                                        {
                                            Printer.PrintDiagnostics("Recorded copy: {0}, from: {1}", x.FilesystemEntry.CanonicalName, x.VersionControlRecord.CanonicalName);
                                            alteration.PriorRecord = x.VersionControlRecord.Id;
                                            alteration.Type = AlterationType.Copy;
                                        }
                                        else if (x.Code == StatusCode.Renamed)
                                        {
                                            Printer.PrintDiagnostics("Recorded rename: {0}, from: {1}", x.FilesystemEntry.CanonicalName, x.VersionControlRecord.CanonicalName);
                                            alteration.PriorRecord = x.VersionControlRecord.Id;
                                            alteration.Type = AlterationType.Move;
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
                            case StatusCode.Unchanged:
                                finalRecords.Add(x.VersionControlRecord);
                                break;
                            case StatusCode.Unversioned:
                            default:
                                break;
                        }
                    }

                    Printer.PrintDiagnostics("Updating internal state.");
                    var ws = LocalData.Workspace;
                    ws.Tip = vs.ID;
                    Objects.Snapshot ss = new Snapshot();
                    bool saveSnapshot = false;
                    List<Objects.RecordRef> ssRefs = new List<RecordRef>();
                    if (st.Alterations.Count + alterations.Count > st.BaseRecords.Count)
                    {
                        Printer.PrintDiagnostics("Current list of {0} alterations vs snapshot size of {1} entries is above threshold.", st.Alterations.Count + alterations.Count, st.BaseRecords.Count);
                        saveSnapshot = true;
                        Printer.PrintDiagnostics("Creating new snapshot.");
                    }
                    Database.BeginTransaction();
                    Database.Insert(ss);
                    if (saveSnapshot)
                        vs.Snapshot = ss.Id;
                    vs.AlterationList = ss.Id;
                    Printer.PrintDiagnostics("Adding {0} object records.", records.Count);
                    foreach (var x in canonicalNameInsertions)
                    {
                        Database.Insert(x.Item2);
                        x.Item1.CanonicalNameId = x.Item2.Id;
                    }
                    foreach (var x in records)
                        Database.Insert(x);
                    foreach (var x in alterationLinkages)
                        x.Item2.NewRecord = x.Item1.Id;

                    if (saveSnapshot)
                    {
                        foreach (var x in finalRecords)
                        {
                            Objects.RecordRef rr = new Objects.RecordRef();
                            rr.RecordID = x.Id;
                            rr.SnapshotID = ss.Id;
                            ssRefs.Add(rr);
                        }
                    }
                    Printer.PrintDiagnostics("Adding {0} alteration records.", alterations.Count);
                    foreach (var x in alterations)
                    {
                        x.Owner = ss.Id;
                        Database.Insert(x);
                    }
                    if (mergeInfo != null)
                        Database.Insert(mergeInfo);
                    if (saveSnapshot)
                    {
                        Printer.PrintDiagnostics("Adding {0} snapshot ref records.", ssRefs.Count);
                        foreach (var x in ssRefs)
                            Database.Insert(x);
                    }
                    if (newHead)
                        Database.Insert(head);
                    else
                        Database.Update(head);
                    Database.Insert(vs);
                    
                    Database.Commit();
                    Printer.PrintDiagnostics("Finished.");
                    CleanStage();
                    LocalData.BeginTransaction();
                    try
                    {
                        LocalData.Update(ws);
                        LocalData.Commit();
                    }
                    catch
                    {
                        LocalData.Rollback();
                        throw;
                    }

                    Printer.PrintMessage("At version {0} on branch \"{1}\"", Database.Version.ID, Database.Branch.Name);
                }
                catch (Exception e)
                {
                    Database.Rollback();
                    Printer.PrintError("Exception during commit: {0}", e.ToString());
                    return false;
                }
            }
            else
            {
                Printer.PrintError("Nothing to do.");
                return false;
            }
            return true;
        }

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "CreateCompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern IntPtr CreateCompressionStream(int level, int windowBits);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DestroyCompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool DestroyCompressionStream(IntPtr stream);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "StreamGetAdler32", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern uint StreamGetAdler32(IntPtr stream);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "CompressData", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int CompressData(IntPtr stream, byte[] input, int inLength, byte[] output, int outLength, bool flush, bool end);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "CreateDecompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern IntPtr CreateDecompressionStream(int windowBits);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DestroyDecompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool DestroyDecompressionStream(IntPtr stream);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DecompressData", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int DecompressData(IntPtr stream, byte[] output, int outLength);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DecompressSetSource", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int DecompressSetSource(IntPtr stream, byte[] output, int outLength);

        private void RecordData(Entry entry, Record oldRecord, Record newRecord)
        {
            if (entry.IsDirectory)
                return;
            if (HasObjectData(newRecord))
                return;
            FileInfo file = GetFileForCode(entry.Hash, entry.Length);
            try
            {
                FileInfo src = new FileInfo(Path.Combine(Root.FullName, entry.CanonicalName));
                Printer.PrintMessage("Recorded data for: {0} - {1}.", entry.CanonicalName, entry.AbbreviatedHash);
                ulong compressedSize = 0;
                using (var fsd = file.OpenWrite())
                using (var fss = src.OpenRead())
                {
                    BinaryWriter sw = new BinaryWriter(fsd);
                    sw.Write(0);
                    sw.Write(entry.Length);

                    var stream = CreateCompressionStream(9, 23);

                    byte[] dataBlob = new byte[16 * 1024 * 1024];
                    byte[] outBufferTemp = new byte[2 * dataBlob.Length];
                    long remainder = entry.Length;
                    while (remainder > 0)
                    {
                        int max = dataBlob.Length;
                        bool end = false;
                        if (max > remainder)
                        {
                            end = true;
                            max = (int)remainder;
                        }
                        fss.Read(dataBlob, 0, max);
                        var result = CompressData(stream, dataBlob, max, outBufferTemp, outBufferTemp.Length, true, end);
                        if (result < 0)
                            throw new Exception();
                        compressedSize += (ulong)result;
                        sw.Write(outBufferTemp, 0, result);
                        remainder -= max;
                    }
                    Printer.PrintMessage("Compressed {0} to {1} bytes.", entry.Length, compressedSize);

                    DestroyCompressionStream(stream);
                }
            }
            catch
            {
                if (file.Exists)
                    file.Delete();
                throw;
            }
        }

        private void RestoreRecord(Record rec, string overridePath = null)
        {
            if (rec.IsDirectory)
            {
                DirectoryInfo directory = new DirectoryInfo(Path.Combine(Root.FullName, rec.CanonicalName));
                if (!directory.Exists)
                {
                    Printer.PrintMessage("Creating directory {0}", rec.CanonicalName);
                    directory.Create();
                    ApplyAttributes(directory, rec);
                }
                return;
            }
            FileInfo file = GetFileForCode(rec.Fingerprint, rec.Size);
            FileInfo dest = overridePath == null ? new FileInfo(Path.Combine(Root.FullName, rec.CanonicalName)) : new FileInfo(overridePath);
            if (dest.Exists)
            {
                try
                {
                    dest.LastWriteTimeUtc = rec.ModificationTime;
                }
                catch
                {
                    // ignore
                }
                if (dest.Length == rec.Size)
                {
                    if (Entry.CheckHash(dest) == rec.Fingerprint)
                        return;
                }
                if (overridePath == null)
                    Printer.PrintMessage("Updating {0}", rec.CanonicalName);
            }
            else if (overridePath == null)
                Printer.PrintMessage("Creating {0}", rec.CanonicalName);
            try
            {
                using (var fss = file.OpenRead())
                using (var fsd = dest.Open(FileMode.Create))
                {
                    BinaryReader sw = new BinaryReader(fss);
                    int compressionType = sw.ReadInt32();
                    if (compressionType != 0)
                        throw new Exception();
                    long outputSize = sw.ReadInt64();
                    if (outputSize != rec.Size)
                        throw new Exception();

                    var stream = CreateDecompressionStream(23);

                    byte[] dataBlob = new byte[20 * 1024 * 1024];
                    byte[] outBufferTemp = new byte[dataBlob.Length];
                    long remainder = file.Length - 12;
                    while (remainder > 0)
                    {
                        int max = dataBlob.Length;
                        if (max > remainder)
                            max = (int)remainder;
                        sw.Read(dataBlob, 0, max);
                        DecompressSetSource(stream, dataBlob, max);
                        bool inputRemaining = true;
                        while (inputRemaining)
                        {
                            var result = DecompressData(stream, outBufferTemp, outBufferTemp.Length);
                            if (result <= 0)
                            {
                                inputRemaining = false;
                                result = -result;
                            }
                            fsd.Write(outBufferTemp, 0, result);
                        }
                        remainder -= max;
                    }

                    DestroyDecompressionStream(stream);
                }
                ApplyAttributes(dest, rec);
            }
            catch (System.IO.IOException)
            {
                Printer.PrintError("Couldn't write file \"{0}\"!", rec.CanonicalName);
                return;
            }
            catch (System.UnauthorizedAccessException)
            {
                Printer.PrintError("Couldn't write file \"{0}\"!", rec.CanonicalName);
                return;
            }
        }

        private void ApplyAttributes(FileSystemInfo info, Record rec)
        {
            info.LastWriteTimeUtc = rec.ModificationTime;
            if (rec.Attributes.HasFlag(Objects.Attributes.Hidden))
                info.Attributes = info.Attributes | FileAttributes.Hidden;
            if (rec.Attributes.HasFlag(Objects.Attributes.ReadOnly))
                info.Attributes = info.Attributes | FileAttributes.ReadOnly;
        }

        private FileInfo GetFileForCode(string hash, long length)
        {
            DirectoryInfo rootDir = new DirectoryInfo(Path.Combine(AdministrationFolder.FullName, "objects"));
            rootDir.Create();
            DirectoryInfo subDir = new DirectoryInfo(Path.Combine(rootDir.FullName, hash.Substring(0, 2)));
            subDir.Create();
            return new FileInfo(Path.Combine(subDir.FullName, hash.Substring(2) + "-" + length.ToString()));
        }

        internal void Remove(string v)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(v);
            FileInfo info = new FileInfo(v);
            string localPath = GetLocalPath(info.FullName);
            if (dirInfo.Exists)
            {
                localPath = localPath + "/";
            }
            else
            {
                Objects.Record rec = Database.Records.Where(x => x.CanonicalName == localPath).FirstOrDefault();
                if (rec == null)
                {
                    Printer.PrintWarning("Object \"{0}\" is not under versionr control.", info.FullName);
                    return;
                }
                foreach (var x in LocalData.StageOperations)
                {
                    if (x.Operand1 == localPath)
                    {
                        Printer.PrintWarning("Object \"{0}\" already has a pending `{1}` operation!", x.Operand1, x.Type);
                        return;
                    }
                }
                if (info.Exists)
                    info.Delete();
                Printer.PrintMessage("Removed \"{0}\"", localPath);
                LocalState.StageOperation ss = new LocalState.StageOperation();
                ss.Type = LocalState.StageOperationType.Remove;
                ss.Operand1 = localPath;
                LocalData.AddStageOperation(ss);
            }
        }

        public bool AddAll()
        {
            return Add(new string[] { "." }.ToList(), true, false, false, false);
        }

        public bool Add(IList<string> files, bool recursive, bool regex, bool fullpath, bool nodirs)
        {
            var stageOperations = LocalData.StageOperations;
            List<StageOperation> newStageOperations = new List<StageOperation>();
            HashSet<string> records = new HashSet<string>(Database.Records.Select(x => x.CanonicalName));
            foreach (var x in files)
            {
                Printer.PrintDiagnostics("Searching for object: {0}", x);
                List<string> matches = MatchObjects(x, recursive, regex, fullpath, nodirs);
                if (matches.Count > 1)
                    Printer.PrintDiagnostics(" - Matched {0} objects.", matches.Count);
                foreach (var y in matches)
                {
                    if (records.Contains(y))
                    {
                        if (!recursive && !regex)
                            Printer.PrintWarning("Object \"{0}\" is already under versionr control.", y);
                    }
                    else
                    {
                        var stageOperation = stageOperations.Concat(newStageOperations).Where(z => z.Operand1 == y).FirstOrDefault();
                        if (stageOperation != null)
                            Printer.PrintWarning("Object \"{0}\" already has a pending `{1}` operation!", stageOperation.Operand1, stageOperation.Type);
                        else
                        {
                            //Printer.PrintMessage("Included in next commit: {0}", y);
                            newStageOperations.Add(new StageOperation() { Type = StageOperationType.Add, Operand1 = y });
                            records.Add(y);
                            DirectoryInfo info = y.EndsWith("/") ? new DirectoryInfo(y).Parent : new FileInfo(y).Directory;
                            while (info.FullName != Root.FullName)
                            {
                                string localPath = GetLocalPath(info.FullName) + "/";
                                if (records.Contains(localPath))
                                    break;
                                records.Add(localPath);
                                Printer.PrintMessage("> Included containing folder: {0}", localPath);
                                newStageOperations.Add(new StageOperation() { Type = StageOperationType.Add, Operand1 = localPath, Flags = 1 });
                                info = info.Parent;
                            }
                        }
                    }
                }
            }
            if (newStageOperations.Count > 0)
            {
                Printer.PrintMessage("Adding {0} {1}", newStageOperations.Count, newStageOperations.Count == 1 ? "Object" : "Objects");
                LocalData.AddStageOperations(newStageOperations);
                return true;
            }
            else
            {
                Printer.PrintError("Add operation failed to match any unversion(r)ed objects.");
                return false;
            }
        }

        private List<string> MatchObjects(string x, bool recursive, bool regex, bool fullpath, bool nodirs)
        {
            List<string> results = new List<string>();
            if (regex)
            {
                System.Text.RegularExpressions.Regex regexPattern = new System.Text.RegularExpressions.Regex(x);
                RegexMatch(results, Root, regexPattern, recursive, fullpath, nodirs);
                return results;
            }
            else
            {
                System.IO.DirectoryInfo dirInfo = new DirectoryInfo(x);
                FileInfo fileInfo = new FileInfo(x);

                if (!fileInfo.Exists && !dirInfo.Exists)
                {
                    Printer.PrintError(string.Format("Object {0} does not exist!", fileInfo.FullName));
                    return results;
                }
                string localPath = GetLocalPath(fileInfo.FullName);
                if (!localPath.StartsWith(".versionr"))
                {
                    if (dirInfo.Exists && dirInfo.Name != ".svn")
                    {
                        localPath = localPath + "/";
                        if (localPath != "/")
                            results.Add(localPath);
                        if (recursive)
                        {
                            AddRecursiveSimple(results, dirInfo);
                        }
                    }
                    else
                        results.Add(localPath);
                }

                return results;
            }
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

        internal void Add(string v)
        {
            if (v == "*")
            {
                var st = Status;
                foreach (var x in st.Elements)
                {
                    if (x.Code == StatusCode.Unversioned)
                    {
                        Printer.PrintMessage("Added \"{0}\"", x.FilesystemEntry.CanonicalName);
                        StageOperation ss2 = new StageOperation();
                        ss2.Type = StageOperationType.Add;
                        ss2.Operand1 = x.FilesystemEntry.CanonicalName;
                        LocalData.AddStageOperation(ss2);
                    }
                }
                return;
            }

            DirectoryInfo dirInfo = new DirectoryInfo(v);
            FileInfo info = new FileInfo(v);
            if (!info.Exists && !dirInfo.Exists)
            {
                throw new Exception(string.Format("File {0} does not exist!", info.FullName));
            }
            string localPath = GetLocalPath(info.FullName);
            if (dirInfo.Exists)
                localPath = localPath + "/";
            Objects.Record rec = Database.Records.Where(x => x.CanonicalName == localPath).FirstOrDefault();
            if (rec != null)
            {
                Printer.PrintWarning("Object \"{0}\" is already under versionr control.", info.FullName);
                return;
            }
            foreach (var x in LocalData.StageOperations)
            {
                if (x.Operand1 == localPath)
                {
                    Printer.PrintWarning("Object \"{0}\" already has a pending `{1}` operation!", x.Operand1, x.Type);
                    return;
                }
            }
            Printer.PrintMessage("Added \"{0}\"", localPath);
            StageOperation ss = new StageOperation();
            ss.Type = StageOperationType.Add;
            ss.Operand1 = localPath;
            LocalData.AddStageOperation(ss);
        }

        public string GetLocalPath(string fullName)
        {
            string rootFolder = Root.FullName.Replace('\\', '/');
            string localFolder = fullName.Replace('\\', '/');
            if (!localFolder.StartsWith(rootFolder))
                throw new Exception();
            else
            {
                if (localFolder == rootFolder)
                    return "";
                return localFolder.Substring(rootFolder.Length + 1);
            }
        }

        public static Area Init(DirectoryInfo workingDir, string branchname = "master")
        {
            Area ws = CreateWorkspace(workingDir);
            if (!ws.Init(branchname))
                throw new Exception("Couldn't initialize versionr.");
            return ws;
        }

        private static Area CreateWorkspace(DirectoryInfo workingDir)
        {
            Area ws = LoadWorkspace(workingDir);
            if (ws != null)
                throw new Exception(string.Format("Path {0} is already a versionr workspace!", workingDir.FullName));
            DirectoryInfo adminFolder = GetAdminFolderForDirectory(workingDir);
            if (adminFolder.Exists)
                throw new Exception(string.Format("Administration folder {0} already present.", adminFolder.FullName));
            ws = new Area(adminFolder);
            return ws;
        }

        public static Area Load(DirectoryInfo workingDir)
        {
            Area ws = LoadWorkspace(workingDir);
            if (ws == null)
                throw new Exception(string.Format("Path {0} is not a valid workspace!", workingDir.FullName));
            return ws;
        }

        private static Area LoadWorkspace(DirectoryInfo workingDir)
        {
            DirectoryInfo adminFolder = FindAdministrationFolder(workingDir);
            if (adminFolder == null)
                return null;
            Area ws = new Area(adminFolder);
            if (!ws.Load())
                return null;
            return ws;
        }

        private static DirectoryInfo FindAdministrationFolder(DirectoryInfo workingDir)
        {
            while (true)
            {
                DirectoryInfo adminFolder = GetAdminFolderForDirectory(workingDir);
                if (adminFolder.Exists)
                    return adminFolder;
                if (workingDir.Root.FullName == workingDir.FullName)
                    return null;
                if (workingDir.Parent != null)
                    workingDir = workingDir.Parent;
            }
        }

        private static DirectoryInfo GetAdminFolderForDirectory(DirectoryInfo workingDir)
        {
            return new DirectoryInfo(Path.Combine(workingDir.FullName, ".versionr"));
        }
    }
}
