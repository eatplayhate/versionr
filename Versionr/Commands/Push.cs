using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class PushVerbOptions : VerbOptionBase
    {
        [Option('h', "host", Required = false, HelpText = "Specifies the hostname to push to.")]
        public string Host { get; set; }
        [Option('p', "port", DefaultValue = -1, Required = false, HelpText = "Specifies the port to push to.")]
        public int Port { get; set; }

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
                return "push";
            }
        }

        [ValueOption(0)]
        public string Name { get; set; }
    }
    class Push : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            PushVerbOptions localOptions = options as PushVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            Network.Client client = new Network.Client(ws);
            bool requireRemoteName = false;
            if (string.IsNullOrEmpty(localOptions.Host) || localOptions.Port == -1)
                requireRemoteName = true;

            LocalState.RemoteConfig config = ws.GetRemote(string.IsNullOrEmpty(localOptions.Name) ? "default" : localOptions.Name);
            if (config == null && requireRemoteName)
            {
                Printer.PrintError("You must specify either a host and port or a remote name.");
                return false;
            }
            else if (config == null)
                config = new LocalState.RemoteConfig() { Host = localOptions.Host, Port = localOptions.Port };
            if (!client.Connect(config.Host, config.Port))
            {
                Printer.PrintError("Couldn't connect to server!");
                return false;
            }
            client.Push();
            client.Close();
            return true;
        }
    }
}
