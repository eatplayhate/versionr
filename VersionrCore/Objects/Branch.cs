using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    [ProtoBuf.ProtoContract]
    public class Branch
    {
        [ProtoBuf.ProtoMember(1)]
        [SQLite.PrimaryKey]
        public Guid ID { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public Guid? Parent { get; set; }
        [ProtoBuf.ProtoMember(3)]
        public string Name { get; set; }
        [ProtoBuf.ProtoMember(4)]
        public Guid? RootVersion { get; set; }
        [ProtoBuf.ProtoMember(5)]
        public Guid? Terminus { get; set; }
        public static Branch Create(string name, Guid? rootVersion, Guid? parent)
        {
            Branch b = new Branch();
            b.ID = Guid.NewGuid();
            b.Parent = parent;
            b.RootVersion = rootVersion;
            b.Name = name;
            return b;
        }

        [SQLite.Ignore]
        [ProtoBuf.ProtoIgnore]
        public string ShortID
        {
            get
            {
                return ID.ToString().Substring(0, 8);
            }
        }
    }

    public enum BranchMetaType
    {
        DisallowMerge,
    }

    public class BranchMetadata
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }

        [SQLite.Indexed]
        public Guid Branch { get; set; }

        public BranchMetaType Type { get; set; }
        public string Operand1 { get; set; }
        public string Operand2 { get; set; }
        public long Option { get; set; }
    }
}
