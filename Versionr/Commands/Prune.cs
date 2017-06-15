using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class PruneVerbOptions : VerbOptionBase
    {
        public override BaseCommand GetCommand()
        {
            return new Prune();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Prunes object data from older versions. Make sure the data has been stored somewhere!"
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "prune";
            }
        }
    }
    class Prune : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            PruneVerbOptions localOptions = options as PruneVerbOptions;

            Area.PruneOptions pruneOptions = new Area.PruneOptions();
            Workspace.Prune(pruneOptions);

            return true;
        }
    }
}
