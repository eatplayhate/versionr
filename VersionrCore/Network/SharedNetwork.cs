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
        public enum Protocol
        {
            Versionr281,
            Versionr29,
            Versionr3,
            Versionr31,
            Versionr32
        }
        public static bool SupportsAuthentication(Protocol protocol)
        {
            if (protocol == Protocol.Versionr281 || protocol == Protocol.Versionr29)
                return false;
            return true;
        }
        public static Protocol[] AllowedProtocols = new Protocol[] { Protocol.Versionr32, Protocol.Versionr31 };
        public static Protocol DefaultProtocol
        {
            get
            {
                return AllowedProtocols[0];
            }
        }

        public static Tuple<string, string> ComponentVersionInfo
        {
            get
            {
                return new Tuple<string, string>("Network Library", Network.Handshake.GetProtocolString(SharedNetwork.DefaultProtocol));
            }
        }

        internal class SharedNetworkInfo : IDisposable
        {
            public Utilities.ChecksumCodec ChecksumType { get; set; }
            public Protocol CommunicationProtocol { get; set; }
            public bool Client { get; set; }
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
            public HashSet<Guid> RemoteCheckedBranchJournal { get; set; }
            public List<Objects.Branch> ReceivedBranches { get; set; }
            public List<VersionInfo> PushedVersions { get; set; }
            public HashSet<Guid> ReceivedVersionSet { get; set; }
            public Dictionary<long, Objects.Record> RemoteRecordMap { get; set; }
            public Dictionary<long, Objects.Record> LocalRecordMap { get; set; }
            public List<long> UnknownRecords { get; set; }
            public HashSet<long> UnknownRecordSet { get; set; }
            public List<BranchJournalPack> ReceivedBranchJournals { get; set; }

            public IntPtr LZHLCompressor { get; set; }
            public IntPtr LZHLDecompressor { get; set; }

            public SharedNetworkInfo()
            {
                RemoteCheckedVersions = new HashSet<Guid>();
                RemoteCheckedBranches = new HashSet<Guid>();
                ReceivedBranches = new List<Branch>();
                RemoteCheckedBranchJournal = new HashSet<Guid>();
                PushedVersions = new List<VersionInfo>();
                UnknownRecords = new List<long>();
                UnknownRecordSet = new HashSet<long>();
                RemoteRecordMap = new Dictionary<long, Record>();
                LocalRecordMap = new Dictionary<long, Record>();
                ReceivedBranchJournals = new List<BranchJournalPack>();
                LZHLCompressor = Versionr.Utilities.LZHL.CreateCompressor();
                LZHLDecompressor = Versionr.Utilities.LZHL.CreateDecompressor();
                ChecksumType = Utilities.ChecksumCodec.Default;
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
                        if (!info.RemoteCheckedVersions.Contains(x.ID))
						{
							info.RemoteCheckedVersions.Add(x.ID);
							currentVersion = x;
							versionsToCheck.Add(x);
						}
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
                                versionsToSend.Push(versionsToCheck[i]);
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
                                if (!QueryBranch(info, branchesToSend, info.Workspace.GetBranch(versionsToCheck[i].Branch)))
                                    return false;
                                var mergeInfo = info.Workspace.GetMergeInfo(versionsToCheck[i].ID);
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

        internal static void ReceiveBranchJournal(SharedNetworkInfo sharedInfo)
        {
            var pack = Utilities.ReceiveEncrypted<BranchJournalPack>(sharedInfo);
            if (sharedInfo.Workspace.HasBranchJournal(pack.Payload.ID))
            {
                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
            }
            else
            {
                sharedInfo.ReceivedBranchJournals.Add(pack);
                ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.DataReceived }, ProtoBuf.PrefixStyle.Fixed32);
            }
        }

        internal static bool SendBranchJournal(SharedNetworkInfo info)
        {
            if (info.CommunicationProtocol < Protocol.Versionr29)
                return true;
            Objects.BranchJournal tip = info.Workspace.GetBranchJournalTip();
            if (tip == null)
                return true;
            try
            {
                Stack<Objects.BranchJournal> openList = new Stack<Objects.BranchJournal>();
                openList.Push(tip);
                while (openList.Count > 0)
                {
                    Objects.BranchJournal journal = openList.Pop();
                    if (info.RemoteCheckedBranchJournal.Contains(journal.ID))
                        continue;

                    List<Objects.BranchJournal> parents = info.Workspace.GetBranchJournalParents(journal);

                    BranchJournalPack pack = new BranchJournalPack()
                    {
                        Payload = journal,
                        Parents = parents.Select(x => x.ID).ToList()
                    };

                    info.RemoteCheckedBranchJournal.Add(journal.ID);

                    Utilities.SendEncryptedPrefixed(new NetCommand() { Type = NetCommandType.PushBranchJournal }, info, pack);

                    NetCommand response = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(info.Stream, ProtoBuf.PrefixStyle.Fixed32);
                    if (response.Type == NetCommandType.Acknowledge)
                        continue;

                    foreach (var x in parents)
                        openList.Push(x);
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

        class SendStats
        {
            public long BytesSent;
        }
        
        internal static bool SendVersions(SharedNetworkInfo sharedInfo, Stack<Objects.Version> versionsToSend)
        {
            try
            {
                int ackCount = 0;
                byte[] tempBuffer = new byte[16 * 1024 * 1024];
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
                    ackCount++;
                }
                while (ackCount-- > 0)
                {
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
                        Printer.InteractivePrinter printer = null;
                        SendStats sstats = null;
                        System.Diagnostics.Stopwatch sw = null;
                        if (sharedInfo.Client)
                        {
                            sstats = new SendStats();
                            sw = new System.Diagnostics.Stopwatch();
                            Printer.PrintMessage("Remote has requested #b#{0}## records...", rrd.Records.Length);
                            printer = Printer.CreateProgressBarPrinter("Sending data", string.Empty,
                                    (obj) =>
                                    {
                                        return string.Format("{0}/sec", Versionr.Utilities.Misc.FormatSizeFriendly((long)(sstats.BytesSent / sw.Elapsed.TotalSeconds)));
                                    },
                                    (obj) =>
                                    {
                                        return (100.0f * (int)obj) / (float)rrd.Records.Length;
                                    },
                                    (pct, obj) =>
                                    {
                                        return string.Format("{0}/{1}", (int)obj, rrd.Records.Length);
                                    },
                                    60);
                            sw.Start();
                        }
                        Func<byte[], int, bool, bool> sender = GetSender(sharedInfo, sstats);
                        int processed = 0;
                        foreach (var x in rrd.Records)
                        {
                            var record = sharedInfo.Workspace.GetRecord(x);
                            Printer.PrintDiagnostics("Sending data for: {0}", record.CanonicalName);
                            if (!sharedInfo.Workspace.TransmitRecordData(record, sender, tempBuffer, () =>
                            {
                                sender(BitConverter.GetBytes(x), 8, false);
                            }))
                            {
                                sender(BitConverter.GetBytes(-x), 8, false);
                            }
                            if (printer != null)
                                printer.Update(processed++);
                        }
                        if (!sender(new byte[0], 0, true))
                            return false;

                        if (printer != null)
                            printer.End(rrd.Records.Length);

                        var dataResponse = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, ProtoBuf.PrefixStyle.Fixed32);
                        if (dataResponse.Type != NetCommandType.Acknowledge)
                            return false;
                    }
                    else if (command.Type == NetCommandType.RequestRecordUnmapped)
                    {
                        SharedNetwork.SendRecordDataUnmapped(sharedInfo);
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

        internal static bool ImportBranchJournal(SharedNetworkInfo sharedInfo, bool interactive)
        {
            return sharedInfo.Workspace.ImportBranchJournal(sharedInfo, interactive);
        }

        internal static bool IsAncestor(Guid ancestor, Guid possibleChild, SharedNetwork.SharedNetworkInfo clientInfo)
        {
            HashSet<Guid> checkedVersions = new HashSet<Guid>();
            return IsAncestorInternal(checkedVersions, ancestor, possibleChild, clientInfo);
        }

        internal static bool IsAncestorInternal(HashSet<Guid> checkedVersions, Guid ancestor, Guid possibleChild, SharedNetwork.SharedNetworkInfo clientInfo)
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

        internal static Objects.Version FindLocalOrRemoteVersionInfo(Guid possibleChild, SharedNetwork.SharedNetworkInfo clientInfo, out List<MergeInfo> mergeInfo)
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

        private static Func<byte[], int, bool, bool> GetSender(SharedNetworkInfo sharedInfo, SendStats stats = null)
        {
            List<byte> datablock = new List<byte>();
            byte[] tempBuffer = new byte[1024 * 1024];
            return (byte[] data, int size, bool flush) =>
            {
                int totalSize = datablock.Count + size;
                int blockSize = 1024 * 1024;
                int remainder = size;
                int offset = 0;
                while (totalSize > blockSize)
                {
                    datablock.CopyTo(0, tempBuffer, 0, datablock.Count);
                    int nextBlockSize = blockSize - datablock.Count;
                    if (nextBlockSize > remainder)
                        nextBlockSize = remainder;
                    Array.Copy(data, offset, tempBuffer, datablock.Count, nextBlockSize);
                    offset += nextBlockSize;
                    datablock.Clear();
                    totalSize -= blockSize;
                    remainder -= nextBlockSize;
                    DataPayload dataPack = new DataPayload() { Data = tempBuffer, EndOfStream = false };
                    Utilities.SendEncrypted<DataPayload>(sharedInfo, dataPack);
                    var reply = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, ProtoBuf.PrefixStyle.Fixed32);
                    if (reply.Type != NetCommandType.DataReceived)
                        return false;
                    if (stats != null)
                    {
                        stats.BytesSent += dataPack.Data.Length;
                    }
                }
                if (remainder > 0)
                {
                    for (int i = offset; i < size; i++)
                        datablock.Add(data[i]);
                }
                if (flush)
                {
                    DataPayload dataPack = new DataPayload() { Data = datablock.ToArray(), EndOfStream = true };
                    Utilities.SendEncrypted<DataPayload>(sharedInfo, dataPack);
                    var reply = ProtoBuf.Serializer.DeserializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, ProtoBuf.PrefixStyle.Fixed32);
                    if (reply.Type != NetCommandType.DataReceived)
                        return false;
                    if (stats != null)
                    {
                        stats.BytesSent += dataPack.Data.Length;
                    }
                }
                return true;
            };
        }

        internal static bool ImportRecords(SharedNetworkInfo sharedInfo, bool enablePrinter = false)
        {
            if (sharedInfo.UnknownRecords.Count == 0)
                return true;
            lock (sharedInfo.Workspace)
            {
                try
                {
                    sharedInfo.Workspace.BeginDatabaseTransaction();
                    Printer.InteractivePrinter printer = null;
                    System.Diagnostics.Stopwatch sw = null;
                    long nextUpdate = 400;
                    if (enablePrinter)
                    {
                        sw = new System.Diagnostics.Stopwatch();
                        sw.Start();
                        printer = Printer.CreateSpinnerBarPrinter(string.Format("Importing metadata for {0} objects", sharedInfo.UnknownRecords.Count), string.Empty, (object obj) => { return string.Empty; }, 60);
                    }
                    List<long> importList = new List<long>();
                    importList.AddRange(sharedInfo.UnknownRecords.Select(x => x).Reverse());
                    while (importList.Count > 0)
                    {
                        List<long> delayed = new List<long>();
                        foreach (var x in importList)
                        {
                            Record rec = sharedInfo.RemoteRecordMap[x];
                            if (rec.Parent.HasValue)
                            {
                                Record parent;
                                if (sharedInfo.LocalRecordMap.TryGetValue(rec.Parent.Value, out parent))
                                    rec.Parent = parent.Id;
                                else
                                {
                                    delayed.Add(x);
                                    continue;
                                }
                            }
                            rec = ProtoBuf.Serializer.DeepClone(rec);
                            sharedInfo.Workspace.ImportRecordNoCommit(rec, true);
                            sharedInfo.LocalRecordMap[x] = rec;
                            if (printer != null)
                            {
                                if (sw.ElapsedMilliseconds > nextUpdate)
                                {
                                    nextUpdate = sw.ElapsedMilliseconds + 400;
                                    printer.Update(null);
                                }
                            }
                        }
                        importList.Clear();
                        importList.AddRange(delayed);
                    }
                    if (printer != null)
                        printer.End(null);
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
            List<string> dependentData = new List<string>();
            var records = sharedInfo.UnknownRecords;
            int index = 0;
            HashSet<string> recordDataIdentifiers = new HashSet<string>();
            while (index < records.Count)
            {
                RequestRecordData rrd = new RequestRecordData();
                List<long> recordsInPack = new List<long>();
                while (recordsInPack.Count < 1024 * 32 && index < records.Count)
                {
                    long recordIndex = records[index++];
                    var rec = sharedInfo.RemoteRecordMap[recordIndex];
                    if (rec.IsDirectory)
                        continue;
                    if (sharedInfo.Workspace.HasObjectData(rec) || !sharedInfo.Workspace.Included(rec.CanonicalName))
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

                    RecordStatus status = new RecordStatus();
                    var receiverStream = new Versionr.Utilities.ChunkedReceiverStream(() =>
                    {
                        ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.DataReceived }, ProtoBuf.PrefixStyle.Fixed32);
                        var pack = Utilities.ReceiveEncrypted<DataPayload>(sharedInfo);
                        status.Bytes += pack.Data.Length;
                        if (pack.EndOfStream)
                        {
                            return new Tuple<byte[], bool>(pack.Data, true);
                        }
                        else
                        {
                            return new Tuple<byte[], bool>(pack.Data, false);
                        }
                    });

                    status.Stopwatch = new System.Diagnostics.Stopwatch();
                    status.Requested = recordsInPack.Count;
                    Printer.InteractivePrinter printer = null;
                    if (sharedInfo.Client)
                        printer = Printer.CreateProgressBarPrinter("Importing data", string.Empty,
                            (obj) =>
                            {
                                RecordStatus stat = (RecordStatus)obj;
                                return string.Format("{0}/sec", Versionr.Utilities.Misc.FormatSizeFriendly((long)(stat.Bytes / stat.Stopwatch.Elapsed.TotalSeconds)));
                            },
                            (obj) =>
                            {
                                RecordStatus stat = (RecordStatus)obj;
                                return (100.0f * stat.Processed) / (float)stat.Requested;
                            },
                            (pct, obj) =>
                            {
                                RecordStatus stat = (RecordStatus)obj;
                                return string.Format("{0}/{1}", stat.Processed, stat.Requested);
                            },
                            60);

                    status.Stopwatch.Start();

                    var transaction = sharedInfo.Workspace.ObjectStore.BeginStorageTransaction();
                    try
                    {
                        while (!receiverStream.EndOfStream)
                        {
                            byte[] blob = new byte[16];
                            long recordIndex;
                            long recordSize;
                            if (receiverStream.Read(blob, 0, 8) != 8)
                                continue;
                            if (printer != null)
                                printer.Update(status);
                            status.Processed++;
                            recordIndex = BitConverter.ToInt64(blob, 0);
                            if (recordIndex < 0)
                            {
                                continue;
                            }
                            receiverStream.Read(blob, 8, 8);
                            recordSize = BitConverter.ToInt64(blob, 8);
                            Printer.PrintDiagnostics("Unpacking remote record {0}, payload size: {1}", recordIndex, recordSize);
                            var rec = sharedInfo.RemoteRecordMap[recordIndex];
                            string dependencies = null;
                            sharedInfo.Workspace.ImportRecordData(transaction, rec.DataIdentifier, new Versionr.Utilities.RestrictedStream(receiverStream, recordSize), out dependencies);
                            if (dependencies != null)
                                dependentData.Add(dependencies);

                            if (transaction.PendingRecords > 8192 || transaction.PendingRecordBytes > 512 * 1024 * 1024)
                                sharedInfo.Workspace.ObjectStore.FlushStorageTransaction(transaction);
                        }
                        sharedInfo.Workspace.ObjectStore.EndStorageTransaction(transaction);
                    }
                    catch
                    {
                        sharedInfo.Workspace.ObjectStore.AbortStorageTransaction(transaction);
                        throw;
                    }
                    if (printer != null)
                        printer.End(status);
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
                }
            }
            List<string> filteredDeps = new List<string>();
            foreach (var x in dependentData)
            {
                if (!recordDataIdentifiers.Contains(x) && !sharedInfo.Workspace.HasObjectDataDirect(x))
                {
                    recordDataIdentifiers.Add(x);
                    filteredDeps.Add(x);
                }
            }
            if (filteredDeps.Count > 0)
                RequestRecordDataUnmapped(sharedInfo, filteredDeps);
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
                    var record = sharedInfo.RemoteRecordMap[sharedInfo.UnknownRecords[index]];
                    if (record.Parent.HasValue && !sharedInfo.UnknownRecordSet.Contains(record.Parent.Value))
                        requests.Add(record.Parent.Value);
                    index++;
                }
                if (requests.Count > 0)
                {
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.RequestRecordParents }, ProtoBuf.PrefixStyle.Fixed32);
                    RequestRecordParents rrp = new RequestRecordParents() { RecordParents = requests.ToArray() };
                    Utilities.SendEncrypted<RequestRecordParents>(sharedInfo, rrp);

                    var response = Utilities.ReceiveEncrypted<RecordParentPack>(sharedInfo);
                    ReceiveRecordParents(sharedInfo, response);
                }
            }
        }

        internal class RecordStatus
        {
            public System.Diagnostics.Stopwatch Stopwatch;
            public int Requested;
            public int Processed;
            public long Bytes;
        }

        internal static List<string> RequestRecordDataUnmapped(SharedNetworkInfo sharedInfo, List<string> missingRecordData)
        {
            HashSet<string> successes = new HashSet<string>();
            int index = 0;
            List<string> returnedRecords = new List<string>();
            HashSet<string> recordDataIdentifiers = new HashSet<string>();
            List<string> dependentData = new List<string>();
            if (sharedInfo.Client)
                Printer.PrintMessage("Requesting #b#{0}## records from remote...", missingRecordData.Count);
            while (index < missingRecordData.Count)
            {
                RequestRecordDataUnmapped rrd = new RequestRecordDataUnmapped();
                List<string> recordsInPack = new List<string>();
                while (recordsInPack.Count < 1024 * 32 && index < missingRecordData.Count)
                {
                    var data = missingRecordData[index++];
                    if (!recordDataIdentifiers.Contains(data))
                    {
                        recordDataIdentifiers.Add(data);
                        recordsInPack.Add(data);
                    }
                }
                if (recordsInPack.Count > 0)
                {
                    rrd.RecordDataKeys = recordsInPack.ToArray();
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.RequestRecordUnmapped }, ProtoBuf.PrefixStyle.Fixed32);
                    Utilities.SendEncrypted<RequestRecordDataUnmapped>(sharedInfo, rrd);

                    RecordStatus status = new RecordStatus();
                    var receiverStream = new Versionr.Utilities.ChunkedReceiverStream(() =>
                    {
                        ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.DataReceived }, ProtoBuf.PrefixStyle.Fixed32);
                        var pack = Utilities.ReceiveEncrypted<DataPayload>(sharedInfo);
                        status.Bytes += pack.Data.Length;
                        if (pack.EndOfStream)
                        {
                            return new Tuple<byte[], bool>(pack.Data, true);
                        }
                        else
                        {
                            return new Tuple<byte[], bool>(pack.Data, false);
                        }
                    });

                    status.Stopwatch = new System.Diagnostics.Stopwatch();
                    status.Requested = recordsInPack.Count;
                    Printer.InteractivePrinter printer = null;
                    if (sharedInfo.Client)
                        printer = Printer.CreateProgressBarPrinter("Importing data", string.Empty,
                            (obj) =>
                            {
                                RecordStatus stat = (RecordStatus)obj;
                                return string.Format("{0}/sec", Versionr.Utilities.Misc.FormatSizeFriendly((long)(stat.Bytes / stat.Stopwatch.Elapsed.TotalSeconds)));
                            },
                            (obj) =>
                            {
                                RecordStatus stat = (RecordStatus)obj;
                                return (100.0f * stat.Processed) / (float)stat.Requested;
                            },
                            (pct, obj) =>
                            {
                                RecordStatus stat = (RecordStatus)obj;
                                return string.Format("{0}/{1}", stat.Processed, stat.Requested);
                            },
                            60);
                    
                    status.Stopwatch.Start();
                    var transaction = sharedInfo.Workspace.ObjectStore.BeginStorageTransaction();
                    try
                    {
                        while (!receiverStream.EndOfStream)
                        {
                            byte[] blob = new byte[8];
                            int successFlag;
                            int requestIndex;
                            long recordSize;
                            if (printer != null)
                                printer.Update(status);
                            status.Processed++;
                            if (receiverStream.Read(blob, 0, 8) != 8)
                                continue;
                            requestIndex = BitConverter.ToInt32(blob, 0);
                            successFlag = BitConverter.ToInt32(blob, 4);
                        
                            if (successFlag == 0)
                            {
                                Printer.PrintDiagnostics("Record {0} not located on remote.", recordsInPack[requestIndex]);
                                continue;
                            }

                            receiverStream.Read(blob, 0, 8);
                            recordSize = BitConverter.ToInt64(blob, 0);
                            Printer.PrintDiagnostics("Unpacking record {0}, payload size: {1}", recordsInPack[requestIndex], recordSize);

                            returnedRecords.Add(recordsInPack[requestIndex]);
                            string dependencies;
                            sharedInfo.Workspace.ImportRecordData(transaction, recordsInPack[requestIndex], new Versionr.Utilities.RestrictedStream(receiverStream, recordSize), out dependencies);
                            if (dependencies != null)
                                dependentData.Add(dependencies);

                            if (transaction.PendingRecords > 1024 || transaction.PendingRecordBytes > 256 * 1024 * 1024)
                                sharedInfo.Workspace.ObjectStore.FlushStorageTransaction(transaction);
                        }
                        sharedInfo.Workspace.ObjectStore.EndStorageTransaction(transaction);
                        if (printer != null)
                            printer.End(status);
                    }
                    catch
                    {
                        sharedInfo.Workspace.ObjectStore.AbortStorageTransaction(transaction);
                        throw;
                    }
                    ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
                }
            }
            List<string> filteredDeps = new List<string>();
            foreach (var x in dependentData)
            {
                if (!recordDataIdentifiers.Contains(x) && !sharedInfo.Workspace.HasObjectDataDirect(x))
                {
                    recordDataIdentifiers.Add(x);
                    filteredDeps.Add(x);
                }
            }
            if (filteredDeps.Count > 0)
                RequestRecordDataUnmapped(sharedInfo, filteredDeps);
            return returnedRecords;
        }

        internal static bool SendRecordDataUnmapped(SharedNetworkInfo sharedInfo)
        {
            var rrd = Utilities.ReceiveEncrypted<RequestRecordDataUnmapped>(sharedInfo);
            byte[] blockBuffer = new byte[16 * 1024 * 1024];
            var sender = GetSender(sharedInfo);
            int index = 0;
            foreach (var x in rrd.RecordDataKeys)
            {
                sender(BitConverter.GetBytes(index), 4, false);
                index++;
                Objects.Record record = sharedInfo.Workspace.GetRecordFromIdentifier(x);
                if (record == null)
                {
                    int failure = 0;
                    sender(BitConverter.GetBytes(failure), 4, false);
                    Printer.PrintDiagnostics("Record with identifier {0} not stored on this vault.", x);
                }
                else
                {
                    Printer.PrintDiagnostics("Sending data for: {0}", record.Fingerprint);

                    if (!sharedInfo.Workspace.TransmitRecordData(record, sender, blockBuffer, () =>
                    {
                        int success = 1;
                        sender(BitConverter.GetBytes(success), 4, false);
                    }))
                    {
                        int failure = 0;
                        sender(BitConverter.GetBytes(failure), 4, false);
                    }
                }
            }
            if (!sender(new byte[0], 0, true))
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
            if (sharedInfo.ReceivedVersionSet == null)
                sharedInfo.ReceivedVersionSet = new HashSet<Guid>();
            foreach (var x in pack.Versions)
            {
                if (!sharedInfo.ReceivedVersionSet.Contains(x.Version.ID))
                {
                    sharedInfo.PushedVersions.Add(x);
                    CheckRecords(sharedInfo, x);
                    sharedInfo.ReceivedVersionSet.Add(x.Version.ID);
                }
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
            sharedInfo.ReceivedBranches.AddRange(branches.Branches);
            ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(sharedInfo.Stream, new NetCommand() { Type = NetCommandType.Acknowledge }, ProtoBuf.PrefixStyle.Fixed32);
        }

        internal static void ImportBranches(SharedNetwork.SharedNetworkInfo sharedInfo)
        {
            Printer.PrintDiagnostics("Received branches:");
            foreach (var x in sharedInfo.ReceivedBranches)
            {
                Printer.PrintDiagnostics(" - {0}: \"{1}\"", x.ID, x.Name);
                sharedInfo.Workspace.ImportBranchNoCommit(x);
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
