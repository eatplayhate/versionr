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
        bool Connected { get; }

        System.IO.DirectoryInfo BaseDirectory { get; set; }

        bool PushStash(Area.StashInfo stash);
		bool PullStash(string x);
        List<Area.StashInfo> ListStashes(List<string> names);
        bool Clone(bool full);
		bool Push(string branchName = null);
		bool PushRecords();
		bool Pull(bool pullRemoteObjects, string branchName, bool allBranches = false);
		bool SyncAllRecords();
		bool SyncCurrentRecords();
		Tuple<List<Objects.Branch>, List<KeyValuePair<Guid, Guid>>, Dictionary<Guid, Objects.Version>> ListBranches();
		List<string> GetMissingData(List<Objects.Record> missingRecords, List<string> missingBlobs);
        void Close();
        bool AcquireLock(string path, string branch, bool allBranches, bool full, bool steal);
        bool ListLocks(string path, string branch, bool allBranches, bool full);
        bool BreakLocks(string path, string branch, bool allBranches, bool full);
        bool ReleaseLocks(List<RemoteLock> locks);
        bool RequestUpdate { get; }
    }

	public interface IRemoteClientProvider
	{
		IRemoteClient Connect(Area workspace, string url, bool requireWrite);
	}
}
