using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.LocalState
{
    public enum StageOperationType
    {
        Add,
        Remove,
        Rename,
        Conflict,
        Merge,
        MergeRecord,
        Reintegrate,
    }
    public class StageOperation
    {
        [SQLite.AutoIncrement, SQLite.PrimaryKey]
        public int Index { get; set; }
        public StageOperationType Type { get; set; }
        public string Operand1 { get; set; }
        public string Operand2 { get; set; }
        public int Flags { get; set; }
        public long ReferenceObject { get; set; }
        public long ReferenceTime { get; set; }

        [SQLite.Ignore]
        public bool IsFileOperation
        {
            get
            {
                return Type != StageOperationType.Merge;
            }
        }
    }
}
