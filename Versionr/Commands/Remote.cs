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
        [Option('h', "host", Required = false, HelpText = "Specifies the hostname of the remote.")]
        public string Host { get; set; }
        [Option('p', "port", Required = false, HelpText = "Specifies the port of the remote.")]
        public int Port { get; set; }

		[Option('l', "list", HelpText = "List the known remotes", MutuallyExclusiveSet = "host list clear")]
		public bool List { get; set; }

		[Option('c', "clear", HelpText = "Clears all existing remotes", MutuallyExclusiveSet = "host list clear")]
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
					Printer.PrintMessage("Remote {0} is {1}:{2}", x.Name, x.Host, x.Port);
				}
			}
			else if (!string.IsNullOrEmpty(localOptions.Host) && localOptions.Port > 0)
			{
				return ws.SetRemote(localOptions.Host, localOptions.Port, string.IsNullOrEmpty(localOptions.Name) ? "default" : localOptions.Name);
			}
			else
			{
				Printer.PrintMessage(localOptions.GetUsage());
				return false;
			}
			return true;
		}
	}
}
