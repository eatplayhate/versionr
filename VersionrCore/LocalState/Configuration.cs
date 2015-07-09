using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.LocalState
{
    public class Configuration
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }
        public int Version { get; set; }
        public Guid WorkspaceID { get; set; }
    }
}
