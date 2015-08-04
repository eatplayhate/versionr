using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.LocalState
{
    public class LockingObject
    {
        [SQLite.PrimaryKey]
        public long Id { get; set; }
        public DateTime LockTime { get; set; }
    }
}
