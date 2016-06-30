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

        [Option('d', "delete", DefaultValue = false, HelpText = "Deletes the stash after applying it.")]
        public bool Delete { get; set; }

        [Option('s', "stage", DefaultValue = true, HelpText = "Records changes made by the stash application.")]
        public bool Stage { get; set; }

        [Option("no-moves", DefaultValue = false, HelpText = "Disables stash move actions.")]
        public bool NoMoves { get; set; }

        [Option("no-deletes", DefaultValue = false, HelpText = "Disables stash delete actions.")]
        public bool NoDeletes { get; set; }

        [Option("relaxed", DefaultValue = false, HelpText = "Allow patches to be applied even if incomplete.")]
        public bool Relaxed { get; set; }

        [Option("reverse", DefaultValue = false, HelpText = "Reverse the application process.")]
        public bool Reverse { get; set; }

        [ValueOption(0)]
        public string Name { get; set; }
    }
    class Unstash : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object _options)
        {
            UnstashVerbOptions localOptions = _options as UnstashVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;

            Area.StashInfo stash = ws.FindStash(localOptions.Name);

            if (stash == null)
                Printer.PrintMessage("#e#Error:## Couldn't find a stash matching a name/key/ID of \"{0}\".", localOptions.Name);
            else
            {
                Area.ApplyStashOptions options = new Area.ApplyStashOptions();
                options.DisallowMoves = localOptions.NoMoves;
                options.DisallowDeletes = localOptions.NoDeletes;
                options.StageOperations = localOptions.Stage;
                options.AllowUncleanPatches = localOptions.Relaxed;
                options.Reverse = localOptions.Reverse;
                ws.Unstash(stash, options, localOptions.Delete);
            }

            return true;
        }
    }
}
