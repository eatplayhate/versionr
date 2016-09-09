using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
    class PushRecordsVerbOptions : RemoteCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Pushes any records that the local client has that are missing remotely."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "push-records";
            }
        }

        public override BaseCommand GetCommand()
        {
            return new PushRecords();
        }
    }
    class PushRecords : RemoteCommand
    {
        protected override bool RunInternal(IRemoteClient client, RemoteCommandVerbOptions options)
        {
            PushVerbOptions localOptions = options as PushVerbOptions;
            return client.PushRecords();
        }
    }
}
