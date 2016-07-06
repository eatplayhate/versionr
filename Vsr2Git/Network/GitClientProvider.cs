using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;
using Versionr.Network;

namespace Vsr2Git.Network
{
	public class GitClientProvider : IRemoteClientProvider
	{
		public IRemoteClient Connect(Area workspace, string url, bool requireWrite)
		{
			// "URL" should be the path to a git repository
			Repository repository = null;
			try
			{
				repository = new Repository(url);
			}
			catch (RepositoryNotFoundException)
			{
				return null;
			}
			return new GitClient(workspace, repository, url);
		}
	}
}
