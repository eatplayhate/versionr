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
        private static System.Security.Cryptography.RSAParameters PrivateKeyData { get; set; }
        private static System.Security.Cryptography.RSAParameters PublicKey { get; set; }
        private static System.Security.Cryptography.RSACryptoServiceProvider PrivateKey { get; set; }
        public static bool Run(System.IO.DirectoryInfo info, int port, bool encryptData = true)
        {
            Area ws = Area.Load(info);
            if (ws == null)
            {
                Printer.PrintError("Can't run server without an active vault.");
                return false;
            }
            if (encryptData)
            {
                Printer.PrintDiagnostics("Creating RSA pair...");
                System.Security.Cryptography.RSACryptoServiceProvider rsaCSP = new System.Security.Cryptography.RSACryptoServiceProvider();
                rsaCSP.KeySize = 2048;
                PrivateKey = rsaCSP;
                PrivateKeyData = rsaCSP.ExportParameters(true);
                PublicKey = rsaCSP.ExportParameters(false);
                Printer.PrintDiagnostics("RSA Fingerprint: {0}", PublicKey.Fingerprint());
            }
            System.Net.Sockets.TcpListener listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port);
            Printer.PrintDiagnostics("Binding to {0}.", listener.LocalEndpoint);
            listener.Start();
            while (true)
            {
                Printer.PrintDiagnostics("Waiting for connection.");
                var client = listener.AcceptTcpClient();
                Task.Run(() => { HandleConnection(Area.Load(info), client); });
            }
            listener.Stop();

            return true;
        }

        internal class ClientStateInfo
        {
            public Dictionary<Guid, Objects.Head> UpdatedHeads { get; set; }
            public List<VersionInfo> MergeVersions { get; set; }
            public SharedNetwork.SharedNetworkInfo SharedInfo { get; set; }
            public ClientStateInfo()
            {
                MergeVersions = new List<VersionInfo>();
            }
        }

        static void HandleConnection(Area ws, TcpClient client)
        {
            ClientStateInfo clientInfo = new ClientStateInfo();
            using (client)
            using (SharedNetwork.SharedNetworkInfo sharedInfo = new SharedNetwork.SharedNetworkInfo())
            {
                try
                {
                    var stream = client.GetStream();
                    Handshake hs = ProtoBuf.Serializer.DeserializeWithLengthPrefix<Handshake>(stream, ProtoBuf.PrefixStyle.Fixed32);
                    Printer.PrintDiagnostics("Received handshake - protocol: {0}", hs.VersionrProtocol);
                    SharedNetwork.Protocol? clientProtocol = hs.CheckProtocol();
                    bool valid = true;
                    if (clientProtocol == null)
                        valid = false;
                    else
                        valid = SharedNetwork.AllowedProtocols.Contains(clientProtocol.Value);
                    if (valid)
                    {
                        sharedInfo.CommunicationProtocol = clientProtocol.Value;
                        Network.StartTransaction startSequence = null;
                        if (PrivateKey != null)
                        {
                            startSequence = Network.StartTransaction.Create(ws.Domain.ToString(), PublicKey, clientProtocol.Value);
                            Printer.PrintDiagnostics("Sending RSA key...");
                            ProtoBuf.Serializer.SerializeWithLengthPrefix<Network.StartTransaction>(stream, startSequence, ProtoBuf.PrefixStyle.Fixed32);
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
                            startSequence = Network.StartTransaction.Create(ws.Domain.ToString(), clientProtocol.Value);
                            ProtoBuf.Serializer.SerializeWithLengthPrefix<Network.StartTransaction>(stream, startSequence, ProtoBuf.PrefixStyle.Fixed32);
                            StartClientTransaction clientKey = ProtoBuf.Serializer.DeserializeWithLengthPrefix<StartClientTransaction>(stream, ProtoBuf.PrefixStyle.Fixed32);
                        }
                        sharedInfo.Stream = stream;
                        sharedInfo.Workspace = ws;

                        clientInfo.SharedInfo = sharedInfo;

                        while (true)
                        {
                            NetCommand command = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(stream, ProtoBuf.PrefixStyle.Fixed32);
                            if (command.Type == NetCommandType.Close)
                            {
                                Printer.PrintDiagnostics("Client closing connection.");
                                break;
                            }
                            else if (command.Type == NetCommandType.PushBranchJournal)
                            {
                                SharedNetwork.ReceiveBranchJournal(sharedInfo);
                            }
                            else if (command.Type == NetCommandType.QueryBranchID)
                            {
                                Printer.PrintDiagnostics("Client is requesting a branch ID with name \"{0}\"", command.AdditionalPayload);
                                bool multiple;
                                var branch = ws.GetBranchByPartialName(command.AdditionalPayload, out multiple);
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
                            else if (command.Type == NetCommandType.RequestRecordUnmapped)
                            {
                                Printer.PrintDiagnostics("Client is requesting specific record data blobs.");
                                SharedNetwork.SendRecordDataUnmapped(sharedInfo);
                            }
                            else if (command.Type == NetCommandType.Clone)
                            {
                                Printer.PrintDiagnostics("Client is requesting to clone the vault.");
                                Objects.Version initialRevision = ws.GetVersion(ws.Domain);
                                Objects.Branch initialBranch = ws.GetBranch(initialRevision.Branch);
                                Utilities.SendEncrypted<ClonePayload>(sharedInfo, new ClonePayload() { InitialBranch = initialBranch, RootVersion = initialRevision });
                            }
                            else if (command.Type == NetCommandType.PullVersions)
                            {
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
                                if (!SharedNetwork.GetVersionList(sharedInfo, sharedInfo.Workspace.GetVersion(sharedInfo.Workspace.GetBranchHead(branch).Version), out branchesToSend, out versionsToSend))
                                    throw new Exception();
                                if (!SharedNetwork.SendBranches(sharedInfo, branchesToSend))
                                    throw new Exception();
                                if (!SharedNetwork.SendVersions(sharedInfo, versionsToSend))
                                    throw new Exception();
                            }
                            else if (command.Type == NetCommandType.PushObjectQuery)
                            {
                                Printer.PrintDiagnostics("Client asking about objects on the server...");
                                SharedNetwork.ProcesPushObjectQuery(sharedInfo);
                            }
                            else if (command.Type == NetCommandType.PushBranch)
                            {
                                Printer.PrintDiagnostics("Client attempting to send branch data...");
                                SharedNetwork.ReceiveBranches(sharedInfo);
                            }
                            else if (command.Type == NetCommandType.PushVersions)
                            {
                                Printer.PrintDiagnostics("Client attempting to send version data...");
                                SharedNetwork.ReceiveVersions(sharedInfo);
                            }
                            else if (command.Type == NetCommandType.PushHead)
                            {
                                Printer.PrintDiagnostics("Determining head information.");
                                string errorData;
                                lock (ws)
                                {
                                    clientInfo.SharedInfo.Workspace.RunLocked(() =>
                                    {
                                        if (AcceptHeads(clientInfo, ws, out errorData))
                                        {
                                            ImportVersions(ws, clientInfo);
                                            ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.AcceptPush }, ProtoBuf.PrefixStyle.Fixed32);
                                            return true;
                                        }
                                        else
                                            ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.RejectPush, AdditionalPayload = errorData }, ProtoBuf.PrefixStyle.Fixed32);
                                        return false;
                                    }, false);
                                }
                            }
                            else if (command.Type == NetCommandType.SynchronizeRecords)
                            {
                                Printer.PrintDiagnostics("Received {0} versions in version pack, but need {1} records to commit data.", sharedInfo.PushedVersions.Count, sharedInfo.UnknownRecords.Count);
                                Printer.PrintDiagnostics("Beginning record synchronization...");
                                if (sharedInfo.UnknownRecords.Count > 0)
                                {
                                    Printer.PrintDiagnostics("Requesting record metadata...");
                                    SharedNetwork.RequestRecordMetadata(clientInfo.SharedInfo);
                                    Printer.PrintDiagnostics("Requesting record data...");
                                    SharedNetwork.RequestRecordData(sharedInfo);
                                    SharedNetwork.ImportRecords(sharedInfo);
                                }
                                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Synchronized }, ProtoBuf.PrefixStyle.Fixed32);
                            }
                            else if (command.Type == NetCommandType.FullClone)
                            {
                                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Acknowledge, Identifier = (int)ws.DatabaseVersion }, ProtoBuf.PrefixStyle.Fixed32);
                                command = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(stream, ProtoBuf.PrefixStyle.Fixed32);
                                if (command.Type == NetCommandType.Acknowledge)
                                {
                                    System.IO.FileInfo fsInfo = new System.IO.FileInfo(System.IO.Path.GetTempFileName());
                                    Printer.PrintDiagnostics("Client requesting full clone, temp file: {0}", fsInfo.FullName);
                                    try
                                    {
                                        if (ws.BackupDB(fsInfo))
                                        {
                                            Printer.PrintDiagnostics("Backup complete. Sending data.");
                                            byte[] blob = new byte[256 * 1024];
                                            long filesize = fsInfo.Length;
                                            long position = 0;
                                            using (System.IO.FileStream reader = fsInfo.OpenRead())
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
                                        }
                                        else
                                        {
                                            Printer.PrintDiagnostics("Backup failed. Aborting.");
                                            Utilities.SendEncrypted<DataPayload>(sharedInfo, new DataPayload() { Data = new byte[0], EndOfStream = true });
                                            ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(stream, new NetCommand() { Type = NetCommandType.Error }, ProtoBuf.PrefixStyle.Fixed32);
                                        }
                                    }
                                    finally
                                    {
                                        fsInfo.Delete();
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
            }
            Printer.PrintDiagnostics("Ended client processor task!");
        }

        private static bool AcceptHeads(ClientStateInfo clientInfo, Area ws, out string errorData)
        {
            Dictionary<Guid, Head> temporaryHeads = new Dictionary<Guid, Head>();
            Dictionary<Guid, Guid> pendingMerges = new Dictionary<Guid, Guid>();
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
                    if (IsAncestor(head.Version, x.Version.ID, clientInfo, ws))
                    {
                        pendingMerges[branch.ID] = Guid.Empty;
                        head.Version = x.Version.ID;
                    }
                    else if (!IsAncestor(x.Version.ID, head.Version, clientInfo, ws))
                    {
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

        private static bool IsAncestor(Guid ancestor, Guid possibleChild, ClientStateInfo clientInfo, Area ws)
        {
            HashSet<Guid> checkedVersions = new HashSet<Guid>();
            return IsAncestorInternal(checkedVersions, ancestor, possibleChild, clientInfo, ws);
        }

        private static bool IsAncestorInternal(HashSet<Guid> checkedVersions, Guid ancestor, Guid possibleChild, ClientStateInfo clientInfo, Area ws)
        {
            Guid nextVersionToCheck = possibleChild;
            if (ancestor == possibleChild)
                return true;
            while (true)
            {
                if (checkedVersions.Contains(nextVersionToCheck))
                    return false;
                checkedVersions.Add(nextVersionToCheck);
                List<MergeInfo> mergeInfo;
                Objects.Version v = FindLocalOrRemoteVersionInfo(nextVersionToCheck, clientInfo, ws, out mergeInfo);
                if (!v.Parent.HasValue)
                    return false;
                else if (v.Parent.Value == ancestor)
                    return true;
                foreach (var x in mergeInfo)
                {
                    if (IsAncestorInternal(checkedVersions, ancestor, x.SourceVersion, clientInfo, ws))
                        return true;
                }
                nextVersionToCheck = v.Parent.Value;
            }
        }

        private static Objects.Version FindLocalOrRemoteVersionInfo(Guid possibleChild, ClientStateInfo clientInfo, Area ws, out List<MergeInfo> mergeInfo)
        {
            VersionInfo info = clientInfo.SharedInfo.PushedVersions.Where(x => x.Version.ID == possibleChild).FirstOrDefault();
            if (info != null)
            {
                mergeInfo = info.MergeInfos != null ? info.MergeInfos.ToList() : new List<MergeInfo>();
                return info.Version;
            }
            Objects.Version localVersion = ws.GetVersion(possibleChild);
            mergeInfo = ws.GetMergeInfo(localVersion.ID).ToList();
            return localVersion;
        }

        private static bool ImportVersions(Area ws, ClientStateInfo clientInfo)
        {
            lock (ws)
            {
                try
                {
                    ws.BeginDatabaseTransaction();
                    if (!SharedNetwork.ImportBranchJournal(clientInfo.SharedInfo, false))
                    {
                        ws.RollbackDatabaseTransaction();
                        return false;
                    }
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
                    ws.CommitDatabaseTransaction();
                    return true;
                }
                catch
                {
                    ws.RollbackDatabaseTransaction();
                    throw;
                }
            }
        }
        
    }
}
