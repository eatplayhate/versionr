using Nancy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VersionrWeb.Models;

namespace VersionrWeb.Modules
{
	public class BrowseModule : NancyModule
	{
		public BrowseModule()
		{
			Get["/src"] = _ => { return CreateView(null, null); };
			Get["/src/{version}"] = ctx => { return CreateView(null, ctx.version); };
			Get["/src/{version}/{path*}"] = ctx => { return CreateView(ctx.path, ctx.version); };
		}

		private dynamic CreateView(string path, string version)
		{
			var model = new BrowseModel(path, version);
			if (model.IsDirectory)
				return View["Browse/Directory", model];
			else
				return View["Browse/File", model];
		}
	}
}
