using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
    class PullVerbOptions : RemoteCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "abandon all ships"
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "pull";
            }
        }
        [Option('b', "branch", HelpText="The name of the branch to pull.")]
        public string RemoteBranch { get; set; }
        [Option('o', "objects", DefaultValue = true, HelpText = "Retreive remote object payloads as well as metadata.")]
        public bool PullObjects { get; set; }
        [Option('u', "update", DefaultValue = false, HelpText = "Update the local revision after pulling data.")]
        public bool Update { get; set; }
    }
    class Pull : RemoteCommand
    {
        protected override bool RunInternal(Client client, RemoteCommandVerbOptions options)
        {
            PullVerbOptions localOptions = options as PullVerbOptions;
            if (!client.Pull(localOptions.PullObjects, localOptions.RemoteBranch))
                return false;
            if (localOptions.Update)
            {
                client.Workspace.Update();
            }
            return true;
        }

        protected override bool UpdateRemoteTimestamp
        {
            get
            {
                return true;
            }
        }
    }
}
