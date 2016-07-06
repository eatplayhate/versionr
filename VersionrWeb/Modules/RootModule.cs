using Nancy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VersionrWeb.Modules
{
	public class RootModule : NancyModule
	{
		public RootModule()
		{
			Get["/"] = _ => "Hello world!";
		}
	}
}
