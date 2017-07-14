using Nancy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;
using Versionr.Objects;

namespace VersionrWeb.Modules
{
    public class DiffModule : NancyModule
    {
        public DiffModule()
        {
            Get["/diff/{version}"] = ctx => { return CreateView(ctx.version, ""); };
            Get["/diff/{version}/{path*}"] = ctx => { return CreateView(ctx.version, ctx.path); };
        }

        private dynamic CreateView(string branchOrVersion, string path)
        {
            // Set common view properties
            ViewBag.RepoTab = "diff";
            ViewBag.BranchOrVersion = branchOrVersion;
            ViewBag.Path = path;
            ViewBag.ParentPath = path == "" ? null : string.Format("/diff/{0}/{1}", branchOrVersion, Path.GetDirectoryName(path).Replace('\\', '/'));

            // Decode version or lookup head of branch
            var area = this.CreateArea();
            Guid versionId = area.GetVersionId(branchOrVersion);
            var version = area.GetVersion(versionId);
            var records = area.GetRecords(version);

            // Normalize path (we don't know if it's a file or directory from the URL)
            path = path.TrimEnd('/');

            // Root directory
            if (path == "")
            {
                return CreateDiffFolderView(area, version, records, null);
            }

            // Find record at path
            var record = records.Where(x => x.CanonicalName == path).FirstOrDefault();
            if (record == null)
            {
                // Not found as file, must be a directory
                record = records.Where(x => x.CanonicalName == path + "/").FirstOrDefault();
                if (record == null)
                {
                    return HttpStatusCode.NotFound;
                }
            }
            if (record.IsDirectory)
                return CreateDiffFolderView(area, version, records, record.CanonicalName);
            return CreateDiffView(area, version, record);
        }

        private dynamic CreateDiffFolderView(Versionr.Area area, Versionr.Objects.Version version, List<Versionr.Objects.Record> versionRecords, string path)
        {
            if (path != null)
                path = path.TrimEnd('/');
            List<KeyValuePair<Versionr.Objects.AlterationType, Versionr.Objects.Record>> directoryChangeRecords = new List<KeyValuePair<AlterationType, Record>>();
            if (version.Parent.HasValue)
            {
                var changes = GetChanges(area, version, versionRecords);
                if (path == null)
                    directoryChangeRecords = changes.Where(x => x.Value.CanonicalName.IndexOf('/') == -1 || x.Value.CanonicalName.IndexOf('/') == x.Value.CanonicalName.LastIndexOf('/')).ToList();
                else
                    directoryChangeRecords = changes.Where(x => Path.GetDirectoryName(x.Value.CanonicalName.TrimEnd('/')).Replace('\\', '/') == path).ToList();
            }
            return View["Diff/Directory", new Models.DirectoryDiffModel(area, version, path, directoryChangeRecords)];
        }

        private List<KeyValuePair<Versionr.Objects.AlterationType, Versionr.Objects.Record>> GetChanges(Versionr.Area area, Versionr.Objects.Version version, List<Versionr.Objects.Record> versionRecords)
        {
            Versionr.Objects.Version parent = area.GetVersion(version.Parent.Value);
            List<Versionr.Objects.Record> parentRecords = area.GetRecords(parent);
            Dictionary<string, Versionr.Objects.Record> oldRecords = new Dictionary<string, Record>();
            List<KeyValuePair<Versionr.Objects.AlterationType, Versionr.Objects.Record>> changes = new List<KeyValuePair<Versionr.Objects.AlterationType, Versionr.Objects.Record>>();
            Dictionary<string, Versionr.Objects.Record> containers = new Dictionary<string, Record>();
            foreach (var x in versionRecords)
            {
                if (x.IsDirectory)
                    containers[x.CanonicalName] = x;
            }
            foreach (var x in parentRecords)
            {
                oldRecords[x.CanonicalName] = x;
            }
            HashSet<string> currentRecords = new HashSet<string>();
            foreach (var x in versionRecords)
            {
                Versionr.Objects.Record rec;
                if (oldRecords.TryGetValue(x.CanonicalName, out rec))
                {
                    if (rec.Fingerprint != x.Fingerprint)
                        changes.Add(new KeyValuePair<AlterationType, Record>(AlterationType.Update, x));
                }
                else
                    changes.Add(new KeyValuePair<AlterationType, Record>(AlterationType.Add, x));
                currentRecords.Add(x.CanonicalName);
            }
            foreach (var x in oldRecords)
            {
                if (!currentRecords.Contains(x.Key))
                    changes.Add(new KeyValuePair<AlterationType, Record>(AlterationType.Delete, x.Value));
            }
            HashSet<string> mappedContainers = new HashSet<string>();
            foreach (var x in changes)
            {
                if (x.Value.IsDirectory)
                    mappedContainers.Add(x.Value.CanonicalName);
            }
            foreach (var x in changes.ToArray())
            {
                if (!x.Value.IsDirectory)
                {
                    int lastPos = x.Value.CanonicalName.LastIndexOf('/');
                    if (lastPos == -1)
                        continue;
                    string pathContainer = x.Value.CanonicalName.Substring(0, lastPos);
                    while (pathContainer.Length > 0)
                    {
                        string cname = pathContainer + "/";
                        if (!mappedContainers.Add(cname))
                            break;
                        if (!containers.ContainsKey(cname))
                            changes.Add(new KeyValuePair<AlterationType, Record>(AlterationType.Update, new Record() { CanonicalName = cname }));
                        else
                            changes.Add(new KeyValuePair<AlterationType, Record>(AlterationType.Update, containers[cname]));
                        lastPos = pathContainer.LastIndexOf('/');
                        if (lastPos == -1)
                            break;
                        pathContainer = pathContainer.Substring(0, lastPos);
                    }
                }
            }
            return changes;
        }

        private dynamic CreateDiffView(Versionr.Area area, Versionr.Objects.Version version, Versionr.Objects.Record record)
        {
            return View["Diff/View", new Models.FileDiffModel(area, version, record)];
        }
    }
}
