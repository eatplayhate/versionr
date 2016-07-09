using Nancy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VersionrWeb.Modules
{
	public class RawModule : NancyModule
	{
		public RawModule()
		{
			Get["/raw/{version}/{path*}"] = ctx => { return CreateView(ctx.path, ctx.version); };
		}

		private dynamic CreateView(string path, string branchOrVersion)
		{
			// Load area
			var area = Versionr.Area.Load(new DirectoryInfo(Environment.CurrentDirectory), true);

			// Decode version or lookup head of branch
			Guid versionId;
			if (!Guid.TryParse(branchOrVersion, out versionId))
			{
				var branch = area.GetBranchByName(branchOrVersion).FirstOrDefault();
				versionId = area.GetBranchHead(branch).Version;
			}

			var version = area.GetVersion(versionId);
			var record = area.GetRecords(version).Where(x => x.CanonicalName == path).FirstOrDefault();
			if (record == null)
				return HttpStatusCode.NotFound;

			string contentType = MimeTypes.GetMimeType(path);

			var stream = area.ObjectStore.GetRecordStream(record);
			stream.Position = 0;
			return Response.FromStream(stream, contentType);
		}
	}
}
