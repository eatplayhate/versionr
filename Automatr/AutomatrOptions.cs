using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CommandLine;

namespace Automatr
{
    public class AutomatrOptions
    {

        [Option('v', "version", Required = false, HelpText = "Print version number")]
        public bool Version { get; set; }

        [Option("verbose", Required = false, HelpText = "Enable verbose output")]
        public bool Verbose { get; set; }

        [Option('c', "config", Required = false, HelpText = "Config Path")]
        public string ConfigPath { get; set; }

        [Option('f', "force", Required = false, HelpText = "Force running of tasks")]
        public bool Force { get; set; }

        public AutomatrOptions()
        {
            ConfigPath = "AutomatrConfig.xml";
        }

    }
}
