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
	public static class ModuleExtensions
	{
		public static Area CreateArea(this NancyModule module)
		{
			var area = Area.Load(new DirectoryInfo(Environment.CurrentDirectory), true);

			// Init Area ViewBag properties
			module.ViewBag.BranchNames = (
				from branch in area.Branches
				where branch.Terminus == null
				orderby branch.Name
				select branch.Name).ToArray();
			
			return area;
		}
	}
}
