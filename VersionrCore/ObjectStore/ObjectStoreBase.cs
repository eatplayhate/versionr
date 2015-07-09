using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.ObjectStore
{
    public interface ObjectStoreBase
    {
        void Create(Area owner);
        bool Open(Area owner);
        bool BeginStorageTransaction();
        bool RecordData(Objects.Record newRecord, Objects.Record priorRecord, Entry fileEntry);
        bool EndStorageTransaction();
        bool RestoreData(Objects.Record record, string pathOverride = null);
    }
}
