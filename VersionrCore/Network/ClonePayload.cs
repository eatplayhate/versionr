using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    [ProtoBuf.ProtoContract]
    internal class ClonePayload
    {
        [ProtoBuf.ProtoMember(1)]
        public Objects.Version RootVersion { get; set; }

        [ProtoBuf.ProtoMember(2)]
        public Objects.Branch InitialBranch { get; set; }
    }
}
