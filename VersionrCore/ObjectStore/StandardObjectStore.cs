//#define SLOW_DATA_CHECK

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;
using Versionr.Utilities;

namespace Versionr.ObjectStore
{
    public enum StorageMode
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
            public byte[] Payload;
        }
        public List<PendingTransaction> PendingTransactions = new List<PendingTransaction>();
        public HashSet<string> Cleanup = new HashSet<string>();
        public HashSet<string> Inputs = new HashSet<string>();
        public byte[] ScratchBuffer = new byte[1024 * 1024 * 16];

        internal long m_PendingBytes;
        internal int m_PendingCount;
        public override long PendingRecordBytes
        {
            get
            {
                return m_PendingBytes;
            }
        }

        public override int PendingRecords
        {
            get
            {
                return m_PendingCount;
            }
        }
    }
    public class Blobject
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }
        public byte[] Data { get; set; }
    }
    public class Blobsize
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }
        [SQLite.Indexed]
        public long BlobID { get; set; }
        public long Length { get; set; }
    }
    public class PackfileObject
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public Guid ID { get; set; }
    }
    public class FileObjectStoreData
    {
        [SQLite.PrimaryKey]
        public string Lookup { get; set; }
        public long FileSize { get; set; }
        public long Offset { get; set; }
        public StorageMode Mode { get; set; }
        public bool HasSignatureData { get; set; }
        public Guid? PackFileID { get; set; }
        public string DeltaBase { get; set; }
        public long? BlobID { get; set; }
        [SQLite.LoadOnly, SQLite.Column("rowid")]
        public uint ID { get; set; }
    }
    public class StandardObjectStoreMetadata
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public int Id { get; set; }
        public int Version { get; set; }
    }
    public enum CompressionMode
    {
        None = 0,
        LZHAM = 1,
        LZ4 = 2,
        LZ4HC = 3
    }
    public class StandardObjectStore : ObjectStoreBase
    {
        Area Owner { get; set; }
        SQLite.SQLiteConnection ObjectDatabase { get; set; }
        SQLite.SQLiteConnection BlobDatabase { get; set; }
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
        System.IO.FileInfo BlobFile
        {
            get
            {
                return new FileInfo(Path.Combine(DataFolder.FullName, "blobs.db"));
            }
        }

        public static Tuple<string, string> ComponentVersionInfo
        {
            get
            {
                return new Tuple<string, string>("Standard Object DB", string.Format("v{0}, database format v{0}", 2));
            }
        }

        public CompressionMode DefaultCompression { get; set; }

        public override void Create(Area owner)
        {
            Owner = owner;
            DataFolder.Create();
            ObjectDatabase = new SQLite.SQLiteConnection(DataFile.FullName, SQLite.SQLiteOpenFlags.FullMutex | SQLite.SQLiteOpenFlags.Create | SQLite.SQLiteOpenFlags.ReadWrite);
            BlobDatabase = new SQLite.SQLiteConnection(BlobFile.FullName, SQLite.SQLiteOpenFlags.FullMutex | SQLite.SQLiteOpenFlags.Create | SQLite.SQLiteOpenFlags.ReadWrite);
            InitializeDBTypes();

            var meta = new StandardObjectStoreMetadata();
            meta.Version = 3;
            ObjectDatabase.InsertSafe(meta);
        }

        private void InitializeDBTypes()
        {
            CompressionMode cmode = CompressionMode.LZHAM;
            if (Owner.Directives != null && !string.IsNullOrEmpty(Owner.Directives.DefaultCompression))
            {
                if (!Enum.TryParse<CompressionMode>(Owner.Directives.DefaultCompression, out cmode))
                    cmode = CompressionMode.LZHAM;
            }
            DefaultCompression = cmode;

            ObjectDatabase.BeginTransaction();
            ObjectDatabase.EnableWAL = true;
            ObjectDatabase.CreateTable<FileObjectStoreData>();
            ObjectDatabase.CreateTable<PackfileObject>();
            ObjectDatabase.CreateTable<StandardObjectStoreMetadata>();
            ObjectDatabase.Commit();

            BlobDatabase.BeginTransaction();
            BlobDatabase.EnableWAL = true;
            BlobDatabase.CreateTable<Blobject>();
            BlobDatabase.CreateTable<Blobsize>();
            BlobDatabase.Commit();

            TempFiles = new HashSet<string>();
            TempFolder.Create();
        }

        public override bool Open(Area owner)
        {
            Owner = owner;
            if (!DataFolder.Exists)
            {
                DataFolder.Create();
            }
            ObjectDatabase = new SQLite.SQLiteConnection(DataFile.FullName, SQLite.SQLiteOpenFlags.Create | SQLite.SQLiteOpenFlags.FullMutex | SQLite.SQLiteOpenFlags.ReadWrite);
            BlobDatabase = new SQLite.SQLiteConnection(BlobFile.FullName, SQLite.SQLiteOpenFlags.FullMutex | SQLite.SQLiteOpenFlags.Create | SQLite.SQLiteOpenFlags.ReadWrite);
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
                meta.Version = 2;
                ObjectDatabase.InsertSafe(meta);
                ObjectDatabase.Commit();
            }
            else if (version.Version < 3)
            {
                Printer.PrintMessage("Upgrading object store database...");
                foreach (var x in BlobDatabase.Table<Blobject>())
                {
                    Blobsize bs = new Blobsize() { BlobID = x.Id, Length = x.Data.Length };
                    BlobDatabase.Insert(bs);
                }
                ObjectDatabase.BeginExclusive();
                ObjectDatabase.DropTable<StandardObjectStoreMetadata>();
                ObjectDatabase.CreateTable<StandardObjectStoreMetadata>();
                var meta = new StandardObjectStoreMetadata();
                meta.Version = 3;
                ObjectDatabase.InsertSafe(meta);
                ObjectDatabase.Commit();
            }
            return true;
        }

        private void ImportRecordFromFlatStore(Record x)
        {
            if (x.HasData)
            {
                if (!CheckFileForDataIDExists(x.DataIdentifier))
                    return;
                var recordData = new FileObjectStoreData();
                recordData.FileSize = x.Size;
                recordData.HasSignatureData = false;
                recordData.Lookup = x.DataIdentifier;
                recordData.Mode = StorageMode.Legacy;
                recordData.Offset = 0;
                recordData.PackFileID = null;
                try
                {
                    ObjectDatabase.InsertSafe(recordData);
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
                if (trans.Inputs.Contains(GetLookup(newRecord)))
                    return true;
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
                trans.Inputs.Add(GetLookup(newRecord));
                trans.m_PendingCount++;
                trans.m_PendingBytes += newRecord.Size;
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
                                Printer.InteractivePrinter printer = null;
                                if (newRecord.Size > 16 * 1024 * 1024)
                                    printer = Printer.CreateSimplePrinter(" Computing Delta", (obj) => { return string.Format("{0:N1}%", (float)((long)obj / (double)newRecord.Size) * 100.0f); });
                                using (var fileInput = fileEntry.Info.OpenRead())
                                {
                                    blocks = ChunkedChecksum.ComputeDelta(fileInput, fileEntry.Length, signature, out deltaSize, (fs, ps) => { if (ps % (512 * 1024) == 0 && printer != null) printer.Update(ps); });
                                }
                                if (printer != null)
                                {
                                    printer.End(newRecord.Size);
                                    printer = null;
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
                                        CompressionMode cmode = DefaultCompression;
                                        if (cmode != CompressionMode.None && deltaSize < 16 * 1024)
                                            cmode = CompressionMode.LZ4;
                                        if (cmode != CompressionMode.None && deltaSize < 1024)
                                            cmode = CompressionMode.None;
                                        int sig = (int)cmode;
                                        fileOutput.Write(BitConverter.GetBytes(sig), 0, 4);
                                        fileOutput.Write(BitConverter.GetBytes(newRecord.Size), 0, 8);
                                        fileOutput.Write(BitConverter.GetBytes(deltaSize), 0, 8);
                                        fileOutput.Write(BitConverter.GetBytes(priorData.Lookup.Length), 0, 4);
                                        byte[] lookupBytes = ASCIIEncoding.ASCII.GetBytes(priorData.Lookup);
                                        fileOutput.Write(lookupBytes, 0, lookupBytes.Length);
                                        if (deltaSize > 16 * 1024 * 1024)
                                        {
                                            printer = Printer.CreateProgressBarPrinter(string.Empty, string.Format(" Writing {0} ", cmode), (obj) =>
                                            {
                                                return string.Format("{0}/{1}", Misc.FormatSizeFriendly((long)obj), Misc.FormatSizeFriendly(deltaSize));
                                            },
                                            (obj) =>
                                            {
                                                return (float)((long)obj / (double)deltaSize) * 100.0f;
                                            },
                                            (obj, lol) => { return string.Empty; }, 40);
                                        }
                                        if (cmode == CompressionMode.LZHAM)
                                            LZHAMWriter.CompressToStream(deltaSize, 16 * 1024 * 1024, out resultSize, fileInput, fileOutput, (fs, ps, cs) => { if (printer != null) printer.Update(ps); });
                                        else if (cmode == CompressionMode.LZ4)
                                            LZ4Writer.CompressToStream(deltaSize, 16 * 1024 * 1024, out resultSize, fileInput, fileOutput, (fs, ps, cs) => { if (printer != null) printer.Update(ps); });
                                        else if (cmode == CompressionMode.LZ4HC)
                                            LZ4HCWriter.CompressToStream(deltaSize, 16 * 1024 * 1024, out resultSize, fileInput, fileOutput, (fs, ps, cs) => { if (printer != null) printer.Update(ps); });
                                        else
                                        {
                                            resultSize = deltaSize;
                                            fileInput.CopyTo(fileOutput);
                                        }
                                        if (printer != null)
                                            printer.End(newRecord.Size);
                                    }
                                    Printer.PrintMessage(" - Compressed: {0} ({1} delta) => {2}", Misc.FormatSizeFriendly(newRecord.Size), Misc.FormatSizeFriendly(deltaSize), Misc.FormatSizeFriendly(resultSize));
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
                    CompressionMode cmode = DefaultCompression;
                    if (cmode != CompressionMode.None && newRecord.Size < 16 * 1024)
                        cmode = CompressionMode.LZ4;
                    if (cmode != CompressionMode.None && newRecord.Size < 1024)
                        cmode = CompressionMode.None;
                    int sig = (int)cmode;
                    if (computeSignature)
                        sig |= 0x8000;
                    fileOutput.Write(BitConverter.GetBytes(sig), 0, 4);
                    fileOutput.Write(BitConverter.GetBytes(newRecord.Size), 0, 8);
                    Printer.InteractivePrinter printer = null;
                    if (newRecord.Size > 16 * 1024 * 1024)
                        printer = Printer.CreateSimplePrinter(" Computing Signature", (obj) => { return string.Format("{0:N1}%", (float)((long)obj / (double)newRecord.Size) * 100.0f); });
                    if (computeSignature)
                    {
                        Printer.PrintDiagnostics(" - Computing signature");
                        var checksum = ChunkedChecksum.Compute(1024, fileInput, (fs, ps) => { if (ps % (512 * 1024) == 0 && printer != null) printer.Update(ps); });
                        fileInput.Position = 0;
                        ChunkedChecksum.Write(fileOutput, checksum);
                    }
                    Printer.PrintDiagnostics(" - Compressing data");
                    if (printer != null)
                    {
                        printer.End(newRecord.Size);
                        printer = Printer.CreateProgressBarPrinter(string.Empty, string.Format(" Writing {0} ", cmode), (obj) =>
                        {
                            return string.Format("{0}/{1}", Misc.FormatSizeFriendly((long)obj), Misc.FormatSizeFriendly(newRecord.Size));
                        },
                        (obj) =>
                        {
                            return (float)((long)obj / (double)newRecord.Size) * 100.0f;
                        },
                        (obj, lol) => { return string.Empty; }, 40);
                    }
                    if (cmode == CompressionMode.LZHAM)
                        LZHAMWriter.CompressToStream(newRecord.Size, 16 * 1024 * 1024, out resultSize, fileInput, fileOutput, (fs, ps, cs) => { if (printer != null) printer.Update(ps); });
                    else if (cmode == CompressionMode.LZ4)
                        LZ4Writer.CompressToStream(newRecord.Size, 16 * 1024 * 1024, out resultSize, fileInput, fileOutput, (fs, ps, cs) => { if (printer != null) printer.Update(ps); });
                    else if (cmode == CompressionMode.LZ4HC)
                        LZ4HCWriter.CompressToStream(newRecord.Size, 16 * 1024 * 1024, out resultSize, fileInput, fileOutput, (fs, ps, cs) => { if (printer != null) printer.Update(ps); });
                    else
                    {
                        resultSize = newRecord.Size;
                        fileInput.CopyTo(fileOutput);
                    }
                    if (printer != null)
                        printer.End(newRecord.Size);
                }
                Printer.PrintMessage(" - Compressed: {1} => {2}{3}", newRecord.CanonicalName, Misc.FormatSizeFriendly(newRecord.Size), Misc.FormatSizeFriendly(resultSize), computeSignature ? " (computed signatures)" : "");
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

        public override bool ReceiveRecordData(ObjectStoreTransaction transaction, string directName, System.IO.Stream dataStream, out string dependency)
        {
            StandardObjectStoreTransaction trans = (StandardObjectStoreTransaction)transaction;
            dependency = null;
            lock (trans)
            {
                trans.m_PendingCount++;
                Printer.PrintDiagnostics("Importing data for {0}", directName);
                FileObjectStoreData data = new FileObjectStoreData()
                {
                    Lookup = directName,
                };
                byte[] buffer = trans.ScratchBuffer;
                string filename = null;
                Stream outputStream;
                MemoryStream backingStore = null;
                if (dataStream.Length > 16 * 1024)
                {
                    lock (this)
                    {
                        List<string> ignored;
                        if (HasDataDirect(directName, out ignored))
                            throw new Exception();
                        do
                        {
                            filename = Path.GetRandomFileName();
                        } while (TempFiles.Contains(filename));
                        TempFiles.Add(filename);
                    }
                    trans.Cleanup.Add(filename);
                    string fn = Path.Combine(TempFolder.FullName, filename);
                    outputStream = new FileInfo(fn).OpenWrite();
                }
                else
                {
                    backingStore = new MemoryStream();
                    outputStream = backingStore;
                }
                using (var fileOutput = outputStream)
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
                                int signature = (int)CompressionMode.LZHAM;
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
                trans.m_PendingBytes += data.FileSize;
                byte[] payload = null;
                if (backingStore != null)
                    payload = backingStore.ToArray();
                trans.PendingTransactions.Add(
                    new StandardObjectStoreTransaction.PendingTransaction()
                    {
                        Data = data,
                        Filename = filename,
                        Payload = payload
                    }
                );
                return true;
            }
        }

        public override ObjectStoreTransaction BeginStorageTransaction()
        {
            return new StandardObjectStoreTransaction();
        }

        public override bool HasData(Record recordInfo, out List<string> requestedData)
        {
            requestedData = null;
            if (!recordInfo.HasData)
                return true;
            return HasDataDirect(GetLookup(recordInfo), out requestedData);
        }
        public override bool HasDataDirect(string x, out List<string> requestedData)
        {
            requestedData = null;
            lock (this)
            {
                var storeData = ObjectDatabase.Find<FileObjectStoreData>(x);
                if (storeData == null)
                {
                    requestedData = new List<string>();
                    requestedData.Add(x);
                    return false;
                }
                if (!string.IsNullOrEmpty(storeData.DeltaBase))
                {
                    if (!HasDataDirect(storeData.DeltaBase, out requestedData))
                    {
                        return false;
                    }
                }
#if SLOW_DATA_CHECK
                if (storeData.BlobID == null)
                    return GetFileForDataID(x).Exists;
                bool present = BlobDatabase.Find<Blobject>(storeData.BlobID.Value) != null;
                if (present == false)
                {
                    requestedData = new List<string>();
                    requestedData.Add(x);
                }
                return present;
#else
                return true;
#endif
            }
        }

        public override bool AbortStorageTransaction(ObjectStoreTransaction transaction)
        {
            lock (this)
            {
                return CompleteTransaction(transaction as StandardObjectStoreTransaction, true);
            }
        }

        public override bool FlushStorageTransaction(ObjectStoreTransaction transaction)
        {
            lock (this)
            {
                return CompleteTransaction(transaction as StandardObjectStoreTransaction, false);
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
                        BlobDatabase.BeginTransaction();
                        ObjectDatabase.BeginTransaction();
                        foreach (var x in transaction.PendingTransactions)
                        {
                            try
                            {
                                if (ObjectDatabase.InsertSafe(x.Data))
                                {
                                    long? blobID = null;
                                    if (x.Filename != null)
                                    {
                                        string fn = Path.Combine(TempFolder.FullName, x.Filename);
                                        var info = new FileInfo(fn);
                                        if (info.Length < 16 * 1024)
                                        {
                                            MemoryStream ms = new MemoryStream();
                                            using (var fsi = info.OpenRead())
                                            {
                                                fsi.CopyTo(ms);
                                            }
                                            Blobject obj = new Blobject() { Data = ms.ToArray() };
                                            if (!BlobDatabase.InsertSafe(obj))
                                                throw new Exception();
                                            blobID = obj.Id;
                                            Blobsize bs = new Blobsize() { BlobID = obj.Id, Length = obj.Data.Length };
                                            if (!BlobDatabase.InsertSafe(bs))
                                                throw new Exception();
                                        }
                                        if (blobID.HasValue)
                                        {
                                            x.Data.BlobID = blobID;
                                            ObjectDatabase.Update(x.Data);
                                            transaction.Cleanup.Add(x.Filename);
                                        }
                                        else
                                        {
                                            if (!GetFileForDataID(x.Data.Lookup).Exists)
                                            {
                                                if (!System.IO.File.Exists(fn))
                                                    throw new Exception();
                                                System.IO.File.Move(fn, GetFileForDataID(x.Data.Lookup).FullName);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (x.Payload.Length < 16 * 1024)
                                        {
                                            Blobject obj = new Blobject() { Data = x.Payload };
                                            if (!BlobDatabase.InsertSafe(obj))
                                                throw new Exception();
                                            blobID = obj.Id;
                                            Blobsize bs = new Blobsize() { BlobID = obj.Id, Length = obj.Data.Length };
                                            if (!BlobDatabase.InsertSafe(bs))
                                                throw new Exception();
                                        }
                                        if (blobID.HasValue)
                                        {
                                            x.Data.BlobID = blobID;
                                            ObjectDatabase.Update(x.Data);
                                        }
                                        else
                                        {
                                            var info = GetFileForDataID(x.Data.Lookup);
                                            if (!info.Exists)
                                            {
                                                using (var stream = info.OpenWrite())
                                                    stream.Write(x.Payload, 0, x.Payload.Length);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (SQLite.SQLiteException ex)
                            {
                                if (ex.Result != SQLite.SQLite3.Result.Constraint)
                                    throw ex;
                            }
                        }
                        transaction.PendingTransactions.Clear();
                        BlobDatabase.Commit();
                        ObjectDatabase.Commit();
                        transaction.m_PendingBytes = 0;
                        transaction.m_PendingCount = 0;
                    }
                    catch
                    {
                        BlobDatabase.Rollback();
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
                transaction.Cleanup.Clear();
            }
            return true;
        }

        private FileInfo GetFileForDataID(string id)
        {
            DirectoryInfo subDir = new DirectoryInfo(Path.Combine(DataFolder.FullName, id.Substring(0, 2)));
            subDir.Create();
            return new FileInfo(Path.Combine(subDir.FullName, id.Substring(2)));
        }

        private bool CheckFileForDataIDExists(string id)
        {
            DirectoryInfo subDir = new DirectoryInfo(Path.Combine(DataFolder.FullName, id.Substring(0, 2)));
            if (!subDir.Exists)
                return false;
            return new FileInfo(Path.Combine(subDir.FullName, id.Substring(2))).Exists;
        }

        public override long GetTransmissionLength(Record record)
        {
            if (!record.HasData)
                return 0;
            string lookup = GetLookup(record);
            var storeData = ObjectDatabase.Find<FileObjectStoreData>(lookup);
            if (storeData == null)
                return -1;
            return GetTransmissionLengthInternal(storeData);
        }

        private long GetTransmissionLengthInternal(FileObjectStoreData storeData)
        {
            if (storeData.BlobID.HasValue)
                return BlobDatabase.Get<Blobsize>(x => x.BlobID == storeData.BlobID.Value).Length;
            return GetFileForDataID(storeData.Lookup).Length;
        }

        public override bool TransmitRecordData(Record record, Func<byte[], int, bool, bool> sender, byte[] scratchBuffer, Action beginTransmission = null)
        {
            if (!record.HasData)
            {
                return true;
            }
            long dataSize = GetTransmissionLength(record);
            if (dataSize == -1)
                return false;
            using (System.IO.Stream dataStream = GetDataStream(record))
            {
                if (dataStream == null)
                    return false;
                if (beginTransmission != null)
                    beginTransmission();
                sender(BitConverter.GetBytes(dataSize), 8, false);
                while (true)
                {
                    var readCount = dataStream.Read(scratchBuffer, 0, scratchBuffer.Length);
                    if (readCount == 0)
                        break;
                    sender(scratchBuffer, readCount, false);
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
            WriteRecordStream(storeData, outputStream);
        }

        protected void WriteRecordStream(FileObjectStoreData storeData, System.IO.Stream outputStream)
        {
            System.IO.Stream dataStream;
            if (storeData.Mode == StorageMode.Delta)
            {
                Stream baseStream;
                FileInfo tempBaseFile;
                dataStream = OpenDeltaCodecStream(OpenLegacyStream(storeData), out baseStream, out tempBaseFile);
                ChunkedChecksum.ApplyDelta(baseStream, dataStream, outputStream);
                dataStream.Dispose();
                baseStream.Dispose();
                tempBaseFile.Delete();
            }
            else
            {
                if (storeData.Mode == StorageMode.Legacy)
                    dataStream = new LZHAMLegacyStream(OpenLegacyStream(storeData), true);
                else if (storeData.Mode == StorageMode.Flat)
                    dataStream = OpenCodecStream(OpenLegacyStream(storeData));
                else
                    throw new Exception();
                Printer.InteractivePrinter printer = null;
                long total = 0;
                byte[] dataBlob = new byte[16 * 1024 * 1024];
                while (true)
                {
                    var res = dataStream.Read(dataBlob, 0, dataBlob.Length);
                    if (res == 0)
                        break;
                    if (printer != null)
                        printer.Update(total);
                    outputStream.Write(dataBlob, 0, res);
                    total += res;
                }
                if (printer != null)
                    printer.End(total);
                dataStream.Dispose();
            }
        }

        private Stream OpenDeltaCodecStream(Stream stream, out Stream baseFileStream, out FileInfo tempFileName)
        {
            string filename;
            lock (this)
            {
                do
                {
                    filename = Path.GetRandomFileName();
                } while (TempFiles.Contains(filename));
                TempFiles.Add(filename);
            }
            string tempFn = Path.Combine(TempFolder.FullName, filename);
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

            tempFileName = new FileInfo(tempFn);
            using (var tempStream = GetStreamForLookup(baseLookup))
            using (var tempStreamOut = tempFileName.Create())
            {
                tempStream.CopyTo(tempStreamOut);
            }

            baseFileStream = tempFileName.OpenRead();

            switch ((CompressionMode)(data & 0x0FFF))
            {
                case CompressionMode.LZ4:
                case CompressionMode.LZ4HC:
                    return new LZ4ReaderStream(deltaLength, stream);
                case CompressionMode.LZHAM:
                    return new LZHAMReaderStream(deltaLength, stream);
                case CompressionMode.None:
                    return new RestrictedStream(stream, deltaLength);
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
            switch ((CompressionMode)(data & 0x0FFF))
            {
                case CompressionMode.None:
                    return new RestrictedStream(stream, length);
                case CompressionMode.LZHAM:
                    return new LZHAMReaderStream(length, stream);
                case CompressionMode.LZ4:
                    return new LZ4ReaderStream(length, stream);
                case CompressionMode.LZ4HC:
                    return new LZ4ReaderStream(length, stream);
                default:
                    throw new Exception();
            }
        }

        private Stream OpenLegacyStream(FileObjectStoreData storeData)
        {
            if (storeData.BlobID.HasValue)
                return new MemoryStream(BlobDatabase.Get<Blobject>(storeData.BlobID.Value).Data);
            FileInfo info = GetFileForDataID(storeData.Lookup);
            if (info == null)
                return null;
            return info.OpenRead();
        }

        private string GetLookup(Record record)
        {
            return record.Fingerprint + "-" + record.Size.ToString();
        }

        internal override RecordInfo GetInfo(Record x)
        {
            if (!x.HasData)
            {
                return new RecordInfo()
                {
                    AllocatedSize = 0,
                    DeltaCompressed = false,
                    ID = -1,
                };
            }
            string lookup = GetLookup(x);
            var storeData = ObjectDatabase.Find<FileObjectStoreData>(lookup);
            if (storeData == null)
                return null;
            return new RecordInfo()
            {
                AllocatedSize = GetTransmissionLengthInternal(storeData),
                DeltaCompressed = storeData.Mode == StorageMode.Delta,
                ID = storeData.ID
            };
        }

        internal override long GetEntryCount()
        {
            return ObjectDatabase.Table<FileObjectStoreData>().Count();
        }
    }
}
