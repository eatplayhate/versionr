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
        public override BaseCommand GetCommand()
        {
            return new Cherrypick();
        }

        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## #q#[options]## <version> #q#[version2 ... versionN]##", Verb);
            }
        }
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Cherry picks changes from a specific version and attempts to apply them to the working vault.",
                    "",
                    "Optionally, it can apply the changes in reverse to undo a specified version."
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

        [ValueList(typeof(List<string>))]
        public List<string> Versions { get; set; }
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
            foreach (var vname in localOptions.Versions)
            {
                var version = ws.GetPartialVersion(vname);
                if (version == null)
                {
                    Printer.PrintError("Could't identify source version to cherrypick from (specified name is \"{0}\")!", vname);
                    return false;
                }
                ws.Cherrypick(version, localOptions.Relaxed, localOptions.Reverse);
            }
            return true;
        }
    }
}
