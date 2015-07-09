using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
    abstract class RemoteCommandVerbOptions : VerbOptionBase
    {
        [Option('h', "host", Required = false, HelpText = "Specifies the hostname to push to.")]
        public string Host { get; set; }
        [Option('p', "port", DefaultValue = -1, Required = false, HelpText = "Specifies the port to push to.")]
        public int Port { get; set; }

        [ValueOption(0)]
        public string Name { get; set; }
    }
    abstract class RemoteCommand : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            RemoteCommandVerbOptions localOptions = options as RemoteCommandVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Network.Client client = null;
            Area ws = null;
            if (NeedsWorkspace)
            {
                ws = Area.Load(workingDirectory);
                if (ws == null)
                    return false;
                client = new Network.Client(ws);
            }
            else
            {
                if (NeedsNoWorkspace)
                {
                    try
                    {
                        ws = Area.Load(workingDirectory);
                        Printer.PrintError("This command cannot function with an active Versionr vault.");
                        return false;
                    }
                    catch
                    {

                    }
                }
                client = new Client(workingDirectory);
            }
            bool requireRemoteName = false;
            if (string.IsNullOrEmpty(localOptions.Host) || localOptions.Port == -1)
                requireRemoteName = true;

            LocalState.RemoteConfig config = null;
            if (ws != null)
            {
                config = ws.GetRemote(string.IsNullOrEmpty(localOptions.Name) ? "default" : localOptions.Name);
            }
            else if (!string.IsNullOrEmpty(localOptions.Name))
            {
                Printer.PrintError("Remote names cannot be used outside of a Versionr vault.");
                return false;
            }
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
            bool result = RunInternal(client, localOptions);
            client.Close();
            return result;
        }

        protected virtual bool NeedsWorkspace
        {
            get
            {
                return true;
            }
        }

        protected virtual bool NeedsNoWorkspace
        {
            get
            {
                return false;
            }
        }

        protected abstract bool RunInternal(Client client, RemoteCommandVerbOptions options);
    }
}
