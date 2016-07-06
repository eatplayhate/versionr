using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace Vsr2Git.Network
{

	class GitClient : Versionr.Network.IRemoteClient
	{
		private Area m_VsrArea;
		private Repository m_GitRepository;
		private string m_URL;

		public GitClient(Area workspace, Repository repository, string url)
		{
			m_VsrArea = workspace;
			m_GitRepository = repository;
			m_URL = url;
		}

		public string URL
		{
			get
			{
				return m_URL;
			}
		}

		public Area Workspace
		{
			get
			{
				return m_VsrArea;
			}
		}

		public bool Clone(bool full)
		{
			throw new NotSupportedException();
		}

		public void Close()
		{
			m_GitRepository.Dispose();
		}

		public List<string> GetRecordData(List<Versionr.Objects.Record> missingRecords)
		{
			throw new NotSupportedException();
		}

		public Tuple<List<Versionr.Objects.Branch>, List<KeyValuePair<Guid, Guid>>, Dictionary<Guid, Versionr.Objects.Version>> ListBranches()
		{
			throw new NotSupportedException();
		}

		public bool Pull(bool pullRemoteObjects, string branchName, bool allBranches = false)
		{
			throw new NotSupportedException();
		}

		public bool PullStash(string x)
		{
			throw new NotSupportedException();
		}

		public bool Push(string branchName = null)
		{
			throw new NotSupportedException();
		}

		public bool PushStash(Area.StashInfo stash)
		{
			throw new NotSupportedException();
		}

		public bool SyncAllRecords()
		{
			throw new NotSupportedException();
		}

		public bool SyncCurrentRecords()
		{
			throw new NotSupportedException();
		}
	}
}
