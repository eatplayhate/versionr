using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    [ProtoBuf.ProtoContract]
    class StartClientTransaction
    {
        [ProtoBuf.ProtoMember(1)]
        public byte[] Key { get; set; }

        [ProtoBuf.ProtoMember(2)]
        public byte[] IV { get; set; }
    }
}
