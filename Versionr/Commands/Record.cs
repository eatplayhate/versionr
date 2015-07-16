using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class RecordVerbOptions : FileCommandVerbOptions
	{
        [Option('d', "deleted", DefaultValue = false, HelpText = "Allows recording deletion of files matched with --all, --recursive or --regex.")]
        public bool Missing { get; set; }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "This command adds objects to the versionr tracking system for",
                    "inclusion in the next commit.",
                    "",
                    "Any recorded files will also add their containing folders to the",
                    "control system unless already present.",
                    "",
                    "The `record` command will respect patterns in the .vrmeta",
                    "directive file.",
                    "",
                    "This command will also allow you to specify missing files as",
                    "being intended for deletion. To match multiple deleted files,",
					"use the `--deleted` option.",
                    "",
                    "NOTE: Unlike other version control systems, adding a file is only",
                    "a mechanism for marking inclusion in a future commit. The object",
                    "must be committed before it is saved in the Versionr system."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "record";
            }
        }

    }
    class Record : FileCommand
	{
		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileCommandVerbOptions options)
		{
			RecordVerbOptions localOptions = options as RecordVerbOptions;
            return ws.RecordChanges(status, targets, localOptions.Missing);
		}
	}
}
