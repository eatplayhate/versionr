using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
	class ClientProvider : IRemoteClientProvider
	{
		public IRemoteClient Connect(Area workspace, string url, bool requireWrite)
		{
			try
			{
				var scheme = new Uri(url).Scheme;
				if (scheme != "vsr" && scheme != "versionr")
					return null;
			}
			catch (UriFormatException)
			{
				return null;
			}

			var client = new Client(workspace);
			client.Connect(url, requireWrite);
			return client;
		}
	}
}
