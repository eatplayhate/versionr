using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
    class PushStashVerbOptions : RemoteCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Pushes one or more stashes to a remote server."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "push-stash";
            }
        }

        [ValueList(typeof(List<string>))]
        public List<string> Names { get; set; }
    }
    class PushStash : RemoteCommand
    {
        protected override bool RunInternal(Client client, RemoteCommandVerbOptions options)
        {
            PushStashVerbOptions localOptions = options as PushStashVerbOptions;
            foreach (var x in localOptions.Names)
            {
                var stash = StashList.LookupStash(client.Workspace, x);
                if (stash != null)
                    client.PushStash(stash);
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
