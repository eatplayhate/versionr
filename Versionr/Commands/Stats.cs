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
        public override string Usage
        {
            get
            {
                return "#b#versionr #i#stats## #q#[object]##";
            }
        }
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "coming soon"
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
