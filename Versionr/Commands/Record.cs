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
                    "#q#This command adds non-pristine objects to the versionr tracking system for inclusion in the next commit.",
                    "",
                    "Objects are matched by path by default. By using the #b#--filename#q# option, objects can be matched regardless of what path they are in. Alternatively, the #b#--regex#q# option allows pattern matching using .NET regular expressions.",
                    "",
                    "All the non-pristine objects can be matched using the #b#--all#q# option. It is possible to match only objects that are part of the vault using the #b#--tracked#q# option.",
                    "",
                    "Any recorded files will also add their containing folders to the control system unless already present.",
                    "",
                    "This command will also allow you to specify missing files as being intended for deletion, but to make this explicit, you must use the `#b#--deleted#q#` option.",
                    "",
                    "#b#NOTE:#q# Unlike other version control systems, ##recording#q# a file is only a mechanism for marking inclusion in a future ##commit#q#. The object must be committed before it is saved in the Versionr system.",
                    "",
                    "This command does not do anything for objects that are unchanged.",
                    "",
                    "The `#b#record#q#` command will respect patterns in the #b#.vrmeta#q# directive file.",
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
			string fileTargetPath = "wow/lol.txt";
            string fileLinkPath = System.IO.Path.Combine(ws.Root.FullName, "wow.txt");

			Printer.PrintMessage("Creating file link {0} to {1}", fileLinkPath, fileTargetPath);
			bool fileCreateSuccess = Utilities.Symlink.Create(fileLinkPath, fileTargetPath);
			Printer.PrintMessage("Successful {0}", fileCreateSuccess);

			string dirTargetPath = "wow/loldir";
			string dirLinkPath = System.IO.Path.Combine(ws.Root.FullName, "wow.dir");

			Printer.PrintMessage("Creating dir link {0} to {1}", dirLinkPath, dirTargetPath);
			bool dirCreateSuccess = Utilities.Symlink.Create(dirLinkPath, dirTargetPath);
			Printer.PrintMessage("Successful {0}", dirCreateSuccess);

			if (fileCreateSuccess)
			{
				Printer.PrintMessage("File link is a symlink? {0}", Utilities.Symlink.Exists(fileLinkPath));
				Printer.PrintMessage("File link targets: {0}", Utilities.Symlink.GetTarget(fileLinkPath));
			}

			if (dirCreateSuccess)
			{
				Printer.PrintMessage("Dir link is a symlink? {0}", Utilities.Symlink.Exists(dirLinkPath));
				Printer.PrintMessage("Dir link targets: {0}", Utilities.Symlink.GetTarget(dirLinkPath));
			}

			return true;

			//RecordVerbOptions localOptions = options as RecordVerbOptions;
   //         return ws.RecordChanges(status, targets, localOptions.Missing);
		}
	}
}
