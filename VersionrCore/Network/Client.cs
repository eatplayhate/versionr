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
        public Client(Area area)
        {
            Workspace = area;
            ServerKnownBranches = new HashSet<Guid>();
            ServerKnownVersions = new HashSet<Guid>();
        }
        public Client(System.IO.DirectoryInfo baseDirectory)
        {
            Versionr.Utilities.MultiArchPInvoke.BindDLLs();
            Workspace = null;
            BaseDirectory = baseDirectory;
            ServerKnownBranches = new HashSet<Guid>();
            ServerKnownVersions = new HashSet<Guid>();
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
                    Workspace = Area.InitRemote(BaseDirectory, clonePack);
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
                    try
                    {
                        using (var stream = fsInfo.OpenWrite())
                        {
                            while (true)
                            {
                                var data = Utilities.ReceiveEncrypted<DataPayload>(SharedInfo);
                                stream.Write(data.Data, 0, data.Data.Length);
                                if (data.EndOfStream)
                                    break;
                            }
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

        public bool Push()
        {
            if (Workspace == null)
                return false;
            try
            {
                Stack<Objects.Branch> branchesToSend = new Stack<Branch>();
                Stack<Objects.Version> versionsToSend = new Stack<Objects.Version>();
                Printer.PrintMessage("Determining data to send...");
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

        public bool Pull(bool pullRemoteObjects, string branchName)
        {
            if (Workspace == null)
                return false;
            try
            {
                string branchID;
                if (branchName == null)
                {
                    Printer.PrintMessage("Getting remote version information for branch \"{0}\"", Workspace.CurrentBranch.Name);
                    branchID = Workspace.CurrentBranch.ID.ToString();
                }
                else
                {
                    Printer.PrintMessage("Querying remote branch ID for \"{0}\"", branchName);
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.QueryBranchID, AdditionalPayload = branchName }, ProtoBuf.PrefixStyle.Fixed32);
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

                while (true)
                {
                    command = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                    if (command.Type == NetCommandType.PushObjectQuery)
                        SharedNetwork.ProcesPushObjectQuery(SharedInfo);
                    else if (command.Type == NetCommandType.PushBranch)
                        SharedNetwork.ReceiveBranches(SharedInfo);
                    else if (command.Type == NetCommandType.PushVersions)
                        SharedNetwork.ReceiveVersions(SharedInfo);
                    else if (command.Type == NetCommandType.SynchronizeRecords)
                    {
                        Printer.PrintMessage("Received {0} versions from remote vault.", SharedInfo.PushedVersions.Count);
                        SharedNetwork.RequestRecordMetadata(SharedInfo);
                        if (pullRemoteObjects)
                        {
                            Printer.PrintDiagnostics("Requesting record data...");
                            SharedNetwork.RequestRecordData(SharedInfo);
                        }
                        bool result = PullVersions(SharedInfo);
                        ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.Synchronized }, ProtoBuf.PrefixStyle.Fixed32);
                        return result;
                    }
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

        private bool PullVersions(SharedNetwork.SharedNetworkInfo sharedInfo)
        {
            SharedNetwork.ImportRecords(sharedInfo);

            Dictionary<Guid, Head> temporaryHeads = new Dictionary<Guid, Head>();
            Dictionary<Guid, Guid> pendingMerges = new Dictionary<Guid, Guid>();
            foreach (var x in sharedInfo.PushedVersions)
            {
                Branch branch = sharedInfo.Workspace.GetBranch(x.Version.Branch);
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
                    if (IsAncestor(head.Version, x.Version.ID, sharedInfo))
                    {
                        pendingMerges[branch.ID] = Guid.Empty;
                        head.Version = x.Version.ID;
                    }
                    else if (!IsAncestor(x.Version.ID, head.Version, sharedInfo))
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
                result = Workspace.MergeRemote(Workspace.GetVersion(x.Value), temporaryHeads[x.Key].Version, sharedInfo, out error);
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

            lock (sharedInfo.Workspace)
            {
                try
                {
                    sharedInfo.Workspace.BeginDatabaseTransaction();
                    var versionsToImport = sharedInfo.PushedVersions.OrderBy(x => x.Version.Timestamp).ToArray();
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
                                    sharedInfo.Workspace.ImportVersionNoCommit(sharedInfo, x, true);
                                    importList[x.Version.ID] = true;
                                    importCount--;
                                }
                            }
                        }
                    }
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
        }

        private static bool IsAncestor(Guid ancestor, Guid possibleChild, SharedNetwork.SharedNetworkInfo clientInfo)
        {
            HashSet<Guid> checkedVersions = new HashSet<Guid>();
            return IsAncestorInternal(checkedVersions, ancestor, possibleChild, clientInfo);
        }

        private static bool IsAncestorInternal(HashSet<Guid> checkedVersions, Guid ancestor, Guid possibleChild, SharedNetwork.SharedNetworkInfo clientInfo)
        {
            Guid nextVersionToCheck = possibleChild;
            while (true)
            {
                if (checkedVersions.Contains(nextVersionToCheck))
                    return false;
                checkedVersions.Add(nextVersionToCheck);
                List<MergeInfo> mergeInfo;
                Objects.Version v = FindLocalOrRemoteVersionInfo(nextVersionToCheck, clientInfo, out mergeInfo);
                if (!v.Parent.HasValue)
                    return false;
                else if (v.Parent.Value == ancestor)
                    return true;
                foreach (var x in mergeInfo)
                {
                    if (IsAncestorInternal(checkedVersions, ancestor, x.SourceVersion, clientInfo))
                        return true;
                }
                nextVersionToCheck = v.Parent.Value;
            }
        }

        private static Objects.Version FindLocalOrRemoteVersionInfo(Guid possibleChild, SharedNetwork.SharedNetworkInfo clientInfo, out List<MergeInfo> mergeInfo)
        {
            VersionInfo info = clientInfo.PushedVersions.Where(x => x.Version.ID == possibleChild).FirstOrDefault();
            if (info != null)
            {
                mergeInfo = info.MergeInfos != null ? info.MergeInfos.ToList() : new List<MergeInfo>();
                return info.Version;
            }
            Objects.Version localVersion = clientInfo.Workspace.GetVersion(possibleChild);
            mergeInfo = clientInfo.Workspace.GetMergeInfo(localVersion.ID).ToList();
            return localVersion;
        }

        public bool Connect(string host, int port)
        {
            Host = host;
            Port = port;
            Connected = false;
            Connection = new System.Net.Sockets.TcpClient(host, port);
            if (Connection.Connected)
            {
                try
                {
                    Printer.PrintDiagnostics("Connected to server at {0}:{1}", host, port);
                    Handshake hs = Handshake.Create();
                    Printer.PrintDiagnostics("Sending handshake...");
                    Connection.NoDelay = true;
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<Handshake>(Connection.GetStream(), hs, ProtoBuf.PrefixStyle.Fixed32);

                    var startTransaction = ProtoBuf.Serializer.DeserializeWithLengthPrefix<Network.StartTransaction>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                    if (!startTransaction.Accepted)
                    {
                        Printer.PrintDiagnostics("Server rejected connection. Protocol mismatch - local: {0}, remote: {1}", hs.VersionrProtocol, startTransaction.ServerHandshake.VersionrProtocol);
                        return false;
                    }
                    Printer.PrintDiagnostics("Server domain: {0}", startTransaction.Domain);
                    if (Workspace != null && startTransaction.Domain != Workspace.Domain.ToString())
                    {
                        Printer.PrintError("Server domain doesn't match client domain. Disconnecting.");
                        return false;
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

        internal List<string> GetRecordData(List<Record> missingRecords)
        {
            return SharedNetwork.RequestRecordDataUnmapped(SharedInfo, missingRecords.Select(x => x.DataIdentifier).ToList());
        }
    }
}
