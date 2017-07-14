using Nancy;
using Nancy.Conventions;
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
        protected override void ConfigureConventions(NancyConventions conventions)
        {
            base.ConfigureConventions(conventions);

            conventions.ViewLocationConventions.Add((viewName, model, context) => string.Concat("views/_shared/", viewName));
        }
    }
}
