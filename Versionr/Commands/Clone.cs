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

        [Option('f', "fullmeta", HelpText = "Clones entire vault metadata table.")]
        public bool? Full { get; set; }

        [Option('u', "update", DefaultValue = true, HelpText = "Runs pull and checkout after cloning.")]
        public bool Update { get; set; }

        [Option('b', "branch", HelpText = "Selects a branch to pull and checkout after cloning, requires #b#--update##")]
        public string Branch { get; set; }

        [Option('s', "sync", DefaultValue = false, HelpText = "Synchronizes all objects in metadata after cloning. Requires #b#--fullmeta## to be useful.")]
        public bool Synchronize { get; set; }

        [Option("partial", HelpText = "Sets a partial path within the vault.")]
        public string Partial { get; set; }

        [Option("quietfail", HelpText = "Disables error messages if the clone directory isn't empty.")]
        public bool QuietFail { get; set; }
    }
    class Clone : RemoteCommand
    {
        protected override bool RunInternal(Client client, RemoteCommandVerbOptions options)
        {
            CloneVerbOptions localOptions = options as CloneVerbOptions;
            if (localOptions.QuietFail && new System.IO.DirectoryInfo(System.IO.Path.Combine(TargetDirectory.FullName, ".versionr")).Exists)
                return true;
            bool result = false;
            try
            {
                TargetDirectory.Create();
            }
            catch
            {
                Printer.PrintError("#e#Error - couldn't create subdirectory \"{0}\"##", TargetDirectory);
                return false;
            }
            if (localOptions.Full.HasValue)
                result = client.Clone(localOptions.Full.Value);
            else
            {
                result = client.Clone(true);
                if (!result)
                    result = client.Clone(false);
            }
            if (result)
            {
                Printer.PrintMessage("Successfully cloned from remote vault. Initializing default remote.");
                string remoteName = "default";
                
                if (client.Workspace.SetRemote(client.Host, client.Port, client.Module, remoteName))
                    Printer.PrintMessage("Configured remote \"#b#{0}##\" as: #b#{1}##", remoteName, client.VersionrURL);

                if (localOptions.Partial != null)
                    client.Workspace.SetPartialPath(localOptions.Partial);

                if (localOptions.Update)
                {
                    client.Pull(false, string.IsNullOrEmpty(localOptions.Branch) ? client.Workspace.CurrentBranch.ID.ToString() : localOptions.Branch);
                    Area area = Area.Load(client.Workspace.Root);
                    area.Checkout(localOptions.Branch, false, false);
                }

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
