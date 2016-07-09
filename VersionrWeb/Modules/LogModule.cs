using Nancy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace VersionrWeb.Modules
{
	public class LogModule : NancyModule
	{
		private const int CommitsPerPage = 50;

		public LogModule()
		{
			Get["/log"] = _ => { return CreateView(null, ""); };
			Get["/log/{version}"] = ctx => { return CreateView(ctx.version, ""); };
			Get["/log/{version}/{path*}"] = ctx => { return CreateView(ctx.version, ctx.path); };
		}

		private dynamic CreateView(string branchOrVersion, string path)
		{
			// Default branch
			if (branchOrVersion == null)
				branchOrVersion = "master";

			// Set common view properties
			ViewBag.RepoTab = "log";
			ViewBag.RepositoryName = Path.GetFileName(Environment.CurrentDirectory);
			ViewBag.BranchOrVersion = branchOrVersion;
			ViewBag.Path = path;

			Area area = this.CreateArea();

			Guid versionId = area.GetVersionId(branchOrVersion);
			var version = area.GetVersion(versionId);
			var history = area.GetHistory(version, CommitsPerPage);

			return View["Log/Log", new Models.LogModel(area, history)];
		}
	}
}
