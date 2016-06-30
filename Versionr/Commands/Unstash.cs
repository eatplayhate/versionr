using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Commands
{
    class UnstashVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [stash name or guid]", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Applies a set of patches stored in a stash file to the working folder."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "unstash";
            }
        }

        [Option('a', "apply", DefaultValue = true, HelpText = "Applies the stash to the vault (rather than just reading the stash contents).")]
        bool Apply { get; set; }

        [ValueList(typeof(List<string>))]
        public IList<string> Name { get; set; }
    }
    class Unstash : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            UnstashVerbOptions localOptions = options as UnstashVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            if (localOptions.Name == null || localOptions.Name.Count == 0)
            {
                var stashes = ws.ListStashes();
                if (stashes.Count == 0)
                    Printer.PrintMessage("Vault has no stashes.");
                else
                {
                    Printer.PrintMessage("Vault has #b#{0}## stash{1} available:", stashes.Count, stashes.Count == 1 ? "" : "es");
                    foreach (var x in stashes)
                    {
                        Printer.PrintMessage(" #b#{0}##: - #q#{4}##\n    {1} - by {2} on #q#{3}##", x.Author + "-" + x.Key, string.IsNullOrEmpty(x.Name) ? "(no name)" : ("\"" + x.Name + "\""), x.Author, x.Time.ToLocalTime(), x.GUID);
                    }
                }
            }
            else
                ws.Unstash(localOptions.Name[0]);
            return true;
        }
    }
}
