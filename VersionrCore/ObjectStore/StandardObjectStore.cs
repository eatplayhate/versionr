using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;

namespace Versionr.ObjectStore
{
    enum StorageMode
    {
        Legacy,
        Flat,
        Packed,
        Delta
    }
    class StandardObjectStoreTransaction : ObjectStoreTransaction
    {
        public class PendingTransaction
        {
            public FileObjectStoreData Data;
            public string Filename;
            public Record Record;
        }
        public List<PendingTransaction> PendingTransactions = new List<PendingTransaction>();
        public HashSet<string> Cleanup = new HashSet<string>();
    }
    class PackfileObject
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public Guid ID { get; set; }
    }
    class FileObjectStoreData
    {
        [SQLite.PrimaryKey]
        public string Lookup { get; set; }
        public long FileSize { get; set; }
        public long Offset { get; set; }
        public StorageMode Mode { get; set; }
        public bool HasSignatureData { get; set; }
        public Guid? PackFileID { get; set; }
        public string DeltaBase { get; set; }
    }
    class StandardObjectStoreMetadata
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public int Id { get; set; }
        public int Version { get; set; }
    }
    public class StandardObjectStore : ObjectStoreBase
    {
        Area Owner { get; set; }
        SQLite.SQLiteConnection ObjectDatabase { get; set; }
        System.IO.DirectoryInfo DataFolder
        {
            get
            {
                return new DirectoryInfo(Path.Combine(Owner.AdministrationFolder.FullName, "objects"));
            }
        }
        System.IO.DirectoryInfo TempFolder
        {
            get
            {
                return new DirectoryInfo(Path.Combine(Owner.AdministrationFolder.FullName, "pending"));
            }
        }
        HashSet<string> TempFiles { get; set; }
        System.IO.FileInfo DataFile
        {
            get
            {
                return new FileInfo(Path.Combine(DataFolder.FullName, "store.db"));
            }
        }
        public override void Create(Area owner)
        {
            Owner = owner;
            DataFolder.Create();
            ObjectDatabase = new SQLite.SQLiteConnection(DataFile.FullName, SQLite.SQLiteOpenFlags.FullMutex | SQLite.SQLiteOpenFlags.Create | SQLite.SQLiteOpenFlags.ReadWrite);
            InitializeDBTypes();

            var meta = new StandardObjectStoreMetadata();
            meta.Version = 1;
            ObjectDatabase.Insert(meta);
        }

        private void InitializeDBTypes()
        {
            ObjectDatabase.EnableWAL = true;
            ObjectDatabase.CreateTable<FileObjectStoreData>();
            ObjectDatabase.CreateTable<PackfileObject>();
            ObjectDatabase.CreateTable<StandardObjectStoreMetadata>();

            TempFiles = new HashSet<string>();
            TempFolder.Create();
        }

        public override bool Open(Area owner)
        {
            Owner = owner;
            if (!DataFolder.Exists)
                return false;
            ObjectDatabase = new SQLite.SQLiteConnection(DataFile.FullName, SQLite.SQLiteOpenFlags.Create | SQLite.SQLiteOpenFlags.FullMutex | SQLite.SQLiteOpenFlags.ReadWrite);
            InitializeDBTypes();

            var version = ObjectDatabase.Table<StandardObjectStoreMetadata>().FirstOrDefault();
            if (version == null)
            {
                ObjectDatabase.BeginExclusive();
                Printer.PrintMessage("Upgrading object store database...");
                var records = owner.GetAllRecords();
                foreach (var x in records)
                {
                    ImportRecordFromFlatStore(x);
                }
                var meta = new StandardObjectStoreMetadata();
                meta.Version = 1;
                ObjectDatabase.Insert(meta);
                ObjectDatabase.Commit();
            }
            return true;
        }

        private void ImportRecordFromFlatStore(Record x)
        {
            if (x.HasData)
            {
                var recordData = new FileObjectStoreData();
                recordData.FileSize = x.Size;
                recordData.HasSignatureData = false;
                recordData.Lookup = x.DataIdentifier;
                recordData.Mode = StorageMode.Legacy;
                recordData.Offset = 0;
                recordData.PackFileID = null;
                try
                {
                    ObjectDatabase.Insert(recordData);
                }
                catch (SQLite.SQLiteException e)
                {
                    if (e.Result != SQLite.SQLite3.Result.Constraint)
                        throw;
                }
            }
        }

        public override bool RecordData(ObjectStoreTransaction transaction, Record newRecord, Record priorRecord, Entry fileEntry)
        {
            StandardObjectStoreTransaction trans = (StandardObjectStoreTransaction)transaction;
            lock (trans)
            {
                string filename;
                lock (this)
                {
                    if (HasData(newRecord))
                        return true;
                    do
                    {
                        filename = Path.GetRandomFileName();
                    } while (TempFiles.Contains(filename));
                    TempFiles.Add(filename);
                }
                Printer.PrintDiagnostics("Processing {0}", fileEntry.CanonicalName);
                trans.Cleanup.Add(filename);
                long resultSize;
                string fn = Path.Combine(TempFolder.FullName, filename);
                if (priorRecord != null)
                {
                    // try to delta encode it
                    string priorLookup = GetLookup(priorRecord);
                    var priorData = ObjectDatabase.Find<FileObjectStoreData>(x => x.Lookup == priorLookup);
                    if (priorData != null)
                    {
                        if (priorData.Mode == StorageMode.Delta)
                            priorData = ObjectDatabase.Find<FileObjectStoreData>(x => x.Lookup == priorData.DeltaBase);
                        if (priorData.HasSignatureData)
                        {
                            try
                            {
                                var signature = LoadSignature(priorData);
                                long deltaSize;
                                List<ChunkedChecksum.FileBlock> blocks;
                                Printer.PrintDiagnostics(" - Trying delta encoding");
                                using (var fileInput = fileEntry.Info.OpenRead())
                                {
                                    blocks = ChunkedChecksum.ComputeDelta(fileInput, fileEntry.Length, signature, out deltaSize);
                                }
                                // dont encode as delta unless we get a 50% saving
                                if (deltaSize < fileEntry.Length / 2)
                                {
                                    FileObjectStoreData data = new FileObjectStoreData()
                                    {
                                        FileSize = newRecord.Size,
                                        HasSignatureData = false,
                                        Lookup = GetLookup(newRecord),
                                        Mode = StorageMode.Delta,
                                        DeltaBase = priorData.Lookup,
                                        Offset = 0
                                    };
                                    trans.Cleanup.Add(filename + ".delta");
                                    Printer.PrintDiagnostics(" - Delta encoding");
                                    using (var fileInput = fileEntry.Info.OpenRead())
                                    using (var fileOutput = new FileInfo(fn + ".delta").OpenWrite())
                                    {
                                        ChunkedChecksum.WriteDelta(fileInput, fileOutput, blocks);
                                    }
                                    Printer.PrintDiagnostics(" - Compressing data");
                                    deltaSize = new FileInfo(fn + ".delta").Length;
                                    using (var fileInput = new FileInfo(fn + ".delta").OpenRead())
                                    using (var fileOutput = new FileInfo(fn).OpenWrite())
                                    {
                                        fileOutput.Write(new byte[] { (byte)'d', (byte)'b', (byte)'l', (byte)'x' }, 0, 4);
                                        int sig = 1;
                                        fileOutput.Write(BitConverter.GetBytes(sig), 0, 4);
                                        fileOutput.Write(BitConverter.GetBytes(newRecord.Size), 0, 8);
                                        fileOutput.Write(BitConverter.GetBytes(deltaSize), 0, 8);
                                        fileOutput.Write(BitConverter.GetBytes(priorData.Lookup.Length), 0, 4);
                                        byte[] lookupBytes = ASCIIEncoding.ASCII.GetBytes(priorData.Lookup);
                                        fileOutput.Write(lookupBytes, 0, lookupBytes.Length);
                                        LZHAMWriter.CompressToStream(deltaSize, 16 * 1024 * 1024, out resultSize, fileInput, fileOutput);
                                    }
                                    Printer.PrintMessage("Packed {0}: {1} => {2}", newRecord.CanonicalName, FormatSizeFriendly(newRecord.Size), FormatSizeFriendly(resultSize));
                                    Printer.PrintMessage(" - Encoded as {0} delta.", FormatSizeFriendly(deltaSize));
                                    trans.PendingTransactions.Add(
                                        new StandardObjectStoreTransaction.PendingTransaction()
                                        {
                                            Record = newRecord,
                                            Data = data,
                                            Filename = filename
                                        }
                                    );
                                    return true;
                                }
                            }
                            catch
                            {

                            }
                        }
                    }
                }
                bool computeSignature = newRecord.Size > 1024 * 64;
                FileObjectStoreData storeData = new FileObjectStoreData()
                {
                    FileSize = newRecord.Size,
                    HasSignatureData = computeSignature,
                    Lookup = GetLookup(newRecord),
                    Mode = StorageMode.Flat,
                    Offset = 0
                };
                using (var fileInput = fileEntry.Info.OpenRead())
                using (var fileOutput = new FileInfo(fn).OpenWrite())
                {
                    fileOutput.Write(new byte[] { (byte)'d', (byte)'b', (byte)'l', (byte)'k' }, 0, 4);
                    int sig = 1;
                    if (computeSignature)
                        sig |= 0x8000;
                    fileOutput.Write(BitConverter.GetBytes(sig), 0, 4);
                    fileOutput.Write(BitConverter.GetBytes(newRecord.Size), 0, 8);
                    if (computeSignature)
                    {
                        Printer.PrintDiagnostics(" - Computing signature");
                        var checksum = ChunkedChecksum.Compute(1024, fileInput);
                        fileInput.Position = 0;
                        ChunkedChecksum.Write(fileOutput, checksum);
                    }
                    Printer.PrintDiagnostics(" - Compressing data");
                    LZHAMWriter.CompressToStream(newRecord.Size, 16 * 1024 * 1024, out resultSize, fileInput, fileOutput);
                }
                Printer.PrintMessage("Packed {0}: {1} => {2}{3}", newRecord.CanonicalName, FormatSizeFriendly(newRecord.Size), FormatSizeFriendly(resultSize), computeSignature ? " (computed signatures)" : "");
                trans.PendingTransactions.Add(
                    new StandardObjectStoreTransaction.PendingTransaction()
                    {
                        Record = newRecord,
                        Data = storeData,
                        Filename = filename
                    }
                );
                return true;
            }
        }

        private ChunkedChecksum LoadSignature(FileObjectStoreData storeData)
        {
            if (storeData.Mode == StorageMode.Legacy)
                throw new Exception();
            else if (storeData.Mode == StorageMode.Flat)
            {
                using (var stream = OpenLegacyStream(storeData))
                {
                    return LoadSignatureFromStream(stream);
                }
            }
            throw new Exception();
        }

        private ChunkedChecksum LoadSignatureFromStream(Stream stream)
        {
            byte[] buffer = new byte[8];
            stream.Read(buffer, 0, 8);
            if (buffer[0] != 'd' || buffer[1] != 'b' || buffer[2] != 'l' || buffer[3] != 'k')
                throw new Exception();
            int data = BitConverter.ToInt32(buffer, 4);
            stream.Read(buffer, 0, 8);
            long length = BitConverter.ToInt64(buffer, 0);
            if (((uint)data & 0x8000) != 0)
                return ChunkedChecksum.Load(length, stream);
            else
                throw new Exception();
        }

        private string FormatSizeFriendly(long size)
        {
            if (size < 1024)
                return string.Format("{0} bytes", size);
            if (size < 1024 * 1024)
                return string.Format("{0:N2} KiB", size / 1024.0);
            return string.Format("{0:N2} MiB", size / (1024.0 * 1024.0));
        }

        public override bool ReceiveRecordData(ObjectStoreTransaction transaction, string directName, System.IO.Stream dataStream, out string dependency)
        {
            StandardObjectStoreTransaction trans = (StandardObjectStoreTransaction)transaction;
            dependency = null;
            lock (trans)
            {
                byte[] buffer = new byte[2 * 1024 * 1024];
                string filename;
                lock (this)
                {
                    if (HasDataDirect(directName))
                        throw new Exception();
                    do
                    {
                        filename = Path.GetRandomFileName();
                    } while (TempFiles.Contains(filename));
                    TempFiles.Add(filename);
                }
                Printer.PrintDiagnostics("Importing data for {0}", directName);
                trans.Cleanup.Add(filename);
                string fn = Path.Combine(TempFolder.FullName, filename);
                FileObjectStoreData data = new FileObjectStoreData()
                {
                    Lookup = directName,
                };
                using (var fileOutput = new FileInfo(fn).OpenWrite())
                {
                    bool readData = true;
                    byte[] sig = new byte[8];
                    dataStream.Read(sig, 0, 4);
                    if (sig[0] == 'd' && sig[1] == 'b' && sig[2] == 'l' && sig[3] == 'k')
                    {
                        data.Mode = StorageMode.Flat;
                        fileOutput.Write(sig, 0, 4);
                        dataStream.Read(sig, 0, 4);
                        fileOutput.Write(sig, 0, 4);
                        if ((BitConverter.ToUInt32(sig, 0) & 0x8000) != 0)
                            data.HasSignatureData = true;
                        dataStream.Read(sig, 0, 8);
                        fileOutput.Write(sig, 0, 8);
                        data.FileSize = BitConverter.ToInt64(sig, 0);
                    }
                    else if (sig[0] == 'd' && sig[1] == 'b' && sig[2] == 'l' && sig[3] == 'x')
                    {
                        data.Mode = StorageMode.Delta;
                        fileOutput.Write(sig, 0, 4);

                        dataStream.Read(sig, 0, 4);
                        fileOutput.Write(sig, 0, 4);

                        dataStream.Read(sig, 0, 8);
                        fileOutput.Write(sig, 0, 8);
                        data.FileSize = BitConverter.ToInt64(sig, 0);

                        dataStream.Read(sig, 0, 8);
                        fileOutput.Write(sig, 0, 8);

                        dataStream.Read(sig, 0, 4);
                        fileOutput.Write(sig, 0, 4);

                        int baseLookupLength = BitConverter.ToInt32(sig, 0);
                        byte[] baseLookupData = new byte[baseLookupLength];
                        dataStream.Read(baseLookupData, 0, baseLookupData.Length);
                        fileOutput.Write(baseLookupData, 0, baseLookupData.Length);
                        string baseLookup = ASCIIEncoding.ASCII.GetString(baseLookupData);

                        dependency = baseLookup;

                        data.Mode = StorageMode.Delta;
                        data.DeltaBase = baseLookup;
                    }
                    else
                    {
                        if (true)
                        {
                            data.Mode = StorageMode.Flat;
                            Printer.PrintDiagnostics(" - Importing legacy record...");
                            fileOutput.Write(new byte[] { (byte)'d', (byte)'b', (byte)'l', (byte)'k' }, 0, 4);
                            dataStream.Read(sig, 0, 8);
                            data.FileSize = BitConverter.ToInt64(sig, 0);

                            string importTemp;
                            lock (this)
                            {
                                do
                                {
                                    importTemp = Path.GetRandomFileName();
                                } while (TempFiles.Contains(importTemp));
                                TempFiles.Add(importTemp);
                            }
                            trans.Cleanup.Add(importTemp);
                            importTemp = Path.Combine(TempFolder.FullName, importTemp);
                            FileInfo importTempInfo = new FileInfo(importTemp);
                            using (var fs = importTempInfo.Create())
                            using (LZHAMLegacyStream legacy = new LZHAMLegacyStream(dataStream, false, data.FileSize))
                            {
                                while (true)
                                {
                                    var read = legacy.Read(buffer, 0, buffer.Length);
                                    if (read == 0)
                                        break;
                                    fs.Write(buffer, 0, read);
                                }
                            }
                            using (var fileInput = importTempInfo.OpenRead())
                            {
                                int signature = 1;
                                bool computeSignature = data.FileSize > 1024 * 64;
                                if (computeSignature)
                                    signature |= 0x8000;
                                fileOutput.Write(BitConverter.GetBytes(signature), 0, 4);
                                fileOutput.Write(BitConverter.GetBytes(data.FileSize), 0, 8);
                                if (computeSignature)
                                {
                                    Printer.PrintDiagnostics(" - Computing signature");
                                    var checksum = ChunkedChecksum.Compute(1024, fileInput);
                                    fileInput.Position = 0;
                                    ChunkedChecksum.Write(fileOutput, checksum);
                                }
                                Printer.PrintDiagnostics(" - Compressing data");
                                long resultSize = 0;
                                LZHAMWriter.CompressToStream(data.FileSize, 16 * 1024 * 1024, out resultSize, fileInput, fileOutput);
                            }
                        }
                        else
                        {
                            fileOutput.Write(sig, 0, 4);
                            data.Mode = StorageMode.Legacy;
                            dataStream.Read(sig, 0, 8);
                            fileOutput.Write(sig, 0, 8);
                            data.FileSize = BitConverter.ToInt64(sig, 0);
                        }
                    }
                    while (readData)
                    {
                        var read = dataStream.Read(buffer, 0, buffer.Length);
                        if (read == 0)
                            break;
                        fileOutput.Write(buffer, 0, read);
                    }
                }
                trans.PendingTransactions.Add(
                    new StandardObjectStoreTransaction.PendingTransaction()
                    {
                        Data = data,
                        Filename = filename
                    }
                );
                return true;
            }
        }

        public override ObjectStoreTransaction BeginStorageTransaction()
        {
            return new StandardObjectStoreTransaction();
        }

        public override bool HasData(Record recordInfo)
        {
            return HasDataDirect(GetLookup(recordInfo));
        }
        public override bool HasDataDirect(string x)
        {
            lock (this)
            {
                var storeData = ObjectDatabase.Find<FileObjectStoreData>(x);
                if (storeData == null)
                    return false;
                return true;
            }
        }

        public override bool AbortStorageTransaction(ObjectStoreTransaction transaction)
        {
            lock (this)
            {
                return CompleteTransaction(transaction as StandardObjectStoreTransaction, true);
            }
        }

        public override bool EndStorageTransaction(ObjectStoreTransaction transaction)
        {
            lock (this)
            {
                return CompleteTransaction(transaction as StandardObjectStoreTransaction, false);
            }
        }

        private bool CompleteTransaction(StandardObjectStoreTransaction transaction, bool abort)
        {
            if (transaction == null)
                throw new Exception();
            lock (transaction)
            {
                if (!abort)
                {
                    try
                    {
                        ObjectDatabase.BeginTransaction();
                        foreach (var x in transaction.PendingTransactions)
                        {
                            string fn = Path.Combine(TempFolder.FullName, x.Filename);
                            ObjectDatabase.Insert(x.Data);
                            if (!GetFileForDataID(x.Data.Lookup).Exists)
                            {
                                if (!System.IO.File.Exists(fn))
                                    throw new Exception();
                                System.IO.File.Move(fn, GetFileForDataID(x.Data.Lookup).FullName);
                            }
                        }
                        ObjectDatabase.Commit();
                    }
                    catch
                    {
                        ObjectDatabase.Rollback();
                        throw;
                    }
                }
                foreach (var x in transaction.Cleanup)
                {
                    string fn = Path.Combine(TempFolder.FullName, x);
                    if (System.IO.File.Exists(fn))
                        System.IO.File.Delete(fn);
                    TempFiles.Remove(x);
                }
            }
            return true;
        }

        private FileInfo GetFileForDataID(string id)
        {
            DirectoryInfo subDir = new DirectoryInfo(Path.Combine(DataFolder.FullName, id.Substring(0, 2)));
            subDir.Create();
            return new FileInfo(Path.Combine(subDir.FullName, id.Substring(2)));
        }

        public override long GetTransmissionLength(Record record)
        {
            if (!record.HasData)
                return 0;
            string lookup = GetLookup(record);
            var storeData = ObjectDatabase.Find<FileObjectStoreData>(lookup);
            return GetFileForDataID(storeData.Lookup).Length;
            throw new Exception();
        }

        public override bool TransmitRecordData(Record record, Func<IEnumerable<byte>, bool, bool> sender)
        {
            if (!record.HasData)
            {
                return true;
            }
            sender(BitConverter.GetBytes(GetTransmissionLength(record)), false);
            using (System.IO.Stream dataStream = GetDataStream(record))
            {
                if (dataStream == null)
                    return false;

                byte[] buffer = new byte[1024 * 1024 * 16];
                while (true)
                {
                    var readCount = dataStream.Read(buffer, 0, buffer.Length);
                    if (readCount == 0)
                        break;
                    if (buffer.Length != readCount)
                        Array.Resize(ref buffer, readCount);
                    sender(buffer, false);
                }
            }
            return true;
        }

        private Stream GetDataStream(Record record)
        {
            string lookup = GetLookup(record);
            var storeData = ObjectDatabase.Find<FileObjectStoreData>(lookup);
            return OpenLegacyStream(storeData);
            throw new Exception();
        }
        public override System.IO.Stream GetRecordStream(Objects.Record record)
        {
            string lookup = GetLookup(record);
            return GetStreamForLookup(lookup);
        }

        private Stream GetStreamForLookup(string lookup)
        {
            var storeData = ObjectDatabase.Find<FileObjectStoreData>(lookup);
            if (storeData.Mode == StorageMode.Legacy)
                return new LZHAMLegacyStream(OpenLegacyStream(storeData), true);
            if (storeData.Mode == StorageMode.Flat)
                return OpenCodecStream(OpenLegacyStream(storeData));
            if (storeData.Mode == StorageMode.Delta)
                throw new Exception();
            throw new Exception();
        }

        public override void WriteRecordStream(Record record, System.IO.Stream outputStream)
        {
            string lookup = GetLookup(record);
            var storeData = ObjectDatabase.Find<FileObjectStoreData>(lookup);

            System.IO.Stream dataStream;
            if (storeData.Mode == StorageMode.Delta)
            {
                Stream baseStream;
                dataStream = OpenDeltaCodecStream(OpenLegacyStream(storeData), out baseStream);
                ChunkedChecksum.ApplyDelta(baseStream, dataStream, outputStream);
                dataStream.Dispose();
                baseStream.Dispose();
            }
            else
            {
                if (storeData.Mode == StorageMode.Legacy)
                    dataStream = new LZHAMLegacyStream(OpenLegacyStream(storeData), true);
                else if (storeData.Mode == StorageMode.Flat)
                    dataStream = OpenCodecStream(OpenLegacyStream(storeData));
                else
                    throw new Exception();
                byte[] dataBlob = new byte[16 * 1024 * 1024];
                while (true)
                {
                    var res = dataStream.Read(dataBlob, 0, dataBlob.Length);
                    if (res == 0)
                        break;
                    outputStream.Write(dataBlob, 0, res);
                }
                dataStream.Dispose();
            }
        }

        private Stream OpenDeltaCodecStream(Stream stream, out Stream baseFileStream)
        {
            byte[] buffer = new byte[8];
            stream.Read(buffer, 0, 8);
            if (buffer[0] != 'd' || buffer[1] != 'b' || buffer[2] != 'l' || buffer[3] != 'x')
                throw new Exception();

            int data = BitConverter.ToInt32(buffer, 4);
            stream.Read(buffer, 0, 8);
            long length = BitConverter.ToInt64(buffer, 0);
            stream.Read(buffer, 0, 8);
            long deltaLength = BitConverter.ToInt64(buffer, 0);
            stream.Read(buffer, 0, 4);

            int baseLookupLength = BitConverter.ToInt32(buffer, 0);
            byte[] baseLookupData = new byte[baseLookupLength];
            stream.Read(baseLookupData, 0, baseLookupData.Length);
            string baseLookup = ASCIIEncoding.ASCII.GetString(baseLookupData);

            baseFileStream = GetStreamForLookup(baseLookup);

            switch (data & 0x0FFF)
            {
                case 1:
                    return new LZHAMReaderStream(deltaLength, stream);
                default:
                    throw new Exception();
            }
        }

        private Stream OpenCodecStream(Stream stream)
        {
            byte[] buffer = new byte[8];
            stream.Read(buffer, 0, 8);
            if (buffer[0] != 'd' || buffer[1] != 'b' || buffer[2] != 'l' || buffer[3] != 'k')
                throw new Exception();
            int data = BitConverter.ToInt32(buffer, 4);
            stream.Read(buffer, 0, 8);
            long length = BitConverter.ToInt64(buffer, 0);
            if (((uint)data & 0x8000) != 0)
                ChunkedChecksum.Skip(stream);
            switch (data & 0x0FFF)
            {
                case 1:
                    return new LZHAMReaderStream(length, stream);
                default:
                    throw new Exception();
            }
        }

        private Stream OpenLegacyStream(FileObjectStoreData storeData)
        {
            FileInfo info = GetFileForDataID(storeData.Lookup);
            if (info == null)
                return null;
            return info.OpenRead();
        }

        private string GetLookup(Record record)
        {
            return record.Fingerprint + "-" + record.Size.ToString();
        }
    }
}
