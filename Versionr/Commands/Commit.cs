using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class CommitVerbOptions : FileCommandVerbOptions
	{
        [Option('f', "force", DefaultValue = false, HelpText = "Forces the commit to happen even if it would create a new branch head.")]
        public bool Force { get; set; }

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

        [Option('m', "message", Required = true, HelpText="Commit message.")]
        public string Message { get; set; }

    }
    class Commit : FileCommand
    {
		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileCommandVerbOptions options)
		{
            CommitVerbOptions localOptions = options as CommitVerbOptions;

            if (targets != null && targets.Count > 0)
            {
				if (!ws.RecordChanges(status, targets, false))
					return false;
            }
            if (!ws.Commit(localOptions.Message, localOptions.Force))
                return false;
            return true;
        }

		protected override bool RequiresTargets { get { return false; } }

	}
}
