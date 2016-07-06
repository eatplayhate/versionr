using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
	public interface IRemoteClient
	{
		Area Workspace { get; }

		string URL { get; }
		
		bool PushStash(Area.StashInfo stash);
		bool PullStash(string x);
		bool Clone(bool full);
		bool Push(string branchName = null);
		bool Pull(bool pullRemoteObjects, string branchName, bool allBranches = false);
		bool SyncAllRecords();
		bool SyncCurrentRecords();
		Tuple<List<Objects.Branch>, List<KeyValuePair<Guid, Guid>>, Dictionary<Guid, Objects.Version>> ListBranches();
		List<string> GetRecordData(List<Objects.Record> missingRecords);
		void Close();
	}

	public interface IRemoteClientProvider
	{
		IRemoteClient Connect(Area workspace, string url, bool requireWrite);
	}
}
