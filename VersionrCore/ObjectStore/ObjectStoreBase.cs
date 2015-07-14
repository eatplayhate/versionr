using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.ObjectStore
{
    public class ObjectStoreTransaction
    {

    }
    public interface ObjectStoreBase
    {
        void Create(Area owner);
        bool Open(Area owner);
        ObjectStoreTransaction BeginStorageTransaction();
        bool RecordData(Objects.Record newRecord, Objects.Record priorRecord, Entry fileEntry);
        bool HasData(Objects.Record recordInfo);
        bool EndStorageTransaction(ObjectStoreTransaction transaction);
        bool RestoreData(Objects.Record record, string pathOverride = null);
    }
}
