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
        [Option("sql-local", Required = false, HelpText = "Runs an JSON-wrapped SQL command on the local cache DB")]
        public string SQLLocal { get; set; }
        [Option("sql", Required = false, HelpText = "Runs an JSON-wrapped SQL command on the main DB")]
        public string SQL { get; set; }
        [Option("replicate", Required = false, HelpText = "Marks the admin command as replicatable (if possible).")]
        public bool Replicate { get; set; }
        [Option("check", Required = false, HelpText = "Runs a general purpose DB consistency check and repair function.")]
        public bool Check { get; set; }
        [Option("vacuum", Required = false, HelpText = "Runs the SQLite VACUUM instruction on the master DB.")]
        public bool Vacuum { get; set; }
    }
    class Admin : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            AdminVerbOptions localOptions = options as AdminVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            if (localOptions.Check)
                Workspace.RunConsistencyCheck();
            if (localOptions.Vacuum)
                Workspace.RunVacuum();
            return true;
        }
    }
}
