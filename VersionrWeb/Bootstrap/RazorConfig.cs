using Nancy.ViewEngines.Razor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VersionrWeb.Bootstrap
{
	public class RazorConfig : IRazorConfiguration
	{
		public bool AutoIncludeModelNamespace
		{
			get
			{
				return true;
			}
		}

		public IEnumerable<string> GetAssemblyNames()
		{
			yield return "VersionrCore";
			yield return "VersionrWeb";
		}

		public IEnumerable<string> GetDefaultNamespaces()
		{
			yield return "VersionrWeb.Models";
		}
	}
}
