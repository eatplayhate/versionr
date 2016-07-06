using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class SetRemoteVerbOptions : VerbOptionBase
    {
        [Option('r', "remote", Required = false, HelpText = "Specifies the remote URL.", MutuallyExclusiveSet = "remotemode")]
        public string Remote { get; set; }

        [Option('l', "list", HelpText = "List the known remotes", MutuallyExclusiveSet = "remotemode")]
		public bool List { get; set; }

		[Option('c', "clear", HelpText = "Clears all existing remotes", MutuallyExclusiveSet = "remotemode")]
		public bool Clear { get; set; }
		public override string Usage
        {
            get
            {
                return string.Format("versionr #i#{0}## --remote #b#vsr://servername:port/path## [remote name]", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
				return new string[]
				{
					"This command stores details of remote Versionr servers.",
					"",
					"You must specify #b#--remote## to register a new server.",
					"",
					"Otherwise the --clear and --list options operator on the existing list of remote servers.",
				};
            }
        }

        public override string Verb
        {
            get
            {
                return "set-remote";
            }
        }

        [ValueOption(0)]
        public string Name { get; set; }

        public override BaseCommand GetCommand()
        {
            return new SetRemote();
        }
    }
    class SetRemote : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            SetRemoteVerbOptions localOptions = options as SetRemoteVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;

			if (localOptions.Clear)
			{
				ws.ClearRemotes();
			}
			else if (localOptions.List)
			{
				foreach (var x in ws.GetRemotes())
                {
					Printer.PrintMessage("Remote \"#b#{0}##\" is #b#{1}##", x.Name, x.URL);
				}
			}
			else
            {
                if (string.IsNullOrEmpty(localOptions.Remote))
                {
                    Printer.PrintError("A remote URL must be specified!");
                    Printer.PrintMessage(localOptions.GetUsage());
                    return false;
                }
                string remoteName = string.IsNullOrEmpty(localOptions.Name) ? "default" : localOptions.Name;
                if (ws.SetRemote(localOptions.Remote, remoteName))
                    Printer.PrintMessage("Configured remote \"#b#{0}##\" as: #b#{1}##", remoteName, localOptions.Remote);
            }
			return true;
		}
	}
}
