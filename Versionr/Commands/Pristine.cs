using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class PristineVerbOptions : VerbOptionBase
    {
        public override BaseCommand GetCommand()
        {
            return new Pristine();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Removes all files which aren't part of the vault",
                    "This has the following exceptions:",
                    $"\tProtected hidden files: {String.Join(", ", ProtectedHiddenFiles)}",
                    $"\tProtected hidden directories: {String.Join(", ", ProtectedHiddenDirectories)}",
                    "Additionally, the \'.versionr\' folder is always retained.",
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "pristine";
            }
        }

        [Option('f', "force", HelpText = "Forces the command - suitable for unattended runs")]
        public bool Force { get; set; }

        [Option('q', "quiet", HelpText = "Removes output text")]
        public bool Quiet { get; set; }

        [Option('l', "list", HelpText = "List files that would be deleted before deleting")]
        public bool List { get; set; }

        [Option('h', "hard", HelpText = "Also clean protected hidden files and directories")]
        public bool Hard { get; set; }

        // Hidden files and directories usually ignored by pristine. Must start with '.'
        [OptionList("protected-hidden-files", ';', HelpText = "Override list of protected hidden files. Names must start with \'.\'")]
        public IEnumerable<string> ProtectedHiddenFiles { get; set; } = new List<string> { ".vruser", ".p4ignore", ".p4config" };

        // Hidden files and directories usually ignored by pristine. Must start with '.'

        [OptionList("protected-hidden-dirs", ';', HelpText = "Override list of protected hidden directories. Names must start with \'.\'")]
        public IEnumerable<string> ProtectedHiddenDirectories { get; set; } = new List<string> { ".svn" };
    }
    class Pristine : BaseWorkspaceCommand
    {
        public override bool Headless { get { return true; } }
        protected override bool RunInternal(object options)
        {
            PristineVerbOptions localOptions = options as PristineVerbOptions;
            var records = Workspace.GetRecords(Workspace.Version);
            HashSet<string> recordNames = new HashSet<string>();
            HashSet<string> lowercaseRecordNames = new HashSet<string>();
            foreach (var x in records)
            {
                recordNames.Add(x.CanonicalName);
                lowercaseRecordNames.Add(x.CanonicalName.ToLower());
            }
            List<string> files = new List<string>();
            List<string> directories = new List<string>();
            PopulateFilesystem(files, directories, Workspace.RootDirectory, localOptions);

            Printer.PrintMessage("#b#{0}## objects in vault, #b#{1}## objects in filesystem.", records.Count, files.Count + directories.Count);
            System.Collections.Concurrent.ConcurrentBag<string> filesToDelete = new System.Collections.Concurrent.ConcurrentBag<string>();
            System.Collections.Concurrent.ConcurrentBag<string> directoriesToDelete = new System.Collections.Concurrent.ConcurrentBag<string>();
            Parallel.ForEach(files, (string fn) =>
            {
                string localName = Workspace.GetLocalPath(fn);
                if (!lowercaseRecordNames.Contains(localName.ToLower()))
                {
                    filesToDelete.Add(fn);
                }
                else if (!recordNames.Contains(localName))
                {
                    Printer.PrintMessage("Incorrect record name - wrong case for object #b#{0}##", localName);
                }
            });
            Parallel.ForEach(directories, (string fn) =>
            {
                string localName = Workspace.GetLocalPath(fn + "/");
                if (!lowercaseRecordNames.Contains(localName.ToLower()))
                {
                    directoriesToDelete.Add(fn);
                }
                else if (!recordNames.Contains(localName))
                {
                    Printer.PrintMessage("Incorrect record name - wrong case for object #b#{0}##", localName);
                }
            });

            if (filesToDelete.Count == 0 && directoriesToDelete.Count == 0)
            {
                Printer.PrintMessage("No changes found.");
                return true;
            }

            if (localOptions.List)
            {
                Printer.PrintMessage("Identified #e#{0}## files and #e#{1}## directories to remove.", filesToDelete.Count, directoriesToDelete.Count);
                foreach (var x in filesToDelete)
                    Printer.PrintMessage(x);
                foreach (var x in directoriesToDelete)
                    Printer.PrintMessage(x);
            }

            Printer.PrintMessage("Identified #e#{0}## files and #e#{1}## directories which will be removed.", filesToDelete.Count, directoriesToDelete.Count);
            if (localOptions.Force || Printer.Prompt("Restore to pristine state?"))
            {
                foreach (var x in filesToDelete)
                {
                    try
                    {
                        System.IO.File.SetAttributes(x, FileAttributes.Normal);
                        System.IO.File.Delete(x);
                        if (!localOptions.Quiet)
                            Printer.PrintMessage("#e#Purging:## {0}", x);
                    }
                    catch
                    {
                        Printer.PrintMessage("#x#Couldn't delete: {0}##", x);
                    }
                }
                Printer.PrintMessage("#e#Removing Directories...##");
                foreach (var x in directoriesToDelete.ToArray().OrderByDescending(x => x.Length))
                {
                    try
                    {
                        System.IO.Directory.Delete(x, true);
                    }
                    catch
                    {
                        Printer.PrintMessage("#x#Couldn't delete: {0}##", x);
                    }
                }
            }
            return true;
        }

        private void PopulateFilesystem(List<string> files, List<string> directories, DirectoryInfo rootDirectory, PristineVerbOptions options)
        {
            foreach (var x in rootDirectory.GetFiles())
            {
                if (!options.Hard && x.Name.StartsWith(".") && options.ProtectedHiddenFiles.Contains(x.Name))
                    continue;

                files.Add(x.FullName);
            }
            foreach (var x in rootDirectory.GetDirectories())
            {
                if (x.Name.StartsWith(".") && (x.Name == ".versionr" || !options.Hard && options.ProtectedHiddenDirectories.Contains(x.Name)))
                    continue;

                directories.Add(x.FullName);
                PopulateFilesystem(files, directories, x, options);
            }
        }

        internal static void DisplayInfo(Area workspace)
        {
            Printer.WriteLineMessage("Version #b#{0}## on branch \"#b#{1}##\" (rev {2})", workspace.Version.ID, workspace.CurrentBranch.Name, workspace.Version.Revision);
            Printer.WriteLineMessage(" - Committed at: #b#{0}##", workspace.Version.Timestamp.ToLocalTime().ToString());
            Printer.WriteLineMessage(" - Branch ID: #c#{0}##", workspace.CurrentBranch.ShortID);
        }
    }
}
