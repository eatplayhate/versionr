using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class BranchVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [options] [target]", Verb);
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
                return "branch";
            }
        }

        [ValueList(typeof(List<string>))]
        public IList<string> Target { get; set; }
    }
    class Branch : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            BranchVerbOptions localOptions = options as BranchVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            if (localOptions.Target.Count != 1)
            {
                return false;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(localOptions.Target[0], "^[A-Za-z0-9_-]+$"))
                ws.Branch(localOptions.Target[0]);
            else
            {
                Printer.PrintError("#e#Error: branch name is not valid.");
                return false;
            }
            return true;
        }
    }
}
