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
}
