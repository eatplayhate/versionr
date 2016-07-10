using Nancy;
using System;
using System.Collections.Concurrent;
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
			Get["/log"] = ctx => { return CreateView(null, ""); };
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
			ViewBag.BranchOrVersion = branchOrVersion;
			ViewBag.Path = path;
			
			Area area = this.CreateArea();

			Guid versionId = area.GetVersionId(branchOrVersion);
			var version = area.GetVersion(versionId);
			List<Versionr.Objects.Version> history;

			int pageCount;
			int pageNumber = Request.Query["page"];

			if (pageNumber <= 1)
			{
				// First page, do limited history
				history = area.GetHistory(version, CommitsPerPage + 1);
				pageNumber = 1;
				pageCount = history.Count > CommitsPerPage ? 2 : 1;
			}
			else
			{
				// Subsequent page, get full history
				history = area.GetHistory(version);
				pageCount = (int)Math.Ceiling(history.Count / (double)CommitsPerPage);
				history = history.Skip(CommitsPerPage * (pageNumber - 1)).Take(CommitsPerPage).ToList();
			}

			return View["Log/Log", new Models.LogModel(area, history, pageNumber, pageCount)];
		}
	}
}
