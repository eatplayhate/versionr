using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Network;

namespace Versionr.Commands
{
    class LockVerbOptions : RemoteCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Acquires a remote lock on a specified path or file.",
                    "",
                    "Optionally, the lock may affect every branch."
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
                return "lock";
            }
        }
        [Option("full", HelpText = "Locks the entire vault.")]
        public bool Full { get; set; }
        [Option("all-branches", HelpText = "Acquires a lock for all branches on the remote.")]
        public bool AllBranches { get; set; }
        [Option('b', "branch", HelpText = "Selects a branch to acquire the lock on - it will use the current branch by default.")]
        public string Branch { get; set; }
        [Option("steal", HelpText = "Invalidates other locks on the server that conflict with your lock.")]
        public bool Steal { get; set; }

        [ValueOption(0)]
        public string Path { get; set; }

        public override BaseCommand GetCommand()
        {
            return new Lock();
        }
    }
    class Lock : RemoteCommand
    {
        protected override bool RunInternal(IRemoteClient client, RemoteCommandVerbOptions options)
        {
            LockVerbOptions localOptions = options as LockVerbOptions;
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
            return client.AcquireLock(localOptions.Path, localOptions.Branch, localOptions.AllBranches, localOptions.Full, localOptions.Steal);
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
