using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Logr
{
    public class LogEntry
    {
        [XmlAttribute("branch")]
        public string Branch { get; set; }
        [XmlAttribute("revision")]
        public uint Revision { get; set; }
        [XmlAttribute("id")]
        public Guid ID { get; set; }

        [XmlElement("author")]
        public string Author { get; set; }
        [XmlElement("date")]
        public string Date { get; set; }
        [XmlElement("msg")]
        public string Message { get; set; }
        [XmlElement("status")]
        public BuildStatus Status { get; set; }

        [XmlArray("tags")]
        [XmlArrayItem(ElementName = "tag")]
        public List<string> Tags { get; set; } = new List<string>();


        public LogEntry() { }

        public LogEntry(string branch, uint revision, Guid id, string author, string data, string message, List<string> tags, BuildStatus status = BuildStatus.Pending)
        {
            Branch = branch;
            Revision = revision;
            ID = id;
            Author = author;
            Date = data;
            Message = message;
            Status = status;
            Tags = tags;
        }
    }
}
