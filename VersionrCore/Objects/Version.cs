using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    [ProtoBuf.ProtoContract]
    public class Version
    {
        [ProtoBuf.ProtoMember(1)]
        [SQLite.PrimaryKey]
        public Guid ID { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public string Author { get; set; }
        [ProtoBuf.ProtoMember(3)]
        public string Message { get; set; }
        [ProtoBuf.ProtoMember(4)]
        public bool Published { get; set; }
        [ProtoBuf.ProtoMember(5)]
        public Guid Branch { get; set; }
        [ProtoBuf.ProtoMember(6)]
        public Guid? Parent { get; set; }
        [ProtoBuf.ProtoMember(7)]
        public DateTime Timestamp { get; set; }
        [ProtoBuf.ProtoIgnore]
        public long? Snapshot { get; set; }
        [ProtoBuf.ProtoIgnore]
        public long AlterationList { get; set; }
        [ProtoBuf.ProtoMember(8)]
        [SQLite.LoadOnly, SQLite.Column("rowid")]
        public uint Revision { get; set; }

        [SQLite.Ignore]
        [ProtoBuf.ProtoIgnore]
        public string ShortName
        {
            get
            {
                return ID.ToString().Substring(0, 8);
            }
        }

        public static Version Create()
        {
            Version vs = new Version();
            vs.ID = Guid.NewGuid();
            vs.Published = false;
            return vs;
        }
    }
}
