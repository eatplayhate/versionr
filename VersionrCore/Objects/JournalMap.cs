using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    [ProtoBuf.ProtoContract]
    public class JournalMap
    {
        [SQLite.PrimaryKey]
        [ProtoBuf.ProtoMember(1)]
        public Guid JournalID { get; set; }
        [ProtoBuf.ProtoMember(2, IsRequired = false)]
        public long TagSequenceID { get; set; }
        [ProtoBuf.ProtoMember(3, IsRequired = false)]
        public long AnnotationSequenceID { get; set; }
    }
    
    [ProtoBuf.ProtoContract]
    public class JournalTips
    {
        [ProtoBuf.ProtoMember(1)]
        public List<JournalMap> Tips { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public Guid LocalJournal { get; set; }
    }

    [ProtoBuf.ProtoContract]
    public class JournalResults
    {
        [ProtoBuf.ProtoMember(1)]
        public List<TagJournal> Tags { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public List<AnnotationJournal> Annotations { get; set; }
        [ProtoBuf.ProtoMember(3)]
        public Dictionary<Guid, Annotation> AnnotationData { get; set; }
        [ProtoBuf.ProtoMember(4)]
        public JournalMap ReturnedJournalMap { get; set; }
    }
}
