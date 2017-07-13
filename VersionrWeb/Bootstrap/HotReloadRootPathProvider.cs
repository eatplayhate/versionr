using Nancy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace VersionrWeb.Bootstrap
{
	public class HotReloadRootPathProvider : IRootPathProvider
	{
		/// <summary>
		/// Override root path when running in a source checkout so that static content and
		/// view template changes are reflected immediately.
		/// </summary>
		public string GetRootPath()
		{
			string assemblyPath = Assembly.GetExecutingAssembly().Location;
			string binDirectory = Path.GetDirectoryName(assemblyPath);
			string hotReloadPath = Path.Combine(binDirectory, "..", "VersionrWeb");
			if (Directory.Exists(hotReloadPath))
				return hotReloadPath;
			else
				return binDirectory;
		}
	}
}
