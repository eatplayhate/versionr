using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;

namespace Versionr.ObjectStore
{
    public class ObjectStoreTransaction
    {

    }
    public abstract class ObjectStoreBase
    {
        public abstract void Create(Area owner);
        public abstract bool Open(Area owner);
        public abstract ObjectStoreTransaction BeginStorageTransaction();
        public abstract bool RecordData(ObjectStoreTransaction transaction, Objects.Record newRecord, Objects.Record priorRecord, Entry fileEntry);
        public abstract bool ReceiveRecordData(ObjectStoreTransaction transaction, string directName, System.IO.Stream dataStream, out string dependency);
        public abstract bool TransmitRecordData(Record record, Func<byte[], int, bool, bool> sender, byte[] scratchBuffer);
        public abstract System.IO.Stream GetRecordStream(Objects.Record record);
        public abstract long GetTransmissionLength(Record record);
        public abstract bool HasData(Objects.Record recordInfo);
        public abstract bool AbortStorageTransaction(ObjectStoreTransaction transaction);
        public abstract bool EndStorageTransaction(ObjectStoreTransaction transaction);
        public abstract void WriteRecordStream(Record rec, System.IO.Stream outputStream);
        public abstract bool HasDataDirect(string x);
    }
}
