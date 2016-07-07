using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    [Flags]
    public enum AnnotationFlags
    {
        Normal = 0,
        File = 1,
        Binary = 2
    }
    [ProtoBuf.ProtoContract]
    public class Annotation
    {
        [SQLite.PrimaryKey]
        [ProtoBuf.ProtoMember(1)]
        public Guid ID { get; set; }
        [ProtoBuf.ProtoMember(2)]
        [SQLite.Indexed]
        public Guid Version { get; set; }
        [ProtoBuf.ProtoMember(3)]
        [SQLite.Indexed]
        public string Key { get; set; }
        [ProtoBuf.ProtoMember(4)]
        public byte[] Value { get; set; }
        [ProtoBuf.ProtoMember(5)]
        public string Author { get; set; }
        [ProtoBuf.ProtoMember(6)]
        public DateTime Timestamp { get; set; }
        [ProtoBuf.ProtoMember(7)]
        public AnnotationFlags Flags { get; set; }
        [ProtoBuf.ProtoMember(8)]
        public bool Active { get; set; }
    }
}
