using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    class WebConfig
    {
        public int HttpPort { get; set; }
        public string HttpSubdirectory { get; set; }
        public bool ProvideBinaries { get; set; }
        public WebConfig()
        {
            ProvideBinaries = true;
        }
    }
}
