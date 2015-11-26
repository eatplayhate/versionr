using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class RemoteVerbOptions : VerbOptionBase
    {
        [Option('h', "host", Required = false, HelpText = "Specifies the hostname of the remote.", MutuallyExclusiveSet = "remotemode")]
		public string Host { get; set; }
        [Option('p', "port", DefaultValue = 5122, Required = false, HelpText = "Specifies the port of the remote.")]
        public int Port { get; set; }
        [Option('r', "remote", Required = false, HelpText = "Specifies the remote URL.")]
        public string Remote { get; set; }
        [Option('m', "module", Required = false, HelpText = "The name of the remote module to select (used if a single server is hosting multiple vaults).")]
        public string Module { get; set; }

        [Option('l', "list", HelpText = "List the known remotes", MutuallyExclusiveSet = "remotemode")]
		public bool List { get; set; }

		[Option('c', "clear", HelpText = "Clears all existing remotes", MutuallyExclusiveSet = "remotemode")]
		public bool Clear { get; set; }
		public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} --host <host> --port <port> [remote name]", Verb);
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
					"You must specify both hostname and port to register a new server.",
					"",
					"Otherwise the --clear and --list options operator on the existing",
					"list of remote servers.",
				};
            }
        }

        public override string Verb
        {
            get
            {
                return "remote";
            }
        }

        [ValueOption(0)]
        public string Name { get; set; }
    }
    class Remote : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            RemoteVerbOptions localOptions = options as RemoteVerbOptions;
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
					Printer.PrintMessage("Remote \"#b#{0}##\" is #b#{1}##", x.Name, Network.Client.ToVersionrURL(x.Host, x.Port, x.Module));
				}
			}
			else
            {
                if (!string.IsNullOrEmpty(localOptions.Remote))
                {
                    var remote = Network.Client.ParseRemoteName(localOptions.Remote);
                    if (remote.Item1)
                    {
                        localOptions.Host = remote.Item2;
                        localOptions.Port = remote.Item3 == -1 ? localOptions.Port : remote.Item3;
                        localOptions.Module = remote.Item4;
                    }
                }
                if (string.IsNullOrEmpty(localOptions.Host))
                {
                    Printer.PrintError("A remote URL or hostname must be specified!");
                    Printer.PrintMessage(localOptions.GetUsage());
                    return false;
                }
                string remoteName = string.IsNullOrEmpty(localOptions.Name) ? "default" : localOptions.Name;
                if (ws.SetRemote(localOptions.Host, localOptions.Port, localOptions.Module, remoteName))
                    Printer.PrintMessage("Configured remote \"#b#{0}##\" as: #b#{1}##", remoteName, Network.Client.ToVersionrURL(localOptions.Host, localOptions.Port));
            }
			return true;
		}
	}
}
