using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    public enum MergeType
    {
        Normal = 0,
    }

    [ProtoBuf.ProtoContract]
    public class MergeInfo
    {
        [ProtoBuf.ProtoIgnore]
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }

        [ProtoBuf.ProtoMember(1)]
        [SQLite.Indexed]
        public Guid DestinationVersion { get; set; }

        [ProtoBuf.ProtoMember(2)]
        public Guid SourceVersion { get; set; }

        [ProtoBuf.ProtoMember(3)]
        public MergeType Type { get; set; }
    }
}
