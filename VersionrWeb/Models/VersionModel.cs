using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VersionrWeb.Models
{
	public class VersionModel
	{
		public Versionr.Objects.Version Version;

		public VersionModel(Versionr.Objects.Version version)
		{
			Version = version;
		}
	}
}
