using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;

namespace Versionr
{
    internal class WorkspaceDB : SQLite.SQLiteConnection
    {
        public const int InternalDBVersion = 7;
        public const int MinimumDBVersion = 3;
        public const int MaximumDBVersion = 10;

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
                    BeginTransaction();
                    Printer.PrintMessage("Updating workspace database version from v{0} to v{1}", Format.InternalFormat, InternalDBVersion);
                    var fmt = Format;
                    int priorFormat = fmt.InternalFormat;
                    DropTable<Objects.FormatInfo>();
                    fmt.InternalFormat = InternalDBVersion;
                    CreateTable<Objects.FormatInfo>();
                    Insert(fmt);

                    if (GetTableInfo("RecordIndex") == null || priorFormat < 6)
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

        private List<Record> Consolidate(List<Record> baseList, List<Alteration> alterations, List<Record> deletions)
        {
            HashSet<Record> records = new HashSet<Record>();
            foreach (var x in baseList)
                records.Add(x);
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
                            records.Add(record);
                            break;
                        }
                    case AlterationType.Move:
                        {
                            var record = Get<Objects.Record>(x.NewRecord);
                            record.CanonicalName = Get<Objects.ObjectName>(record.CanonicalNameId).CanonicalName;
                            rec = Get<Objects.Record>(x.PriorRecord);
                            if (deletions != null)
                                deletions.Add(rec);
                            records.RemoveWhere(y => y.Id == x.PriorRecord);
                            records.Add(record);
                            break;
                        }
                    case AlterationType.Update:
                        {
                            var record = Get<Objects.Record>(x.NewRecord);
                            record.CanonicalName = Get<Objects.ObjectName>(record.CanonicalNameId).CanonicalName;
                            records.RemoveWhere(y => y.Id == x.PriorRecord);
                            records.Add(record); 
                            break;
                        }
                    case AlterationType.Delete:
                        rec = Get<Objects.Record>(x.PriorRecord);
                        if (deletions != null)
                            deletions.Add(rec);
                        records.RemoveWhere(y => y.Id == x.PriorRecord);
                        break;
                    default:
                        throw new Exception();
                }
            }
            Dictionary<string, Record> result = new Dictionary<string, Record>();
            foreach (var x in records)
                result[x.CanonicalName] = x;
            return result.OrderBy(x => x.Key).Select(x => x.Value).ToList();
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
                  ancestors(ID, Author, Message, Published, Branch, Parent, Timestamp, Snapshot, AlterationList) AS (
                      SELECT Version.* FROM Version WHERE Version.ID = ?
                      UNION ALL
                      SELECT Version.* FROM ancestors, Version
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
                  ancestors(ID, Author, Message, Published, Branch, Parent, Timestamp, Snapshot, AlterationList) AS (
                      SELECT Version.* FROM Version WHERE Version.ID = ?
                      UNION ALL
                      SELECT Version.* FROM ancestors, Version
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
                ws.Insert(formatInfo);
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
