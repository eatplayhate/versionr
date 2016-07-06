using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    public class Tag
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }
        [SQLite.Indexed]
        public Guid Version { get; set; }
        [SQLite.Indexed]
        public string TagValue { get; set; }
    }
}
