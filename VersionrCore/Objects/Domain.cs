using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    public class Domain
    {
        [SQLite.PrimaryKey]
        public Guid InitialRevision { get; set; }
        public Guid? JournalTip { get; set; }
    }
}
