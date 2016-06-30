using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Commands
{
    class StashDeleteVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [stash name or guids]", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Deletes stashes from the vault."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "stash-delete";
            }
        }

        [Option('a', "all", HelpText = "Deletes all stashes")]
        public bool All { get; set; }

        [ValueList(typeof(List<string>))]
        public List<string> Names { get; set; }
    }
    class StashDelete : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            StashDeleteVerbOptions localOptions = options as StashDeleteVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            if (localOptions.All)
            {
                var stashes = ws.ListStashes();
                if (stashes.Count == 0)
                    Printer.PrintMessage("Vault has no stashes.");
                else
                {
                    foreach (var stash in stashes)
                    {
                        Printer.PrintMessage("Deleting stash #b#{0}##: #q#{1}##", stash.Author + "-" + stash.Key, stash.GUID);
                        ws.DeleteStash(stash);
                    }
                }
            }
            else if (localOptions.Names.Count == 0)
                Printer.PrintMessage("#e#Error:## No stashes specified for deletion.");
            else
            {
                foreach (var x in localOptions.Names)
                {
                    var stash = StashList.LookupStash(ws, x);
                    if (stash != null)
                    {
                        Printer.PrintMessage("Deleting stash #b#{0}##: #q#{1}##", stash.Author + "-" + stash.Key, stash.GUID);
                        ws.DeleteStash(stash);
                    }
                }
            }
            return true;
        }
    }
}
