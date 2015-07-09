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
        [Option('p', "port", Required = true, HelpText = "Specifies the port to run on.")]
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
                return "server";
            }
        }
    }
    class Server : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            ServerVerbOptions localOptions = options as ServerVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            return Network.Server.Run(ws, localOptions.Port);
        }
    }
}
