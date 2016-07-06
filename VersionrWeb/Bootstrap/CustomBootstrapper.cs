using Nancy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VersionrWeb.Bootstrap
{
	public class CustomBootstrapper : DefaultNancyBootstrapper
	{
		protected override IRootPathProvider RootPathProvider
		{
			get
			{
				return new HotReloadRootPathProvider();
			}
		}
	}
}
