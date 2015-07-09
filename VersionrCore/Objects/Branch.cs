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
        public bool Deleted { get; set; }
        public static Branch Create(string name, Guid? parent = null)
        {
            Branch b = new Branch();
            b.ID = Guid.NewGuid();
            b.Parent = parent;
            b.Name = name;
            b.Deleted = false;
            return b;
        }
    }
}
