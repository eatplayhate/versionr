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
        [Option('p', "port", DefaultValue = Client.VersionrDefaultPort, Required = false, HelpText = "Specifies the port to connect to.")]
        public int Port { get; set; }
        [Option('r', "remote", Required = false, HelpText = "Specifies the remote URL.")]
        public string Remote { get; set; }
        [Option('m', "module", Required = false, HelpText = "The name of the remote module to select (used if a single server is hosting multiple vaults).")]
        public string Module { get; set; }

        [ValueOption(0)]
        public string Name { get; set; }
    }
    abstract class RemoteCommand : BaseCommand
    {
        protected System.IO.DirectoryInfo TargetDirectory { get; set; }
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            RemoteCommandVerbOptions localOptions = options as RemoteCommandVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Network.Client client = null;
            Area ws = null;
            
            Tuple<bool, string, int, string> parsedRemoteName = null;
            if (!string.IsNullOrEmpty(localOptions.Remote))
            {
                if (parsedRemoteName == null)
                    parsedRemoteName = TryParseRemoteName(localOptions.Remote);
                localOptions.Host = parsedRemoteName.Item2;
                if (parsedRemoteName.Item3 != -1)
                    localOptions.Port = parsedRemoteName.Item3;
                localOptions.Module = parsedRemoteName.Item4;
            }

            if (NeedsWorkspace)
            {
                ws = Area.Load(workingDirectory);
                if (ws == null)
                {
                    Printer.Write(Printer.MessageType.Error, string.Format("#x#Error:##\n  The current directory #b#`{0}`## is not part of a vault.\n", workingDirectory.FullName));
                    return false;
                }
                client = new Network.Client(ws);
            }
            else
            {
                if (NeedsNoWorkspace)
                {
                    string subdir = localOptions.Name;
                    if (string.IsNullOrEmpty(subdir) && !string.IsNullOrEmpty(localOptions.Module))
                        subdir = localOptions.Module;
                    if (!string.IsNullOrEmpty(subdir))
                    {
                        System.IO.DirectoryInfo info;
                        try
                        {
                            info = new System.IO.DirectoryInfo(System.IO.Path.Combine(workingDirectory.FullName, subdir));
                        }
                        catch
                        {
                            Printer.PrintError("#e#Error - invalid subdirectory \"{0}\"##", subdir);
                            return false;
                        }
                        Printer.PrintMessage("Target directory: #b#{0}##.", info);
                        workingDirectory = info;
                    }
                    try
                    {
                        ws = Area.Load(workingDirectory);
                        if (ws != null)
                        {
                            Printer.PrintError("This command cannot function with an active Versionr vault.");
                            return false;
                        }
                    }
                    catch
                    {

                    }
                }
                client = new Client(workingDirectory);
            }
            TargetDirectory = workingDirectory;
            bool requireRemoteName = false;
            if (string.IsNullOrEmpty(localOptions.Host) || localOptions.Port == -1)
                requireRemoteName = true;
            LocalState.RemoteConfig config = null;
            if (ws != null)
            {
                if (requireRemoteName)
                    config = ws.GetRemote(string.IsNullOrEmpty(localOptions.Name) ? "default" : localOptions.Name);
                if (UpdateRemoteTimestamp && config != null)
                    ws.UpdateRemoteTimestamp(config);
            }

            if (config == null && requireRemoteName)
            {
                if (parsedRemoteName != null && parsedRemoteName.Item1 == false)
                {
                    Printer.PrintError("You must specify either a host and port or a remote name.");
                    return false;
                }
            }
            if (config == null)
                config = new LocalState.RemoteConfig() { Host = localOptions.Host, Port = localOptions.Port, Module = localOptions.Module };
            if (!client.Connect(config.Host, config.Port, config.Module, RequiresWriteAccess))
            {
                Printer.PrintError("Couldn't connect to server!");
                return false;
            }
            bool result = RunInternal(client, localOptions);
            client.Close();
            return result;
        }
        protected virtual bool RequiresWriteAccess
        {
            get
            {
                return false;
            }
        }

        private Tuple<bool, string, int, string> TryParseRemoteName(string name)
        {
            if (CanParseRemoteName)
                return Client.ParseRemoteName(name);
            return new Tuple<bool, string, int, string>(false, string.Empty, -1, string.Empty);
        }

        protected virtual bool NeedsWorkspace
        {
            get
            {
                return true;
            }
        }

        protected virtual bool CanParseRemoteName
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

        protected virtual bool UpdateRemoteTimestamp
        {
            get
            {
                return false;
            }
        }

        protected abstract bool RunInternal(Client client, RemoteCommandVerbOptions options);
    }
}
