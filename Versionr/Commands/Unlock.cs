using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Network;

namespace Versionr.Commands
{
    class UnlockVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## #q#[options]## <remote> or lock ID>", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Relinquishes locks and communicates with the remote nodes to release them on the server.",
                    "",
                    "Locks can be specified by their lock number, their GUID, or by the remote node which has been locked."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "unlock";
            }
        }
        [Option("all", HelpText = "Unlock all locks.")]
        public bool All { get; set; }
        [Option('r', "remote", HelpText = "Show locks for a specific remote.")]
        public string RemoteName { get; set; }
        [Option("prefix", HelpText = "Release locks with a specific path or path prefix.")]
        public string PathPrefix { get; set; }
        [Option('b', "branch", HelpText = "Release locks on a specific branch.")]
        public string Branch { get; set; }

        [ValueList(typeof(List<string>))]
        public List<string> Locks { get; set; }

        public override BaseCommand GetCommand()
        {
            return new Unlock();
        }
    }
    class Unlock : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            UnlockVerbOptions localOptions = options as UnlockVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            string filterRemote = null;
            if (localOptions.RemoteName != null)
            {
                var remote = ws.GetRemote(string.IsNullOrEmpty(localOptions.RemoteName) ? "default" : localOptions.RemoteName);
                if (remote == null)
                {
                    Printer.PrintError("#e#Error:## couldn't find a remote with name #b#\"{0}\". Assuming versionr URL.", localOptions.RemoteName);
                    filterRemote = localOptions.RemoteName;
                }
                else
                    filterRemote = Network.Client.ToVersionrURL(remote);
            }

            List<LocalState.RemoteLock> remoteLocks = ws.HeldLocks;
            IEnumerable<LocalState.RemoteLock> locks = remoteLocks;
            List<string> guidMatching = new List<string>();

            if (filterRemote != null)
                locks = locks.Where(x => x.RemoteHost == filterRemote);
            if (!string.IsNullOrEmpty(localOptions.PathPrefix))
                locks = locks.Where(x => x.LockingPath.StartsWith(localOptions.PathPrefix, StringComparison.OrdinalIgnoreCase));

            Guid? branchFilter = null;
            if (!string.IsNullOrEmpty(localOptions.Branch))
            {
                bool multipleBranches;
                var localBranch = ws.GetBranchByPartialName(localOptions.Branch, out multipleBranches);
                if (localBranch != null)
                {
                    branchFilter = localBranch.ID;
                }
                else
                {
                    Printer.PrintError("#x#Error:## Couldn't find branch \"{0}\".", localOptions.Branch);
                    return false;
                }

                locks = locks.Where(x => x.LockedBranch == branchFilter);
            }

            Dictionary<string, string> remoteMap = new Dictionary<string, string>();
            if (!localOptions.All)
            {
                List<LocalState.RemoteLock> matchedLocks = new List<LocalState.RemoteLock>();
                foreach (var x in localOptions.Locks)
                {
                    int lockID = 0;
                    if (x.StartsWith("#") && int.TryParse(x.Substring(1), out lockID))
                    {
                        if (lockID < 0 || lockID > remoteLocks.Count)
                            Printer.PrintMessage("#w#Warning:## Can't find lock #b#\\#{0}##!", lockID);
                        else if (locks.Contains(remoteLocks[lockID]))
                            matchedLocks.Add(remoteLocks[lockID]);
                    }
                    else
                    {
                        var matching = locks.Where(y => y.ID.ToString().StartsWith(x, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (matching.Count == 0)
                            Printer.PrintMessage("#w#Warning:## Can't find lock with GUID prefix of #b#{0}##!", x);
                        else if (matching.Count == 1)
                            matchedLocks.Add(matching[0]);
                        else
                        {
                            Printer.PrintMessage("#e#Error:## Ambiguous lock specifier - #b#{0}##\nCould be:", x);
                            foreach (var y in matchedLocks)
                            {
                                LockList.FormatLock(ws, y, true, remoteMap, null);
                            }
                            return false;
                        }
                    }
                }
                locks = matchedLocks.Distinct();
            }

            List<LocalState.RemoteLock> finalList = locks.OrderBy(y => y.RemoteHost).ToList();
            if (finalList.Count == 0)
            {
                Printer.PrintMessage("No locks specified to release.");
                return true;
            }

            Printer.PrintMessage("Release the folowing locks:");
            foreach (var x in finalList)
                LockList.FormatLock(ws, x, false, remoteMap, null);

            if (Printer.Prompt("Is this correct?"))
            {
                string lastRemote = string.Empty;
                Client client = new Client(ws);
                HashSet<LocalState.RemoteLock> bucketed = new HashSet<LocalState.RemoteLock>();
                List<Tuple<string, List<LocalState.RemoteLock>>> lockBuckets = new List<Tuple<string, List<LocalState.RemoteLock>>>();

                foreach (var x in finalList)
                {
                    if (bucketed.Contains(x))
                        continue;
                    var bucket = lockBuckets.Where(z => z.Item1 == x.RemoteHost).ToList();
                    List<LocalState.RemoteLock> remoteLockList = null;
                    if (bucket.Count == 0)
                    {
                        remoteLockList = new List<LocalState.RemoteLock>();
                        lockBuckets.Add(new Tuple<string, List<LocalState.RemoteLock>>(x.RemoteHost, remoteLockList));
                    }
                    else
                        remoteLockList = bucket[0].Item2;

                    remoteLockList.Add(x);
                }

                foreach (var x in lockBuckets)
                {
                    var parsedRemoteName = Client.ParseRemoteName(x.Item1);
                    if (parsedRemoteName.Item1 == false)
                    {
                        if (Printer.Prompt(string.Format("#e#Error:## can't parse remote name #b#{0}##, release locks anyway", x.Item1)))
                        {
                            ws.ReleaseLocks(x.Item2.Select(z => z.ID));
                        }
                    }
                    string host = parsedRemoteName.Item2;
                    int port = parsedRemoteName.Item3;
                    string modulePath = parsedRemoteName.Item4;
                    
                    if (!client.Connect(host, port, modulePath))
                    {
                        if (Printer.Prompt(string.Format("#e#Error:## couldn't connect to remote #b#{0}##, release locks anyway", x.Item1)))
                        {
                            ws.ReleaseLocks(x.Item2.Select(z => z.ID));
                        }
                    }
                    else if (!client.ReleaseLocks(x.Item2))
                    {
                        if (Printer.Prompt(string.Format("#e#Error:## couldn't communicate with remote #b#{0}##, release locks anyway", x.Item1)))
                        {
                            ws.ReleaseLocks(x.Item2.Select(z => z.ID));
                        }
                    }
                }
            }
            return true;
        }
    }
}
