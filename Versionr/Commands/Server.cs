using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class ServerVerbOptions : VerbOptionBase
    {
        [Option('p', "port", DefaultValue = Network.Client.VersionrDefaultPort, HelpText = "Specifies the port to run on.")]
        public int Port { get; set; }
        [Option('u', "unsecure", DefaultValue = false, HelpText = "Disables AES encryption for data communications.")]
        public bool Unsecure { get; set; }
        [Option('c', "config", HelpText = "Specify a server config file for authentication/options.")]
        public string Config { get; set; }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Runs the server daemon for this versionr vault, allowing other nodes to connect to it.",
                    "",
                    "By providing a server config, a single versionr instance can host multiple vaults."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "server";
            }
        }

        public override BaseCommand GetCommand()
        {
            return new Server();
        }
    }
    class Server : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            ServerVerbOptions localOptions = options as ServerVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            bool? encrypt = null;
            if (localOptions.Unsecure)
                encrypt = false;
            return Network.Server.Run(workingDirectory, localOptions.Port, localOptions.Config, encrypt);
        }
    }
}
