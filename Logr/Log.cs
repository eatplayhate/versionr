using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Versionr;
using Versionr.Objects;
using Version = Versionr.Objects.Version;

namespace Logr
{
    [XmlRoot(ElementName = "log")]
    public class Log
    {
        private Area m_Area;
        private List<Branch> m_Branches = new List<Branch>();
        private List<Version> m_Versions = new List<Version>();
        private int? m_Limit;
        private Log m_PreviousLog = null;

        private string m_Destination;

        [XmlElement("logentry")]
        public List<LogEntry> LogEntries = new List<LogEntry>();

        public Log() { }

        public Log(string directory, string destination, int? limit = null)
        {
            m_Destination = destination;
            m_Limit = limit;
            m_Area = Area.Load(new DirectoryInfo(directory));
            foreach (Branch branch in m_Area.Branches)
                m_Branches.Add(branch);

            if (m_Branches.Count == 0)
            {
                Console.WriteLine("No branches found in area!");
                Environment.Exit(1);
            }
            
            // Attempt to load any previous log file as it will contain the previous build statuses.
            m_PreviousLog = Load(m_Destination);
        }

        public void Update()
        {
            foreach (Branch branch in m_Branches)
                m_Versions.AddRange(m_Area.GetLogicalHistory(m_Area.GetBranchHeadVersion(branch), m_Limit));

            m_Versions = m_Versions.OrderByDescending(o => o.Timestamp).ToList();
        }

        public string ToJavascriptTimestamp(DateTime timestamp)
        {
            return timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        public void Serialize()
        {
            LogEntries.Clear();

            Version buildVersion = null;
            Guid buildVersionID;
            if (Guid.TryParse(Program.Options.BuildVersionID, out buildVersionID))
                buildVersion = m_Area.GetVersion(buildVersionID);
            
            IEnumerable<Version> versions = (m_Limit.HasValue && m_Limit.Value > 0) ? m_Versions.Take(m_Limit.Value) : m_Versions;
            foreach (Version version in versions)
            {
                LogEntries.Add(new LogEntry(m_Area.GetBranch(version.Branch).Name, version.Revision, version.ID, version.Author, ToJavascriptTimestamp(version.Timestamp), version.Message, GetBuildStatus(version, buildVersion)));
            }

            try
            {
                XmlSerializer seralizer = new XmlSerializer(typeof(Log));
                using (StreamWriter writer = new StreamWriter(m_Destination))
                {
                    seralizer.Serialize(writer, this);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(String.Format("Failed to write file {0}\n{1}", m_Destination, ex.Message));
            }
        }

        private static Log Load(string path)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(Log));

            Log result = null;
            try
            {
                using (StreamReader reader = new StreamReader(path))
                {
                    result = (Log)serializer.Deserialize(reader);
                }
            }
            catch { }

            return result;
        }

        private BuildStatus GetBuildStatus(Version version, Version buildVersion)
        {
            if (m_PreviousLog == null)
            {
                // No previous log - starting from scratch
                if (buildVersion != null && version.Branch != buildVersion.Branch)
                    return BuildStatus.Pending;
                else
                    return Program.Options.Status;
            }

            // Newer than specified version and on the same branch, must be pending
            if (buildVersion != null && version.Branch == buildVersion.Branch && version.Timestamp > buildVersion.Timestamp)
                return BuildStatus.Pending;

            // Check for an existing entry for this version
            LogEntry oldEntry = m_PreviousLog.LogEntries.FirstOrDefault(x => x.ID == version.ID);
            if (oldEntry != null)
            {
                // Version previously passed or failed.
                if (oldEntry.Status == BuildStatus.Passed || oldEntry.Status == BuildStatus.Failed)
                    return oldEntry.Status;
                
                // Once it's building, it can only go to pass or fail
                if (oldEntry.Status == BuildStatus.Building && Program.Options.Status != BuildStatus.Passed && Program.Options.Status != BuildStatus.Failed)
                    return oldEntry.Status;

                // Don't change entries on a different branch
                if (buildVersion != null && version.Branch != buildVersion.Branch)
                    return oldEntry.Status;
            }

            // Newer than specified version and on different branch, but no previous version, must be pending
            if (buildVersion != null && version.Timestamp > buildVersion.Timestamp)
                return BuildStatus.Pending;

            // Everything else gets the passed in status
            return Program.Options.Status;
        }
    }
}
