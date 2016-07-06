using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    [Flags]
    public enum AnnotationFlags
    {
        Normal = 0,
        File = 1,
        Binary = 2
    }
    public class Annotation
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public Guid ID { get; set; }
        [SQLite.Indexed]
        public Guid Version { get; set; }
        [SQLite.Indexed]
        public string Key { get; set; }
        public byte[] Value { get; set; }
        public string Author { get; set; }
        public DateTime Timestamp { get; set; }
        public AnnotationFlags Flags { get; set; }
    }
}
