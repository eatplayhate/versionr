﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    public enum AnnotationFlags
    {
        Normal
    }
    public class Annotation
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }
        [SQLite.Indexed]
        public Guid Version { get; set; }
        [SQLite.Indexed]
        public string Key { get; set; }
        public string Value { get; set; }
        public DateTime Timestamp { get; set; }
        public AnnotationFlags Flags { get; set; }
    }
}
