using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;

namespace Versionr.Network
{
    public class Client
    {
        System.Net.Sockets.TcpClient Connection { get; set; }
        public Area Workspace { get; set; }
        System.Security.Cryptography.AesManaged AESProvider { get; set; }
        byte[] AESKey { get; set; }
        byte[] AESIV { get; set; }
        public bool Connected { get; set; }
        public string Host { get; set; }
        public string RemoteDomain { get; set; }
        public string Module { get; set; }
        public int Port { get; set; }

        System.IO.DirectoryInfo BaseDirectory { get; set; }

        HashSet<Guid> ServerKnownBranches { get; set; }
        HashSet<Guid> ServerKnownVersions { get; set; }

        SharedNetwork.SharedNetworkInfo SharedInfo { get; set; }

        System.Security.Cryptography.ICryptoTransform Encryptor
        {
            get
            {
                return AESProvider.CreateEncryptor(AESKey, AESIV);
            }
        }
        System.Security.Cryptography.ICryptoTransform Decryptor
        {
            get
            {
                return AESProvider.CreateDecryptor(AESKey, AESIV);
            }
        }

        public string VersionrURL
        {
            get
            {
                return ToVersionrURL(Host, Port, Module);
            }
        }

        public Client(Area area)
        {
            Workspace = area;
            ServerKnownBranches = new HashSet<Guid>();
            ServerKnownVersions = new HashSet<Guid>();
        }

        bool SkipContainmentCheck { get; set; }
        public Client(System.IO.DirectoryInfo baseDirectory, bool skipChecks = false)
        {
            SkipContainmentCheck = skipChecks;
            Versionr.Utilities.MultiArchPInvoke.BindDLLs();
            Workspace = null;
            BaseDirectory = baseDirectory;
            ServerKnownBranches = new HashSet<Guid>();
            ServerKnownVersions = new HashSet<Guid>();
        }
        public bool SyncCurrentRecords()
        {
            return Workspace.SyncCurrentRecords();
        }
        public bool SyncRecords()
        {
            List<Record> missingRecords = Workspace.GetAllMissingRecords();
            Printer.PrintMessage("Vault is missing data for {0} records.", missingRecords.Count);
            List<string> returnedData = GetRecordData(missingRecords);
            Printer.PrintMessage(" - Got {0} records from remote.", returnedData.Count);
            if (returnedData.Count != missingRecords.Count)
                return false;
            return true;
        }

        public static string ToVersionrURL(string host, int port, string domain = null)
        {
            return "vsr://" + host + ":" + port + (string.IsNullOrEmpty(domain) ? "" : ("/" + domain));
        }

        public void Close()
        {
            if (Connected)
            {
                try
                {
                    Connected = false;
                    Printer.PrintDiagnostics("Disconnecting...");
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.Close }, ProtoBuf.PrefixStyle.Fixed32);
                    NetCommand response = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                    Connection.Close();
                }
                catch
                {

                }
                finally
                {
                    SharedInfo.Dispose();
                }
                Printer.PrintDiagnostics("Disconnected.");
            }
        }

        public bool Clone(bool full)
        {
            if (Workspace != null)
                return false;
            try
            {
                if (!full)
                {
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.Clone }, ProtoBuf.PrefixStyle.Fixed32);
                    var clonePack = Utilities.ReceiveEncrypted<ClonePayload>(SharedInfo);
                    Workspace = Area.InitRemote(BaseDirectory, clonePack, SkipContainmentCheck);
                }
                else
                {
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.FullClone }, ProtoBuf.PrefixStyle.Fixed32);

                    var response = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                    if (response.Type == NetCommandType.Acknowledge)
                    {
                        int dbVersion = (int)response.Identifier;
                        if (!WorkspaceDB.AcceptDBVersion(dbVersion))
                        {
                            Printer.PrintError("Server database version is incompatible (v{0}). Use non-full clone to perform the operation.", dbVersion);
                            ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.Error }, ProtoBuf.PrefixStyle.Fixed32);
                            return false;
                        }
                        else
                            ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
                    }

                    System.IO.FileInfo fsInfo = new System.IO.FileInfo(System.IO.Path.GetRandomFileName());
                    Printer.PrintMessage("Attempting to import metadata file to temp path {0}", fsInfo.FullName);
                    var printer = Printer.CreateSimplePrinter("Progress", (obj) =>
                    {
                        return string.Format("#b#{0}## received.", Versionr.Utilities.Misc.FormatSizeFriendly((long)obj));
                    });
                    try
                    {
                        long total = 0;
                        using (var stream = fsInfo.OpenWrite())
                        {
                            while (true)
                            {
                                var data = Utilities.ReceiveEncrypted<DataPayload>(SharedInfo);
                                stream.Write(data.Data, 0, data.Data.Length);
                                total += data.Data.Length;
                                printer.Update(total);
                                if (data.EndOfStream)
                                    break;
                            }
                            printer.End(total);
                            response = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                            if (response.Type == NetCommandType.Error)
                            {
                                Printer.PrintError("Server failed to clone the database.");
                                return false;
                            }
                        }
                        Printer.PrintMessage("Metadata written, importing DB.");
                        Area area = new Area(Area.GetAdminFolderForDirectory(BaseDirectory));
                        try
                        {
                            fsInfo.MoveTo(area.MetadataFile.FullName);
                            if (!area.ImportDB())
                                throw new Exception("Couldn't import data.");
                            Workspace = Area.Load(BaseDirectory);
                            SharedInfo.Workspace = Workspace;
                            return true;
                        }
                        catch
                        {
                            if (area.MetadataFile.Exists)
                                area.MetadataFile.Delete();
                            area.AdministrationFolder.Delete();
                            throw;
                        }
                    }
                    catch
                    {
                        if (fsInfo.Exists)
                            fsInfo.Delete();
                        return false;
                    }
                }
                SharedInfo.Workspace = Workspace;
                return true;
            }
            catch (Exception e)
            {
                Printer.PrintError(e.ToString());
                return false;
            }
        }

        public static Tuple<bool, string, int, string> ParseRemoteName(string name)
        {
            if (!string.IsNullOrEmpty(name))
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

        public bool Push()
        {
            if (Workspace == null)
                return false;
            try
            {
                if (string.IsNullOrEmpty(RemoteDomain))
                {
                    Printer.PrintError("#b#Initializing bare remote...##");
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(SharedInfo.Stream, new NetCommand() { Type = NetCommandType.PushInitialVersion }, ProtoBuf.PrefixStyle.Fixed32);
                    Objects.Version initialRevision = Workspace.GetVersion(Workspace.Domain);
                    Objects.Branch initialBranch = Workspace.GetBranch(initialRevision.Branch);
                    Utilities.SendEncrypted<ClonePayload>(SharedInfo, new ClonePayload() { InitialBranch = initialBranch, RootVersion = initialRevision });
                }
                Stack<Objects.Branch> branchesToSend = new Stack<Branch>();
                Stack<Objects.Version> versionsToSend = new Stack<Objects.Version>();
                Printer.PrintMessage("Determining data to send...");
                if (!SharedNetwork.SendBranchJournal(SharedInfo))
                    return false;
                if (!SharedNetwork.GetVersionList(SharedInfo, Workspace.Version, out branchesToSend, out versionsToSend))
                    return false;
                Printer.PrintDiagnostics("Need to send {0} versions and {1} branches.", versionsToSend.Count, branchesToSend.Count);
                if (!SharedNetwork.SendBranches(SharedInfo, branchesToSend))
                    return false;
                if (!SharedNetwork.SendVersions(SharedInfo, versionsToSend))
                    return false;

                Printer.PrintDiagnostics("Committing changes remotely.");
                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(SharedInfo.Stream, new NetCommand() { Type = NetCommandType.PushHead }, ProtoBuf.PrefixStyle.Fixed32);
                NetCommand response = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(SharedInfo.Stream, ProtoBuf.PrefixStyle.Fixed32);
                if (response.Type == NetCommandType.RejectPush)
                {
                    Printer.PrintError("Server rejected push, return code: {0}", response.AdditionalPayload);
                    return false;
                }
                else if (response.Type != NetCommandType.AcceptPush)
                {
                    Printer.PrintError("Unknown error pushing branch head.");
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Printer.PrintError("Error: {0}", e);
                Close();
                return false;
            }
        }

        public bool ReceivedData { get; set; }

        public bool Pull(bool pullRemoteObjects, string branchName)
        {
            ReceivedData = false;
            if (Workspace == null)
                return false;
            if (string.IsNullOrEmpty(RemoteDomain))
            {
                Printer.PrintError("#x#Error:##\n  Remote vault is not yet initialized. Can't pull.");
                return false;
            }
            try
            {
                string branchID = null;
                if (branchName == null)
                {
                    Printer.PrintMessage("Getting remote version information for branch \"{0}\"", Workspace.CurrentBranch.Name);
                    branchID = Workspace.CurrentBranch.ID.ToString();
                }
                else
                {
                    Printer.PrintMessage("Querying remote branch ID for \"{0}\"", branchName);
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.QueryBranchID, AdditionalPayload = string.IsNullOrEmpty(branchID) ? branchName : branchID }, ProtoBuf.PrefixStyle.Fixed32);
                    var queryResult = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                    if (queryResult.Type == NetCommandType.Error)
                        Printer.PrintError("Couldn't pull remote branch - error: {0}", queryResult.AdditionalPayload);
                    branchID = queryResult.AdditionalPayload;
                    Printer.PrintMessage(" - Matched query to remote branch ID {0}", branchID);
                }
                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.PullVersions, AdditionalPayload = branchID }, ProtoBuf.PrefixStyle.Fixed32);

                var command = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                if (command.Type == NetCommandType.Error)
                    throw new Exception("Remote error: " + command.AdditionalPayload);
                
                Printer.InteractivePrinter printer = Printer.CreateSpinnerPrinter(string.Empty, (object obj) =>
                {
                    NetCommandType type = (NetCommandType)obj;
                    if (type == NetCommandType.PushObjectQuery)
                        return "Determining Missing Versions";
                    else if (type == NetCommandType.PushVersions)
                        return "Receiving Version Data";
                    else if (type == NetCommandType.PushBranch)
                        return "Receiving Branch Data";
                    else if (type == NetCommandType.SynchronizeRecords)
                        return "Processing";
                    return "Communicating";
                });

                while (true)
                {
                    command = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                    if (printer != null)
                        printer.Update(command.Type);
                    if (command.Type == NetCommandType.PushObjectQuery)
                        SharedNetwork.ProcesPushObjectQuery(SharedInfo);
                    else if (command.Type == NetCommandType.PushBranchJournal)
                        SharedNetwork.ReceiveBranchJournal(SharedInfo);
                    else if (command.Type == NetCommandType.PushBranch)
                        SharedNetwork.ReceiveBranches(SharedInfo);
                    else if (command.Type == NetCommandType.PushVersions)
                        SharedNetwork.ReceiveVersions(SharedInfo);
                    else if (command.Type == NetCommandType.SynchronizeRecords)
                    {
                        if (printer != null)
                        {
                            printer.End(command.Type);
                            printer = null;
                        }
                        Printer.PrintMessage("Received #b#{0}## versions from remote vault.", SharedInfo.PushedVersions.Count);
                        SharedNetwork.RequestRecordMetadata(SharedInfo);
                        if (pullRemoteObjects)
                        {
                            Printer.PrintDiagnostics("Requesting record data...");
                            SharedNetwork.RequestRecordData(SharedInfo);
                        }
                        bool gotData = false;
                        bool result = PullVersions(SharedInfo, out gotData);
                        ReceivedData = gotData;
                        ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.Synchronized }, ProtoBuf.PrefixStyle.Fixed32);
                        return result;
                    }
                }
            }
            catch (Exception e)
            {
                Printer.PrintError("Error: {0}", e);
                Close();
                return false;
            }
        }

        private bool PullVersions(SharedNetwork.SharedNetworkInfo sharedInfo, out bool receivedData)
        {
            bool importResult = sharedInfo.Workspace.RunLocked(() =>
            {
                return SharedNetwork.ImportRecords(sharedInfo, true);
            }, false);
            receivedData = false;
            if (!importResult)
                return false;
            if (sharedInfo.PushedVersions.Count == 0 && sharedInfo.ReceivedBranchJournals.Count == 0 && sharedInfo.ReceivedBranches.Count == 0)
                return true;
            receivedData = true;
            return sharedInfo.Workspace.RunLocked(() =>
            {
                lock (sharedInfo.Workspace)
                {
                    try
                    {
                        sharedInfo.Workspace.BeginDatabaseTransaction();
                        if (!SharedNetwork.ImportBranchJournal(sharedInfo, true))
                            return false;
                        SharedNetwork.ImportBranches(sharedInfo);
                        Dictionary<Guid, Head> temporaryHeads = new Dictionary<Guid, Head>();
                        Dictionary<Guid, Guid> pendingMerges = new Dictionary<Guid, Guid>();
                        foreach (var x in sharedInfo.PushedVersions)
                        {
                            Branch branch = sharedInfo.Workspace.GetBranch(x.Version.Branch);
                            if (branch.Terminus.HasValue)
                                continue;
                            Head head;
                            if (!temporaryHeads.TryGetValue(branch.ID, out head))
                            {
                                var heads = sharedInfo.Workspace.GetBranchHeads(branch);
                                if (heads.Count == 0)
                                    head = new Head() { Branch = branch.ID, Version = x.Version.ID };
                                else if (heads.Count == 1)
                                    head = heads[0];
                                else
                                {
                                    Printer.PrintError("Multiple ({0}) heads for branch {1}", heads.Count, branch.ID);
                                    return false;
                                }
                                temporaryHeads[branch.ID] = head;
                            }
                            if (head.Version != x.Version.ID)
                            {
                                if (SharedNetwork.IsAncestor(head.Version, x.Version.ID, sharedInfo))
                                {
                                    pendingMerges[branch.ID] = Guid.Empty;
                                    head.Version = x.Version.ID;
                                }
                                else if (!SharedNetwork.IsAncestor(x.Version.ID, head.Version, sharedInfo))
                                {
                                    pendingMerges[branch.ID] = head.Version;
                                    head.Version = x.Version.ID;
                                }
                            }
                        }
                        List<Head> newHeads = new List<Head>();
                        List<VersionInfo> autoMerged = new List<VersionInfo>();
                        foreach (var x in pendingMerges)
                        {
                            if (x.Value == Guid.Empty)
                            {
                                Printer.PrintDiagnostics("Uncontested head update for branch \"{0}\".", Workspace.GetBranch(x.Key).Name);
                                Printer.PrintDiagnostics(" - Head updated to {0}", temporaryHeads[x.Key].Version);
                                continue;
                            }
                            Branch branch = Workspace.GetBranch(x.Key);
                            VersionInfo result;
                            string error;
                            result = Workspace.MergeRemote(Workspace.GetLocalOrRemoteVersion(x.Value, sharedInfo), temporaryHeads[x.Key].Version, sharedInfo, out error);
                            if (result == null)
                            {
                                if (x.Value != Workspace.Version.ID && temporaryHeads[x.Key].Branch == Workspace.CurrentBranch.ID)
                                {
                                    Printer.PrintError("Not on head revision!");
                                    return false;
                                }
                                newHeads.Add(new Head() { Branch = temporaryHeads[x.Key].Branch, Version = temporaryHeads[x.Key].Version });
                                Printer.PrintError("New head revision downloaded - requires manual merge between:");
                                Printer.PrintError(" - Local {0}", x.Value);
                                Printer.PrintError(" - Remote {0}", temporaryHeads[x.Key].Version);
                                temporaryHeads[x.Key] = null;
                            }
                            else
                            {
                                autoMerged.Add(result);
                                Printer.PrintMessage("Resolved incoming merge for branch \"{0}\".", branch.Name);
                                Printer.PrintDiagnostics(" - Merge local input {0}", x.Value);
                                Printer.PrintDiagnostics(" - Merge remote input {0}", temporaryHeads[x.Key].Version);
                                Printer.PrintDiagnostics(" - Head updated to {0}", result.Version.ID);
                                temporaryHeads[x.Key].Version = result.Version.ID;
                            }
                        }
                        var versionsToImport = sharedInfo.PushedVersions.OrderBy(x => x.Version.Timestamp).ToArray();
                        if (versionsToImport.Length != 0)
                        {
                            Dictionary<Guid, bool> importList = new Dictionary<Guid, bool>();
                            foreach (var x in versionsToImport)
                                importList[x.Version.ID] = false;
                            int importCount = versionsToImport.Length;
                            var orderedImports = versionsToImport.OrderBy(x => x.Version.Revision).ToList();
                            Printer.InteractivePrinter printer = null;
                            Printer.PrintMessage("Importing #b#{0}## versions...", orderedImports.Count);
                            printer = Printer.CreateProgressBarPrinter("Importing", string.Empty,
                                    (obj) =>
                                    {
                                        return string.Empty;
                                    },
                                    (obj) =>
                                    {
                                        return (100.0f * (int)(orderedImports.Count - importCount)) / (float)orderedImports.Count;
                                    },
                                    (pct, obj) =>
                                    {
                                        return string.Format("{0}/{1}", (int)(orderedImports.Count - importCount), orderedImports.Count);
                                    },
                                    60);
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
                                            sharedInfo.Workspace.ImportVersionNoCommit(sharedInfo, x, true);
                                            importList[x.Version.ID] = true;
                                            importCount--;
                                            printer.Update(importCount);
                                        }
                                    }
                                }
                            }
                            printer.End(importCount);
                        }
                        Printer.PrintMessage("Updating internal state...");
                        foreach (var x in autoMerged)
                            Workspace.ImportVersionNoCommit(sharedInfo, x, false);
                        foreach (var x in temporaryHeads)
                        {
                            if (x.Value != null)
                                Workspace.ImportHeadNoCommit(x);
                        }
                        foreach (var x in newHeads)
                        {
                            Workspace.AddHeadNoCommit(x);
                        }
                        Workspace.CommitDatabaseTransaction();
                        sharedInfo.Workspace.CommitDatabaseTransaction();
                        return true;
                    }
                    catch
                    {
                        sharedInfo.Workspace.RollbackDatabaseTransaction();
                        throw;
                    }
                }
            }, false);
        }

        public const int VersionrDefaultPort = 5122;
        public bool Connect(string host, int port, string module, bool requirewrite = false)
        {
            if (port == -1)
                port = VersionrDefaultPort;
            IEnumerator<SharedNetwork.Protocol> protocols = SharedNetwork.AllowedProtocols.Cast<SharedNetwork.Protocol>().GetEnumerator();
            Retry:
            if (!protocols.MoveNext())
            {
                Printer.PrintMessage("#e#No valid protocols available.##");
                return false;
            }
            Host = host;
            Port = port;
            Module = module;
            Connected = false;
            try
            {
                Connection = new System.Net.Sockets.TcpClient(host, port);
            }
            catch (Exception e)
            {
                Printer.PrintError(e.Message);
                return false;
            }
            if (Connection.Connected)
            {
                try
                {
                    Printer.PrintDiagnostics("Connected to server at {0}:{1}", host, port);
                    Handshake hs = Handshake.Create(protocols.Current);
                    hs.RequestedModule = Module;
                    Printer.PrintDiagnostics("Sending handshake...");
                    Connection.NoDelay = true;
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<Handshake>(Connection.GetStream(), hs, ProtoBuf.PrefixStyle.Fixed32);

                    var startTransaction = ProtoBuf.Serializer.DeserializeWithLengthPrefix<Network.StartTransaction>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                    if (startTransaction == null || !startTransaction.Accepted)
                    {
                        Printer.PrintError("#b#Server rejected connection.##");
                        if (startTransaction != null && hs.VersionrProtocol != startTransaction.ServerHandshake.VersionrProtocol)
                            Printer.PrintError("## Protocol mismatch - local: {0}, remote: {1}", hs.VersionrProtocol, startTransaction.ServerHandshake.VersionrProtocol);
                        else
                        {
                            if (startTransaction == null)
                                Printer.PrintError("## Connection terminated unexpectedly.");
                            else
                                Printer.PrintError("## Rejected request.");
                            return false;
                        }
                        goto Retry;
                    }
                    Printer.PrintDiagnostics("Server domain: {0}", startTransaction.Domain);
                    if (Workspace != null && !string.IsNullOrEmpty(startTransaction.Domain) && startTransaction.Domain != Workspace.Domain.ToString())
                    {
                        Printer.PrintError("Server domain doesn't match client domain. Disconnecting.");
                        return false;
                    }

                    RemoteDomain = startTransaction.Domain;

                    if (SharedNetwork.SupportsAuthentication(startTransaction.ServerHandshake.CheckProtocol().Value))
                    {
                        var command = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                        if (command.Type == NetCommandType.Authenticate)
                        {
                            bool runauth = true;
                            var challenge = ProtoBuf.Serializer.DeserializeWithLengthPrefix<AuthenticationChallenge>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                            if (!requirewrite && command.Identifier == 1) // server supports unauthenticated read
                            {
                                AuthenticationResponse response = new AuthenticationResponse()
                                {
                                    IdentifierToken = string.Empty,
                                    Mode = AuthenticationMode.Guest
                                };
                                ProtoBuf.Serializer.SerializeWithLengthPrefix(Connection.GetStream(), response, ProtoBuf.PrefixStyle.Fixed32);
                                command = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                                if (command.Type == NetCommandType.Acknowledge)
                                    runauth = false;
                            }
                            if (runauth)
                            {
                                bool q = Printer.Quiet;
                                Printer.Quiet = false;
                                Printer.PrintMessage("Server at #b#{0}## requires authentication.", VersionrURL);
                                while (true)
                                {
                                    if (challenge.AvailableModes.Contains(AuthenticationMode.Simple))
                                    {
                                        System.Console.CursorVisible = true;
                                        Printer.PrintMessageSingleLine("#b#Username:## ");
                                        string user = System.Console.ReadLine();
                                        Printer.PrintMessageSingleLine("#b#Password:## ");
                                        string pass = GetPassword();
                                        System.Console.CursorVisible = false;

                                        user = user.Trim(new char[] { '\r', '\n', ' ' });

                                        AuthenticationResponse response = new AuthenticationResponse()
                                        {
                                            IdentifierToken = user,
                                            Mode = AuthenticationMode.Simple,
                                            Payload = System.Text.ASCIIEncoding.ASCII.GetBytes(BCrypt.Net.BCrypt.HashPassword(pass, challenge.Salt))
                                        };
                                        Printer.PrintMessage("\n");
                                        ProtoBuf.Serializer.SerializeWithLengthPrefix(Connection.GetStream(), response, ProtoBuf.PrefixStyle.Fixed32);
                                        command = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                                        if (command.Type == NetCommandType.AuthRetry)
                                            Printer.PrintError("#e#Authentication failed.## Retry.");
                                        if (command.Type == NetCommandType.AuthFail)
                                        {
                                            Printer.PrintError("#e#Authentication failed.##");
                                            return false;
                                        }
                                        if (command.Type == NetCommandType.Acknowledge)
                                            break;
                                    }
                                    else
                                    {
                                        Printer.PrintError("Unsupported authentication requirements!");
                                        return false;
                                    }
                                }
                                Printer.Quiet = q;
                            }
                        }
                    }
                    if (startTransaction.Encrypted)
                    {
                        var key = startTransaction.RSAKey;
                        Printer.PrintDiagnostics("Server RSA Key: {0}", key.Fingerprint());

                        var publicKey = new System.Security.Cryptography.RSACryptoServiceProvider();
                        publicKey.ImportParameters(key);

                        Printer.PrintDiagnostics("Generating secret key for data channel...");
                        System.Security.Cryptography.RSAOAEPKeyExchangeFormatter exch = new System.Security.Cryptography.RSAOAEPKeyExchangeFormatter(publicKey);

                        System.Security.Cryptography.AesManaged aesProvider = new System.Security.Cryptography.AesManaged();
                        aesProvider.KeySize = 256;
                        aesProvider.GenerateIV();
                        aesProvider.GenerateKey();

                        AESProvider = aesProvider;
                        AESKey = aesProvider.Key;
                        AESIV = aesProvider.IV;

                        Printer.PrintDiagnostics("Key: {0}", System.Convert.ToBase64String(aesProvider.Key));
                        var keyExchangeObject = new Network.StartClientTransaction() { Key = exch.CreateKeyExchange(aesProvider.Key), IV = exch.CreateKeyExchange(aesProvider.IV) };

                        ProtoBuf.Serializer.SerializeWithLengthPrefix<StartClientTransaction>(Connection.GetStream(), keyExchangeObject, ProtoBuf.PrefixStyle.Fixed32);
                        Connection.GetStream().Flush();
                        Connected = true;
                        SharedNetwork.SharedNetworkInfo sharedInfo = new SharedNetwork.SharedNetworkInfo()
                        {
                            DecryptorFunction = () => { return Decryptor; },
                            EncryptorFunction = () => { return Encryptor; },
                            Stream = Connection.GetStream(),
                            Workspace = Workspace,
                            Client = true,
                            CommunicationProtocol = protocols.Current
                        };

                        SharedInfo = sharedInfo;
                    }
                    else
                    {
                        Printer.PrintDiagnostics("Using cleartext communication");
                        var keyExchangeObject = new Network.StartClientTransaction();
                        ProtoBuf.Serializer.SerializeWithLengthPrefix<StartClientTransaction>(Connection.GetStream(), keyExchangeObject, ProtoBuf.PrefixStyle.Fixed32);
                        Connection.GetStream().Flush();
                        Connected = true;
                        SharedNetwork.SharedNetworkInfo sharedInfo = new SharedNetwork.SharedNetworkInfo()
                        {
                            DecryptorFunction = null,
                            EncryptorFunction = null,
                            Stream = Connection.GetStream(),
                            Workspace = Workspace,
                            Client = true,
                            CommunicationProtocol = protocols.Current
                        };
                        SharedInfo = sharedInfo;
                    }
                    return true;
                }
                catch (Exception e)
                {
                    Printer.PrintError("Error encountered: {0}", e);
                    return false;
                }
            }
            else
                return false;
        }

        private string GetPassword()
        {
            List<char> pwd = new List<char>();
            while (true)
            {
                ConsoleKeyInfo i = Console.ReadKey(true);
                if (i.Key == ConsoleKey.Enter)
                {
                    break;
                }
                else if (i.Key == ConsoleKey.Backspace)
                {
                    if (pwd.Count > 0)
                    {
                        pwd.RemoveAt(pwd.Count - 1);
                        Console.Write("\b \b");
                    }
                }
                else
                {
                    pwd.Add(i.KeyChar);
                    Console.Write("*");
                }
            }
            return new string(pwd.ToArray());
        }

        internal List<string> GetRecordData(List<Record> missingRecords)
        {
            return SharedNetwork.RequestRecordDataUnmapped(SharedInfo, missingRecords.Select(x => x.DataIdentifier).ToList());
        }
    }
}
