using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.LocalState
{
    public class CachedRecords
    {
        [SQLite.PrimaryKey]
        public Guid AssociatedVersion { get; set; }
        public int Version { get; set; }
        public byte[] Data { get; set; }
    }
}
