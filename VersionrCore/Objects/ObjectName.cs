using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    [ProtoBuf.ProtoContract]
    public class ObjectName
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long NameId { get; set; }
        [ProtoBuf.ProtoMember(1)]
        [SQLite.Unique]
        public string CanonicalName { get; set; }
    }
    public class ObjectNameOld
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }
        [SQLite.Indexed]
        public string CanonicalName { get; set; }
    }
}
