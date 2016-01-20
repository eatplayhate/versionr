using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;
using System.Xml.Serialization;

namespace Automatr
{

    public class AutomatrTask
    {
        [XmlElement]
        public string Command { get; set; }

        [XmlElement]
        public string Args { get; set; }
    }


    [XmlRoot(ElementName = "AutomatrConfig")]
    public class AutomatrConfig
    {
        [XmlElement]
        public string Path { get; set; }

        [XmlElement]
        public string BranchName { get; set; }

        [XmlArray("Tasks")]
        [XmlArrayItem("Task")]
        public List<AutomatrTask> Tasks { get; set; }

        public AutomatrConfig()
        {
            BranchName = "master";
            Tasks = new List<AutomatrTask>();
        }

        public static AutomatrConfig Load(string path)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(AutomatrConfig));

            AutomatrConfig result = null;
            try
            {
                using (StreamReader reader = new StreamReader(path))
                {
                    result = (AutomatrConfig)serializer.Deserialize(reader);
                }
                AutomatrLog.Log("Loaded config " + path, AutomatrLog.LogLevel.Verbose);
            }
            catch
            {
                result = new AutomatrConfig();
            }

            return result;
        }

        public void Write(string path)
        {
            XmlSerializer seralizer = new XmlSerializer(GetType());
            using (StreamWriter writer = new StreamWriter(path))
            {
                seralizer.Serialize(writer, this);
            }
            AutomatrLog.Log("Wrote config " + path, AutomatrLog.LogLevel.Verbose);
        }
    }

}
