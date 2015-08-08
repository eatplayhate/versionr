using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class UpdateVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0}", Verb);
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
                return "update";
            }
        }
    }
    class Update : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            UpdateVerbOptions localOptions = options as UpdateVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            ws.Update();
            return true;
        }
    }
}
