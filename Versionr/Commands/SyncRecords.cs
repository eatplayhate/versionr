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
                    "This operation is used to download all record data from a remote node. It is useful if you wish to perform a full, deep copy of a server."
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

        public override BaseCommand GetCommand()
        {
            return new SyncRecords();
        }
    }
    class SyncRecords : RemoteCommand
    {
        protected override bool RunInternal(IRemoteClient client, RemoteCommandVerbOptions options)
        {
            SyncRecordsOptions localOptions = options as SyncRecordsOptions;
            if (localOptions.Current)
                return client.SyncCurrentRecords();
            else
                return client.SyncAllRecords();
        }
    }
}
