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
        public virtual string OptionsString
        {
            get
            {
                return "#q#[options]##";
            }
        }
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## [--server remote_name] {1}\n" +
                    "#b#versionr #i#{0}## [--remote vsr://remote_host:port/path] {1}", Verb, OptionsString);
            }
        }
		[Option('r', "remote", Required = false, HelpText = "Specifies the remote URL.")]
		public string Remote { get; set; }

		[Option('s', "server", Required = false, HelpText = "The saved name of the server.")]
        public string Name { get; set; }
    }
    abstract class RemoteCommand : BaseCommand
    {
        protected System.IO.DirectoryInfo TargetDirectory { get; set; }
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            RemoteCommandVerbOptions localOptions = options as RemoteCommandVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            IRemoteClient client = null;
            Area ws = null;
            
            if (NeedsWorkspace)
            {
                ws = Area.Load(workingDirectory, Headless);
                if (ws == null)
                {
                    Printer.Write(Printer.MessageType.Error, string.Format("#x#Error:##\n  The current directory #b#`{0}`## is not part of a vault.\n", workingDirectory.FullName));
                    return false;
                }
            }
            else
            {
                if (NeedsNoWorkspace)
                {
					// Choose target directory from server name or path
                    string subdir = localOptions.Name;
                    if (string.IsNullOrEmpty(subdir) && !string.IsNullOrEmpty(localOptions.Remote))
                        subdir = System.IO.Path.GetFileNameWithoutExtension(new Uri(localOptions.Remote).AbsolutePath);
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
                        ws = Area.Load(workingDirectory, Headless);
                        if (ws != null)
                        {
                            CloneVerbOptions cloneOptions = options as CloneVerbOptions;
                            if (cloneOptions != null && cloneOptions.QuietFail)
                            {
                                Printer.PrintMessage("Directory already contains a vault. Skipping.");
                                return false;
                            }
                            Printer.PrintError("This command cannot function with an active Versionr vault.");
                            return false;
                        }
                    }
                    catch
                    {

                    }
                }
            }
            TargetDirectory = workingDirectory;
            bool requireRemoteName = false;
            if (string.IsNullOrEmpty(localOptions.Remote))
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
                Printer.PrintError("You must specify either a remote server or name.");
                return false;
            }
            if (config == null)
                config = new LocalState.RemoteConfig() { Module = localOptions.Remote };

			if (ws == null)
			{
				// No workspace; must use Versionr Client
				client = new Client(workingDirectory);
				if (!((Client)client).Connect(config.URL, RequiresWriteAccess))
					client = null;
			}
			else
			{
				client = ws.Connect(config.URL, RequiresWriteAccess);
			}
			
            if (client == null)
            {
                Printer.PrintError("Couldn't connect to server #b#{0}##", config.URL);
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

        protected virtual bool Headless
        {
            get
            {
                return false;
            }
        }
		
        public Tuple<bool, string, int, string> TryParseRemoteName(string name)
        {
            if (CanParseRemoteName)
            {
                return ParseRemoteName(name);
            }
            return new Tuple<bool, string, int, string>(false, string.Empty, -1, string.Empty);
        }

        static public Tuple<bool, string, int, string> ParseRemoteName(string name)
        {
            string host;
            int port;
            string module;
            if (Client.TryParseVersionrURL(name, out host, out port, out module))
                return new Tuple<bool, string, int, string>(true, host, port, module);
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

        protected abstract bool RunInternal(IRemoteClient client, RemoteCommandVerbOptions options);
    }
}
