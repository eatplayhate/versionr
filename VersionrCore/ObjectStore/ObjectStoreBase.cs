using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;

namespace Versionr.ObjectStore
{
    public abstract class ObjectStoreTransaction
    {
        public abstract long PendingRecordBytes { get; }
        public abstract int PendingRecords { get; }
    }
    public class RecordInfo
    {
        public long AllocatedSize { get; set; }
        public bool DeltaCompressed { get; set; }
        public long ID { get; set; }
    }
    public abstract class ObjectStoreBase
    {
        public abstract void Create(Area owner);
        public abstract bool Open(Area owner);
        public abstract ObjectStoreTransaction BeginStorageTransaction();
        public abstract bool RecordData(ObjectStoreTransaction transaction, Objects.Record newRecord, Objects.Record priorRecord, Entry fileEntry);
        public abstract bool ReceiveRecordData(ObjectStoreTransaction transaction, string directName, System.IO.Stream dataStream, out string dependency);
        public abstract bool TransmitRecordData(Record record, Func<byte[], int, bool, bool> sender, byte[] scratchBuffer, Action beginTransmission = null);
        public abstract System.IO.Stream GetRecordStream(Objects.Record record);
        public abstract long GetTransmissionLength(Record record);
        public abstract bool HasData(Objects.Record recordInfo, out List<string> requestedData);
        public abstract bool AbortStorageTransaction(ObjectStoreTransaction transaction);
        public abstract bool FlushStorageTransaction(ObjectStoreTransaction transaction);
        public abstract bool EndStorageTransaction(ObjectStoreTransaction transaction);
        public abstract void WriteRecordStream(Record rec, System.IO.Stream outputStream);
        public abstract bool HasDataDirect(string x, out List<string> requestedData);
        internal abstract RecordInfo GetInfo(Record x);
        internal abstract long GetEntryCount();
        public virtual bool HasData(Record recordInfo)
        {
            List<string> ignored;
            return HasData(recordInfo, out ignored);
        }
    }
}
