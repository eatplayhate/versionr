using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;
using System.Xml.Serialization;
using Versionr;
using Versionr.Objects;

using Version = Versionr.Objects.Version;

namespace Logr
{
    [XmlRoot(ElementName = "log")]
    public class Log
    {
        private const int LIMIT = 10;

        private Area m_Area;
        private List<Branch> m_Branches = new List<Branch>();
        private List<Version> m_Versions = new List<Version>();

        private string m_Destination;

        [XmlElement("logentry")]
        public List<LogEntry> LogEntries = new List<LogEntry>();

        public Log() {}

        public Log(string directory, string destination)
        {
            m_Destination = destination;
            m_Area = Area.Load(new DirectoryInfo(directory));
            foreach (Branch branch in m_Area.Branches)
                m_Branches.Add(branch);

            if (m_Branches.Count == 0)
            {
                Console.WriteLine("No branches found in area!");
                Environment.Exit(1);
            }
        }

        public void Update()
        {
            foreach (Branch branch in m_Branches)
                m_Versions.AddRange(m_Area.GetLogicalHistory(m_Area.GetBranchHeadVersion(branch), LIMIT));

            m_Versions = m_Versions.OrderByDescending(o => o.Timestamp).ToList();
        }

        public string ToJavascriptTimestamp(DateTime timestamp)
        {
            return timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        public void Serialize()
        {
            LogEntries.Clear();

            foreach (Version version in m_Versions)
                LogEntries.Add(new LogEntry(m_Area.GetBranch(version.Branch).Name, version.Revision, version.Author, ToJavascriptTimestamp(version.Timestamp), version.Message));

            XmlSerializer seralizer = new XmlSerializer(this.GetType());
            using (StreamWriter writer = new StreamWriter(Path.Combine(m_Destination, "VersionrLog.xml")))
            {
                seralizer.Serialize(writer, this);
            }
        }

    }
}
