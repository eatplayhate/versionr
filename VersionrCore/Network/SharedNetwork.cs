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
    static class SharedNetwork
    {
        internal class SharedNetworkInfo : IDisposable
        {
            public Func<ICryptoTransform> EncryptorFunction { get; set; }
            public ICryptoTransform Encryptor
            {
                get
                {
                    return EncryptorFunction();
                }
            }
            public Func<ICryptoTransform> DecryptorFunction { get; set; }
            public ICryptoTransform Decryptor
            {
                get
                {
                    return DecryptorFunction();
                }
            }
            public NetworkStream Stream { get; set; }
            public Area Workspace { get; set; }
            public HashSet<Guid> RemoteCheckedVersions { get; set; }
            public HashSet<Guid> RemoteCheckedBranches { get; set; }
            public List<Objects.Branch> ReceivedBranches { get; set; }
            public List<VersionInfo> PushedVersions { get; set; }
            public Dictionary<long, Objects.Record> RemoteRecordMap { get; set; }
            public Dictionary<long, Objects.Record> LocalRecordMap { get; set; }
            public List<long> UnknownRecords { get; set; }
            public HashSet<long> UnknownRecordSet { get; set; }

            public IntPtr LZHLCompressor { get; set; }
            public IntPtr LZHLDecompressor { get; set; }

            public SharedNetworkInfo()
            {
                RemoteCheckedVersions = new HashSet<Guid>();
                RemoteCheckedBranches = new HashSet<Guid>();
                ReceivedBranches = new List<Branch>();
                PushedVersions = new List<VersionInfo>();
                UnknownRecords = new List<long>();
                UnknownRecordSet = new HashSet<long>();
                RemoteRecordMap = new Dictionary<long, Record>();
                LocalRecordMap = new Dictionary<long, Record>();
                LZHLCompressor = Versionr.Utilities.LZHL.CreateCompressor();
                LZHLDecompressor = Versionr.Utilities.LZHL.CreateDecompressor();
            }

            #region IDisposable Support
            private bool m_DisposedValue = false; // To detect redundant calls

            protected virtual void Dispose(bool disposing)
            {
                if (!m_DisposedValue)
                {
                    Versionr.Utilities.LZHL.DestroyCompressor(LZHLCompressor);
                    Versionr.Utilities.LZHL.DestroyDecompressor(LZHLDecompressor);

                    m_DisposedValue = true;
                }
            }
            
            ~SharedNetworkInfo()
            {
              Dispose(false);
            }
            
            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
            #endregion
        }

        internal static bool GetVersionList(SharedNetworkInfo info, Objects.Version version, out Stack<Branch> branchesToSend, out Stack<Objects.Version> versionsToSend)
        {
            branchesToSend = new Stack<Branch>();
            versionsToSend = new Stack<Objects.Version>();
            return GetVersionListInternal(info, version, branchesToSend, versionsToSend);
        }
        static bool GetVersionListInternal(SharedNetworkInfo info, Objects.Version version, Stack<Branch> branchesToSend, Stack<Objects.Version> versionsToSend)
        {
            try
            {
                if (info.RemoteCheckedVersions.Contains(version.ID))
                    return true;
                Printer.PrintDiagnostics("Sending remote vault local version information.");
                Objects.Version currentVersion = version;
                while (true)
                {
                    Objects.Version testedVersion = currentVersion;
                    List<Objects.Version> versionsToCheck = new List<Objects.Version>();
                    List<Objects.Version> partialHistory = info.Workspace.GetHistory(currentVersion, 64);
                    foreach (var x in partialHistory)
                    {
                        if (info.RemoteCheckedVersions.Contains(x.ID))
                            break;
                        info.RemoteCheckedVersions.Add(x.ID);
                        currentVersion = x;
                        versionsToCheck.Add(x);
                    }

                    if (versionsToCheck.Count > 0)
                    {
                        PushObjectQuery query = new PushObjectQuery();
                        query.Type = ObjectType.Version;
                        query.IDs = versionsToCheck.Select(x => x.ID.ToString()).ToArray();

                        ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(info.Stream, new NetCommand() { Type = NetCommandType.PushObjectQuery }, ProtoBuf.PrefixStyle.Fixed32);
                        Utilities.SendEncrypted<PushObjectQuery>(info, query);

                        PushObjectResponse response = Utilities.ReceiveEncrypted<PushObjectResponse>(info);
                        if (response.Recognized.Length != query.IDs.Length)
                            throw new Exception("Invalid response!");
                        int recognized = 0;
                        for (int i = 0; i < response.Recognized.Length; i++)
                        {
                            Printer.PrintDiagnostics(" - Version ID: {0}", query.IDs[i]);
                            if (!response.Recognized[i])
                            {
                                versionsToSend.Push(partialHistory[i]);
                                Printer.PrintDiagnostics("   (not recognized on remote vault)");
                            }
                            else
                            {
                                recognized++;
                                Printer.PrintDiagnostics("   (version already on remote vault)");
                            }
                        }
                        for (int i = 0; i < response.Recognized.Length; i++)
                        {
                            if (!response.Recognized[i])
                            {
                                if (!QueryBranch(info, branchesToSend, info.Workspace.GetBranch(partialHistory[i].Branch)))
                                    return false;
                                var mergeInfo = info.Workspace.GetMergeInfo(partialHistory[i].ID);
                                foreach (var x in mergeInfo)
                                {
                                    var srcVersion = info.Workspace.GetVersion(x.SourceVersion);
                                    if (srcVersion != null)
                                    {
                                        if (!GetVersionListInternal(info, srcVersion, branchesToSend, versionsToSend))
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
                    else if (testedVersion == currentVersion)
                        break;
                }
                return true;
            }
            catch (Exception e)
            {
                Printer.PrintError("Error: {0}", e);
                return false;
            }
        }

        private static VersionPack CreatePack(SharedNetworkInfo sharedInfo, List<Objects.Version> versionData)
        {
            VersionPack pack = new VersionPack();
            pack.Versions = versionData.Select(x => CreateVersionInfo(sharedInfo, x)).ToArray();
            return pack;
        }

        private static VersionInfo CreateVersionInfo(SharedNetworkInfo sharedInfo, Objects.Version x)
        {
            VersionInfo info = new VersionInfo();
            info.Version = x;
            info.MergeInfos = sharedInfo.Workspace.GetMergeInfo(x.ID).ToArray();
            info.Alterations = sharedInfo.Workspace.GetAlterations(x).Select(y => CreateFusedAlteration(sharedInfo, y)).ToArray();
            return info;
        }

        private static FusedAlteration CreateFusedAlteration(SharedNetworkInfo sharedInfo, Alteration y)
        {
            FusedAlteration alteration = new FusedAlteration();
            alteration.Alteration = y.Type;
            alteration.NewRecord = y.NewRecord.HasValue ? sharedInfo.Workspace.GetRecord(y.NewRecord.Value) : null;
            alteration.PriorRecord = y.PriorRecord.HasValue ? sharedInfo.Workspace.GetRecord(y.PriorRecord.Value) : null;
            return alteration;
        }
        
        internal static bool SendVersions(SharedNetworkInfo sharedInfo, Stack<Objects.Version> versionsToSend)
        {
            try
            {
                if (versionsToSend.Count == 0)
                {
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.SynchronizeRecords }, ProtoBuf.PrefixStyle.Fixed32);
                    var dataResponse = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, ProtoBuf.PrefixStyle.Fixed32);
                    if (dataResponse.Type == NetCommandType.Synchronized)
                        return true;
                    return false;
                }
                Printer.PrintDiagnostics("Synchronizing {0} versions to server.", versionsToSend.Count);
                while (versionsToSend.Count > 0)
                {
                    List<Objects.Version> versionData = new List<Objects.Version>();
                    while (versionData.Count < 512 && versionsToSend.Count > 0)
                    {
                        versionData.Add(versionsToSend.Pop());
                    }
                    Printer.PrintDiagnostics("Sending version data pack...");
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.PushVersions }, ProtoBuf.PrefixStyle.Fixed32);
                    VersionPack pack = CreatePack(sharedInfo, versionData);
                    Utilities.SendEncrypted(sharedInfo, pack);
                    NetCommand response = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, ProtoBuf.PrefixStyle.Fixed32);
                    if (response.Type != NetCommandType.Acknowledge)
                        return false;
                }
                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.SynchronizeRecords }, ProtoBuf.PrefixStyle.Fixed32);
                while (true)
                {
                    var command = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, ProtoBuf.PrefixStyle.Fixed32);
                    if (command.Type == NetCommandType.RequestRecordParents)
                    {
                        Printer.PrintDiagnostics("Remote vault is asking for record metadata...");
                        var rrp = Utilities.ReceiveEncrypted<RequestRecordParents>(sharedInfo);
                        RecordParentPack rp = new RecordParentPack();
                        rp.Parents = rrp.RecordParents.Select(x => sharedInfo.Workspace.GetRecord(x)).ToArray();
                        Utilities.SendEncrypted<RecordParentPack>(sharedInfo, rp);
                    }
                    else if (command.Type == NetCommandType.RequestRecord)
                    {
                        var rrd = Utilities.ReceiveEncrypted<RequestRecordData>(sharedInfo);
                        List<byte> datablock = new List<byte>();
                        Func<IEnumerable<byte>, bool, bool> sender = (IEnumerable<byte> data, bool flush) =>
                        {
                            datablock.AddRange(data);
                            int blockSize = 1024 * 1024;
                            while (datablock.Count > blockSize)
                            {
                                DataPayload dataPack = new DataPayload() { Data = datablock.Take(blockSize).ToArray(), EndOfStream = false };
                                Utilities.SendEncrypted<DataPayload>(sharedInfo, dataPack);
                                datablock.RemoveRange(0, blockSize);
                                var reply = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, ProtoBuf.PrefixStyle.Fixed32);
                                if (reply.Type != NetCommandType.DataReceived)
                                    return false;
                            }
                            if (flush)
                            {
                                DataPayload dataPack = new DataPayload() { Data = datablock.ToArray(), EndOfStream = true };
                                Utilities.SendEncrypted<DataPayload>(sharedInfo, dataPack);
                            }
                            return true;
                        };
                        foreach (var x in rrd.Records)
                        {
                            var record = sharedInfo.Workspace.GetRecord(x);
                            Printer.PrintDiagnostics("Sending data for: {0}", record.CanonicalName);
                            sender(BitConverter.GetBytes(x), false);
                            if (!sharedInfo.Workspace.TransmitRecordData(record, sender))
                                return false;
                        }
                        if (!sender(new byte[0], true))
                            return false;

                        var dataResponse = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, ProtoBuf.PrefixStyle.Fixed32);
                        if (dataResponse.Type != NetCommandType.Acknowledge)
                            return false;
                    }
                    else if (command.Type == NetCommandType.Synchronized)
                    {
                        return true;
                    }
                    else
                    {
                        throw new Exception(string.Format("Invalid command: {0}", command.Type));
                    }
                }
            }
            catch (Exception e)
            {
                Printer.PrintError("Error: {0}", e);
                return false;
            }
        }

        internal static bool ImportRecords(SharedNetworkInfo sharedInfo)
        {
            lock (sharedInfo.Workspace)
            {
                try
                {
                    sharedInfo.Workspace.BeginDatabaseTransaction();
                    foreach (var x in sharedInfo.UnknownRecords.Select(x => x).Reverse())
                    {
                        Record rec = sharedInfo.RemoteRecordMap[x];
                        if (rec.Parent.HasValue)
                        {
                            rec.Parent = sharedInfo.LocalRecordMap[rec.Parent.Value].Id;
                        }
                        sharedInfo.Workspace.ImportRecordNoCommit(rec);
                        sharedInfo.LocalRecordMap[x] = rec;
                    }
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

        internal static void RequestRecordData(SharedNetworkInfo sharedInfo)
        {
            var records = sharedInfo.UnknownRecords;
            int index = 0;
            while (index < records.Count)
            {
                RequestRecordData rrd = new RequestRecordData();
                List<long> recordsInPack = new List<long>();
                HashSet<string> recordDataIdentifiers = new HashSet<string>();
                while (recordsInPack.Count < 1024 * 32 && index < records.Count)
                {
                    long recordIndex = records[index++];
                    var rec = sharedInfo.RemoteRecordMap[recordIndex];
                    if (rec.IsDirectory)
                        continue;
                    if (sharedInfo.Workspace.HasObjectData(rec))
                        continue;
                    if (recordDataIdentifiers.Contains(rec.DataIdentifier))
                        continue;
                    recordDataIdentifiers.Add(rec.DataIdentifier);
                    recordsInPack.Add(recordIndex);
                }
                if (recordsInPack.Count > 0)
                {
                    Printer.PrintDiagnostics("Requesting {0} records.", recordsInPack.Count);
                    rrd.Records = recordsInPack.ToArray();
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.RequestRecord }, ProtoBuf.PrefixStyle.Fixed32);
                    Utilities.SendEncrypted<RequestRecordData>(sharedInfo, rrd);

                    var receiverStream = new Versionr.Utilities.ChunkedReceiverStream(() =>
                    {
                        var pack = Utilities.ReceiveEncrypted<DataPayload>(sharedInfo);
                        if (pack.EndOfStream)
                        {
                            return new Tuple<byte[], bool>(pack.Data, true);
                        }
                        else
                        {
                            ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.DataReceived }, ProtoBuf.PrefixStyle.Fixed32);
                            return new Tuple<byte[], bool>(pack.Data, false);
                        }
                    });

                    while (!receiverStream.EndOfStream)
                    {
                        byte[] blob = new byte[16];
                        long recordIndex;
                        long recordSize;
                        if (receiverStream.Read(blob, 0, 8) != 8)
                            continue;
                        receiverStream.Read(blob, 8, 8);
                        recordIndex = BitConverter.ToInt64(blob, 0);
                        recordSize = BitConverter.ToInt64(blob, 8);
                        Printer.PrintDiagnostics("Unpacking remote record {0}, payload size: {1}", recordIndex, recordSize);
                        var rec = sharedInfo.RemoteRecordMap[recordIndex];

                        sharedInfo.Workspace.ImportRecordData(rec, new Versionr.Utilities.RestrictedStream(receiverStream, recordSize));
                    }
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
                }
            }
        }

        internal static void RequestRecordMetadata(SharedNetworkInfo sharedInfo)
        {
            int index = 0;
            while (true)
            {
                if (index == sharedInfo.UnknownRecords.Count)
                    break;
                List<long> requests = new List<long>();
                while (index < sharedInfo.UnknownRecords.Count)
                {
                    if (requests.Count == 512)
                        break;
                    requests.Add(sharedInfo.UnknownRecords[index]);
                    index++;
                }
                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.RequestRecordParents }, ProtoBuf.PrefixStyle.Fixed32);
                RequestRecordParents rrp = new RequestRecordParents() { RecordParents = requests.ToArray() };
                Utilities.SendEncrypted<RequestRecordParents>(sharedInfo, rrp);

                var response = Utilities.ReceiveEncrypted<RecordParentPack>(sharedInfo);
                ReceiveRecordParents(sharedInfo, response);
            }
        }

        internal static List<Record> RequestRecordDataUnmapped(SharedNetworkInfo sharedInfo, List<Record> missingRecords)
        {
            HashSet<string> successes = new HashSet<string>();
            int index = 0;
            List<Record> returnedRecords = new List<Record>();
            Dictionary<string, List<Record>> oneToManyMapping = new Dictionary<string, List<Record>>();
            while (index < missingRecords.Count)
            {
                RequestRecordDataUnmapped rrd = new RequestRecordDataUnmapped();
                List<Record> recordsInPack = new List<Record>();
                HashSet<string> recordDataIdentifiers = new HashSet<string>();
                while (recordsInPack.Count < 1024 * 32 && index < missingRecords.Count)
                {
                    int recordIndex = index;
                    Record rec = missingRecords[index++];
                    if (rec.IsDirectory)
                        continue;
                    if (recordDataIdentifiers.Contains(rec.DataIdentifier))
                    {
                        List<Record> multirecData = null;
                        if (!oneToManyMapping.TryGetValue(rec.DataIdentifier, out multirecData))
                        {
                            multirecData = new List<Record>();
                            oneToManyMapping[rec.DataIdentifier] = multirecData;
                        }
                        multirecData.Add(rec);
                        continue;
                    }
                    recordDataIdentifiers.Add(rec.DataIdentifier);
                    recordsInPack.Add(rec);
                }
                if (recordsInPack.Count > 0)
                {
                    rrd.RecordDataKeys = recordsInPack.Select(x => x.DataIdentifier).ToArray();
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.RequestRecordUnmapped }, ProtoBuf.PrefixStyle.Fixed32);
                    Utilities.SendEncrypted<RequestRecordDataUnmapped>(sharedInfo, rrd);

                    var receiverStream = new Versionr.Utilities.ChunkedReceiverStream(() =>
                    {
                        var pack = Utilities.ReceiveEncrypted<DataPayload>(sharedInfo);
                        if (pack.EndOfStream)
                        {
                            return new Tuple<byte[], bool>(pack.Data, true);
                        }
                        else
                        {
                            ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.DataReceived }, ProtoBuf.PrefixStyle.Fixed32);
                            return new Tuple<byte[], bool>(pack.Data, false);
                        }
                    });

                    while (!receiverStream.EndOfStream)
                    {
                        byte[] blob = new byte[8];
                        int successFlag;
                        int requestIndex;
                        long recordSize;
                        if (receiverStream.Read(blob, 0, 8) != 8)
                            continue;
                        successFlag = BitConverter.ToInt32(blob, 0);
                        requestIndex = BitConverter.ToInt32(blob, 4);

                        Objects.Record rec = recordsInPack[requestIndex];
                        if (successFlag == 0)
                        {
                            Printer.PrintDiagnostics("Record {0} not located on remote.", rec.DataIdentifier);
                            continue;
                        }

                        receiverStream.Read(blob, 0, 8);
                        recordSize = BitConverter.ToInt64(blob, 0);
                        Printer.PrintDiagnostics("Unpacking record {0}, payload size: {1}", rec.DataIdentifier, recordSize);

                        returnedRecords.Add(rec);
                        List<Record> multireturns = null;
                        if (oneToManyMapping.TryGetValue(rec.DataIdentifier, out multireturns))
                            returnedRecords.AddRange(multireturns);

                        sharedInfo.Workspace.ImportRecordData(rec, new Versionr.Utilities.RestrictedStream(receiverStream, recordSize));
                    }
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
                }
            }
            return returnedRecords;
        }

        internal static bool SendRecordDataUnmapped(SharedNetworkInfo sharedInfo)
        {
            var rrd = Utilities.ReceiveEncrypted<RequestRecordDataUnmapped>(sharedInfo);
            List<byte> datablock = new List<byte>();
            Func<IEnumerable<byte>, bool, bool> sender = (IEnumerable<byte> data, bool flush) =>
            {
                datablock.AddRange(data);
                int blockSize = 1024 * 1024;
                while (datablock.Count > blockSize)
                {
                    DataPayload dataPack = new DataPayload() { Data = datablock.Take(blockSize).ToArray(), EndOfStream = false };
                    Utilities.SendEncrypted<DataPayload>(sharedInfo, dataPack);
                    datablock.RemoveRange(0, blockSize);
                    var reply = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, ProtoBuf.PrefixStyle.Fixed32);
                    if (reply.Type != NetCommandType.DataReceived)
                        return false;
                }
                if (flush)
                {
                    DataPayload dataPack = new DataPayload() { Data = datablock.ToArray(), EndOfStream = true };
                    Utilities.SendEncrypted<DataPayload>(sharedInfo, dataPack);
                }
                return true;
            };
            int index = 0;
            foreach (var x in rrd.RecordDataKeys)
            {
                sender(BitConverter.GetBytes(index), false);
                index++;
                Objects.Record record = sharedInfo.Workspace.GetRecordFromIdentifier(x);
                if (record == null)
                {
                    int failure = 0;
                    sender(BitConverter.GetBytes(failure), false);
                }
                else
                {
                    int success = 1;
                    sender(BitConverter.GetBytes(success), false);
                    Printer.PrintDiagnostics("Sending data for: {0}", record.Fingerprint);

                    if (!sharedInfo.Workspace.TransmitRecordData(record, sender))
                        return false;
                }
            }
            if (!sender(new byte[0], true))
                return false;

            var dataResponse = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, ProtoBuf.PrefixStyle.Fixed32);
            if (dataResponse.Type != NetCommandType.Acknowledge)
                return false;

            return true;
        }

        private static void ReceiveRecordParents(SharedNetwork.SharedNetworkInfo sharedInfo, RecordParentPack response)
        {
            foreach (var x in response.Parents)
            {
                CheckRecord(sharedInfo, x);
            }
        }

        internal static void ReceiveVersions(SharedNetworkInfo sharedInfo)
        {
            VersionPack pack = Utilities.ReceiveEncrypted<VersionPack>(sharedInfo);
            ReceivePack(sharedInfo, pack);
            ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
        }

        private static void ReceivePack(SharedNetworkInfo sharedInfo, VersionPack pack)
        {
            foreach (var x in pack.Versions)
            {
                sharedInfo.PushedVersions.Add(x);
                CheckRecords(sharedInfo, x);
            }
        }

        private static void CheckRecords(SharedNetworkInfo sharedInfo, VersionInfo info)
        {
            if (info.Alterations != null)
            {
                foreach (var x in info.Alterations)
                {
                    CheckRecord(sharedInfo, x.NewRecord);
                    CheckRecord(sharedInfo, x.PriorRecord);
                }
            }
        }

        public static void CheckRecord(SharedNetworkInfo sharedInfo, Record record)
        {
            if (record == null)
                return;
            sharedInfo.RemoteRecordMap[record.Id] = record;
            if (!sharedInfo.UnknownRecordSet.Contains(record.Id))
            {
                var localRecord = sharedInfo.Workspace.LocateRecord(record);
                if (localRecord == null)
                {
                    Printer.PrintDiagnostics("Received version dependent on missing record {0}", record.UniqueIdentifier);
                    sharedInfo.UnknownRecords.Add(record.Id);
                    sharedInfo.UnknownRecordSet.Add(record.Id);
                }
                else
                {
                    localRecord.CanonicalName = record.CanonicalName;
                    sharedInfo.LocalRecordMap[record.Id] = localRecord;
                }
            }
        }

        private static bool QueryBranch(SharedNetworkInfo info, Stack<Branch> branchesToSend, Branch branch)
        {
            try
            {
                if (info.RemoteCheckedBranches.Contains(branch.ID))
                    return true;
                Printer.PrintDiagnostics("Sending remote vault local branch information.");
                Branch currentBranch = branch;
                while (true)
                {
                    int branchesPerBlock = 16;
                    List<Objects.Branch> branchIDs = new List<Objects.Branch>();
                    while (branchesPerBlock > 0)
                    {
                        if (info.RemoteCheckedBranches.Contains(currentBranch.ID))
                            break;
                        info.RemoteCheckedBranches.Add(currentBranch.ID);
                        branchesPerBlock--;
                        branchIDs.Add(currentBranch);
                        if (currentBranch.Parent.HasValue)
                            currentBranch = info.Workspace.GetBranch(currentBranch.Parent.Value);
                        else
                        {
                            currentBranch = null;
                            break;
                        }
                    }

                    if (branchIDs.Count > 0)
                    {
                        PushObjectQuery query = new PushObjectQuery();
                        query.Type = ObjectType.Branch;
                        query.IDs = branchIDs.Select(x => x.ID.ToString()).ToArray();

                        ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(info.Stream, new NetCommand() { Type = NetCommandType.PushObjectQuery }, ProtoBuf.PrefixStyle.Fixed32);
                        Utilities.SendEncrypted<PushObjectQuery>(info, query);

                        PushObjectResponse response = Utilities.ReceiveEncrypted<PushObjectResponse>(info);
                        if (response.Recognized.Length != query.IDs.Length)
                            throw new Exception("Invalid response!");
                        int recognized = 0;
                        for (int i = 0; i < response.Recognized.Length; i++)
                        {
                            Printer.PrintDiagnostics(" - Branch ID: {0}", query.IDs[i]);
                            if (!response.Recognized[i])
                            {
                                branchesToSend.Push(branchIDs[i]);
                                Printer.PrintDiagnostics("   (not recognized on remote vault)");
                            }
                            else
                            {
                                info.RemoteCheckedBranches.Add(branchIDs[i].ID);
                                recognized++;
                                Printer.PrintDiagnostics("   (branch already on remote vault)");
                            }
                        }
                        if (recognized != 0) // we found a common parent somewhere
                            break;
                    }
                    else if (info.RemoteCheckedBranches.Count > 0)
                        break;
                }
                return true;
            }
            catch (Exception e)
            {
                Printer.PrintError("Error: {0}", e);
                return false;
            }
        }

        internal static void ReceiveBranches(SharedNetworkInfo sharedInfo)
        {
            PushBranches branches = Utilities.ReceiveEncrypted<PushBranches>(sharedInfo);
            ReceiveBranchesInternal(sharedInfo, branches);
            ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
        }

        private static void ReceiveBranchesInternal(SharedNetworkInfo sharedInfo, PushBranches branches)
        {
            sharedInfo.ReceivedBranches.AddRange(branches.Branches);
            Printer.PrintDiagnostics("Received branches:");
            foreach (var x in branches.Branches)
            {
                Printer.PrintDiagnostics(" - {0}: \"{1}\"", x.ID, x.Name);
                sharedInfo.Workspace.ImportBranch(x);
            }
            Printer.PrintDiagnostics("Branches imported.");
        }

        internal static bool SendBranches(SharedNetworkInfo sharedInfo, Stack<Branch> branchesToSend)
        {
            try
            {
                if (branchesToSend.Count == 0)
                    return true;
                Printer.PrintDiagnostics("Synchronizing {0} branches to remote.", branchesToSend.Count);
                while (branchesToSend.Count > 0)
                {
                    List<Objects.Branch> branchData = new List<Branch>();
                    while (branchData.Count < 512 && branchesToSend.Count > 0)
                    {
                        branchData.Add(branchesToSend.Pop());
                    }
                    Printer.PrintDiagnostics("Sending branch data pack...");
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.PushBranch }, ProtoBuf.PrefixStyle.Fixed32);
                    PushBranches pb = new PushBranches() { Branches = branchData.ToArray() };
                    Utilities.SendEncrypted(sharedInfo, pb);
                    NetCommand response = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, ProtoBuf.PrefixStyle.Fixed32);
                    if (response.Type != NetCommandType.Acknowledge)
                        return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Printer.PrintError("Error: {0}", e);
                return false;
            }
        }

        internal static void ProcesPushObjectQuery(SharedNetworkInfo sharedInfo)
        {
            PushObjectQuery query = Utilities.ReceiveEncrypted<PushObjectQuery>(sharedInfo);
            PushObjectResponse response = ProcessPushObjectQuery(sharedInfo, query);
            Utilities.SendEncrypted<PushObjectResponse>(sharedInfo, response);
        }

        private static PushObjectResponse ProcessPushObjectQuery(SharedNetworkInfo sharedInfo, PushObjectQuery query)
        {
            PushObjectResponse response = new PushObjectResponse() { Recognized = new bool[query.IDs == null ? 0 : query.IDs.Length] };
            if (query.IDs != null)
            {
                if (query.Type == ObjectType.Branch)
                {
                    for (int i = 0; i < query.IDs.Length; i++)
                    {
                        if (sharedInfo.Workspace.GetBranch(new Guid(query.IDs[i])) != null)
                            response.Recognized[i] = true;
                        else
                            response.Recognized[i] = false;
                    }
                }
                else if (query.Type == ObjectType.Version)
                {
                    for (int i = 0; i < query.IDs.Length; i++)
                    {
                        if (sharedInfo.Workspace.GetVersion(new Guid(query.IDs[i])) != null)
                            response.Recognized[i] = true;
                        else
                            response.Recognized[i] = false;
                    }
                }
                else
                    throw new Exception("Unrecognized object type for push object query.");
            }
            return response;
        }
    }
}
