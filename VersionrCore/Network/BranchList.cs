using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    [ProtoBuf.ProtoContract]
    internal class BranchList
    {
        [ProtoBuf.ProtoMember(1)]
        public Objects.Branch[] Branches { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public Objects.Version[] ImportantVersions { get; set; }
        [ProtoBuf.ProtoMember(3)]
        public KeyValuePair<Guid, Guid>[] Heads { get; set; }
    }
}
