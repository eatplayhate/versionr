using Nancy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VersionrWeb.Modules
{
	public class VersionModule : NancyModule
	{
		public VersionModule()
		{
			Get["/version/{version}"] = ctx => { return CreateView(Guid.Parse(ctx.version)); };
		}

		private dynamic CreateView(Guid versionId)
		{
			var area = this.CreateArea();
			var version = area.GetVersion(versionId);

			// Set common view properties
			ViewBag.RepoTab = "log";
			ViewBag.BranchOrVersion = versionId.ToString("D");
			ViewBag.Path = "";

			return View["Log/Version", new Models.VersionModel(area, version)];
		}
	}
}
