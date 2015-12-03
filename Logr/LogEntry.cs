using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace Logr
{
    public class LogEntry
    {
        [XmlAttribute("branch")]
        public string Branch { get; set; }
        [XmlAttribute("revision")]
        public uint Revision { get; set; }

        [XmlElement("author")]
        public string Author { get; set; }
        [XmlElement("date")]
        public string Date { get; set; }
        [XmlElement("msg")]
        public string Message { get; set; }

        public LogEntry() {}

        public LogEntry(string branch, uint revision, string author, string data, string message)
        {
            Branch = branch;
            Revision = revision;
            Author = author;
            Date = data;
            Message = message;
        }


    }
}
