using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
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
    }
}
