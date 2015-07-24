using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;
using Versionr.Utilities;

namespace Versionr
{
    internal class WorkspaceDB : SQLite.SQLiteConnection
    {
        public const int InternalDBVersion = 11;
        public const int MinimumDBVersion = 3;
        public const int MaximumDBVersion = 15;

        public LocalDB LocalDatabase { get; set; }

        private WorkspaceDB(string path, SQLite.SQLiteOpenFlags flags, LocalDB localDB) : base(path, flags)
        {
            Printer.PrintDiagnostics("Metadata DB Open.");
            EnableWAL = true;
            LocalDatabase = localDB;

            CreateTable<Objects.FormatInfo>();
            if (flags.HasFlag(SQLite.SQLiteOpenFlags.Create))
            {
                PrepareTables();
                return;
            }

            if (!ValidForUpgrade)
                return;

            if (Format.InternalFormat < InternalDBVersion)
            {
                try
                {
                    BeginExclusive(true);
                    PrepareTables();
                    Printer.PrintMessage("Updating workspace database version from v{0} to v{1}", Format.InternalFormat, InternalDBVersion);
                    var fmt = Format;
                    int priorFormat = fmt.InternalFormat;
                    DropTable<Objects.FormatInfo>();
                    fmt.InternalFormat = InternalDBVersion;
                    CreateTable<Objects.FormatInfo>();
                    Insert(fmt);

                    if (priorFormat < 11)
                    {
                        Printer.PrintMessage(" - Upgrading database - running full consistency check.");
                        Dictionary<Tuple<string, long, DateTime>, Record> records = new Dictionary<Tuple<string, long, DateTime>, Record>();
                        foreach (var x in Table<Objects.Record>().ToList())
                        {
                            var key = new Tuple<string, long, DateTime>(x.UniqueIdentifier, x.CanonicalNameId, x.ModificationTime);
                            if (records.ContainsKey(key))
                            {
                                var other = records[key];
                                Printer.PrintDiagnostics("Found duplicate records {0} ==> {1}", x.Id, other.Id);
                                Printer.PrintDiagnostics(" - UID: {0}", x.UniqueIdentifier);
                                Printer.PrintDiagnostics(" - Time: {0}", x.ModificationTime);
                                Printer.PrintDiagnostics(" - Name: {0}", Get<ObjectName>(x.CanonicalNameId).CanonicalName);

                                int updates = 0;
                                foreach (var s in Table<Alteration>().Where(z => z.PriorRecord == x.Id || z.NewRecord == x.Id))
                                {
                                    if (s.NewRecord.HasValue && s.NewRecord.Value == x.Id)
                                        s.NewRecord = other.Id;
                                    if (s.PriorRecord.HasValue && s.PriorRecord.Value == x.Id)
                                        s.PriorRecord = other.Id;
                                    Update(s);
                                    updates++;
                                }
                                foreach (var s in Table<RecordRef>().Where(z => z.RecordID == x.Id))
                                {
                                    s.RecordID = other.Id;
                                    Update(s);
                                    updates++;
                                }
                                Delete(x);
                                Printer.PrintDiagnostics("Deleted record and updated {0} links.", updates);
                            }
                            else
                                records[key] = x;
                        }
                    }
                    else if (priorFormat == 7)
                    {
                        Printer.PrintMessage(" - Upgrading database - adding branch root version.");
                        foreach (var x in Table<Objects.Branch>().ToList())
                        {
                            var allVersions = Table<Objects.Version>().Where(y => y.Branch == x.ID);
                            Guid? rootVersion = null;
                            foreach (var y in allVersions)
                            {
                                if (y.Parent.HasValue)
                                {
                                    Objects.Version parent = Get<Objects.Version>(y.Parent);
                                    if (parent.Branch != x.ID)
                                    {
                                        rootVersion = parent.ID;
                                        break;
                                    }
                                }
                            }
                            x.RootVersion = rootVersion;
                            Update(x);
                        }
                    }
                    else if (priorFormat < 6 && GetTableInfo("RecordIndex") == null)
                    {
                        Printer.PrintMessage(" - Upgrading database - adding record index.");
                        foreach (var x in Table<Objects.Record>().ToList())
                        {
                            Objects.RecordIndex index = new RecordIndex() { DataIdentifier = x.DataIdentifier, Index = x.Id, Pruned = false };
                            Insert(index);
                        }
                    }

                    Commit();
                }
                catch
                {
                    Rollback();
                }
            }
        }

        private void PrepareTables()
        {
            CreateTable<Objects.Record>();
            CreateTable<Objects.RecordRef>();
            CreateTable<Objects.Version>();
            CreateTable<Objects.Snapshot>();
            CreateTable<Objects.Branch>();
            CreateTable<Objects.Alteration>();
            CreateTable<Objects.Head>();
            CreateTable<Objects.MergeInfo>();
            CreateTable<Objects.ObjectName>();
            CreateTable<Objects.Domain>();
            CreateTable<Objects.RecordIndex>();
        }

        public Guid Domain
        {
            get
            {
                return Table<Objects.Domain>().First().InitialRevision;
            }
        }

        public List<Objects.Head> GetHeads(Branch branch)
        {
            return Table<Objects.Head>().Where(x => x.Branch == branch.ID).ToList();
        }

        public List<Objects.Record> Records
        {
            get
            {
                return GetRecords(Version);
            }
        }
        public List<Record> GetRecords(Objects.Version version)
        {
            List<Record> baseList;
            List<Alteration> alterations;
            return GetRecords(version, out baseList, out alterations);
        }

        public List<Record> GetRecords(Objects.Version version, out List<Record> baseList, out List<Alteration> alterations)
        {
            Printer.PrintDiagnostics("Getting records for version {0}.", version.ID);
            long? snapshotID = null;
            List<Objects.Version> parents = new List<Objects.Version>();
            Objects.Version snapshotVersion = version;
            while (!snapshotID.HasValue)
            {
                parents.Add(snapshotVersion);
                if (snapshotVersion.Snapshot.HasValue)
                    snapshotID = snapshotVersion.Snapshot;
                else
                {
                    snapshotVersion = Get<Objects.Version>(snapshotVersion.Parent);
                }
            }
            Printer.PrintDiagnostics(" - Last snapshot version: {0}", snapshotVersion.ID);
            baseList = Query<Record>("SELECT * FROM ObjectName INNER JOIN (SELECT Record.* FROM Record INNER JOIN RecordRef ON RecordRef.RecordID = Record.Id WHERE RecordRef.SnapshotID = ?) AS results ON ObjectName.Id = results.CanonicalNameId", snapshotID.Value).ToList();
            Printer.PrintDiagnostics(" - Snapshot {0} has {1} records.", snapshotID, baseList.Count);
            alterations = GetAlterationsInternal(parents);
            Printer.PrintDiagnostics(" - Target has {0} alterations.", alterations.Count);
            return Consolidate(baseList, alterations, null);
        }

        public List<Record> GetAllRecords()
        {
            return Query<Record>("SELECT * FROM ObjectName INNER JOIN Record AS results ON ObjectName.Id = results.CanonicalNameId").ToList();
        }

        private List<Record> Consolidate(List<Record> baseList, List<Alteration> alterations, List<Record> deletions)
        {
            Dictionary<long, Record> records = new Dictionary<long, Record>();
			foreach (var x in baseList)
				records[x.Id] = x;
			HashSet<KeyValuePair<long, long>> moveDeletions = new HashSet<KeyValuePair<long, long>>();
			foreach (var x in alterations.Select(x => x).Reverse())
            {
                Objects.Record rec = null;
                switch (x.Type)
                {
                    case AlterationType.Add:
                    case AlterationType.Copy:
                        {
                            var record = Get<Objects.Record>(x.NewRecord);
                            record.CanonicalName = Get<Objects.ObjectName>(record.CanonicalNameId).CanonicalName;
							records[record.Id] = record;
                            break;
                        }
                    case AlterationType.Move:
                        {
                            var record = Get<Objects.Record>(x.NewRecord);
                            record.CanonicalName = Get<Objects.ObjectName>(record.CanonicalNameId).CanonicalName;
							if (deletions != null)
							{
								rec = Get<Objects.Record>(x.PriorRecord);
								rec.CanonicalName = Get<Objects.ObjectName>(rec.CanonicalNameId).CanonicalName;
								deletions.Add(rec);
                            }
							if (!records.Remove(x.PriorRecord.Value))
                            {
                                if (!moveDeletions.Contains(new KeyValuePair<long, long>(x.Owner, x.PriorRecord.Value)))
                                {
                                    long? repaired = RelinkMissingDelete(records, x);
                                    if (!repaired.HasValue)
                                        throw new Exception("this is bad");
                                }
							}
							moveDeletions.Add(new KeyValuePair<long, long>(x.Owner, x.PriorRecord.Value));
							records[record.Id] = record;
                            break;
                        }
                    case AlterationType.Update:
						{
                            var record = Get<Objects.Record>(x.NewRecord);
                            record.CanonicalName = Get<Objects.ObjectName>(record.CanonicalNameId).CanonicalName;
							if (!records.Remove(x.PriorRecord.Value))
							{
								if (!moveDeletions.Contains(new KeyValuePair<long, long>(x.Owner, x.PriorRecord.Value)))
                                {
                                    long? repaired = RelinkMissingDelete(records, x);
                                    if (!repaired.HasValue)
                                        throw new Exception("this is bad");
                                }
							}
							records[record.Id] = record;
                            break;
                        }
                    case AlterationType.Delete:
						if (deletions != null)
						{
							rec = Get<Objects.Record>(x.PriorRecord);
							rec.CanonicalName = Get<Objects.ObjectName>(rec.CanonicalNameId).CanonicalName;
							deletions.Add(rec);
                        }
						if (!records.Remove(x.PriorRecord.Value))
						{
							if (!moveDeletions.Contains(new KeyValuePair<long, long>(x.Owner, x.PriorRecord.Value)))
                            {
                                long? repaired = RelinkMissingDelete(records, x);
                                if (!repaired.HasValue)
                                    throw new Exception("this is bad");
                            }
                        }
						moveDeletions.Add(new KeyValuePair<long, long>(x.Owner, x.PriorRecord.Value));
						break;
                    default:
                        throw new Exception();
                }
            }
            return records.OrderBy(x => x.Value.CanonicalName).Select(x => x.Value).ToList();
        }

        private long? RelinkMissingDelete(Dictionary<long, Record> records, Alteration x)
        {
            // Attempt to repair a possible disaster
            var rec = Get<Objects.Record>(x.PriorRecord.Value);
            long? result = null;
            foreach (var z in records)
            {
                if (z.Value.CanonicalNameId == rec.CanonicalNameId && z.Value.UniqueIdentifier == rec.UniqueIdentifier)
                {
                    result = z.Key;
                    break;
                }
            }
            if (!result.HasValue)
            {
                foreach (var z in records)
                {
                    if (z.Value.CanonicalNameId == rec.CanonicalNameId && z.Value.DataIdentifier == rec.DataIdentifier)
                    {
                        result = z.Key;
                        break;
                    }
                }
            }
            if (result.HasValue)
            {
                records.Remove(result.Value);
                x.PriorRecord = result.Value;
                Update(x);
            }
            return result;
        }

        internal static bool AcceptDBVersion(int dbVersion)
        {
            return dbVersion <= MaximumDBVersion
             && dbVersion >= InternalDBVersion;
        }

        private List<Alteration> GetAlterations(Objects.Version version)
        {
            List<Objects.Version> parents = Query<Objects.Version>(
                @"WITH RECURSIVE
                  ancestors(rowid, ID, Author, Message, Published, Branch, Parent, Timestamp, Snapshot, AlterationList) AS (
                      SELECT rowid, * FROM Version WHERE Version.ID = ?
                      UNION ALL
                      SELECT Version.rowid, Version.* FROM ancestors, Version
                      WHERE ancestors.Parent = Version.ID AND ancestors.Snapshot IS NULL
                  )
                  SELECT * FROM ancestors;", version.ID);
            return GetAlterationsInternal(parents);
            //List<Objects.Version> parents = new List<Objects.Version>();
            //Objects.Version snapshotVersion = version;
            //while (true)
            //{
            //    parents.Add(snapshotVersion);
            //    if (snapshotVersion.Snapshot.HasValue)
            //        break;
            //    else
            //    {
            //        snapshotVersion = Get<Objects.Version>(snapshotVersion.Parent);
            //    }
            //}
            //return GetAlterationsInternal(parents);
        }

        public List<Alteration> GetAlterationsForVersion(Objects.Version version)
        {
            return Table<Alteration>().Where(x => x.Owner == version.AlterationList).ToList();
        }

        private List<Alteration> GetAlterationsInternal(List<Objects.Version> parents)
        {
            return parents.Where(x => !x.Snapshot.HasValue).SelectMany(x => Table<Alteration>().Where(y => y.Owner == x.AlterationList)).ToList();
        }

        public Objects.Version Version
        {
            get
            {
                var ver = Get<Objects.Version>(x => x.ID == LocalDatabase.Workspace.Tip);
                Printer.PrintDiagnostics("Getting current version - {0}", ver.ID);
                return ver;
            }
        }

        public Objects.Branch Branch
        {
            get
            {
                var branch = Get<Objects.Branch>(x => x.ID == LocalDatabase.Workspace.Branch);
                Printer.PrintDiagnostics("Getting current branch - {0}", branch.Name);
                return branch;
            }
        }

        public Objects.FormatInfo Format
        {
            get
            {
                var table = Table<Objects.FormatInfo>();
                return table.First();
            }
        }

        public bool ValidForUpgrade
        {
            get
            {
                return Format.InternalFormat >= MinimumDBVersion && Format.InternalFormat <= MaximumDBVersion;
            }
        }

        public bool Valid
        {
            get
            {
                return AcceptDBVersion(Format.InternalFormat);
            }
        }

        public List<Objects.Version> History
        {
            get
            {
                return GetHistory(Version);
            }
        }

        public static Tuple<string, string> ComponentVersionInfo
        {
            get
            {
                return new Tuple<string, string>("Metadata DB", string.Format("v{0} #q#(compat {1}-{2})", InternalDBVersion, MinimumDBVersion, MaximumDBVersion));
            }
        }

        public List<Objects.Version> GetHistory(Objects.Version version, int? limit = null)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            //List<Objects.Version> result = new List<Objects.Version>();
            //result.Add(version);
            //var results = Query<Objects.Version>(
            //    @"WITH RECURSIVE
            //        ancestors(n) AS (
            //            VALUES(?)
            //            UNION
            //            SELECT Parent FROM ancestors, Version
            //            WHERE ancestors.n = Version.ID
            //        )
            //        SELECT Version.* FROM Version INNER JOIN ancestors ON ancestors.n = Version.ID;", version.Parent);
            //result.AddRange(results);
            var result = Query<Objects.Version>(string.Format(
                @"WITH RECURSIVE
                  ancestors(rowid, ID, Author, Message, Published, Branch, Parent, Timestamp, Snapshot, AlterationList) AS (
                      SELECT rowid, * FROM Version WHERE Version.ID = ?
                      UNION ALL
                      SELECT Version.rowid, Version.* FROM ancestors, Version
                      WHERE ancestors.Parent = Version.ID
                      {0}
                  )
                  SELECT * FROM ancestors;", limit.HasValue ? "LIMIT " + limit.Value.ToString() : ""), version.ID);
            // Naive version
            // var ver = version;
            // while (ver != null)
            // {
            //     result.Add(ver);
            //     ver = Find<Objects.Version>(ver.Parent);
            // }
            sw.Stop();
            Printer.PrintDiagnostics("History determined in {0} ms", sw.ElapsedMilliseconds);
            return result;
        }

        public static WorkspaceDB Open(LocalDB localDB, string fullPath)
        {
            return new WorkspaceDB(fullPath, SQLite.SQLiteOpenFlags.ReadWrite | SQLite.SQLiteOpenFlags.NoMutex, localDB);
        }

        public static WorkspaceDB Create(LocalDB localDB, string fullPath)
        {
            WorkspaceDB ws = new WorkspaceDB(fullPath, SQLite.SQLiteOpenFlags.Create | SQLite.SQLiteOpenFlags.ReadWrite | SQLite.SQLiteOpenFlags.NoMutex, localDB);
            ws.BeginTransaction();
            try
            {
                Objects.FormatInfo formatInfo = new FormatInfo();
                formatInfo.InternalFormat = InternalDBVersion;
                ws.InsertSafe(formatInfo);
                ws.Commit();
                return ws;
            }
            catch (Exception e)
            {
                ws.Rollback();
                ws.Dispose();
                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);
                throw new Exception("Couldn't create database!", e);
            }
        }

        internal IEnumerable<Objects.MergeInfo> GetMergeInfo(Guid versionID)
        {
            return Table<Objects.MergeInfo>().Where(x => x.DestinationVersion == versionID);
        }
    }
}
