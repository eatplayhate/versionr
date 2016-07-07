using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Versionr
{
	public class Plugin
	{
		public Assembly Assembly { get; set; }
		public VersionrPluginAttribute Attributes { get; set; }
	}
}
