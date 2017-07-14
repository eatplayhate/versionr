using Nancy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Versionr;
using VersionrWeb.Models;

namespace VersionrWeb.Modules
{
	public class BrowseModule : NancyModule
	{
		public BrowseModule()
		{
			Get["/src"] = _ => { return CreateView(null, ""); };
			Get["/src/{version}"] = ctx => { return CreateView(ctx.version, ""); };
			Get["/src/{version}/{path*}"] = ctx => { return CreateView(ctx.version, ctx.path); };
		}

		private dynamic CreateView(string branchOrVersion, string path)
		{
			// Default branch
			if (branchOrVersion == null)
				branchOrVersion = "master";

			// Set common view properties
			ViewBag.RepoTab = "src";
			ViewBag.BranchOrVersion = branchOrVersion;
			ViewBag.Path = path;
            ViewBag.TabPath = path == "" ? branchOrVersion : string.Format("{0}/{1}", branchOrVersion, Path.GetDirectoryName(path).Replace('\\', '/'));
            ViewBag.ParentPath = path == "" ? null : string.Format("/src/{0}", ViewBag.TabPath);

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
				return CreateDirectoryView(area, version, branchOrVersion, records, "");
			}

			// Find record at path
			var record = records.Where(x => x.CanonicalName == path).FirstOrDefault();
			if (record != null)
			{
				return CreateFileView(area, version, record);
			}

			// Not found as file, must be a directory
			record = records.Where(x => x.CanonicalName == path + "/").FirstOrDefault();
			if (record == null)
			{
				return HttpStatusCode.NotFound;
			}

			return CreateDirectoryView(area, version, branchOrVersion, records, path);
		}

		private dynamic CreateDirectoryView(Area area, Versionr.Objects.Version version, string branchOrVersion, List<Versionr.Objects.Record> records, string path)
		{
			var directoryRecords = records.Where(x => Path.GetDirectoryName(x.CanonicalName.TrimEnd('/')).Replace('\\', '/') == path).ToList();
			return View["Browse/Directory", new DirectoryBrowseModel(area, version, branchOrVersion, path, directoryRecords)];
		}

		private dynamic CreateFileView(Area area, Versionr.Objects.Version version, Versionr.Objects.Record record)
		{
			return View["Browse/File", new FileBrowseModel(area, version, record)];
		}
	}
}
