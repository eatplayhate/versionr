using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.LocalState;
using Versionr.Objects;
using Versionr.Utilities;

namespace Versionr
{
    internal class LocalDB : SQLite.SQLiteConnection
    {
        public const int LocalDBVersion = 10;
        private LocalDB(string path, SQLite.SQLiteOpenFlags flags) : base(path, flags)
        {
            Printer.PrintDiagnostics("Local DB Open.");
            if ((flags & SQLite.SQLiteOpenFlags.Create) != 0)
            {
                PrepareTables();
            }
        }

        private void PrepareTables()
        {
            BeginTransaction();
            EnableWAL = true;
            CreateTable<LocalState.Workspace>();
            CreateTable<LocalState.Configuration>();
            CreateTable<LocalState.StageOperation>();
            CreateTable<LocalState.RemoteConfig>();
            CreateTable<LocalState.FileTimestamp>();
            CreateTable<LocalState.LockingObject>();
            CreateTable<LocalState.CachedRecords>();
            Commit();
        }

        public DateTime WorkspaceReferenceTime
        {
            get
            {
                return Workspace.LocalCheckoutTime;
            }
            set
            {
                lock (this)
                {
                    Workspace ws = Workspace;
                    ws.LocalCheckoutTime = value;
                    try
                    {
                        BeginTransaction();
                        Update(ws);
                        Commit();
                    }
                    catch
                    {
                        Rollback();
                    }
                }
            }
        }

        public LocalState.Workspace Workspace
        {
            get
            {
                lock (this)
                {
                    return Get<LocalState.Workspace>(x => x.ID == Configuration.WorkspaceID);
                }
            }
        }

        private string GetPartialPath()
        {
            var path = Workspace.PartialPath;
            if (path == null)
                return string.Empty;
            return path;
        }

        string m_PartialPath = string.Empty;

        public string PartialPath
        {
            get
            {
                return m_PartialPath;
            }
        }

        public Guid Domain
        {
            get
            {
                return Workspace.Domain;
            }
        }

        public List<LocalState.StageOperation> StageOperations
        {
            get
            {
                lock (this)
                {
                    var table = Table<LocalState.StageOperation>();
                    var list = table.ToList();
                    Printer.PrintDiagnostics("Stage has {0} events.", list.Count);
                    return list;
                }
            }
        }

        public LocalState.Configuration Configuration
        {
            get
            {
                lock (this)
                {
                    var table = Table<LocalState.Configuration>();
                    return table.First();
                }
            }
        }

        public bool Valid
        {
            get
            {
                return Configuration.Version >= LocalDBVersion;
            }
        }

        public static Tuple<string, string> ComponentVersionInfo
        {
            get
            {
                return new Tuple<string, string>("User DB", string.Format("v{0}", LocalDBVersion));
            }
        }

        public static LocalDB Open(string fullPath)
        {
            LocalDB db = new LocalDB(fullPath, SQLite.SQLiteOpenFlags.ReadWrite | SQLite.SQLiteOpenFlags.FullMutex);
            if (!db.Upgrade())
                return null;
            return db;
        }

        public bool RefreshLocalTimes { get; set; }

        private bool Upgrade()
        {
            RefreshPartialPath();
            if (Configuration.Version != LocalDBVersion)
                Printer.PrintMessage("Upgrading local cache DB from version v{0} to v{1}", Configuration.Version, LocalDBVersion);
            else
                return true;
            PrepareTables();
            if (Configuration.Version < 5)
            {
                Configuration config = Configuration;
                config.Version = LocalDBVersion;
                try
                {
                    var fs = LoadFileTimes();
                    ReplaceFileTimes(fs);
                    BeginTransaction();
                    Update(config);
                    Commit();
                    ExecuteDirect("VACUUM");
                    return true;
                }
                catch
                {
                    Rollback();
                    return false;
                }
            }
            else if (Configuration.Version == 2)
            {
                Configuration config = Configuration;
                config.Version = LocalDBVersion;
                try
                {
                    BeginTransaction();
                    Update(config);
                    RefreshLocalTimes = true;
                    Commit();
                    return true;
                }
                catch
                {
                    Rollback();
                    return false;
                }
            }

            Configuration cconfig = Configuration;
            cconfig.Version = LocalDBVersion;
            try
            {
                BeginTransaction();
                Update(cconfig);
                Commit();
                return true;
            }
            catch
            {
                Rollback();
                return false;
            }
        }

        internal void RefreshPartialPath()
        {
            m_PartialPath = GetPartialPath();
        }

        public static LocalDB Create(string fullPath)
        {
            LocalDB ldb = new LocalDB(fullPath, SQLite.SQLiteOpenFlags.Create | SQLite.SQLiteOpenFlags.ReadWrite | SQLite.SQLiteOpenFlags.FullMutex);
            ldb.BeginTransaction();
            try
            {
                LocalState.Configuration conf = new LocalState.Configuration();
                conf.Version = LocalDBVersion;
                ldb.InsertSafe(conf);
                ldb.Commit();
                return ldb;
            }
            catch (Exception e)
            {
                ldb.Rollback();
                ldb.Dispose();
                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);
                throw new Exception("Couldn't create database!", e);
            }
        }

        internal void ReplaceFileTimes(Dictionary<string, LocalState.FileTimestamp> filetimes)
        {
            lock (this)
            {
                try
                {
                    BeginTransaction();

                    DeleteAll<LocalState.FileTimestamp>();
                    foreach (var x in filetimes)
                    {
                        LocalState.FileTimestamp fst = new FileTimestamp() { DataIdentifier = x.Value.DataIdentifier, CanonicalName = x.Key, LastSeenTime = x.Value.LastSeenTime };
                        Insert(fst);
                    }

                    Commit();
                    var oldList = LoadFileTimes();
                }
                catch
                {
                    Rollback();
                    throw;
                }
            }
        }

        internal void RemoveStageOperation(LocalState.StageOperation ss)
        {
            lock (this)
            {
                BeginTransaction();
                try
                {
                    Delete(ss);
                    Commit();
                }
                catch (Exception e)
                {
                    Rollback();
                    throw new Exception("Unable to remove stage operation!", e);
                }
            }
        }

        internal void AddStageOperation(LocalState.StageOperation ss)
        {
            lock (this)
            {
                BeginTransaction();
                try
                {
                    Insert(ss);
                    Commit();
                }
                catch (Exception e)
                {
                    Rollback();
                    throw new Exception("Unable to stage operation!", e);
                }
            }
        }

        internal void AddStageOperations(IEnumerable<StageOperation> newStageOperations)
        {
            lock (this)
            {
                BeginTransaction();
                try
                {
                    foreach (var x in newStageOperations)
                        Insert(x);
                    Commit();
                }
                catch (Exception e)
                {
                    Rollback();
                    throw new Exception("Unable to add stage operations!", e);
                }
            }
        }

        internal Dictionary<string, List<StageOperation>> GetMappedStage()
        {
            Dictionary<string, List<StageOperation>> result = new Dictionary<string, List<StageOperation>>();
            foreach (var x in StageOperations)
            {
                if (x.Type == StageOperationType.Merge)
                    continue;
                List<StageOperation> ops = null;
                if (!result.TryGetValue(x.Operand1, out ops))
                {
                    ops = new List<StageOperation>();
                    result[x.Operand1] = ops;
                }
                ops.Add(x);
            }
            return result;
        }

        internal Dictionary<string, LocalState.FileTimestamp> LoadFileTimes()
        {
            Dictionary<string, LocalState.FileTimestamp> result = new Dictionary<string, LocalState.FileTimestamp>();
            bool refresh = false;
            foreach (var x in Table<LocalState.FileTimestamp>().ToList())
            {
                if (x.CanonicalName != null)
                    result[x.CanonicalName] = x;
                else
                    refresh = true;
            }
            if (refresh)
                ReplaceFileTimes(result);
            return result;
        }

        internal void UpdateFileTime(string canonicalName, LocalState.FileTimestamp ft, bool? present)
        {
            lock (this)
            {
                LocalState.FileTimestamp prior = null;
                if (!present.HasValue || present.Value == true)
                    prior = Find<LocalState.FileTimestamp>(x => x.CanonicalName == canonicalName);
                if (prior == null)
                {
                    prior = new FileTimestamp() { CanonicalName = canonicalName, LastSeenTime = ft.LastSeenTime, DataIdentifier = ft.DataIdentifier };
                    Insert(prior);
                }
                else
                {
                    prior.LastSeenTime = ft.LastSeenTime;
                    prior.DataIdentifier = ft.DataIdentifier;
                    Update(prior);
                }
            }
        }

        internal void RemoveFileTime(string canonicalName)
        {
            lock (this)
            {
                var timestamp = Find<LocalState.FileTimestamp>(x => x.CanonicalName == canonicalName);
                if (timestamp != null)
                    Delete(timestamp);
            }
        }

        internal bool AcquireLock()
        {
            Retry:
            var lockingObject = Table<LockingObject>().FirstOrDefault();
            if (lockingObject == null)
            {
                lockingObject = new LockingObject() { Id = 0, LockTime = DateTime.UtcNow };
                try
                {
                    Insert(lockingObject);
                    return true;
                }
                catch
                {
                    goto Retry;
                }
            }
            lockingObject.LockTime = DateTime.UtcNow;
            try
            {
                Update(lockingObject);
                return true;
            }
            catch
            {
                return false;
            }
        }

        const int CachedRecordVersion = 2;

        internal bool GetCachedRecords(Guid iD, out List<Record> results)
        {
            var rec = Find<CachedRecords>(iD);
            if (rec != null && rec.Version == CachedRecordVersion)
                return DeserializeCachedRecords(rec.Data, out results);
            results = null;
            return false;
        }

        private bool DeserializeCachedRecords(byte[] data, out List<Record> results)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream(data))
            using (System.IO.BinaryReader br = new System.IO.BinaryReader(ms))
            {
                int count = br.ReadInt32();
                results = new List<Record>(count);
                for (int i = 0; i < count; i++)
                    results.Add(DeserializeRecord(br));
            }
            return true;
        }

        private byte[] SerializeCachedRecords(List<Record> results)
        {
            System.IO.MemoryStream ms = new System.IO.MemoryStream();
            using (System.IO.BinaryWriter bw = new System.IO.BinaryWriter(ms))
            {
                bw.Write(results.Count);
                foreach (var x in results)
                {
                    SerializeRecord(x, bw);
                }
            }
            return ms.ToArray();
        }

        private Record DeserializeRecord(BinaryReader br)
        {
            Record rec = new Record();
            rec.Id = br.ReadInt64();
            long parent = br.ReadInt64();
            if (parent != -1)
                rec.Parent = parent;
            rec.Size = br.ReadInt64();
            rec.Attributes = (Attributes)br.ReadUInt32();
            rec.Fingerprint = br.ReadString();
            rec.CanonicalNameId = br.ReadInt64();
            rec.ModificationTime = new DateTime(br.ReadInt64());
            rec.CanonicalName = br.ReadString();
            return rec;
        }

        private void SerializeRecord(Record x, BinaryWriter bw)
        {
            bw.Write(x.Id);
            bw.Write(x.Parent.HasValue ? x.Parent.Value : -1L);
            bw.Write(x.Size);
            bw.Write((uint)x.Attributes);
            bw.Write(x.Fingerprint);
            bw.Write(x.CanonicalNameId);
            bw.Write(x.ModificationTime.Ticks);
            bw.Write(x.CanonicalName);
        }

        internal void CacheRecords(Guid iD, List<Record> results)
        {
            BeginTransaction();
            DeleteAll<CachedRecords>();
            CachedRecords cr = new CachedRecords()
            {
                AssociatedVersion = iD,
                Data = SerializeCachedRecords(results),
                Version = CachedRecordVersion
            };
            InsertOrReplace(cr);
            
            Commit();
        }
    }
}
