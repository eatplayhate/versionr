using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.LocalState
{
    public class FileTimestamp
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }
        [SQLite.Indexed]
        public string CanonicalName { get; set; }
        public DateTime LastSeenTime { get; set; }
        public string DataIdentifier { get; set; }
    }
}
