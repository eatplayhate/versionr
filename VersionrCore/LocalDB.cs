using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.LocalState;
using Versionr.Utilities;

namespace Versionr
{
    internal class LocalDB : SQLite.SQLiteConnection
    {
        public const int LocalDBVersion = 3;
        private LocalDB(string path, SQLite.SQLiteOpenFlags flags) : base(path, flags)
        {
            Printer.PrintDiagnostics("Local DB Open.");
            EnableWAL = true;
            CreateTable<LocalState.Workspace>();
            CreateTable<LocalState.Configuration>();
            CreateTable<LocalState.StageOperation>();
            CreateTable<LocalState.RemoteConfig>();
            CreateTable<LocalState.FileTimestamp>();
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
            if (Configuration.Version == 2)
            {
                Configuration config = Configuration;
                config.Version = LocalDBVersion;
                try
                {
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
            else if (Configuration.Version == 1)
            {
                Configuration config = Configuration;
                config.Version = LocalDBVersion;
                try
                {
                    BeginTransaction();
                    Update(config);
                    Commit();
                    return true;
                }
                catch
                {
                    Rollback();
                    return false;
                }
            }
            return true;
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

                    var oldList = LoadFileTimes();
                    foreach (var x in filetimes)
                    {
                        LocalState.FileTimestamp fst = new FileTimestamp() { DataIdentifier = x.Value.DataIdentifier, CanonicalName = x.Key, LastSeenTime = x.Value.LastSeenTime };
                        Insert(fst);
                    }
                    foreach (var x in oldList)
                    {
                        if (!filetimes.ContainsKey(x.Key))
                            Delete<LocalState.FileTimestamp>(x.Value.Id);
                    }

                    Commit();
                }
                catch
                {
                    Rollback();
                    throw;
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
            foreach (var x in Table<LocalState.FileTimestamp>())
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

        internal void UpdateFileTime(string canonicalName, LocalState.FileTimestamp ft)
        {
            lock (this)
            {
                var timestamp = Find<LocalState.FileTimestamp>(x => x.CanonicalName == canonicalName);
                if (timestamp == null)
                {
                    timestamp = new FileTimestamp() { CanonicalName = canonicalName, LastSeenTime = ft.LastSeenTime, DataIdentifier = ft.DataIdentifier };
                    Insert(timestamp);
                }
                else
                {
                    timestamp.LastSeenTime = ft.LastSeenTime;
                    timestamp.DataIdentifier = ft.DataIdentifier;
                    Update(timestamp);
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
    }
}
