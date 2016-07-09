using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace VersionrWeb.Models
{
	public class LogModel
	{
		public List<Versionr.Objects.Version> Versions;

		public LogModel(Area area, List<Versionr.Objects.Version> versions)
		{
			Versions = versions;
		}
	}
}
