using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    [ProtoBuf.ProtoContract]
    public class VaultLock
    {
        [ProtoBuf.ProtoMember(1)]
        [SQLite.PrimaryKey]
        public Guid ID { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public Guid? Branch { get; set; }
        [ProtoBuf.ProtoMember(3)]
        public string Path { get; set; }
        [ProtoBuf.ProtoMember(4)]
        public string User { get; set; }
    }
}
