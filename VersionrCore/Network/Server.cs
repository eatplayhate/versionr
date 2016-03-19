using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;

namespace Versionr.Network
{
    static class RSAExtensions
    {
        static public string Fingerprint(this System.Security.Cryptography.RSAParameters param)
        {
            var md5Inst = System.Security.Cryptography.MD5.Create();
            byte[] bytes = md5Inst.ComputeHash(param.Modulus);
            string s = string.Empty;
            foreach (var x in bytes)
            {
                if (s != string.Empty)
                    s += ":";
                s += string.Format("{0:X2}", x);
            }
            return s;
        }
    }
    public class Server
    {
        private static ServerConfig Config { get; set; }
        private static System.Security.Cryptography.RSAParameters PrivateKeyData { get; set; }
        private static System.Security.Cryptography.RSAParameters PublicKey { get; set; }
        private static System.Security.Cryptography.RSACryptoServiceProvider PrivateKey { get; set; }
        public static object SyncObject = new object();
        class DomainInfo
        {
            public System.IO.DirectoryInfo Directory { get; set; }
            public bool Bare { get; set; }
            public BackupInfo Backup { get; set; }

            public DomainInfo()
            {
                Backup = new BackupInfo();
            }
        }
        class BackupInfo
        {
            public string Key;
            public int Refs;
            public System.IO.FileInfo Backup;
        }
        static Dictionary<string, DomainInfo> Domains = new Dictionary<string, DomainInfo>();
        public static string ConfigFile;
        public static System.IO.DirectoryInfo BaseDirectory;
        public static bool Run(System.IO.DirectoryInfo info, int port, string configFile = null, bool? encryptData = null)
        {
            BaseDirectory = info;
            Area ws = Area.Load(info, true);
            if (ws == null)
            {
                Versionr.Utilities.MultiArchPInvoke.BindDLLs();
            }
            Config = new ServerConfig();
            if (!string.IsNullOrEmpty(configFile))
            {
                ConfigFile = configFile;
                LoadConfig();
            }
            if ((Config.IncludeRoot.HasValue && Config.IncludeRoot.Value) || (!Config.IncludeRoot.HasValue && Domains.Count == 0))
                Domains[string.Empty] = new DomainInfo() { Bare = ws == null, Directory = info };
            bool enableEncryption = encryptData.HasValue ? encryptData.Value : Config.Encrypted;
            if (enableEncryption)
            {
                Printer.PrintDiagnostics("Creating RSA pair...");
                System.Security.Cryptography.RSACryptoServiceProvider rsaCSP = new System.Security.Cryptography.RSACryptoServiceProvider();
                rsaCSP.KeySize = 2048;
                PrivateKey = rsaCSP;
                PrivateKeyData = rsaCSP.ExportParameters(true);
                PublicKey = rsaCSP.ExportParameters(false);
                Printer.PrintDiagnostics("RSA Fingerprint: {0}", PublicKey.Fingerprint());
            }
            System.Net.Sockets.TcpListener listener = null;
#if __MonoCS__
            listener = new TcpListener(System.Net.IPAddress.Any, port);
#else
            try
            {
                if (System.Net.Sockets.Socket.OSSupportsIPv6)
                {
                    listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.IPv6Any, port);
                    listener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, 0);
                }
                else
                    listener = new TcpListener(System.Net.IPAddress.Any, port);
            }
            catch
            {
                listener = new TcpListener(System.Net.IPAddress.Any, port);
            }
#endif
            Printer.PrintDiagnostics("Binding to {0}.", listener.LocalEndpoint);
            listener.Start();
            if (Config.WebService != null && Config.WebService.HttpPort != 0 && false)
            {
                Task.Run(() =>
                {
                    RunWebServer();
                });
            }
            Printer.PrintMessage("Server started, bound to #b#{0}##.", listener.LocalEndpoint);
            while (true)
            {
                Printer.PrintDiagnostics("Waiting for connection.");
                var client = listener.AcceptTcpClientAsync();
                Retry:
                if (client.Wait(5000))
                {
                    Task.Run(() =>
                    {
                        Printer.PrintMessage("Received connection from {0}.", client.Result.Client.RemoteEndPoint);
                        HandleConnection(info, client.Result);
                    });
                }
                else
                    goto Retry;
            }
            listener.Stop();

            return true;
        }

        private static void RunWebServer()
        {
            while (true)
            {
                try
                {
                    SimpleWebService.WebService service = new SimpleWebService.WebService(Config);
                    service.Run();
                }
                catch (Exception e)
                {
                    Printer.PrintError("Error: {0}", e.ToString());
                    Printer.PrintMessage("Error starting web interface. Restarting in 10s.");
                    System.Threading.Thread.Sleep(10000);
                }
            }
        }

        private static void LoadConfig()
        {
            lock (SyncObject)
            {
                Printer.PrintMessage("Loading config...");
                var configInfo = new System.IO.FileInfo(ConfigFile);
                if (!configInfo.Exists)
                {
                    Printer.PrintError("#x#Error:##\n  Can't find config file #b#{0}##! Using default.", ConfigFile);
                }
                else
                {
                    using (var fs = configInfo.OpenText())
                    {
                        Config = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerConfig>(fs.ReadToEnd());
                    }
                    if (!string.IsNullOrEmpty(Config.AutoDomains))
                    {
                        string path = System.IO.Path.GetFullPath(Config.AutoDomains);
                        var vaultdir = new System.IO.DirectoryInfo(path);
                        if (!vaultdir.Exists)
                            Printer.PrintError("#x#Error:##\n  Can't find auto-domain location: {0}.", path);
                        else
                        {
                            foreach (var x in vaultdir.GetDirectories())
                            {
                                Config.Domains.Add(x.Name, x.FullName);
                            }
                        }
                    }
                    List<string> deletedDomains = new List<string>();
                    foreach (var z in Domains)
                    {
                        if (z.Key != string.Empty && !Config.Domains.ContainsKey(z.Key))
                            deletedDomains.Add(z.Key);
                    }
                    foreach (var z in deletedDomains)
                        Domains.Remove(z);
                    foreach (var x in Config.Domains)
                    {
                        System.IO.DirectoryInfo domInfo = new System.IO.DirectoryInfo(x.Value);
                        if (domInfo.Exists)
                        {
                            Area dm = null;
                            try
                            {
                                dm = Area.Load(domInfo, true, true);
                            }
                            catch
                            {
                                dm = null;
                            }
                            Printer.PrintMessage("Module: {0} => ./{1} {2}", x.Key, domInfo, dm == null ? "bare" : dm.Domain.ToString());

                            DomainInfo info = new DomainInfo() { Bare = dm == null, Directory = domInfo };
                            Domains[x.Key] = info;
                            if (dm != null)
                                dm.Dispose();
                        }
                        else
                            Printer.PrintError("#x#Error:##\n  Can't find domain location {0}!", x.Value);
                    }
                    if ((Config.IncludeRoot.HasValue && Config.IncludeRoot.Value) || (!Config.IncludeRoot.HasValue && Domains.Count != 0))
                    {
                        using (Area a = Area.Load(BaseDirectory))
                        {
                            Domains[string.Empty] = new DomainInfo() { Bare = a == null, Directory = BaseDirectory };
                            Printer.PrintMessage("Root Module {1} {2}", BaseDirectory, a == null ? "bare" : a.Domain.ToString());
                        }
                    }
                    else if (Config.IncludeRoot.HasValue && Config.IncludeRoot.Value == false)
                        Domains.Remove(string.Empty);

                    if (Config.RequiresAuthentication)
                        Printer.PrintMessage("Configured to use authentication. Unauthenticated read {0}.", (Config.AllowUnauthenticatedRead ? "allowed" : "disabled"));
                }
            }
        }

        internal class ClientStateInfo
        {
            public Dictionary<Guid, Objects.Head> UpdatedHeads { get; set; }
            public List<VersionInfo> MergeVersions { get; set; }
            public SharedNetwork.SharedNetworkInfo SharedInfo { get; set; }
            public Rights Access { get; set; }
            public bool BareAccessRequired { get; set; }
            public ClientStateInfo()
            {
                MergeVersions = new List<VersionInfo>();
                BareAccessRequired = false;
            }
        }

        static void HandleConnection(System.IO.DirectoryInfo info, TcpClient client)
        {
            Area ws = null;
            ClientStateInfo clientInfo = new ClientStateInfo();
            using (client)
            using (SharedNetwork.SharedNetworkInfo sharedInfo = new SharedNetwork.SharedNetworkInfo())
            {
                try
                {
                    var stream = client.GetStream();
                    Handshake hs = ProtoBuf.Serializer.DeserializeWithLengthPrefix<Handshake>(stream, ProtoBuf.PrefixStyle.Fixed32);
                    DomainInfo domainInfo = null;
                    lock (SyncObject)
                    {
                        if (hs.RequestedModule == null)
                            hs.RequestedModule = string.Empty;
                        if (!Domains.TryGetValue(hs.RequestedModule, out domainInfo))
                        {
                            domainInfo = Domains.Where(x => x.Key.Equals(hs.RequestedModule, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).FirstOrDefault();
                        }
                        if (domainInfo == null)
                        {
                            if (!Config.AllowVaultCreation || !Config.RequiresAuthentication || string.IsNullOrEmpty(hs.RequestedModule) || System.IO.Directory.Exists(System.IO.Path.Combine(info.FullName, hs.RequestedModule)))
                            {
                                Network.StartTransaction startSequence = Network.StartTransaction.CreateRejection();
                                Printer.PrintDiagnostics("Rejecting client due to invalid domain: \"{0}\".", hs.RequestedModule);
                                ProtoBuf.Serializer.SerializeWithLengthPrefix<Network.StartTransaction>(stream, startSequence, ProtoBuf.PrefixStyle.Fixed32);
                                return;
                            }
                            domainInfo = new DomainInfo()
                            {
                                Bare = true,
                                Directory = null
                            };
                        }
                    }
                    try
                    {
                        ws = Area.Load(domainInfo.Directory, true, true);
                        if (domainInfo.Bare)
                            throw new Exception("Domain is bare, but workspace could be loaded!");
                    }
                    catch
                    {
                        if (!domainInfo.Bare)
                            throw new Exception("Domain not bare, but couldn't load workspace!");
                    }
                    Printer.PrintDiagnostics("Received handshake - protocol: {0}", hs.VersionrProtocol);
                    SharedNetwork.Protocol? clientProtocol = hs.CheckProtocol();
                    bool valid = true;
                    if (clientProtocol == null)
                        valid = false;
                    else
                    {
                        valid = SharedNetwork.AllowedProtocols.Contains(clientProtocol.Value);
                        if (Config.RequiresAuthentication && !SharedNetwork.SupportsAuthentication(clientProtocol.Value))
                            valid = false;
                    }
                    if (valid)
                    {
                        sharedInfo.CommunicationProtocol = clientProtocol.Value;
                        Network.StartTransaction startSequence = null;
                        clientInfo.Access = Rights.Read | Rights.Write;
                        clientInfo.BareAccessRequired = domainInfo.Bare;
                        if (PrivateKey != null)
                        {
                            startSequence = Network.StartTransaction.Create(domainInfo.Bare ? string.Empty : ws.Domain.ToString(), PublicKey, clientProtocol.Value);
                            Printer.PrintDiagnostics("Sending RSA key...");
                            ProtoBuf.Serializer.SerializeWithLengthPrefix<Network.StartTransaction>(stream, startSequence, ProtoBuf.PrefixStyle.Fixed32);
                            if (!HandleAuthentication(clientInfo, client, sharedInfo))
                                throw new Exception("Authentication failed.");
                            StartClientTransaction clientKey = ProtoBuf.Serializer.DeserializeWithLengthPrefix<StartClientTransaction>(stream, ProtoBuf.PrefixStyle.Fixed32);
                            System.Security.Cryptography.RSAOAEPKeyExchangeDeformatter exch = new System.Security.Cryptography.RSAOAEPKeyExchangeDeformatter(PrivateKey);
                            byte[] aesKey = exch.DecryptKeyExchange(clientKey.Key);
                            byte[] aesIV = exch.DecryptKeyExchange(clientKey.IV);
                            Printer.PrintDiagnostics("Got client key: {0}", System.Convert.ToBase64String(aesKey));

                            var aesCSP = System.Security.Cryptography.AesManaged.Create();

                            sharedInfo.DecryptorFunction = () => { return aesCSP.CreateDecryptor(aesKey, aesIV); };
                            sharedInfo.EncryptorFunction = () => { return aesCSP.CreateEncryptor(aesKey, aesIV); };
                        }
                        else
                        {
                            startSequence = Network.StartTransaction.Create(domainInfo.Bare ? string.Empty : ws.Domain.ToString(), clientProtocol.Value);
                            ProtoBuf.Serializer.SerializeWithLengthPrefix<Network.StartTransaction>(stream, startSequence, ProtoBuf.PrefixStyle.Fixed32);
                            if (!HandleAuthentication(clientInfo, client, sharedInfo))
                                throw new Exception("Authentication failed.");
                            StartClientTransaction clientKey = ProtoBuf.Serializer.DeserializeWithLengthPrefix<StartClientTransaction>(stream, ProtoBuf.PrefixStyle.Fixed32);
                        }
                        sharedInfo.Stream = stream;
                        sharedInfo.Workspace = ws;
                        sharedInfo.ChecksumType = Config.ChecksumType;

                        clientInfo.SharedInfo = sharedInfo;

                        while (true)
                        {
                            NetCommand command = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(stream, ProtoBuf.PrefixStyle.Fixed32);
                            if (command.Type == NetCommandType.Close)
                            {
                                Printer.PrintDiagnostics("Client closing connection.");
                                break;
                            }
                            else if (command.Type == NetCommandType.PushInitialVersion)
                            {
                                bool fresh = false;
                                if (domainInfo.Directory == null)
                                {
                                    if (!clientInfo.Access.HasFlag(Rights.Create))
                                        throw new Exception("Access denied.");
                                    fresh = true;
                                    System.IO.DirectoryInfo newDirectory = new System.IO.DirectoryInfo(System.IO.Path.Combine(info.FullName, hs.RequestedModule));
                                    newDirectory.Create();
                                    domainInfo.Directory = newDirectory;
                                    if (!newDirectory.Exists)
                                        throw new Exception("Access denied.");
                                }
                                if (!clientInfo.Access.HasFlag(Rights.Write))
                                    throw new Exception("Access denied.");
                                lock (SyncObject)
                                {
                                    ws = Area.InitRemote(domainInfo.Directory, Utilities.ReceiveEncrypted<ClonePayload>(clientInfo.SharedInfo));
                                    clientInfo.SharedInfo.Workspace = ws;
                                    domainInfo.Bare = false;
                                    if (fresh)
                                        Domains[hs.RequestedModule] = domainInfo;
                                }
                            }
                            else if (command.Type == NetCommandType.PushBranchJournal)
                            {
                                if (!clientInfo.Access.HasFlag(Rights.Write))
                                    throw new Exception("Access denied.");
                                SharedNetwork.ReceiveBranchJournal(sharedInfo);
                            }
                            else if (command.Type == NetCommandType.QueryBranchID)
                            {
                                if (!clientInfo.Access.HasFlag(Rights.Read))
                                    throw new Exception("Access denied.");
                                Printer.PrintDiagnostics("Client is requesting a branch info for {0}", string.IsNullOrEmpty(command.AdditionalPayload) ? "<root>" : "\"" + command.AdditionalPayload + "\"");
                                bool multiple = false;
                                Objects.Branch branch = string.IsNullOrEmpty(command.AdditionalPayload) ? ws.RootBranch : ws.GetBranchByPartialName(command.AdditionalPayload, out multiple);
                                if (branch != null)
                                {
                                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Acknowledge, AdditionalPayload = branch.ID.ToString() }, ProtoBuf.PrefixStyle.Fixed32);
                                }
                                else if (!multiple)
                                {
                                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Error, AdditionalPayload = "branch not recognized" }, ProtoBuf.PrefixStyle.Fixed32);
                                }
                                else
                                {
                                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Error, AdditionalPayload = "multiple branches with that name!" }, ProtoBuf.PrefixStyle.Fixed32);
                                }
                            }
                            else if (command.Type == NetCommandType.ListBranches)
                            {
                                if (!clientInfo.Access.HasFlag(Rights.Read))
                                    throw new Exception("Access denied.");
                                Printer.PrintDiagnostics("Client is requesting a branch list.");
                                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
                                if (command.Identifier == 1) // send extra data
                                {
                                    BranchList bl = new BranchList();
                                    bl.Branches = clientInfo.SharedInfo.Workspace.Branches.ToArray();
                                    Dictionary<Guid, Objects.Version> importantVersions = new Dictionary<Guid, Objects.Version>();
                                    List<KeyValuePair<Guid, Guid>> allHeads = new List<KeyValuePair<Guid, Guid>>();
                                    foreach (var x in bl.Branches)
                                    {
                                        if (x.Terminus.HasValue && !importantVersions.ContainsKey(x.Terminus.Value))
                                        {
                                            importantVersions[x.Terminus.Value] = clientInfo.SharedInfo.Workspace.GetVersion(x.Terminus.Value);
                                            continue;
                                        }
                                        var heads = clientInfo.SharedInfo.Workspace.GetBranchHeads(x);
                                        foreach (var head in heads)
                                        {
                                            if (!importantVersions.ContainsKey(head.Version))
                                                importantVersions[head.Version] = clientInfo.SharedInfo.Workspace.GetVersion(head.Version);
                                        }
                                        allHeads.AddRange(heads.Select(y => new KeyValuePair<Guid, Guid>(y.Branch, y.Version)));
                                    }
                                    bl.Heads = allHeads.ToArray();
                                    bl.ImportantVersions = importantVersions.Values.ToArray();
                                    Utilities.SendEncrypted<BranchList>(clientInfo.SharedInfo, bl);
                                }
                                else
                                {
                                    BranchList bl = new BranchList();
                                    bl.Branches = clientInfo.SharedInfo.Workspace.Branches.ToArray();
                                    List<KeyValuePair<Guid, Guid>> allHeads = new List<KeyValuePair<Guid, Guid>>();
                                    foreach (var x in bl.Branches)
                                    {
                                        if (x.Terminus.HasValue)
                                            continue;
                                        var heads = clientInfo.SharedInfo.Workspace.GetBranchHeads(x);
                                        if (heads.Count == 1)
                                            allHeads.Add(new KeyValuePair<Guid, Guid>(x.ID, heads[0].Version));
                                    }
                                    bl.Heads = allHeads.ToArray();
                                    Utilities.SendEncrypted<BranchList>(clientInfo.SharedInfo, bl);
                                }
                            }
                            else if (command.Type == NetCommandType.RequestRecordUnmapped)
                            {
                                if (!clientInfo.Access.HasFlag(Rights.Read))
                                    throw new Exception("Access denied.");
                                Printer.PrintDiagnostics("Client is requesting specific record data blobs.");
                                SharedNetwork.SendRecordDataUnmapped(sharedInfo);
                            }
                            else if (command.Type == NetCommandType.Clone)
                            {
                                if (!clientInfo.Access.HasFlag(Rights.Read))
                                    throw new Exception("Access denied.");
                                Printer.PrintDiagnostics("Client is requesting to clone the vault.");
                                Objects.Version initialRevision = ws.GetVersion(ws.Domain);
                                Objects.Branch initialBranch = ws.GetBranch(initialRevision.Branch);
                                Utilities.SendEncrypted<ClonePayload>(sharedInfo, new ClonePayload() { InitialBranch = initialBranch, RootVersion = initialRevision });
                            }
                            else if (command.Type == NetCommandType.PullVersions)
                            {
                                if (!clientInfo.Access.HasFlag(Rights.Read))
                                    throw new Exception("Access denied.");
                                Printer.PrintDiagnostics("Client asking for remote version information.");
                                Branch branch = ws.GetBranch(new Guid(command.AdditionalPayload));
                                if (branch == null)
                                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Error, AdditionalPayload = string.Format("Unknown branch {0}", command.AdditionalPayload) }, ProtoBuf.PrefixStyle.Fixed32);
                                else
                                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
                                Stack<Objects.Branch> branchesToSend = new Stack<Branch>();
                                Stack<Objects.Version> versionsToSend = new Stack<Objects.Version>();
                                if (!SharedNetwork.SendBranchJournal(sharedInfo))
                                    throw new Exception();
                                if (!SharedNetwork.GetVersionList(sharedInfo, sharedInfo.Workspace.GetBranchHeadVersion(branch), out branchesToSend, out versionsToSend))
                                    throw new Exception();
                                if (!SharedNetwork.SendBranches(sharedInfo, branchesToSend))
                                    throw new Exception();
                                if (!SharedNetwork.SendVersions(sharedInfo, versionsToSend))
                                    throw new Exception();
                            }
                            else if (command.Type == NetCommandType.PushObjectQuery)
                            {
                                if (!clientInfo.Access.HasFlag(Rights.Write))
                                    throw new Exception("Access denied.");
                                Printer.PrintDiagnostics("Client asking about objects on the server...");
                                SharedNetwork.ProcesPushObjectQuery(sharedInfo);
                            }
                            else if (command.Type == NetCommandType.PushBranch)
                            {
                                if (!clientInfo.Access.HasFlag(Rights.Write))
                                    throw new Exception("Access denied.");
                                Printer.PrintDiagnostics("Client attempting to send branch data...");
                                SharedNetwork.ReceiveBranches(sharedInfo);
                            }
                            else if (command.Type == NetCommandType.PushVersions)
                            {
                                if (!clientInfo.Access.HasFlag(Rights.Write))
                                    throw new Exception("Access denied.");
                                Printer.PrintDiagnostics("Client attempting to send version data...");
                                SharedNetwork.ReceiveVersions(sharedInfo);
                            }
                            else if (command.Type == NetCommandType.PushHead)
                            {
                                if (!clientInfo.Access.HasFlag(Rights.Write))
                                    throw new Exception("Access denied.");
                                Printer.PrintDiagnostics("Determining head information.");
                                string errorData;
                                lock (ws)
                                {
                                    clientInfo.SharedInfo.Workspace.RunLocked(() =>
                                    {
                                        try
                                        {
                                            clientInfo.SharedInfo.Workspace.BeginDatabaseTransaction();
                                            if (!SharedNetwork.ImportBranchJournal(clientInfo.SharedInfo, false))
                                            {
                                                clientInfo.SharedInfo.Workspace.RollbackDatabaseTransaction();
                                                return false;
                                            }
                                            if (AcceptHeads(clientInfo, ws, out errorData))
                                            {
                                                ImportVersions(ws, clientInfo);
                                                clientInfo.SharedInfo.Workspace.CommitDatabaseTransaction();
                                                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.AcceptPush }, ProtoBuf.PrefixStyle.Fixed32);
                                                return true;
                                            }
                                            else
                                                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.RejectPush, AdditionalPayload = errorData }, ProtoBuf.PrefixStyle.Fixed32);
                                            clientInfo.SharedInfo.Workspace.RollbackDatabaseTransaction();
                                            return false;
                                        }
                                        catch
                                        {
                                            clientInfo.SharedInfo.Workspace.RollbackDatabaseTransaction();
                                            return false;
                                        }
                                    }, false);
                                }
                            }
                            else if (command.Type == NetCommandType.SynchronizeRecords)
                            {
                                if (!clientInfo.Access.HasFlag(Rights.Read))
                                    throw new Exception("Access denied.");
                                Printer.PrintDiagnostics("Received {0} versions in version pack, but need {1} records to commit data.", sharedInfo.PushedVersions.Count, sharedInfo.UnknownRecords.Count);
                                Printer.PrintDiagnostics("Beginning record synchronization...");
                                if (sharedInfo.UnknownRecords.Count > 0)
                                {
                                    Printer.PrintDiagnostics("Requesting record metadata...");
                                    SharedNetwork.RequestRecordMetadata(clientInfo.SharedInfo);
                                    Printer.PrintDiagnostics("Requesting record data...");
                                    SharedNetwork.RequestRecordData(sharedInfo);
                                    if (!sharedInfo.Workspace.RunLocked(() =>
                                    {
                                        return SharedNetwork.ImportRecords(sharedInfo);
                                    }, false))
                                    {
                                        throw new Exception("Unable to import records!");
                                    }
                                }
                                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Synchronized }, ProtoBuf.PrefixStyle.Fixed32);
                            }
                            else if (command.Type == NetCommandType.FullClone)
                            {
                                if (!clientInfo.Access.HasFlag(Rights.Read))
                                    throw new Exception("Access denied.");
                                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Acknowledge, Identifier = (int)ws.DatabaseVersion }, ProtoBuf.PrefixStyle.Fixed32);
                                command = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(stream, ProtoBuf.PrefixStyle.Fixed32);
                                if (command.Type == NetCommandType.Acknowledge)
                                {
                                    bool accept = false;
                                    BackupInfo backupInfo = null;
                                    lock (domainInfo)
                                    {
                                        string backupKey = ws.LastVersion + "-" + ws.LastBranch + "-" + ws.BranchJournalTipID.ToString();
                                        backupInfo = domainInfo.Backup;
                                        if (backupKey != backupInfo.Key)
                                        {
                                            Printer.PrintMessage("Backup key out of date for domain DB[{0}] - {1}", domainInfo.Directory, backupKey);
                                            if (System.Threading.Interlocked.Decrement(ref backupInfo.Refs) == 0)
                                                backupInfo.Backup.Delete();
                                            backupInfo = new BackupInfo();
                                            domainInfo.Backup = backupInfo;
                                            var directory = new System.IO.DirectoryInfo(System.IO.Path.Combine(ws.AdministrationFolder.FullName, "backups"));
                                            directory.Create();
                                            backupInfo.Backup = new System.IO.FileInfo(System.IO.Path.Combine(directory.FullName, System.IO.Path.GetRandomFileName()));
                                            if (ws.BackupDB(backupInfo.Backup))
                                            {
                                                System.Threading.Interlocked.Increment(ref backupInfo.Refs);
                                                accept = true;
                                                backupInfo.Key = backupKey;
                                            }
                                        }
                                        else
                                        {
                                            accept = true;
                                        }

                                        if (accept)
                                            System.Threading.Interlocked.Increment(ref backupInfo.Refs);
                                    }
                                    if (accept)
                                    {
                                        Printer.PrintDiagnostics("Backup complete. Sending data.");
                                        byte[] blob = new byte[256 * 1024];
                                        long filesize = backupInfo.Backup.Length;
                                        long position = 0;
                                        using (System.IO.FileStream reader = backupInfo.Backup.OpenRead())
                                        {
                                            while (true)
                                            {
                                                long remainder = filesize - position;
                                                int count = blob.Length;
                                                if (count > remainder)
                                                    count = (int)remainder;
                                                reader.Read(blob, 0, count);
                                                position += count;
                                                Printer.PrintDiagnostics("Sent {0}/{1} bytes.", position, filesize);
                                                if (count == remainder)
                                                {
                                                    Utilities.SendEncrypted(sharedInfo, new DataPayload()
                                                    {
                                                        Data = blob.Take(count).ToArray(),
                                                        EndOfStream = true
                                                    });
                                                    break;
                                                }
                                                else
                                                {
                                                    Utilities.SendEncrypted(sharedInfo, new DataPayload()
                                                    {
                                                        Data = blob,
                                                        EndOfStream = false
                                                    });
                                                }
                                            }
                                        }
                                        ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
                                        lock (domainInfo)
                                        {
                                            if (System.Threading.Interlocked.Decrement(ref backupInfo.Refs) == 0)
                                                backupInfo.Backup.Delete();
                                        }
                                    }
                                    else
                                    {
                                        Utilities.SendEncrypted<DataPayload>(sharedInfo, new DataPayload() { Data = new byte[0], EndOfStream = true });
                                        ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Error }, ProtoBuf.PrefixStyle.Fixed32);
                                    }
                                }
                            }
                            else
                            {
                                Printer.PrintDiagnostics("Client sent invalid command: {0}", command.Type);
                                throw new Exception();
                            }
                        }
                    }
                    else
                    {
                        Network.StartTransaction startSequence = Network.StartTransaction.CreateRejection();
                        Printer.PrintDiagnostics("Rejecting client due to protocol mismatch.");
                        ProtoBuf.Serializer.SerializeWithLengthPrefix<Network.StartTransaction>(stream, startSequence, ProtoBuf.PrefixStyle.Fixed32);
                        return;
                    }
                }
                catch (Exception e)
                {
                    Printer.PrintDiagnostics("Client was a terrible person, because: {0}", e);
                }
                finally
                {
                    if (ws != null)
                        ws.Dispose();
                }
            }

            Printer.PrintDiagnostics("Ended client processor task!");
        }

        private static bool HandleAuthentication(ClientStateInfo clientInfo, TcpClient client, SharedNetwork.SharedNetworkInfo sharedInfo)
        {
            if (Config.RequiresAuthentication)
            {
                int flags = ((Config.AllowUnauthenticatedWrite ? 2 : 0) | (Config.AllowUnauthenticatedRead ? 1 : 0));
                if (clientInfo.BareAccessRequired)
                    flags = 0;
                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(client.GetStream(), new NetCommand() { Type = NetCommandType.Authenticate, Identifier = flags }, ProtoBuf.PrefixStyle.Fixed32);
                AuthenticationChallenge challenge = new AuthenticationChallenge();
                challenge.AvailableModes = new List<AuthenticationMode>();
                if (Config.SupportsSimpleAuthentication)
                    challenge.AvailableModes.Add(AuthenticationMode.Simple);
                challenge.Salt = BCrypt.Net.BCrypt.GenerateSalt();
                ProtoBuf.Serializer.SerializeWithLengthPrefix(client.GetStream(), challenge, ProtoBuf.PrefixStyle.Fixed32);
                int retries = Config.AuthenticationAttempts;
                bool success = false;
                while (true)
                {
                    var response = ProtoBuf.Serializer.DeserializeWithLengthPrefix<AuthenticationResponse>(client.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                    Rights accessRights;
                    if (CheckAuthentication(response, challenge.Salt, out accessRights))
                    {
                        clientInfo.Access = accessRights;
                        success = true;
                        break;
                    }
                    else
                    {
                        if (--retries == 0)
                            break;
                        ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(client.GetStream(), new NetCommand() { Type = NetCommandType.AuthRetry }, ProtoBuf.PrefixStyle.Fixed32);
                    }
                }
                if (success)
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(client.GetStream(), new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
                else
                {
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(client.GetStream(), new NetCommand() { Type = NetCommandType.AuthFail }, ProtoBuf.PrefixStyle.Fixed32);
                    return false;
                }
            }
            else
            {
                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(client.GetStream(), new NetCommand() { Type = NetCommandType.SkipAuthentication }, ProtoBuf.PrefixStyle.Fixed32);
            }
            return true;
        }

        private static bool CheckAuthentication(AuthenticationResponse response, string salt, out Rights accessRights)
        {
            accessRights = 0;
            if (response.Mode == AuthenticationMode.Guest && Config.AllowUnauthenticatedRead)
            {
                accessRights = Rights.Read;
                if (Config.AllowUnauthenticatedWrite)
                    accessRights |= Rights.Write;
                return true;
            }
            else if (response.Mode == AuthenticationMode.Simple)
            {
                var login = Config.GetSimpleLogin(response.IdentifierToken);
                if (login == null)
                {
                    string doSomeWork = BCrypt.Net.BCrypt.GenerateSalt();
                    return false;
                }
                else
                {
                    if (BCrypt.Net.BCrypt.HashPassword(login.Password, salt) == System.Text.ASCIIEncoding.ASCII.GetString(response.Payload))
                    {
                        accessRights = login.Access;
                        return true;
                    }
                    return false;
                }
            }
            else
                throw new Exception(string.Format("Unknown authentication mode: {0}", response.Mode));
        }

        private static bool AcceptHeads(ClientStateInfo clientInfo, Area ws, out string errorData)
        {
            SharedNetwork.ImportBranches(clientInfo.SharedInfo);
            Dictionary<Guid, Head> temporaryHeads = new Dictionary<Guid, Head>();
            Dictionary<Guid, Guid> pendingMerges = new Dictionary<Guid, Guid>();
            Dictionary<Guid, HashSet<Guid>> headAncestry = new Dictionary<Guid, HashSet<Guid>>();
            foreach (var x in clientInfo.SharedInfo.PushedVersions)
            {
                Branch branch = ws.GetBranch(x.Version.Branch);
                Head head;
                if (!temporaryHeads.TryGetValue(branch.ID, out head))
                {
                    var heads = ws.GetBranchHeads(branch);
                    if (heads.Count == 0)
                        head = new Head() { Branch = branch.ID, Version = x.Version.ID };
                    else if (heads.Count == 1)
                        head = heads[0];
                    else
                    {
                        // ???
                        Printer.PrintError("OMG 1");
                        errorData = string.Format("Multiple ({0}) heads for branch {1}", heads.Count, branch.ID);
                        return false;
                    }
                    temporaryHeads[branch.ID] = head;
                }
                if (head.Version != x.Version.ID)
                {
                    HashSet<Guid> headAncestors = null;
                    if (!headAncestry.TryGetValue(head.Version, out headAncestors))
                    {
                        headAncestors = SharedNetwork.GetAncestry(head.Version, clientInfo.SharedInfo);
                        headAncestry[head.Version] = headAncestors;
                    }
                    if (headAncestors.Contains(x.Version.ID))
                    {
                        // all best
                    }
                    else if (SharedNetwork.IsAncestor(head.Version, x.Version.ID, clientInfo.SharedInfo))
                    {
                        headAncestry.Remove(head.Version);
                        pendingMerges[branch.ID] = Guid.Empty;
                        head.Version = x.Version.ID;
                    }
                    else if (!SharedNetwork.IsAncestor(x.Version.ID, head.Version, clientInfo.SharedInfo))
                    {
                        headAncestry.Remove(head.Version);
                        pendingMerges[branch.ID] = head.Version;
                        head.Version = x.Version.ID;
                    }
                }
            }
            foreach (var x in pendingMerges)
            {
                if (x.Value == Guid.Empty)
                {
                    Printer.PrintDiagnostics("Uncontested head update for branch \"{0}\".", ws.GetBranch(x.Key).Name);
                    Printer.PrintDiagnostics(" - Head updated to {0}", temporaryHeads[x.Key].Version);
                    continue;
                }
                Branch branch = ws.GetBranch(x.Key);
                VersionInfo result;
                string error;
                result = ws.MergeRemote(ws.GetLocalOrRemoteVersion(x.Value, clientInfo.SharedInfo), temporaryHeads[x.Key].Version, clientInfo.SharedInfo, out error);
                if (result == null)
                {
                    // safe merge?
                    Printer.PrintError("OMG 2");
                    errorData = string.Format("Can't automatically merge data - multiple heads in branch \'{0}\'.\nAttempted merge result: {1}", branch.Name, error);
                    return false;
                }
                else
                {
                    clientInfo.MergeVersions.Add(result);
                    Printer.PrintMessage("Resolved incoming merge for branch \"{0}\".", branch.Name);
                    Printer.PrintDiagnostics(" - Merge local input {0}", x.Value);
                    Printer.PrintDiagnostics(" - Merge remote input {0}", temporaryHeads[x.Key].Version);
                    Printer.PrintDiagnostics(" - Head updated to {0}", result.Version.ID);
                    temporaryHeads[x.Key].Version = result.Version.ID;
                }
            }
            // theoretically best
            clientInfo.UpdatedHeads = temporaryHeads;
            errorData = string.Empty;
            return true;
        }

        private static bool ImportVersions(Area ws, ClientStateInfo clientInfo)
        {
            lock (ws)
            {
                var versionsToImport = clientInfo.SharedInfo.PushedVersions.OrderBy(x => x.Version.Timestamp).ToArray();
                Dictionary<Guid, bool> importList = new Dictionary<Guid, bool>();
                foreach (var x in versionsToImport)
                    importList[x.Version.ID] = false;
                int importCount = versionsToImport.Length;
                var orderedImports = versionsToImport.OrderBy(x => x.Version.Revision).ToList();
                while (importCount > 0)
                {
                    foreach (var x in orderedImports)
                    {
                        if (importList[x.Version.ID] != true)
                        {
                            bool accept;
                            if (!x.Version.Parent.HasValue || !importList.TryGetValue(x.Version.Parent.Value, out accept))
                                accept = true;
                            if (accept)
                            {
                                ws.ImportVersionNoCommit(clientInfo.SharedInfo, x, true);
                                importList[x.Version.ID] = true;
                                importCount--;
                            }
                        }
                    }
                }
                foreach (var x in clientInfo.MergeVersions)
                    ws.ImportVersionNoCommit(clientInfo.SharedInfo, x, false);
                foreach (var x in clientInfo.UpdatedHeads)
                    ws.ImportHeadNoCommit(x);
                return true;
            }
        }
        
    }
}
