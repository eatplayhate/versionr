using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
    class CloneVerbOptions : RemoteCommandVerbOptions
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
                return "clone";
            }
        }

        [Option('f', "fullmeta", DefaultValue = false, HelpText = "Clones entire vault metadata table.")]
        public bool Full { get; set; }

        [Option('s', "sync", DefaultValue = false, HelpText = "Synchronizes all objects in metadata after cloning. Requires #b#--fullmeta## to be useful.")]
        public bool Synchronize { get; set; }
    }
    class Clone : RemoteCommand
    {
        protected override bool RunInternal(Client client, RemoteCommandVerbOptions options)
        {
            CloneVerbOptions localOptions = options as CloneVerbOptions;
            bool result = client.Clone(localOptions.Full);
            if (result)
            {
                Printer.PrintMessage("Successfully cloned from remote vault. Initializing default remote.");
                string remoteName = string.IsNullOrEmpty(localOptions.Name) ? "default" : localOptions.Name;
                client.Workspace.SetRemote(client.Host, client.Port, remoteName);

                if (localOptions.Synchronize)
                    return client.SyncRecords();
            }
            return result;
        }
        protected override bool NeedsWorkspace
        {
            get
            {
                return false;
            }
        }
        protected override bool NeedsNoWorkspace
        {
            get
            {
                return true;
            }
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
