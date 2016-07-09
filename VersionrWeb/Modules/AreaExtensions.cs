using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace VersionrWeb.Modules
{
	public static class AreaExtensions
	{
		public static Guid GetVersionId(this Area area, string branchOrVersion)
		{
			Guid versionId;
			if (!Guid.TryParse(branchOrVersion, out versionId))
			{
				var branch = area.GetBranchByName(branchOrVersion).FirstOrDefault();
				versionId = area.GetBranchHead(branch).Version;
			}
			return versionId;
		}
	}
}
