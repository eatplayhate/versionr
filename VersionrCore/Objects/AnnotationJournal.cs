using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    [ProtoBuf.ProtoContract]
    public class AnnotationJournal
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }
        [ProtoBuf.ProtoMember(1)]
        public Guid JournalID { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public long SequenceID { get; set; }
        [ProtoBuf.ProtoMember(3)]
        public Guid Value { get; set; }
        [ProtoBuf.ProtoMember(4)]
        public bool Delete { get; set; }
    }
}
