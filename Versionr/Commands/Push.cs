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
                    "abandon all ships"
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
    }
    class Push : RemoteCommand
    {
        protected override bool RunInternal(Client client, RemoteCommandVerbOptions options)
        {
            PushVerbOptions localOptions = options as PushVerbOptions;
            return client.Push(localOptions.Branch);
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
