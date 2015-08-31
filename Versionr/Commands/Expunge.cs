using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class ExpungeVerbOptions : VerbOptionBase
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
                    "abandon all ships"
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "expunge";
            }
        }

        [ValueOption(0)]
        public string Target { get; set; }
    }
    class Expunge : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            ExpungeVerbOptions localOptions = options as ExpungeVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            Objects.Version version;
            if (string.IsNullOrEmpty(localOptions.Target))
            {
                version = ws.Version;
            }
            else if (!ws.FindVersion(localOptions.Target, out version))
            {
                Printer.PrintError("Can't find version with partial name \"{0}.\"", localOptions.Target);
                return false;
            }
            return ws.ExpungeVersion(version);
        }
    }
}
