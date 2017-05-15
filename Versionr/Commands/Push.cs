using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
    class PushVerbOptions : RemoteCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Pushes the currently active version (or the head of a specific branch) to a specified remote.",
                    "",
                    "Remotes can be configured using the #b#set-remote## command."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "push";
            }
        }
        [Option('b', "branch", HelpText = "The name of the branch to push.")]
        public string Branch { get; set; }
        [Option("release-locks", HelpText = "Releases locks that apply to this push.")]
        public bool ReleaseLocks { get; set; }

        public override BaseCommand GetCommand()
        {
            return new Push();
        }
    }
    class Push : RemoteCommand
    {
        protected override bool RunInternal(IRemoteClient client, RemoteCommandVerbOptions options)
        {
            PushVerbOptions localOptions = options as PushVerbOptions;
            if (!client.Push(localOptions.Branch))
                return false;

            if (client.RequestUpdate)
            {
                Printer.PrintMessage("Server has remotely merged the current branch.");
                if (Printer.Prompt("Update?"))
                {
                    client.Close();
                    client = client.Workspace.Connect(URL, false);
                    if (!client.Pull(true, null, false))
                        return false;
                    client.Workspace.Update(new Area.MergeSpecialOptions());
                }
            }
            return true;
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
