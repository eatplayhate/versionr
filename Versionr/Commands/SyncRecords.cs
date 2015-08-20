using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
    class SyncRecordsOptions : RemoteCommandVerbOptions
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
                return "syncrecords";
            }
        }

        [Option("current", HelpText = "Sync objects for current version only.")]
        public bool Current { get; set; }
    }
    class SyncRecords : RemoteCommand
    {
        protected override bool RunInternal(Client client, RemoteCommandVerbOptions options)
        {
            SyncRecordsOptions localOptions = options as SyncRecordsOptions;
            if (localOptions.Current)
                return client.SyncCurrentRecords();
            else
                return client.SyncRecords();
        }
    }
}
