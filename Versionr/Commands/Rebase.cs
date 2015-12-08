using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class RebaseVerbOptions : VerbOptionBase
    {
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
                return "rebase";
            }
        }

        [Option('c', "collapse", DefaultValue = true, HelpText = "Collapses the changes from the rebase target to this node to a single operation.")]
        public bool Collapse { get; set; }

        [ValueOption(0)]
        public string Target { get; set; }

        [Option('m', "message", HelpText = "Commit message for rebased version.")]
        public string Message { get; set; }
    }
    class Rebase : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            RebaseVerbOptions localOptions = options as RebaseVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            if (string.IsNullOrEmpty(localOptions.Message))
            {
                Printer.PrintError("#e#Error: Rebase requires commit message.");
                return false;
            }
            if (localOptions.Collapse)
            {
                // Step 1: get the rebase target node
                Objects.Version currentVersion = ws.Version;
                Objects.Version parentVersion = ws.GetPartialVersion(localOptions.Target);
                if (parentVersion == null)
                {
                    Printer.PrintError("#e#Error: Can't identify parent version for rebase with name #b#\"{0}\"#e#.", localOptions.Target);
                    return false;
                }
                if (parentVersion.ID == currentVersion.ID)
                {
                    Printer.PrintError("#e#Error: Rebase parent can't be the current version.");
                    return false;
                }
                return ws.RebaseCollapse(currentVersion, parentVersion, localOptions.Message);
            }
            else
            {
                Printer.PrintError("#e#Error: rebase only works when collapsing operations.");
                return false;
            }
            return true;
        }
    }
}
