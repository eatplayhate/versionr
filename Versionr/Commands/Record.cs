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
        [Option('i', "interactive", HelpText = "Provides an interactive prompt for each matched file.")]
        public bool Interactive { get; set; }

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
                    "",
                    "Full rules for selecting files are below:",
                }.Concat(FileCommandVerbOptions.SharedDescription).ToArray();
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
		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
		{
			RecordVerbOptions localOptions = options as RecordVerbOptions;
			return ws.RecordChanges(status, targets, localOptions.Missing, localOptions.Interactive, new Action<Versionr.Status.StatusEntry, StatusCode, bool>(RecordFeedback));
        }

        protected override bool ComputeTargets(FileBaseCommandVerbOptions options)
        {
            if (!base.ComputeTargets(options))
            {
                RecordVerbOptions localOptions = options as RecordVerbOptions;
                return localOptions.All || localOptions.Tracked;
            }
            return true;
        }

        protected void RecordFeedback(Versionr.Status.StatusEntry entry, StatusCode code, bool auto)
        {
            string name = Workspace.GetLocalCanonicalName(entry.CanonicalName);
            int index = name.LastIndexOf('/');
            if (index != name.Length - 1)
                name = name.Insert(index + 1, "#b#");
            var previous = Status.GetStatusText(entry);
            var now = Status.GetStatusText(code, true);
            string output = "#" + now.Item1 + "#(" + now.Item2 + ")## ";
            while (output.Length < 20)
                output = " " + output;
            Printer.PrintMessage(output + " " + name + "##" + (auto ? " #q#(auto)##" : ""));
        }
	}
}
