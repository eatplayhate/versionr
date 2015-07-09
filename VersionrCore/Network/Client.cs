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
        Area Workspace { get; set; }
        System.Security.Cryptography.AesManaged AESProvider { get; set; }
        byte[] AESKey { get; set; }
        byte[] AESIV { get; set; }
        public bool Connected { get; set; }

        System.IO.DirectoryInfo BaseDirectory { get; set; }

        HashSet<Guid> ServerKnownBranches { get; set; }
        HashSet<Guid> ServerKnownVersions { get; set; }

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
            Workspace = null;
            BaseDirectory = baseDirectory;
            ServerKnownBranches = new HashSet<Guid>();
            ServerKnownVersions = new HashSet<Guid>();
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
                }
                catch
                {

                }
                Printer.PrintDiagnostics("Disconnected.");
            }
            Connection.Close();
        }

        public bool Clone()
        {
            if (Workspace != null)
                return false;
            try
            {
                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.Clone }, ProtoBuf.PrefixStyle.Fixed32);
                var clonePack = Utilities.ReceiveEncrypted<ClonePayload>(Connection.GetStream(), Decryptor);
                Workspace = Area.InitRemote(BaseDirectory, clonePack);
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
                if (!GetVersionList(branchesToSend, versionsToSend, Workspace.Version))
                    return false;
                Printer.PrintDiagnostics("Need to send {0} versions and {1} branches.", versionsToSend.Count, branchesToSend.Count);
                if (!SendBranches(branchesToSend))
                    return false;
                if (!SendVersions(versionsToSend))
                    return false;
                return true;
            }
            catch (Exception e)
            {
                Printer.PrintError("Error: {0}", e);
                Close();
                return false;
            }
        }

        private bool SendVersions(Stack<Objects.Version> versionsToSend)
        {
            try
            {
                if (versionsToSend.Count == 0)
                    return true;
                Printer.PrintDiagnostics("Synchronizing {0} versions to server.", versionsToSend.Count);
                while (versionsToSend.Count > 0)
                {
                    List<Objects.Version> versionData = new List<Objects.Version>();
                    while (versionData.Count < 512 && versionsToSend.Count > 0)
                    {
                        versionData.Add(versionsToSend.Pop());
                    }
                    Printer.PrintDiagnostics("Sending version data pack...");
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.PushVersions }, ProtoBuf.PrefixStyle.Fixed32);
                    VersionPack pack = CreatePack(versionData);
                    Utilities.SendEncrypted(Connection.GetStream(), Encryptor, pack);
                    NetCommand response = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                    if (response.Type != NetCommandType.Acknowledge)
                        return false;

                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.SynchronizeRecords }, ProtoBuf.PrefixStyle.Fixed32);
                    while (true)
                    {
                        var command = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                        if (command.Type == NetCommandType.RequestRecordParents)
                        {
                            Printer.PrintDiagnostics("Server is asking for record metadata...");
                            var rrp = Utilities.ReceiveEncrypted<RequestRecordParents>(Connection.GetStream(), Decryptor);
                            RecordParentPack rp = new RecordParentPack();
                            rp.Parents = rrp.RecordParents.Select(x => Workspace.GetRecord(x)).ToArray();
                            Utilities.SendEncrypted<RecordParentPack>(Connection.GetStream(), Encryptor, rp);
                        }
                        else if (command.Type == NetCommandType.RequestRecord)
                        {
                            var rrd = Utilities.ReceiveEncrypted<RequestRecordData>(Connection.GetStream(), Decryptor);
                            List<byte> datablock = new List<byte>();
                            Func<IEnumerable<byte>, bool, bool> sender = (IEnumerable<byte> data, bool flush) =>
                            {
                                datablock.AddRange(data);
                                int blockSize = 1024 * 1024;
                                while (datablock.Count > blockSize)
                                {
                                    DataPayload dataPack = new DataPayload() { Data = datablock.Take(blockSize).ToArray(), EndOfStream = false };
                                    Utilities.SendEncrypted<DataPayload>(Connection.GetStream(), Encryptor, dataPack);
                                    datablock.RemoveRange(0, blockSize);
                                    var reply = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                                    if (reply.Type != NetCommandType.DataReceived)
                                        return false;
                                }
                                if (flush)
                                {
                                    DataPayload dataPack = new DataPayload() { Data = datablock.ToArray(), EndOfStream = true };
                                    Utilities.SendEncrypted<DataPayload>(Connection.GetStream(), Encryptor, dataPack);
                                }
                                return true;
                            };
                            foreach (var x in rrd.Records)
                            {
                                var record = Workspace.GetRecord(x);
                                Printer.PrintDiagnostics("Sending data for: {0}", record.CanonicalName);
                                sender(BitConverter.GetBytes(x), false);
                                if (!Workspace.TransmitRecordData(record, sender))
                                    return false;
                            }
                            if (!sender(new byte[0], true))
                                return false;

                            var dataResponse = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                            if (dataResponse.Type != NetCommandType.Acknowledge)
                                return false;
                        }
                        else if (command.Type == NetCommandType.Synchronized)
                        {
                            Printer.PrintDiagnostics("Committing changes remotely.");
                            ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.PushHead }, ProtoBuf.PrefixStyle.Fixed32);
                            response = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
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

        private VersionPack CreatePack(List<Objects.Version> versionData)
        {
            VersionPack pack = new VersionPack();
            pack.Versions = versionData.Select(x => CreateVersionInfo(x)).ToArray();
            return pack;
        }

        private VersionInfo CreateVersionInfo(Objects.Version x)
        {
            VersionInfo info = new VersionInfo();
            info.Version = x;
            info.MergeInfos = Workspace.GetMergeInfo(x.ID).ToArray();
            info.Alterations = Workspace.GetAlterations(x).Select(y => CreateFusedAlteration(y)).ToArray();
            return info;
        }

        private FusedAlteration CreateFusedAlteration(Alteration y)
        {
            FusedAlteration alteration = new FusedAlteration();
            alteration.Alteration = y.Type;
            alteration.NewRecord = y.NewRecord.HasValue ? Workspace.GetRecord(y.NewRecord.Value) : null;
            alteration.PriorRecord = y.PriorRecord.HasValue ? Workspace.GetRecord(y.PriorRecord.Value) : null;
            return alteration;
        }

        private bool GetVersionList(Stack<Branch> branchesToSend, Stack<Objects.Version> versionsToSend, Objects.Version version)
        {
            try
            {
                if (ServerKnownVersions.Contains(version.ID))
                    return true;
                Printer.PrintDiagnostics("Sending server local version information.");
                Objects.Version currentVersion = version;
                while (true)
                {
                    List<Objects.Version> partialHistory = Workspace.GetHistory(currentVersion, 64);
                    foreach (var x in partialHistory)
                    {
                        if (ServerKnownVersions.Contains(x.ID))
                            break;
                        ServerKnownVersions.Add(x.ID);
                        currentVersion = x;
                    }

                    PushObjectQuery query = new PushObjectQuery();
                    query.Type = ObjectType.Version;
                    query.IDs = partialHistory.Select(x => x.ID.ToString()).ToArray();

                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.PushObjectQuery }, ProtoBuf.PrefixStyle.Fixed32);
                    Utilities.SendEncrypted<PushObjectQuery>(Connection.GetStream(), Encryptor, query);

                    PushObjectResponse response = Utilities.ReceiveEncrypted<PushObjectResponse>(Connection.GetStream(), Decryptor);
                    if (response.Recognized.Length != query.IDs.Length)
                        throw new Exception("Invalid response!");
                    int recognized = 0;
                    for (int i = 0; i < response.Recognized.Length; i++)
                    {
                        Printer.PrintDiagnostics(" - Version ID: {0}", query.IDs[i]);
                        if (!response.Recognized[i])
                        {
                            versionsToSend.Push(partialHistory[i]);
                            Printer.PrintDiagnostics("   (not recognized on server)");
                        }
                        else
                        {
                            recognized++;
                            Printer.PrintDiagnostics("   (version already on server)");
                        }
                    }
                    for (int i = 0; i < response.Recognized.Length; i++)
                    {
                        if (!response.Recognized[i])
                        {
                            QueryBranch(branchesToSend, Workspace.GetBranch(partialHistory[i].Branch));
                            var info = Workspace.GetMergeInfo(partialHistory[i].ID);
                            foreach (var x in info)
                            {
                                var srcVersion = Workspace.GetVersion(x.SourceVersion);
                                if (srcVersion != null)
                                {
                                    if (!GetVersionList(branchesToSend, versionsToSend, srcVersion))
                                    {
                                        Printer.PrintDiagnostics("Sending merge data for: {0}", srcVersion.ID);
                                    }
                                }
                            }
                        }
                    }
                    if (recognized != 0) // we found a common parent somewhere
                        break;
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

        internal bool QueryBranch(Stack<Objects.Branch> branchesToSend, Branch branch)
        {
            try
            {
                if (ServerKnownBranches.Contains(branch.ID))
                    return true;
                Printer.PrintDiagnostics("Sending server local branch information.");
                Branch currentBranch = branch;
                while (true)
                {
                    int branchesPerBlock = 16;
                    List<Objects.Branch> branchIDs = new List<Objects.Branch>();
                    while (branchesPerBlock > 0)
                    {
                        if (ServerKnownBranches.Contains(currentBranch.ID))
                            break;
                        ServerKnownBranches.Add(currentBranch.ID);
                        branchesPerBlock--;
                        branchIDs.Add(currentBranch);
                        if (currentBranch.Parent.HasValue)
                            currentBranch = Workspace.GetBranch(currentBranch.Parent.Value);
                        else
                        {
                            currentBranch = null;
                            break;
                        }
                    }
                    
                    PushObjectQuery query = new PushObjectQuery();
                    query.Type = ObjectType.Branch;
                    query.IDs = branchIDs.Select(x => x.ID.ToString()).ToArray();

                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.PushObjectQuery }, ProtoBuf.PrefixStyle.Fixed32);
                    Utilities.SendEncrypted<PushObjectQuery>(Connection.GetStream(), Encryptor, query);

                    PushObjectResponse response = Utilities.ReceiveEncrypted<PushObjectResponse>(Connection.GetStream(), Decryptor);
                    if (response.Recognized.Length != query.IDs.Length)
                        throw new Exception("Invalid response!");
                    int recognized = 0;
                    for (int i = 0; i < response.Recognized.Length; i++)
                    {
                        Printer.PrintDiagnostics(" - Branch ID: {0}", query.IDs[i]);
                        if (!response.Recognized[i])
                        {
                            branchesToSend.Push(branchIDs[i]);
                            Printer.PrintDiagnostics("   (not recognized on server)");
                        }
                        else
                        {
                            ServerKnownBranches.Add(branchIDs[i].ID);
                            recognized++;
                            Printer.PrintDiagnostics("   (branch already on server)");
                        }
                    }
                    if (recognized != 0) // we found a common parent somewhere
                        break;
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

        internal bool SendBranches(Stack<Objects.Branch> branchesToSend)
        {
            try
            {
                if (branchesToSend.Count == 0)
                    return true;
                Printer.PrintDiagnostics("Synchronizing {0} branches to server.", branchesToSend.Count);
                while (branchesToSend.Count > 0)
                {
                    List<Objects.Branch> branchData = new List<Branch>();
                    while (branchData.Count < 512 && branchesToSend.Count > 0)
                    {
                        branchData.Add(branchesToSend.Pop());
                    }
                    Printer.PrintDiagnostics("Sending branch data pack...");
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(Connection.GetStream(), new NetCommand() { Type = NetCommandType.PushBranch }, ProtoBuf.PrefixStyle.Fixed32);
                    PushBranches pb = new PushBranches() { Branches = branchData.ToArray() };
                    Utilities.SendEncrypted(Connection.GetStream(), Encryptor, pb);
                    NetCommand response = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(Connection.GetStream(), ProtoBuf.PrefixStyle.Fixed32);
                    if (response.Type != NetCommandType.Acknowledge)
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

        public bool Connect(string host, int port)
        {
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
                    Printer.PrintDiagnostics("Server domain: {0}", startTransaction.Domain);
                    if (Workspace != null && startTransaction.Domain != Workspace.Domain.ToString())
                    {
                        Printer.PrintError("Server domain doesn't match client domain. Disconnecting.");
                        return false;
                    }

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
    }
}
