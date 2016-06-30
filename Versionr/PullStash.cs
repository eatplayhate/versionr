using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
    class PullStashVerbOptions : RemoteCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Pulls one or more stashes from a remote server."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "pull-stash";
            }
        }

        [ValueList(typeof(List<string>))]
        public List<string> Names { get; set; }
    }
    class PullStash : RemoteCommand
    {
        protected override bool RunInternal(Client client, RemoteCommandVerbOptions options)
        {
            PullStashVerbOptions localOptions = options as PullStashVerbOptions;
            foreach (var x in localOptions.Names)
            {
                bool ambiguous;
                var stash = StashList.LookupStash(client.Workspace, x, out ambiguous);
                if (stash == null && !ambiguous)
                    client.PullStash(x);
            }
            return true;
        }
    }
}
