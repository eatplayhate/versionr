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
        [Option('p', "port", DefaultValue = 5122, Required = false, HelpText = "Specifies the port to connect to.")]
        public int Port { get; set; }
        [Option('v', "vault", Required = false, HelpText = "The server vault to connect to (used if a single server is hosting multiple vaults).")]
        public string Vault { get; set; }

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
            bool requireRemoteName = false;
            if (string.IsNullOrEmpty(localOptions.Host) || localOptions.Port == -1)
                requireRemoteName = true;

            Tuple<bool, string, int, string> parsedRemoteName = null;
            LocalState.RemoteConfig config = null;
            if (ws != null)
            {
                if (requireRemoteName)
                    config = ws.GetRemote(string.IsNullOrEmpty(localOptions.Name) ? "default" : localOptions.Name);
                if (UpdateRemoteTimestamp && config != null)
                    ws.UpdateRemoteTimestamp(config);
            }
            else if (!string.IsNullOrEmpty(localOptions.Name))
            {
                if (parsedRemoteName == null)
                    parsedRemoteName = TryParseRemoteName(localOptions.Name);
                if (parsedRemoteName.Item1 == false)
                {
                    Printer.PrintError("Remote names cannot be used outside of a Versionr vault.");
                    return false;
                }
                localOptions.Name = null;
            }
            if (config == null && requireRemoteName)
            {
                if (parsedRemoteName == null)
                    parsedRemoteName = TryParseRemoteName(localOptions.Name);
                if (parsedRemoteName.Item1 == false)
                {
                    Printer.PrintError("You must specify either a host and port or a remote name.");
                    return false;
                }
                localOptions.Name = null;
                localOptions.Host = parsedRemoteName.Item2;
                if (parsedRemoteName.Item3 != -1)
                    localOptions.Port = parsedRemoteName.Item3;
            }
            if (config == null)
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

        private Tuple<bool, string, int, string> TryParseRemoteName(string name)
        {
            if (CanParseRemoteName && !string.IsNullOrEmpty(name))
            {
                System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(
                    "((vsr|versionr)\\://)?" +
                    "(?<host>" +
                        "(?:(?:\\w|\\.|-|_|~|\\d)+)|" +
                        "(?:(?:(?:[0-9]|[0-9]{2}|1[0-9]{2}|2[0-4][0-9]|25[0-5])\\.){3}(?:[0-9]|[0-9]{2}|1[0-9]{2}|2[0-4][0-9]|25[0-5]))|" +
                        "(?:(([0-9a-fA-F]{1,4}:){7,7}[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,7}:|([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|:((:[0-9a-fA-F]{1,4}){1,7}|:)|fe80:(:[0-9a-fA-F]{0,4}){0,4}%[0-9a-zA-Z]{1,}|::(ffff(:0{1,4}){0,1}:){0,1}((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])|([0-9a-fA-F]{1,4}:){1,4}:((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])))" +
                    ")" +
                    "(?:\\:(?<port>[0-9]+))?" +
                    "(?:/(?<vault>[A-Za-z_0-9]+))?$");
                var match = regex.Match(name);
                if (match.Success)
                {
                    string host = match.Groups["host"].Value;
                    int port = -1;
                    var portGroup = match.Groups["port"];
                    if (portGroup.Success)
                    {
                        bool fail = false;
                        if (!int.TryParse(portGroup.Value, out port))
                            fail = true;
                        if (port < 1 || port > ushort.MaxValue)
                            fail = true;
                        if (fail)
                        {
                            return new Tuple<bool, string, int, string>(false, string.Empty, -1, string.Empty);
                        }
                    }
                    string domain = match.Groups["vault"].Success ? match.Groups["vault"].Value : null;
                    return new Tuple<bool, string, int, string>(true, host, port, domain);
                }
            }
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
