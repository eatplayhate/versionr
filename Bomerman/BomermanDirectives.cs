using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;
using Versionr.Commands;
using Versionr.Utilities;

namespace Bomerman
{
    public class BomermanOptions
    {
        [HelpOption]
        public string GetUsage()
        {
            var help = new CommandLine.Text.HelpText
            {
                Heading = new CommandLine.Text.HeadingInfo("Plugin: #b#Bomerman##"),
                AddDashesToOption = false,
            };
            help.AddPreOptionsLine("Versionr plugin for dealing with merges with dirty whitespace/newlines/BOM changes.\n\n#b#Commands:");
            help.ShowHelpOption = false;
            help.AddOptions(this);
            return help;
        }

        [VerbOption("bom-clean", HelpText = "Cleans useless text changes from a set of files.")]
        public BomCleanOptions bomclean { get; set; }

        [VerbOption("bom-unstage", HelpText = "Purges erroneously staged files.")]
        public BomUnstageOptions bomunstage { get; set; }

        [VerbOption("set-eol", HelpText = "Sets the line ending style of a file or set of files.")]
        public BomSetEOLOptions bomseteol { get; set; }

        [VerbOption("entab", HelpText = "Converts spaces to tabs.")]
        public BomEntabOptions bomentab { get; set; }

        [VerbOption("detab", HelpText = "Converts tabs to spaces.")]
        public BomDetabOptions bomdetab { get; set; }
    }
}
