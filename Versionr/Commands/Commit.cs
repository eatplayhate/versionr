using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class CommitVerbOptions : VerbOptionBase
    {
        [Option('a', "all", DefaultValue = false, HelpText = "Commits all modified/renamed files whether they are staged or not.")]
        public bool AllModified { get; set; }

        [Option('f', "force", DefaultValue = false, HelpText = "Forces the commit to happen even if it would create a new branch head.")]
        public bool Force { get; set; }

        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [options] <additional files>", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "This command will record any staged changes into the Versionr",
                    "repository.",
                    "",
                    "The process will create a new version with its parent set to the",
                    "currently checked out revision. It will then update the current ",
                    "branch head information to point to the newly created version.",
                    "",
                    "When the `--all` option is specified, modified files that are under",
                    "version control are immediately committed.",
                    "",
                    "If you are committing from a version which is not the branch head",
                    "a new head will be created. As this is not typically a desired",
                    "outcome, the commit operation will not succeed without the",
                    "`--force` option.",
                    "",
                    "Additional files to add to the commit can be added during this",
                    "operation.",
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "commit";
            }
        }

        [Option('m', "message", HelpText="Commit message.")]
        public string Message { get; set; }

        [ValueList(typeof(List<string>))]
        public IList<string> AdditionalFiles { get; set; }
    }
    class Commit : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            CommitVerbOptions localOptions = options as CommitVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            if (localOptions.AdditionalFiles != null && localOptions.AdditionalFiles.Count > 0)
            {
                if (!ws.RecordChanges(localOptions.AdditionalFiles, false, true, false, false, true))
                    return false;
            }
            if (!ws.Commit(localOptions.Message, localOptions.Force, localOptions.AllModified))
                return false;
            return true;
        }
    }
}
