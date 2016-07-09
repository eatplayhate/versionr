using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace VersionrWeb.Modules
{
	public static class ModuleUtil
	{
		public static Area CreateArea()
		{
			return Area.Load(new DirectoryInfo(Environment.CurrentDirectory), true);
		}
	}
}
