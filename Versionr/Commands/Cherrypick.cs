using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Commands
{
    class CherrypickVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} version", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Cherry picks changes from a specific version and attempts to apply them to the working vault."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "cherry-pick";
            }
        }

        [Option("reverse", DefaultValue = false, HelpText = "Reverse the application process.")]
        public bool Reverse { get; set; }

        [Option("relaxed", DefaultValue = false, HelpText = "Allow patches to be applied even if incomplete.")]
        public bool Relaxed { get; set; }

        [ValueOption(0)]
        public string Version { get; set; }
    }
    class Cherrypick : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            CherrypickVerbOptions localOptions = options as CherrypickVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            var version = ws.GetPartialVersion(localOptions.Version);
            if (version == null)
            {
                Printer.PrintError("Could't identify source version to cherrypick from!");
                return false;
            }
            ws.Cherrypick(version, localOptions.Relaxed, localOptions.Reverse);
            return true;
        }
    }
}
