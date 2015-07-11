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
        [ValueOption(1)]
        public string RemoteObject { get; set; }
    }
    class Pull : RemoteCommand
    {
        protected override bool RunInternal(Client client, RemoteCommandVerbOptions options)
        {
            PullVerbOptions localOptions = options as PullVerbOptions;
            return client.Pull(localOptions.RemoteObject);
        }
    }
}
