using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{

    [ProtoBuf.ProtoContract]
    public class LockTokenList
    {
        [ProtoBuf.ProtoMember(1)]
        public List<Guid> Locks { get; set; }
    }

    [ProtoBuf.ProtoContract]
    public class RequestLockInformation
    {
        [ProtoBuf.ProtoMember(1)]
        public Guid? Branch { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public string Path { get; set; }
        [ProtoBuf.ProtoMember(3)]
        public string Author { get; set; }
        [ProtoBuf.ProtoMember(4)]
        public bool Full { get; set; }
        [ProtoBuf.ProtoMember(5)]
        public bool Steal { get; set; }
    }

    [ProtoBuf.ProtoContract]
    public class LockGrantInformation
    {
        [ProtoBuf.ProtoMember(1)]
        public Guid LockID { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public LockConflictInformation BrokenLocks { get; set; }
    }

    [ProtoBuf.ProtoContract]
    public class LockConflictInformation
    {
        [ProtoBuf.ProtoMember(1)]
        public List<Objects.VaultLock> Conflicts;
    }
}
