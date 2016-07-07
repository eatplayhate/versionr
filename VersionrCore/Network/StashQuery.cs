using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    [ProtoBuf.ProtoContract]
    internal class StashQuery
    {
        [ProtoBuf.ProtoMember(1)]
        public List<string> FilterNames { get; set; }
    }
    [ProtoBuf.ProtoContract]
    internal class StashQueryResults
    {
        [ProtoBuf.ProtoMember(1)]
        public List<Area.StashInfo> Results { get; set; }
    }
}
