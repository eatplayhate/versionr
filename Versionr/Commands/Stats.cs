using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class StatsVerbOptions : VerbOptionBase
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Displays stats about the vault as well as the underlying object storage system."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "stats";
            }
        }

        [ValueOption(0)]
        public string Object { get; set; }

        public override BaseCommand GetCommand()
        {
            return new Stats();
        }
    }
    class Stats : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            StatsVerbOptions localOptions = options as StatsVerbOptions;
            Workspace.PrintStats(localOptions.Object);
            return true;
        }
    }
}
