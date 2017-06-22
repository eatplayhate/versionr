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
        public override BaseCommand GetCommand()
        {
            return new Clone();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "This command will clone a vault from a remote server and (by default) checkout the latest revision of the initial branch."
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

        [ValueList(typeof(List<string>))]
        public List<string> Path { get; set; }
    }
    class Clone : RemoteCommand
    {
        protected override bool RunInternal(IRemoteClient client, RemoteCommandVerbOptions options)
        {
            CloneVerbOptions localOptions = options as CloneVerbOptions;
            if (localOptions.Path.Count > 1)
            {
                Printer.PrintError("#e#Error:## Clone path is invalid. Please specify a subfolder to clone in to or leave empty to clone into the current directory.");
                return false;
            }

            // Choose target directory from server name or path
            if (localOptions.Path != null && localOptions.Path.Count == 1)
            {
                string subdir = localOptions.Path[0];
                if (!string.IsNullOrEmpty(subdir))
                {
                    System.IO.DirectoryInfo info;
                    try
                    {
                        info = new System.IO.DirectoryInfo(System.IO.Path.Combine(TargetDirectory.FullName, subdir));
                    }
                    catch
                    {
                        Printer.PrintError("#e#Error - invalid subdirectory \"{0}\"##", subdir);
                        return false;
                    }
                    Printer.PrintMessage("Target directory: #b#{0}##.", info);
                    TargetDirectory = info;
                }
            }

            if (localOptions.QuietFail && new System.IO.DirectoryInfo(System.IO.Path.Combine(TargetDirectory.FullName, ".versionr")).Exists)
                return true;

            try
            {
                var ws = Area.Load(TargetDirectory, Headless, localOptions.BreachContainment);
                if (ws != null)
                {
                    CloneVerbOptions cloneOptions = options as CloneVerbOptions;
                    if (cloneOptions != null && cloneOptions.QuietFail)
                    {
                        Printer.PrintMessage("Directory already contains a vault. Skipping.");
                        return false;
                    }
                    Printer.PrintError("This command cannot function with an active Versionr vault.");
                    return false;
                }
            }
            catch
            {

            }
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
            client.BaseDirectory = TargetDirectory;
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
                
                if (client.Workspace.SetRemote(client.URL, remoteName))
                    Printer.PrintMessage("Configured remote \"#b#{0}##\" as: #b#{1}##", remoteName, client.URL);

                if (localOptions.Partial != null)
                    client.Workspace.SetPartialPath(localOptions.Partial);

                if (localOptions.Update)
                {
                    client.Pull(false, string.IsNullOrEmpty(localOptions.Branch) ? client.Workspace.CurrentBranch.ID.ToString() : localOptions.Branch);
                    Area area = Area.Load(client.Workspace.Root);
                    area.Checkout(localOptions.Branch, false, false);
                }

                if (localOptions.Synchronize)
                    return client.SyncAllRecords();
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
