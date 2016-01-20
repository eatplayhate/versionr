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
            rebaseVersion.Author = Environment.UserName;
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

        internal bool SyncCurrentRecords()
        {
            return GetMissingRecords(Database.Records);
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

        public void Update(string updateTarget = null)
        {
            Merge(string.IsNullOrEmpty(updateTarget) ? CurrentBranch.ID.ToString() : updateTarget, true, false);
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

        public bool SetRemote(string host, int port, string module, string name)
        {
            Regex validNames = new Regex("^[A-Za-z0-9-_]+$");
            if (!validNames.IsMatch(name))
            {
                Printer.PrintError("#e#Name \"{0}\" invalid for remote. Only alphanumeric characters, underscores and dashes are allowed.", name);
                return false;
            }
            if (port == -1)
                port = Client.VersionrDefaultPort;
            LocalData.BeginTransaction();
            try
            {
                RemoteConfig config = LocalData.Find<RemoteConfig>(x => x.Name == name);
                if (config == null)
				{
					config = new RemoteConfig() { Name = name };
					config.Host = host;
                    config.Module = module;
                    config.Port = port;
					LocalData.InsertSafe(config);
				}
				else
                {
                    config.Module = module;
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

        public List<Objects.Version> GetLogicalHistory(Objects.Version version, int? limit = null, HashSet<Guid> excludes = null)
        {
            var versions = Database.GetHistory(version, limit);
            List<Objects.Version> results = new List<Objects.Version>();
            HashSet<Guid> primaryLine = new HashSet<Guid>();
            HashSet<Guid> addedLine = new HashSet<Guid>();
            foreach (var x in versions)
            {
                if (excludes == null || !excludes.Contains(x.ID))
                    primaryLine.Add(x.ID);
            }
            foreach (var x in versions)
            {
                if (excludes != null && excludes.Contains(x.ID))
                    continue;
                var merges = Database.GetMergeInfo(x.ID);
                bool rebased = false;
                bool automerged = false;
                if (excludes != null)
                    excludes.Add(x.ID);
                foreach (var y in merges)
                {
                    if (y.Type == MergeType.Rebase)
                        rebased = true;
                    if (y.Type == MergeType.Automatic)
                        automerged = true;
                    var mergedVersion = GetVersion(y.SourceVersion);
                    if (mergedVersion.Branch == x.Branch && !rebased)
                    {
                        // automerge or manual reconcile
                        var mergedHistory = GetLogicalHistory(mergedVersion, limit, excludes != null ? excludes : primaryLine);
                        foreach (var z in mergedHistory)
                        {
                            if (!addedLine.Contains(z.ID))
                            {
                                addedLine.Add(z.ID);
                                results.Add(z);
                            }
                            else
                                break;
                        }
                    }
                }
                if (!automerged)
                {
                    addedLine.Add(x.ID);
                    results.Add(x);
                }
            }
            var ordered = results.OrderByDescending(x => x.Timestamp);
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
                return "v1.1.34";
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
            RootDirectory = AdministrationFolder.Parent;
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

        public List<Objects.Alteration> GetAlterations(Objects.Version x)
        {
            return Database.GetAlterationsForVersion(x);
        }

        public Objects.Record GetRecord(long id)
        {
            Objects.Record rec = Database.Query<Objects.Record>("SELECT * FROM ObjectName INNER JOIN (SELECT Record.* FROM Record WHERE Record.Id = ?) AS results ON ObjectName.NameId = results.CanonicalNameId", id).FirstOrDefault();
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

        internal void CommitDatabaseTransaction()
        {
            Database.Commit();
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

		private void LoadDirectives()
		{
            Configuration = null;
            FileInfo info = new FileInfo(Path.Combine(Root.FullName, ".vrmeta"));
            if (info.Exists)
            {
                string data = string.Empty;
                using (var sr = info.OpenText())
                {
                    data = sr.ReadToEnd();
                }
                Configuration = Newtonsoft.Json.Linq.JObject.Parse(data);
                Directives = LoadConfigurationElement<Directives>("Versionr");
            }
            else
                Directives = new Directives();
            FileInfo localInfo = new FileInfo(Path.Combine(Root.FullName, ".vruser"));
            if (localInfo.Exists)
            {
                string data = string.Empty;
                using (var sr = localInfo.OpenText())
                {
                    data = sr.ReadToEnd();
                }
                var localObj = Newtonsoft.Json.Linq.JObject.Parse(data);
                var localDirJSON = localObj["Versionr"];
                if (localDirJSON != null)
                {
                    var localDirectives = Newtonsoft.Json.JsonConvert.DeserializeObject<Directives>(localDirJSON.ToString());
                    if (localDirectives != null)
                    {
                        if (Directives != null)
                            Directives.Merge(localDirectives);
                        else
                            Directives = localDirectives;
                    }
                }
            }
        }

        public T LoadConfigurationElement<T>(string v)
            where T : new()
        {
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

        public void Merge(string v, bool updateMode, bool force, bool allowrecursiveMerge = false, bool reintegrate = false)
        {
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
                if (possibleBranch == null && reintegrate)
                    throw new Exception("Can't reintegrate when merging a version and not a branch.");

                var parents = GetCommonParents(null, mergeVersion);
                if (parents == null || parents.Count == 0)
                    throw new Exception("No common parent!");

                Objects.Version parent = null;
                Printer.PrintMessage("Starting merge:");
                Printer.PrintMessage(" - Local: {0} #b#\"{1}\"##", Database.Version.ID, GetBranch(Database.Version.Branch).Name);
                Printer.PrintMessage(" - Remote: {0} #b#\"{1}\"##", mergeVersion.ID, GetBranch(mergeVersion.Branch).Name);
                if (parents.Count == 1 || !allowrecursiveMerge)
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
                        Merge(updateHeads.First(x => x.ID != parentVersion.ID).ID.ToString(), false, false, true, false);
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
                        if (x.Author == Environment.UserName)
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
            Dictionary<string, bool> parentIgnoredList = new Dictionary<string,bool>();

            foreach (var x in CheckoutOrder(foreignRecords))
            {
                TransientMergeObject parentObject = null;
                parentDataLookup.TryGetValue(x.CanonicalName, out parentObject);
                Status.StatusEntry localObject = null;
                status.Map.TryGetValue(x.CanonicalName, out localObject);

                bool included = Included(x.CanonicalName);
                if (localObject == null || localObject.Removed)
                {
                    if (localObject != null && localObject.Staged == false && localObject.IsDirectory)
                    {
                        if (included)
                        {
                            Printer.PrintMessage("Recreating locally missing directory: #b#{0}##.", localObject.CanonicalName);
                            RestoreRecord(x, newRefTime);
                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                        }
                    }
                    else if (parentObject == null)
                    {
                        // Added
                        if (included)
                            RestoreRecord(x, newRefTime);
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
                        // Removed locally
                        if (parentObject.DataEquals(x))
                        {
                            // this is fine, we removed it in our branch
                        }
                        else
                        {
                            if (!included && !updateMode)
                            {
                                Printer.PrintError("#x#Error:##\n  Merge results in a tree change outside the current restricted path. Aborting.");
                                return;
                            }
                            // less fine
                            if (included)
                            {
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
                                    Printer.PrintMessage("Resolved incoming update of ignored file: #b#{0}##", x.CanonicalName);
                                    if (!directoryRemoved)
                                        delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                                }
                                else
                                {
                                    Printer.PrintWarning("Object \"{0}\" removed locally but changed in target version.", x.CanonicalName);
                                    RestoreRecord(x, newRefTime);
                                    LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
                                    if (!updateMode)
                                        delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (localObject.DataEquals(x))
                    {
                        // all good, same data in both places
                        if (localObject.Code == StatusCode.Unversioned)
                        {
                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.Add, Operand1 = x.CanonicalName });
                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                        }
                    }
                    else
                    {
                        if (localObject.Code == StatusCode.Masked)
                        {
                            // don't care, update the merge info because someone else does care
                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
                        }
                        else if (parentObject != null && parentObject.DataEquals(localObject))
                        {
                            // modified in foreign branch
                            if (included)
                            {
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
                            // modified locally
                        }
                        else if (parentObject == null)
                        {
                            if (included)
                            {
                                // added in both places
                                var mf = GetTemporaryFile(x);
                                var ml = localObject.FilesystemEntry.Info;
                                var mr = GetTemporaryFile(x);
                            
                                RestoreRecord(x, newRefTime, mf.FullName);

                                mf = new FileInfo(mf.FullName);

                                FileInfo result = Merge2Way(x, mf, localObject.VersionControlRecord, ml, mr, true, ref resolveAll);
                                if (result != null)
                                {
                                    if (result != ml)
                                        ml.Delete();
                                    if (result != mr)
                                        mr.Delete();
                                    if (result != mf)
                                        mf.Delete();
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
                                    mr.Delete();
                                    LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
								    if (!updateMode)
                                        delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
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
                            if (included)
                            {
                                var mf = GetTemporaryFile(x);
                                FileInfo mb;
                                var ml = localObject.FilesystemEntry.Info;
                                var mr = GetTemporaryFile(x);

                                if (parentObject.TemporaryFile == null)
                                {
                                    mb = GetTemporaryFile(parentObject.Record);
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
                                    if (result != ml)
                                        ml.Delete();
                                    if (result != mr)
                                        mr.Delete();
                                    if (result != mf)
                                        mf.Delete();
                                    if (result != mb)
                                        mb.Delete();
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
                                    LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Conflict, Operand1 = x.CanonicalName });
								    if (!updateMode)
                                        delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = x.Id });
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
            List<Record> deletionList = new List<Record>();
            foreach (var x in DeletionOrder(parentData))
            {
                Objects.Record foreignRecord = null;
                foreignLookup.TryGetValue(x.CanonicalName, out foreignRecord);
                Status.StatusEntry localObject = null;
                status.Map.TryGetValue(x.CanonicalName, out localObject);
                if (foreignRecord == null)
                {
                    // deleted by branch
                    if (localObject != null)
                    {
                        if (localObject.Code == StatusCode.Masked)
                        {
                            Printer.PrintMessage("Removing record for ignored object #b#{0}##", x.CanonicalName);
                            delayedStageOperations.Add(new StageOperation() { Type = StageOperationType.MergeRecord, Operand1 = x.CanonicalName, ReferenceObject = -1 });
                        }
                        else if (!localObject.Removed)
                        {
                            string path = System.IO.Path.Combine(Root.FullName, x.CanonicalName);
                            if (x.DataEquals(localObject))
                            {
                                Printer.PrintMessage("Removing {0}", x.CanonicalName);
                                deletionList.Add(x.Record);
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
            foreach (var x in deletionList)
            {
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
					catch (Exception e)
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
                    x.TemporaryFile.Delete();
            }
            LocalData.BeginTransaction();
            foreach (var x in filetimesToRemove)
                RemoveFileTimeCache(x);
            LocalData.Commit();
            if (reintegrate)
                LocalData.AddStageOperation(new StageOperation() { Type = StageOperationType.Reintegrate, Operand1 = possibleBranch.ID.ToString() });
            if (!updateMode)
            {
                Dictionary<Guid, int> mergeVersionGraph = null;
                foreach (var x in LocalData.StageOperations)
                {
                    if (x.Type == StageOperationType.Merge)
                    {
                        if (mergeVersionGraph == null)
                            mergeVersionGraph = GetParentGraph(mergeVersion);
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
                Merge(postUpdateMerge, false, false, true, false);
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
                    x.TemporaryFile.Delete();
            }
            return results;
        }

        private FileInfo Merge3Way(Record x, FileInfo foreign, Record localRecord, FileInfo local, Record record, FileInfo parentFile, FileInfo temporaryFile, bool allowConflict, ref ResolveType? resolveAll)
        {
            Printer.PrintMessage("Merging {0}", x.CanonicalName);
            // modified in both places
            string mf = foreign.FullName;
            string mb = parentFile.FullName;
            string ml = local.FullName;
            string mr = temporaryFile.FullName;
            bool isBinary = FileClassifier.Classify(foreign) == FileEncoding.Binary ||
                FileClassifier.Classify(local) == FileEncoding.Binary ||
                FileClassifier.Classify(parentFile) == FileEncoding.Binary;

            System.IO.File.Copy(ml, ml + ".mine", true);
            if (!isBinary && Utilities.DiffTool.Merge3Way(mb, ml, mf, mr, Directives.ExternalMerge))
            {
                System.IO.File.Delete(ml + ".mine");
                Printer.PrintMessage(" - Resolved.");
                return temporaryFile;
            }
            else
            {
                ResolveType resolution = GetResolution(isBinary, ref resolveAll);
                if (resolution == ResolveType.Mine)
                {
                    System.IO.File.Delete(ml + ".mine");
                    return local;
                }
                if (resolution == ResolveType.Theirs)
                {
                    System.IO.File.Delete(ml + ".mine");
                    return foreign;
                }
                else
                {
                    if (!allowConflict)
                        throw new Exception();
                    System.IO.File.Move(mf, ml + ".theirs");
                    System.IO.File.Move(mb, ml + ".base");
                    Printer.PrintMessage(" - File not resolved. Please manually merge and then mark as resolved.");
                    return null;
                }
            }
        }

        enum ResolveType
        {
            Mine,
            Theirs,
            Conflict
        }

        private FileInfo Merge2Way(Record x, FileInfo foreign, Record localRecord, FileInfo local, FileInfo temporaryFile, bool allowConflict, ref ResolveType? resolveAll)
        {
            Printer.PrintMessage("Merging {0}", x.CanonicalName);
            string mf = foreign.FullName;
            string ml = local.FullName;
            string mr = temporaryFile.FullName;

            bool isBinary = FileClassifier.Classify(foreign) == FileEncoding.Binary ||
                FileClassifier.Classify(local) == FileEncoding.Binary;

            System.IO.File.Copy(ml, ml + ".mine", true);
            if (!isBinary && Utilities.DiffTool.Merge(ml, mf, mr, Directives.ExternalMerge2Way))
            {
                System.IO.File.Delete(ml + ".mine");
                Printer.PrintMessage(" - Resolved.");
                return temporaryFile;
            }
            else
            {
                ResolveType resolution = GetResolution(isBinary, ref resolveAll);
                if (resolution == ResolveType.Mine)
                {
                    System.IO.File.Delete(ml + ".mine");
                    return local;
                }
                if (resolution == ResolveType.Theirs)
                {
                    System.IO.File.Delete(ml + ".mine");
                    return foreign;
                }
                else
                {
                    if (!allowConflict)
                        throw new Exception();
                    System.IO.File.Move(mf, ml + ".theirs");
                    Printer.PrintMessage(" - File not resolved. Please manually merge and then mark as resolved.");
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
            if (!binary)
                Printer.PrintMessage("Merge marked as failure, use #s#(m)ine##, #c#(t)heirs## or #e#(c)onflict##? (Use #b#*## for all)");
            else
                Printer.PrintMessage("File is binary, use #s#(m)ine##, #c#(t)heirs## or #e#(c)onflict##? (Use #b#*## for all)");
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
            else
            {
                if (resolution.Contains("*"))
                    resolveAll = ResolveType.Conflict;
                return ResolveType.Conflict;
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
            List<KeyValuePair<Guid, int>> shared = GetSharedParentGraphMinimal(version, foreignGraph);
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
                var parents = GetParentGraph(GetVersion(shared[i].Key));
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

        private List<KeyValuePair<Guid, int>> GetSharedParentGraphMinimal(Objects.Version version, Dictionary<Guid, int> foreignGraph)
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
                    foreach (var x in Database.GetMergeInfo(currentNode.ID))
                    {
                        if (!visited.ContainsKey(x.SourceVersion))
                            openNodes.Push(new Tuple<Objects.Version, int>(Database.Get<Objects.Version>(x.SourceVersion), currentNodeData.Item2 + 1));
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

        public Dictionary<Guid, int> GetParentGraph(Objects.Version mergeVersion)
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
            if (!System.Text.RegularExpressions.Regex.IsMatch(v, "^[A-Za-z0-9_-]+$"))
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
            DAG <Objects.Version, Guid> result = new DAG<Objects.Version, Guid>();
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

		private static IEnumerable<Record> CheckoutOrder(List<Record> targetRecords)
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
                            System.IO.File.Delete(path);
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
                    catch (Utilities.Symlink.TargetNotFoundException e)
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
					catch (Utilities.Symlink.TargetNotFoundException e)
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
                    var result = Client.ParseRemoteName(x.Value.Host);
                    if (result.Item1 == false)
                    {
                        Printer.PrintError("#x#Error:##\n  Couldn't parse remote hostname \"#b#{0}##\" while processing extern \"#b#{1}##\"!", x.Value.Host, x.Key);
                        continue;
                    }
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

                    if (!client.Connect(result.Item2, result.Item3, result.Item4))
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
                        client.Workspace.SetRemote(result.Item2, result.Item3, result.Item4, "default");
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
                    external.SetRemote(result.Item2, result.Item3, result.Item4, "default");
                    if (fresh)
                    {
                        external.Checkout(x.Value.Target, false, false);
                    }
                    else
                    {
                        bool multiple;
                        Objects.Branch externBranch = external.GetBranchByPartialName(string.IsNullOrEmpty(x.Value.Branch) ? external.GetVersion(external.Domain).Branch.ToString() : x.Value.Branch, out multiple);
                        if (x.Value.Target == null)
                        {
                            if (external.CurrentBranch.ID == externBranch.ID)
                                external.Update();
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

                    Client client = new Client(this);
                    try
                    {
                        if (!client.Connect(x.Host, x.Port, x.Module))
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
			foreach (var x in targets)
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
                    if (callback != null)
                        callback(x, StatusCode.Modified);
                }

				if (revertRecord && x.Code != StatusCode.Unchanged)
				{
					Record rec = Database.Records.Where(z => z.CanonicalName == x.CanonicalName).FirstOrDefault();
					if (rec != null)
					{
						Printer.PrintMessage("Reverted: #b#{0}##", x.CanonicalName);
						RestoreRecord(rec, DateTime.UtcNow);
					}
                    if (deleteNewFiles &&
                        (x.Code == StatusCode.Unversioned || x.Code == StatusCode.Copied || x.Code == StatusCode.Renamed))
                    {
                        if (x.FilesystemEntry.IsDirectory)
                            directoryDeletionList.Add(x);
                        else
                            deletionList.Add(x);
                    }
				}
			}
            foreach (var x in deletionList)
            {
                Printer.PrintMessage("#e#Deleted:## #b#{0}##", x.CanonicalName);
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
                                                            Printer.PrintMessage("Removed (ignored locally): #b#{0}##", x.CanonicalName);
                                                            Alteration localAlteration = new Alteration();
                                                            localAlteration.Type = AlterationType.Delete;
                                                            localAlteration.PriorRecord = x.VersionControlRecord.Id;
                                                            alterations.Add(localAlteration);
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
                            return false;
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
                Database.GetCachedRecords(Version);
                return result;
            }
            catch
            {
                Printer.PrintWarning("#w#Warning:##\n  Error during commit.");
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
                DirectoryInfo directory = new DirectoryInfo(GetRecordPath(rec));
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
                        Printer.PrintMessage("Updating {0}", GetLocalCanonicalName(rec));
                    else
                        feedback(RecordUpdateType.Updated, GetLocalCanonicalName(rec), rec);
                }
            }
            else if (overridePath == null)
            {
                if (feedback == null)
                    Printer.PrintMessage("Creating {0}", GetLocalCanonicalName(rec));
                else
                    feedback(RecordUpdateType.Created, GetLocalCanonicalName(rec), rec);
            }
            int retries = 0;
        Retry:
            try
            {
                if (rec.Size == 0)
                {
                    using (var fs = dest.Create()) { }
                }
                else
                {
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
                    return PartialPath == null ? "" : PartialPath;
                string local = localFolder.Substring(rootFolder.Length + 1);
                if (local.Equals(".vrmeta", StringComparison.OrdinalIgnoreCase))
                    return local;
                return PartialPath + local;
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
