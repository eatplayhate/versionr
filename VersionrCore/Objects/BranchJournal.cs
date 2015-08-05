using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    public enum BranchAlterationType
    {
        Rename,
        Terminate,
        Merge
    }

    [ProtoBuf.ProtoContract]
    public class BranchJournal
    {
        [ProtoBuf.ProtoMember(1)]
        [SQLite.PrimaryKey]
        public Guid ID { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public Guid Branch { get; set; }
        [ProtoBuf.ProtoMember(3)]
        public string Operand { get; set; }
        [ProtoBuf.ProtoMember(4)]
        public BranchAlterationType Type { get; set; }
    }

    [ProtoBuf.ProtoContract]
    public class BranchJournalLink
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }

        [ProtoBuf.ProtoMember(1)]
        [SQLite.Indexed]
        public Guid Link { get; set; }

        [ProtoBuf.ProtoMember(2)]
        public Guid Parent { get; set; }
    }

    [ProtoBuf.ProtoContract]
    public class BranchJournalPack
    {
        [ProtoBuf.ProtoMember(1)]
        public BranchJournal Payload { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public List<Guid> Parents { get; set; }
    }
}
