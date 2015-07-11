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
    }
    class Clone : RemoteCommand
    {
        protected override bool RunInternal(Client client, RemoteCommandVerbOptions options)
        {
            CloneVerbOptions localOptions = options as CloneVerbOptions;
            bool result = client.Clone();
            if (result)
            {
                string remoteName = string.IsNullOrEmpty(localOptions.Name) ? "default" : localOptions.Name;
                client.Workspace.SetRemote(client.Host, client.Port, remoteName);
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
