using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.LocalState;

namespace Versionr.Network
{
	public interface IRemoteClient
	{
		Area Workspace { get; }

		string URL { get; }

        System.IO.DirectoryInfo BaseDirectory { get; set; }

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
        bool AcquireLock(string path, string branch, bool allBranches, bool full, bool steal);
        bool ReleaseLocks(List<RemoteLock> locks);
    }

	public interface IRemoteClientProvider
	{
		IRemoteClient Connect(Area workspace, string url, bool requireWrite);
	}
}
