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
        [Option('h', "host", Required = true, HelpText = "Specifies the hostname of the remote.")]
        public string Host { get; set; }
        [Option('p', "port", Required = true, HelpText = "Specifies the port of the remote.")]
        public int Port { get; set; }
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} --host <host> --p <port> [remote name]", Verb);
            }
        }

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
            return ws.SetRemote(localOptions.Host, localOptions.Port, string.IsNullOrEmpty(localOptions.Name) ? "default" : localOptions.Name);
        }
    }
}
