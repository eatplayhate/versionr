﻿using System;
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

        public LocalState.Workspace Workspace
        {
            get
            {
                return Get<LocalState.Workspace>(x => x.ID == Configuration.WorkspaceID);
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
                var table = Table<LocalState.StageOperation>();
                var list = table.ToList();
                Printer.PrintDiagnostics("Stage has {0} events.", list.Count);
                return list;
            }
        }

        public LocalState.Configuration Configuration
        {
            get
            {
                var table = Table<LocalState.Configuration>();
                return table.First();
            }
        }

        public bool Valid
        {
            get
            {
                return Configuration.Version == LocalDBVersion;
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

        internal void ReplaceFileTimes(Dictionary<string, DateTime> filetimes)
        {
            try
            {
                BeginExclusive();
                DropTable<LocalState.FileTimestamp>();
                CreateTable<LocalState.FileTimestamp>();

                foreach (var x in filetimes)
                {
                    LocalState.FileTimestamp fst = new FileTimestamp() { CanonicalName = x.Key, LastSeenTime = x.Value };
                    Insert(fst);
                }

                Commit();
            }
            catch
            {
                Rollback();
                throw;
            }
        }

        internal void AddStageOperation(LocalState.StageOperation ss)
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

        internal void AddStageOperations(IEnumerable<StageOperation> newStageOperations)
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

        internal Dictionary<string, DateTime> LoadFileTimes()
        {
            Dictionary<string, DateTime> result = new Dictionary<string, DateTime>();
            foreach (var x in Table<LocalState.FileTimestamp>())
                result[x.CanonicalName] = x.LastSeenTime;
            return result;
        }

        internal void UpdateFileTime(string canonicalName, DateTime lastAccessTimeUtc)
        {
            var timestamp = Find<LocalState.FileTimestamp>(x => x.CanonicalName == canonicalName);
            if (timestamp == null)
            {
                timestamp = new FileTimestamp() { CanonicalName = canonicalName, LastSeenTime = lastAccessTimeUtc };
                Insert(timestamp);
            }
            else
            {
                timestamp.LastSeenTime = lastAccessTimeUtc;
                Update(timestamp);
            }
        }

        internal void RemoveFileTime(string canonicalName)
        {
            var timestamp = Find<LocalState.FileTimestamp>(x => x.CanonicalName == canonicalName);
            if (timestamp != null)
                Delete(timestamp);
        }
    }
}
