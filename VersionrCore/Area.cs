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
using Versionr.Utilities;

namespace Versionr
{
    public class Area
    {
        public ObjectStore.ObjectStoreBase ObjectStore { get; private set; }
        public DirectoryInfo AdministrationFolder { get; private set; }
        private WorkspaceDB Database { get; set; }
        private LocalDB LocalData { get; set; }
        public Directives Directives { get; set; }
        public DateTime ReferenceTime { get; set; }
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

        internal List<Record> GetAllMissingRecords()
        {
            return FindMissingRecords(Database.GetAllRecords());
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

        public Versionr.Status GetStatus(DirectoryInfo activeDirectory)
        {
            if (activeDirectory.FullName == Root.FullName)
                return Status;
            return new Status(this, Database, LocalData, new FileStatus(this, activeDirectory), GetLocalPath(activeDirectory.FullName) + "/");
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

        public bool SetRemote(string host, int port, string name)
        {
            LocalData.BeginTransaction();
            try
            {
                RemoteConfig config = LocalData.Find<RemoteConfig>(x => x.Name == name);
                if (config == null)
				{
					config = new RemoteConfig() { Name = name };
					config.Host = host;
					config.Port = port;
					LocalData.InsertSafe(config);
				}
				else
				{
					config.Host = host;
					config.Port = port;
					LocalData.UpdateSafe(config);
				}

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

        public static string CoreVersion
        {
            get
            {
                return "v1.0";
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

        public void UpdateReferenceTime(DateTime utcNow)
        {
            LocalData.WorkspaceReferenceTime = utcNow;
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

            ws.Name = Environment.UserName;
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

        internal void AddHeadNoCommit(Head x)
        {
            Database.InsertSafe(x);
        }

        internal long GetTransmissionLength(Record record)
        {
            return ObjectStore.GetTransmissionLength(record);
        }

        internal bool TransmitRecordData(Record record, Func<byte[], int, bool, bool> sender, byte[] scratchBuffer)
        {
            return ObjectStore.TransmitRecordData(record, sender, scratchBuffer);
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
                        Database.InsertSafe(x);
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

            List<Record> baseList;
            List<Alteration> alterationList;
            var records = Database.GetRecords(x.Version, out baseList, out alterationList);
            if (alterationList.Count > baseList.Count)
            {
                Objects.Snapshot snapshot = new Snapshot();
                Database.InsertSafe(snapshot);
                foreach (var z in records)
                {
                    Objects.RecordRef rref = new RecordRef();
                    rref.RecordID = z.Id;
                    rref.SnapshotID = snapshot.Id;
                    Database.InsertSafe(rref);
                }
                x.Version.Snapshot = snapshot.Id;
                Database.UpdateSafe(x.Version);
            }
        }

        internal bool HasObjectDataDirect(string x)
        {
            return ObjectStore.HasDataDirect(x);
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
                Database.InsertSafe(result);
            }
            rec.CanonicalNameId = result.Id;

            Database.InsertSafe(rec);

            RecordIndex recIndex = new RecordIndex() { DataIdentifier = rec.DataIdentifier, Index = rec.Id, Pruned = false };

            Database.InsertSafe(recIndex);
        }

        internal void RollbackDatabaseTransaction()
        {
            Database.Rollback();
        }

        internal void BeginDatabaseTransaction()
        {
            Database.BeginTransaction();
        }

        internal void ImportRecordData(Versionr.ObjectStore.ObjectStoreTransaction transaction, string directName, Stream data, out string dependency)
        {
            if (!ObjectStore.ReceiveRecordData(transaction, directName, data, out dependency))
                throw new Exception();
         /*   DirectoryInfo tempDirectory = new DirectoryInfo(System.IO.Path.Combine(AdministrationFolder.FullName, "temp"));
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
            }*/
        }

        internal bool HasObjectData(Record rec)
        {
            return ObjectStore.HasData(rec);
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

		public bool RecordChanges(Status status, IList<Status.StatusEntry> files, bool missing, bool interactive)
        {
            List<LocalState.StageOperation> stageOps = new List<StageOperation>();

			HashSet<string> stagedPaths = new HashSet<string>();
            HashSet<string> removals = new HashSet<string>();
			foreach (var x in files)
			{
				if (x.Staged == false && (
					x.Code == StatusCode.Added ||
					x.Code == StatusCode.Unversioned ||
					x.Code == StatusCode.Renamed ||
					x.Code == StatusCode.Modified ||
					x.Code == StatusCode.Copied ||
					(x.Code == StatusCode.Missing && missing)))
				{
					stagedPaths.Add(x.CanonicalName);

					if (x.Code == StatusCode.Missing)
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

                        Printer.PrintMessage("Recorded deletion: #b#{0}##", x.VersionControlRecord.CanonicalName);
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

                        Printer.PrintMessage("Recorded: #b#{0}##", x.FilesystemEntry.CanonicalName);
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
                    while (entry.FilesystemEntry.Parent != null)
                    {
                        entry = status.Map[entry.FilesystemEntry.Parent.CanonicalName];
                        if (entry.Staged == false && (
                            entry.Code == StatusCode.Added ||
                            entry.Code == StatusCode.Unversioned))
                        {
                            if (!stagedPaths.Contains(entry.CanonicalName))
                            {
                                Printer.PrintMessage("#q#Recorded (auto): #b#{0}##", entry.CanonicalName);
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
            var index = Database.Table<Objects.RecordIndex>().Where(x => x.DataIdentifier == id).First();
            if (index != null)
                return Database.Find<Objects.Record>(index.Index);
            else
                Printer.PrintDiagnostics("Record not in index");
            return null;
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
                LocalData = LocalDB.Open(LocalMetadataFile.FullName);
                // Load metadata DB
                if (!LocalData.Valid)
                    return false;
                Database = WorkspaceDB.Open(LocalData, MetadataFile.FullName);
                if (!Database.Valid)
                    return false;
                if (LocalData.Domain != Database.Domain)
                    return false;

                ReferenceTime = LocalData.WorkspaceReferenceTime;

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

        private void TestDeltas(string v1, string v2, int chunksize = 2048, string output = null)
        {
            FileInfo test = new FileInfo(v1);
            FileInfo test2 = new FileInfo(v2);
            if (output == null)
                output = test2 + ".out";
            using (var fs = test.OpenRead())
            using (var fs2 = test2.OpenRead())
            {
                var result = Versionr.ObjectStore.ChunkedChecksum.Compute(chunksize, fs);
                using (var fs4 = new FileInfo(v1 + ".hash").Open(FileMode.Create))
                {
                    Versionr.ObjectStore.ChunkedChecksum.Write(fs4, result);
                }
                fs.Position = 0;
                long deltaLength;
                var deltas = Versionr.ObjectStore.ChunkedChecksum.ComputeDelta(fs2, test2.Length, result, out deltaLength);
                Printer.PrintMessage("Delta compressed {0} -> {1}: {2} bytes ({3:N2}%)", v1, v2, deltaLength, deltaLength / (double)test2.Length * 100.0);

                using (var fs4 = new FileInfo(output).Open(FileMode.Create))
                    Versionr.ObjectStore.ChunkedChecksum.WriteDelta(fs2, fs4, deltas);
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
                            m_Length = TemporaryFile.Length;
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

        public void Merge(string v, bool updateMode, bool force, bool allowrecursiveMerge = false)
        {
            Objects.Version mergeVersion = null;
            Objects.Version parentVersion = null;
            Versionr.Status status = Status;
            List<TransientMergeObject> parentData;
            if (!updateMode)
            {
                foreach (var x in LocalData.StageOperations)
                {
                    if (x.Type == StageOperationType.Merge)
                    {
                        throw new Exception("Please commit data before merging again.");
                    }
                }

                if (!force)
                {
                    if (status.HasModifications(false))
                    {
                        Printer.PrintMessage("Repository is not clean!");
                        Printer.PrintMessage(" - Until this is fixed, please commit your changes before starting a merge!");
                        return;
                    }
                }

                var possibleBranch = Database.Table<Objects.Branch>().Where(x => x.Name == v).FirstOrDefault();
                if (possibleBranch != null)
                {
                    Head head = GetBranchHead(possibleBranch);
                    mergeVersion = Database.Find<Objects.Version>(head.Version);
                }
                else
                    mergeVersion = GetPartialVersion(v);
                if (mergeVersion == null)
                    throw new Exception("Couldn't find version to merge from!");

                var parents = GetCommonParents(Database.Version, mergeVersion);
                if (parents == null || parents.Count == 0)
                    throw new Exception("No common parent!");

                Objects.Version parent = null;
                Printer.PrintMessage("Starting merge:");
                Printer.PrintMessage(" - Local: {0}", Database.Version.ID);
                Printer.PrintMessage(" - Remote: {0}", mergeVersion.ID);
                if (parents.Count == 1 || !allowrecursiveMerge)
                {
                    parent = GetVersion(parents[0].Key);
                    if (parent.ID == mergeVersion.ID)
                    {
                        Printer.PrintMessage("Merge information is already up to date.");
                        return;
                    }
                    Printer.PrintMessage(" - Parent: {0}", parent.ID);
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
                mergeVersion = GetVersion(GetBranchHead(CurrentBranch).Version);
                if (mergeVersion.ID == parentVersion.ID)
                {
                    Printer.PrintMessage("Already up-to-date.");
                    return;
                }

                Printer.PrintMessage("Updating current vault:");
                Printer.PrintMessage(" - Old version: {0}", parentVersion.ID);
                Printer.PrintMessage(" - New version: {0}", mergeVersion.ID);
            }
            
            var foreignRecords = Database.GetRecords(mergeVersion);
            DateTime newRefTime = DateTime.UtcNow;

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

            foreach (var x in foreignRecords)
            {
                TransientMergeObject parentObject = null;
                parentDataLookup.TryGetValue(x.CanonicalName, out parentObject);
                Status.StatusEntry localObject = null;
                status.Map.TryGetValue(x.CanonicalName, out localObject);

                if (localObject == null || localObject.Removed)
                {
                    if (parentObject == null)
                    {
                        // Added
                        RestoreRecord(x, newRefTime);
                        if (!updateMode)
                        {
                            LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                            LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                        }
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
                            // less fine
                            Printer.PrintWarning("Object \"{0}\" removed locally but changed in target version.", x.CanonicalName);
                            RestoreRecord(x, newRefTime);
                            LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
                            if (!updateMode)
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                        }
                    }
                }
                else
                {
                    if (localObject.DataEquals(x))
                    {
                        // all good, same data in both places
                    }
                    else
                    {
                        if (parentObject != null && parentObject.DataEquals(localObject))
                        {
                            // modified in foreign branch
                            RestoreRecord(x, newRefTime);
                            if (!updateMode)
                            {
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                            }
                        }
                        else if (parentObject != null && parentObject.DataEquals(x))
                        {
                            // modified locally
                        }
                        else if (parentObject == null)
                        {
                            // added in both places
                            var mf = GetTemporaryFile(x);
                            var ml = localObject.FilesystemEntry.Info;
                            var mr = GetTemporaryFile(x);
                            
                            RestoreRecord(x, newRefTime, mf.FullName);

                            FileInfo result = Merge2Way(x, mf, localObject.VersionControlRecord, ml, mr, true);
                            if (result != null)
                            {
                                if (result != ml)
                                    ml.Delete();
                                if (result != mr)
                                    mr.Delete();
                                if (result != mf)
                                    mf.Delete();
                                result.MoveTo(ml.FullName);
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                            }
                            else
                            {
                                mr.Delete();
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
								LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
							}
						}
                        else
                        {
                            var mf = GetTemporaryFile(x);
                            FileInfo mb;
                            var ml = localObject.FilesystemEntry.Info;
                            var mr = GetTemporaryFile(x);

                            if (parentObject.TemporaryFile == null)
                            {
                                mb = GetTemporaryFile(parentObject.Record);
                                RestoreRecord(parentObject.Record, newRefTime, mb.FullName);
                            }
                            else
                                mb = parentObject.TemporaryFile;
                            
                            RestoreRecord(x, newRefTime, mf.FullName);

                            FileInfo result = Merge3Way(x, mf, localObject.VersionControlRecord, ml, parentObject.Record, mb, mr, true);
                            if (result != null)
                            {
                                if (result != ml)
                                    ml.Delete();
                                if (result != mr)
                                    mr.Delete();
                                if (result != mf)
                                    mf.Delete();
                                if (result != mb)
                                    mb.Delete();
                                result.MoveTo(ml.FullName);
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                            }
                            else
                            {
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
								LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
							}
                        }
                    }
                }
            }
            List<Tuple<string, string, bool>> deletionList = new List<Tuple<string, string, bool>>();
            foreach (var x in parentData)
            {
                Objects.Record foreignRecord = null;
                foreignLookup.TryGetValue(x.CanonicalName, out foreignRecord);
                Status.StatusEntry localObject = null;
                status.Map.TryGetValue(x.CanonicalName, out localObject);
                if (foreignRecord == null)
                {
                    // deleted by branch
                    if (localObject != null && !localObject.Removed)
                    {
						string path = System.IO.Path.Combine(Root.FullName, x.CanonicalName);
						if (x.DataEquals(localObject))
                        {
							Printer.PrintMessage("Removing {0}", x.CanonicalName);
                            deletionList.Add(new Tuple<string, string, bool>(path, x.CanonicalName, x.CanonicalName.EndsWith("/")));
                        }
                        else
                        {
                            Printer.PrintError("Can't remove object \"{0}\", tree confict!", x.CanonicalName);
                            Printer.PrintMessage("Resolve conflict by: (r)emoving file, (k)eeping local or (c)onflict?");
                            string resolution = System.Console.ReadLine();
                            if (resolution.StartsWith("k"))
                                continue;
                            if (resolution.StartsWith("r"))
							{
                                deletionList.Add(new Tuple<string, string, bool>(path, x.CanonicalName, false));
							}
							if (resolution.StartsWith("c"))
                                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
                        }
                    }
                }
            }
            foreach (var x in deletionList.Where(x => x.Item3 == false))
            {
                System.IO.File.Delete(x.Item1);
                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Remove, Operand1 = x.Item2 });
            }
            foreach (var x in deletionList.Where(x => x.Item3 == true).OrderByDescending(x => x.Item2.Length))
            {
                try
                {
                    System.IO.Directory.Delete(x.Item1);
                    LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Remove, Operand1 = x.Item2 });
                }
                catch
                {
                    
                }
            }
            foreach (var x in parentData)
            {
                if (x.TemporaryFile != null)
                    x.TemporaryFile.Delete();
            }
            if (!updateMode)
                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Merge, Operand1 = mergeVersion.ID.ToString() });
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
        }

        class MergeResult
        {
            public List<TransientMergeObject> Contents { get; set; }
            public List<Objects.Version> Inputs { get; set; }
        }

        private List<TransientMergeObject> MergeCoreRecursive(Objects.Version v1, Objects.Version v2)
        {
            var parents = GetCommonParents(v1, v2);
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
                    throw new Exception();
                }
                Printer.PrintMessage("Starting recursive merge:");
                Printer.PrintMessage(" - Left: {0}", v1.ID);
                Printer.PrintMessage(" - Right: {0}", v2.ID);
                Printer.PrintMessage(" - Parent: {0}", parent.ID);
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
                            throw new Exception();
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

                            FileInfo info = Merge2Way(x, foreign, localRecord, local, transientResult.TemporaryFile, false);
                            if (info != transientResult.TemporaryFile)
                            {
                                transientResult.TemporaryFile.Delete();
                                System.IO.File.Move(info.FullName, transientResult.TemporaryFile.FullName);
                            }
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
                            }
                            else
                                parentFile = parentObject.TemporaryFile;

                            RestoreRecord(x, newRefTime, foreign.FullName);
                            RestoreRecord(localRecord, newRefTime, local.FullName);

                            FileInfo info = Merge3Way(x, foreign, localRecord, local, parentObject.Record, parentFile, transientResult.TemporaryFile, false);
                            if (info != transientResult.TemporaryFile)
                            {
                                transientResult.TemporaryFile.Delete();
                                System.IO.File.Move(info.FullName, transientResult.TemporaryFile.FullName);
                            }
                            foreign.Delete();
                            local.Delete();
                            if (parentObject.TemporaryFile == null)
                                parentFile.Delete();
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
                var localRecord = localRecords.Where(z => x.CanonicalName == z.CanonicalName).FirstOrDefault();
                if (foreignRecord == null)
                {
                    // deleted by branch
                    if (localRecord != null)
                    {
                        string path = System.IO.Path.Combine(Root.FullName, x.CanonicalName);
                        if (x.DataEquals(localRecord))
                        {
                            Printer.PrintMessage("Removing {0}", x.CanonicalName);
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
            foreach (var x in parentData)
            {
                if (x.TemporaryFile != null)
                    x.TemporaryFile.Delete();
            }
            return results;
        }

        private FileInfo Merge3Way(Record x, FileInfo foreign, Record localRecord, FileInfo local, Record record, FileInfo parentFile, FileInfo temporaryFile, bool allowConflict)
        {
            Printer.PrintMessage("Merging {0}", x.CanonicalName);
            // modified in both places
            string mf = foreign.FullName;
            string mb = parentFile.FullName;
            string ml = local.FullName;
            string mr = temporaryFile.FullName;

            if (Utilities.DiffTool.Merge3Way(mb, ml, mf, mr))
            {
                Printer.PrintMessage(" - Resolved.");
                return temporaryFile;
            }
            else
            {
                Printer.PrintMessage("Merge marked as failure, use (m)ine, (t)heirs or (c)onflict?");
                string resolution = System.Console.ReadLine();
                if (resolution.StartsWith("m"))
                {
                    return local;
                }
                if (resolution.StartsWith("t"))
                {
                    return foreign;
                }
                else
                {
                    if (!allowConflict)
                        throw new Exception();
                    System.IO.File.Move(ml, ml + ".mine");
                    System.IO.File.Move(mf, ml + ".theirs");
                    System.IO.File.Move(mb, ml + ".base");
                    Printer.PrintMessage(" - File not resolved. Please manually merge and then mark as resolved.");
                    return null;
                }
            }
        }

        private FileInfo Merge2Way(Record x, FileInfo foreign, Record localRecord, FileInfo local, FileInfo temporaryFile, bool allowConflict)
        {
            Printer.PrintMessage("Merging {0}", x.CanonicalName);
            string mf = foreign.FullName;
            string ml = local.FullName;
            string mr = temporaryFile.FullName;

            if (Utilities.DiffTool.Merge(ml, mf, mr))
            {
                Printer.PrintMessage(" - Resolved.");
                return temporaryFile;
            }
            else
            {
                Printer.PrintMessage("Merge marked as failure, use (m)ine, (t)heirs or (c)onflict?");
                string resolution = System.Console.ReadLine();
                if (resolution.StartsWith("m"))
                {
                    return local;
                }
                if (resolution.StartsWith("t"))
                {
                    return foreign;
                }
                else
                {
                    if (!allowConflict)
                        throw new Exception();
                    System.IO.File.Move(ml, ml + ".mine");
                    System.IO.File.Move(mf, ml + ".theirs");
                    Printer.PrintMessage(" - File not resolved. Please manually merge and then mark as resolved.");
                    return null;
                }
            }
        }

        int m_TempFileIndex = 0;
        private FileInfo GetTemporaryFile(Record rec)
        {
            DirectoryInfo info = new DirectoryInfo(Path.Combine(AdministrationFolder.FullName, "temp"));
            info.Create();
            lock (this)
            {
                while (true)
                {
                    string fn = rec.Name + m_TempFileIndex++.ToString() + ".tmp";
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

        private List<KeyValuePair<Guid, int>> GetCommonParents(Objects.Version version, Objects.Version mergeVersion)
        {
            Dictionary<Guid, int> foreignGraph = GetParentGraph(mergeVersion);
            Dictionary<Guid, int> localGraph = GetParentGraph(version);
            var shared = new List<KeyValuePair<Guid, int>>(foreignGraph.Where(x => localGraph.ContainsKey(x.Key)).OrderBy(x => x.Value));
            if (shared.Count == 0)
                return null;
            HashSet<Guid> ignored = new HashSet<Guid>();
            var pruned = new List<KeyValuePair<Guid, int>>();
            for (int i = 0; i < shared.Count; i++)
            {
                if (ignored.Contains(shared[i].Key))
                    continue;
                pruned.Add(shared[i]);
                var parents = GetParentGraph(GetVersion(shared[i].Key));
                for (int j = i + 1; j < shared.Count; j++)
                {
                    if (parents.ContainsKey(shared[j].Key))
                        ignored.Add(shared[j].Key);
                }
            }
            return pruned;
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

        public void Checkout(string v, bool purge)
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

			if (purge)
				Purge();

            Printer.PrintMessage("At version {0} on branch \"{1}\"", Database.Version.ID, Database.Branch.Name);
        }

		private void Purge()
		{
			var status = Status;
			foreach (var x in status.Elements)
			{
				if (x.Code == StatusCode.Unversioned)
				{
					System.IO.File.Delete(System.IO.Path.Combine(Root.FullName, x.CanonicalName));
					Printer.PrintMessage("Purging unversioned file {0}", x.CanonicalName);
                }
                else if (x.Code == StatusCode.Copied)
                {
                    System.IO.File.Delete(System.IO.Path.Combine(Root.FullName, x.CanonicalName));
                    Printer.PrintMessage("Purging copied file {0}", x.CanonicalName);
                }
            }
		}

		public bool ExportRecord(string cannonicalPath, Guid? versionId, string outputPath)
		{
			Objects.Version version = null;
			if (versionId != null)
				version = Database.Get<Objects.Version>(versionId);
            if (version == null)
                version = Version;

			List<Record> records = Database.GetRecords(version);
			foreach (var x in records)
			{
				if (x.CanonicalName == cannonicalPath)
				{
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

            DateTime newRefTime = DateTime.UtcNow;

            if (!GetMissingRecords(targetRecords))
            {
                Printer.PrintError("Missing record data!");
                return;
            }

            HashSet<string> canonicalNames = new HashSet<string>();
            foreach (var x in targetRecords.Where(x => x.IsDirectory).OrderBy(x => x.CanonicalName.Length))
            {
                RestoreRecord(x, newRefTime);
                canonicalNames.Add(x.CanonicalName);
            }
            List<Task> tasks = new List<Task>();
            foreach (var x in targetRecords.Where(x => !x.IsDirectory))
            {
                tasks.Add(LimitedTaskDispatcher.Factory.StartNew(() => { RestoreRecord(x, newRefTime); }));
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
        }

        private bool GetMissingRecords(List<Record> targetRecords)
        {
            List<Record> missingRecords = FindMissingRecords(targetRecords);
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

        private List<Record> FindMissingRecords(IEnumerable<Record> targetRecords)
        {
            List<Record> missingRecords = new List<Record>();
            HashSet<string> requestedData = new HashSet<string>();
            foreach (var x in targetRecords)
            {
                if (x.Size == 0)
                    continue;
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
            return missingRecords;
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

        public void Revert(IList<Status.StatusEntry> targets, bool revertRecord, bool interactive)
        {
			foreach (var x in targets)
            {
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
                if (x.Staged == true)
                {
                    Printer.PrintMessage("Removing {0} from inclusion in next commit", x.CanonicalName);
					LocalData.BeginTransaction();
					try
					{
						foreach (var y in LocalData.StageOperations)
						{
							if (y.Operand1 == x.CanonicalName)
							{
								LocalData.Delete(y);
							}
						}
						LocalData.Commit();
					}
					catch (Exception e)
					{
						LocalData.Rollback();
						throw new Exception("Unable to remove stage operations!", e);
					}
				}
                else if (x.Code == StatusCode.Conflict)
                {
                    Printer.PrintMessage("Marking {0} as resolved.", x.CanonicalName);
                    LocalData.BeginTransaction();
                    try
                    {
                        foreach (var y in LocalData.StageOperations)
                        {
                            if (y.Type == StageOperationType.Conflict && y.Operand1 == x.CanonicalName)
                            {
                                LocalData.Delete(y);
                            }
                        }
                        LocalData.Commit();
                    }
                    catch (Exception e)
                    {
                        LocalData.Rollback();
                        throw new Exception("Unable to remove stage operations!", e);
                    }
                }

				if (revertRecord && x.Code != StatusCode.Unchanged)
				{
					Record rec = Database.Records.Where(z => z.CanonicalName == x.CanonicalName).FirstOrDefault();
					if (rec != null)
					{
						Printer.PrintMessage("Restoring pristine copy of {0}", x.CanonicalName);
						RestoreRecord(rec, DateTime.UtcNow);
					}
				}
			}
        }

        public bool Commit(string message = "", bool force = false)
        {
            List<Guid> mergeIDs = new List<Guid>();
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
            }
            Objects.Version parentVersion = Database.Version;
            Printer.PrintDiagnostics("Getting status for commit.");
            Status st = Status;
            if (st.HasModifications(true) || mergeIDs.Count > 0)
            {
                Printer.PrintMessage("Committing changes..");
                Versionr.ObjectStore.ObjectStoreTransaction transaction = null;
                try
                {
                    Objects.Version vs = null;
                    vs = Objects.Version.Create();
                    vs.Author = Environment.UserName;
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

                        Printer.PrintMessage("Input merge: #b#{0}## on branch \"#b#{1}\"##", guid, GetBranch(GetVersion(guid).Branch).Name);
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
                            Printer.PrintWarning("#w#This branch has a previously recorded head, but a new head has to be inserted.");
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

                                        Objects.Alteration alteration = new Alteration();
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
                                        if (!ObjectStore.HasData(record))
                                            ObjectStore.RecordData(transaction, record, x.VersionControlRecord, x.FilesystemEntry);

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
                                            Printer.PrintDiagnostics("Record parent ID: {0}", record.Parent);

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

                    ObjectStore.EndStorageTransaction(transaction);
                    transaction = null;

                    Printer.PrintMessage("Updating internal state.");
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
                    Database.InsertSafe(ss);
                    if (saveSnapshot)
                        vs.Snapshot = ss.Id;
                    vs.AlterationList = ss.Id;
                    Printer.PrintDiagnostics("Adding {0} object records.", records.Count);
                    foreach (var x in canonicalNameInsertions)
                    {
                        Database.InsertSafe(x.Item2);
                        x.Item1.CanonicalNameId = x.Item2.Id;
                    }
                    foreach (var x in records)
                    {
                        Database.InsertSafe(x);
                        Database.InsertSafe(new RecordIndex() { DataIdentifier = x.DataIdentifier, Index = x.Id, Pruned = false });
                    }
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
                        Database.InsertSafe(x);
                    }
                    foreach (var info in mergeInfos)
                        Database.InsertSafe(info);
                    if (saveSnapshot)
                    {
                        Printer.PrintDiagnostics("Adding {0} snapshot ref records.", ssRefs.Count);
                        foreach (var x in ssRefs)
                            Database.InsertSafe(x);
                    }
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
                    
                    Database.Commit();
                    Printer.PrintDiagnostics("Finished.");
                    CleanStage();
                    LocalData.BeginTransaction();
                    try
                    {
                        LocalData.UpdateSafe(ws);
                        LocalData.Commit();
                    }
                    catch
                    {
                        LocalData.Rollback();
                        throw;
                    }

                    Printer.PrintMessage("At version #b#{0}## on branch \"#b#{1}##\"", Database.Version.ID, Database.Branch.Name);
                }
                catch (Exception e)
                {
                    if (transaction != null)
                        ObjectStore.AbortStorageTransaction(transaction);
                    Database.Rollback();
                    Printer.PrintError("Exception during commit: {0}", e.ToString());
                    return false;
                }
            }
            else
            {
                Printer.PrintWarning("#w#Warning:##\n  Nothing to do.");
                return false;
            }
            return true;
        }
        private void RestoreRecord(Record rec, DateTime referenceTime, string overridePath = null)
        {
            if (rec.IsDirectory)
            {
                DirectoryInfo directory = new DirectoryInfo(Path.Combine(Root.FullName, rec.CanonicalName));
                if (!directory.Exists)
                {
                    Printer.PrintMessage("Creating directory {0}", rec.CanonicalName);
                    directory.Create();
                    ApplyAttributes(directory, referenceTime, rec);
                }
                return;
            }
            FileInfo dest = overridePath == null ? new FileInfo(Path.Combine(Root.FullName, rec.CanonicalName)) : new FileInfo(overridePath);
            if (rec.Size == 0)
            {
                using (var fs = dest.Create()) { }
                ApplyAttributes(dest, referenceTime, rec);
                return;
            }
            if (dest.Exists)
            {
                if ((dest.LastWriteTimeUtc <= ReferenceTime || dest.LastWriteTimeUtc == rec.ModificationTime) && dest.Length == rec.Size)
                    return;
                if (dest.Length == rec.Size)
                {
                    if (Entry.CheckHash(dest) == rec.Fingerprint)
                    {
                        try
                        {
                            dest.LastWriteTimeUtc = referenceTime;
                        }
                        catch
                        {
                            // ignore
                        }
                        return;
                    }
                }
                if (overridePath == null)
                    Printer.PrintMessage("Updating {0}", rec.CanonicalName);
            }
            else if (overridePath == null)
                Printer.PrintMessage("Creating {0}", rec.CanonicalName);
            int retries = 0;
        Retry:
            try
            {
                using (var fsd = dest.Open(FileMode.Create))
                {
                    ObjectStore.WriteRecordStream(rec, fsd);
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
                    throw new Exception();
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

        private void ApplyAttributes(FileSystemInfo info, DateTime newRefTime, Record rec)
        {
            info.LastWriteTimeUtc = newRefTime;
            if (rec.Attributes.HasFlag(Objects.Attributes.Hidden))
                info.Attributes = info.Attributes | FileAttributes.Hidden;
            if (rec.Attributes.HasFlag(Objects.Attributes.ReadOnly))
                info.Attributes = info.Attributes | FileAttributes.ReadOnly;
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
            if (ws == null)
                return null;
            if (!ws.Init(branchname))
                throw new Exception("Couldn't initialize versionr.");
            return ws;
        }

        private static Area CreateWorkspace(DirectoryInfo workingDir)
        {
            Area ws = LoadWorkspace(workingDir);
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

        public static Area Load(DirectoryInfo workingDir)
        {
            Area ws = LoadWorkspace(workingDir);
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

        public static DirectoryInfo GetAdminFolderForDirectory(DirectoryInfo workingDir)
        {
            return new DirectoryInfo(Path.Combine(workingDir.FullName, ".versionr"));
        }
    }
}
