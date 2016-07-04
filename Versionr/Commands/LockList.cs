using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.LocalState;

namespace Versionr.Commands
{
    class LockListVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## #q#[options]## [remote]", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Lists the locks held by the local versionr node.",
                    "",
                    "Optionally can display only locks targeting a specific remote."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "lock-list";
            }
        }
        [Option('g', "guids", HelpText = "Show lock GUIDs.")]
        public bool ShowGuids { get; set; }
        [Option('r', "remote", HelpText = "Show locks for a specific remote.")]
        public string RemoteName { get; set; }

        public override BaseCommand GetCommand()
        {
            return new LockList();
        }
    }
    class LockList : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            LockListVerbOptions localOptions = options as LockListVerbOptions;
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
            List<LocalState.RemoteLock> locks = ws.HeldLocks;
            if (locks.Count == 0)
            {
                Printer.PrintMessage("Vault has no locks.");
                return true;
            }
            if (filterRemote != null)
                locks = ws.HeldLocks.Where(x => x.RemoteHost == filterRemote).ToList();
            if (locks.Count == 0)
            {
                Printer.PrintMessage("Vault has no locks for remote #b#{0}##.", filterRemote);
            }
            else
            {
                Printer.PrintMessage("Vault has #b#{0}## lock{1}:", locks.Count, locks.Count == 1 ? "" : "s");
                int lockIndex = 0;
                Dictionary<string, string> remoteMap = new Dictionary<string, string>();
                foreach (var x in locks)
                {
                    FormatLock(ws, x, localOptions.ShowGuids, remoteMap, lockIndex++);
                }
            }
            return true;
        }

        internal static void FormatLock(Area ws, RemoteLock x, bool showGUIDs, Dictionary<string, string> remoteMap, int? index)
        {
            string lockPath = x.LockingPath;
            if (string.IsNullOrEmpty(lockPath) || lockPath == "/")
                lockPath = "<full vault>";
            string branch = "<all branches>";
            if (x.LockedBranch.HasValue)
            {
                var branchLocal = ws.GetBranch(x.LockedBranch.Value);
                if (branchLocal == null)
                    branch = x.LockedBranch.Value.ToString();
                else
                    branch = branchLocal.ShortID + " (" + branchLocal.Name + ")";
            }
            string guid = string.Empty;
            if (showGUIDs)
                guid = "\n\t#q#" + x.ID;
            string remote = x.RemoteHost;
            string fname;
            if (!remoteMap.TryGetValue(remote, out fname))
            {
                var cachedRemote = ws.FindRemoteFromURL(remote);
                if (cachedRemote == null)
                    fname = string.Empty;
                else
                    fname = " (" + cachedRemote.Name + ")";
                remoteMap[remote] = fname;
            }
            remote += fname;
            string prefix = "=";
            if (index.HasValue)
                prefix = string.Format("#s#\\#{0}##:", index.Value);
            Printer.PrintMessage(" {0} #b#{1}##, branch #c#{2}## on #b#{3}##{4}", prefix, lockPath, branch, remote, guid);
        }
    }
}
