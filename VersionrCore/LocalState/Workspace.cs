using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.LocalState
{
    public class Workspace
    {
        [SQLite.PrimaryKey]
        public Guid ID { get; set; }
        public Guid Branch { get; set; }
        public Guid Tip { get; set; }
        public Guid Domain { get; set; }
        public string Name { get; set; }
        public DateTime LocalCheckoutTime { get; set; }
        public string PartialPath { get; set; }

        public static Workspace Create()
        {
            Workspace ws = new Workspace();
            ws.ID = Guid.NewGuid();
            return ws;
        }
    }
}
