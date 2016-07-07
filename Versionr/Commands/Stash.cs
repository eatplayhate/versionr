using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Commands
{
    class StashVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## #q#[options]## [stash description]", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Stash all currently staged changes to a named stash file and (optionally, but by default) reverts them to a pristine state."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "stash";
            }
        }
        [Option('r', "revert", DefaultValue = true, HelpText = "Reverts file changes once they are stashed away.")]
        public bool Revert { get; set; }

        [ValueList(typeof(List<string>))]
        public string Name { get; set; }

        public override BaseCommand GetCommand()
        {
            return new Stash();
        }
    }
    class Stash : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            StashVerbOptions localOptions = options as StashVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            ws.Stash(localOptions.Name != null ? string.Join(" ", localOptions.Name.ToArray()) : null, localOptions.Revert, Unrecord.UnrecordFeedback);
            if (localOptions.Revert)
            {
                if (ws.HasPendingMerge)
                {
                    Printer.PrintMessage("#b#Info:## Current vault has an outstanding pending merge that will be attached to the next commit.\nIf you wish to clear this merge information, you will need to use the #b#checkout## command.");
                }
            }
            return true;
        }
    }
}
