using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class RecordVerbOptions : VerbOptionBase
    {
        [Option("regex", DefaultValue = false, HelpText = "Use regex pattern matching for arguments.")]
        public bool Regex{ get; set; }
        [Option('f', "filename", DefaultValue = false, HelpText = "Matches filenames regardless of full path.")]
        public bool Filename { get; set; }
        [Option('a', "all", DefaultValue = false, HelpText = "Adds every changed or unversioned file.", MutuallyExclusiveSet ="fullpath regex recursive nodirs")]
        public bool All { get; set; }
        [Option('m', "missing", DefaultValue = false, HelpText = "Allows recording deletion of files matched with --all, --recursive or --regex.")]
        public bool Missing { get; set; }
        [Option('r', "recursive", DefaultValue = true, HelpText = "Recursively add objects in directories.")]
        public bool Recursive { get; set; }
        [Option('i', "insensitive", DefaultValue = true, HelpText = "Use case-insensitive matching for objects")]
        public bool Insensitive { get; set; }
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [options] file1 [file2 ... fileN]", Verb);
            }
        }

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
                    "The `--recursive` option will add all objects contained in any",
                    "specified directories, and the `--regex` option allows you to",
                    "match several objects for inclusion at once using regex matches.",
                    "",
                    "The `record` command will respect patterns in the .vrmeta",
                    "directive file.",
                    "",
                    "This command will also allow you to specify missing files as",
                    "being intended for deletion. To match multiple deleted files,",
                    "use the `--missing` option.",
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

        [ValueList(typeof(List<string>))]
        public IList<string> Objects { get; set; }
    }
    class Record : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            RecordVerbOptions localOptions = options as RecordVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            if (localOptions.All)
            {
                if (localOptions.Objects != null && localOptions.Objects.Count != 0)
                {
                    System.Console.WriteLine("Error: --all cannot be used with any additional patterns.");
                    return false;
                }
                if (!ws.RecordAllChanges(localOptions.Missing))
                    return false;
            }
            else if (localOptions.Objects.Count == 0)
            {
                System.Console.WriteLine("Error: No objects specified.");
                return false;
            }
            else if (!ws.RecordChanges(localOptions.Objects, localOptions.Missing, localOptions.Recursive, localOptions.Regex, localOptions.Filename, localOptions.Insensitive))
                return false;
            return true;
        }
    }
}
