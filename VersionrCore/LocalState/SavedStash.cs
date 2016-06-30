using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.LocalState
{
    class SavedStash
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }
        public string StashCode { get; set; }
        public string Filename { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public Guid GUID { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
