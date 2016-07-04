using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.LocalState
{
    public class RemoteLock
    {
        [SQLite.PrimaryKey]
        public Guid ID { get; set; }
        public string RemoteHost { get; set; }
        public string LockingPath { get; set; }
        public Guid? LockedBranch { get; set; }
    }
}
