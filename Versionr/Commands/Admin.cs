using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Network;

namespace Versionr.Commands
{
    class AdminVerbOptions : VerbOptionBase
    {
        public override BaseCommand GetCommand()
        {
            return new Admin();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Runs a special administration command. For advanced users only."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "admin";
            }
        }
        [Option("sql-local", HelpText = "Runs an JSON-wrapped SQL command on the local cache DB")]
        public string SQLLocal { get; set; }
        [Option("sql", HelpText = "Runs an JSON-wrapped SQL command on the main DB")]
        public string SQL { get; set; }
        [Option("replicate", HelpText = "Marks the admin command as replicatable (if possible).")]
        public bool Replicate { get; set; }
    }
    class Admin : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            AdminVerbOptions localOptions = options as AdminVerbOptions;
            return true;
        }
    }
}
