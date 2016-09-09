using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Network;

namespace Versionr.Commands
{
    class LockCheckVerbOptions : RemoteCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Checks if a remote has a lock on a specified file/path or branch.",
                    "",
                    "Optionally, you can check for locks affecting every branch."
                };
            }
        }
        public override string OptionsString
        {
            get
            {
                return "#q#[options]## <lock path>";
            }
        }

        public override string Verb
        {
            get
            {
                return "lock-check";
            }
        }
        [Option("full", HelpText = "Checks the entire vault.")]
        public bool Full { get; set; }
        [Option("all-branches", HelpText = "Tests all branches on the remote.")]
        public bool AllBranches { get; set; }
        [Option('b', "branch", HelpText = "Selects a branch to test - it will use the current branch by default.")]
        public string Branch { get; set; }
        [Option("break", HelpText = "Invalidates other locks on the server that conflict with the specified path/branch.")]
        public bool Break { get; set; }

        [ValueOption(0)]
        public string Path { get; set; }

        public override BaseCommand GetCommand()
        {
            return new LockCheck();
        }
    }
    class LockCheck : RemoteCommand
    {
        protected override bool RunInternal(IRemoteClient client, RemoteCommandVerbOptions options)
        {
            LockCheckVerbOptions localOptions = options as LockCheckVerbOptions;
            if (string.IsNullOrEmpty(localOptions.Path) && !localOptions.Full)
            {
                Printer.PrintMessage("#x#Error:## missing specification of lock path!");
            }
            if (!localOptions.Full)
            {
                string fullPath = System.IO.Path.GetFullPath(localOptions.Path);
                if (System.IO.Directory.Exists(fullPath))
                    fullPath += "/";
                else if (!System.IO.File.Exists(fullPath))
                    Printer.PrintMessage("#w#Warning:## File #b#\"{0}\"## does not exist (yet).", localOptions.Path);
                localOptions.Path = client.Workspace.GetLocalPath(fullPath);
            }
            if (string.IsNullOrEmpty(localOptions.Branch))
                localOptions.Branch = client.Workspace.CurrentBranch.ID.ToString();
            if (localOptions.Break)
                return client.BreakLocks(localOptions.Path, localOptions.Branch, localOptions.AllBranches, localOptions.Full);
            else
                return client.ListLocks(localOptions.Path, localOptions.Branch, localOptions.AllBranches, localOptions.Full);
        }

        protected override bool RequiresWriteAccess
        {
            get
            {
                return true;
            }
        }
    }
}
