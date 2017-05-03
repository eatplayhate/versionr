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
                    "Removes all files which aren't part of the vault (skips directories which start with a '.')"
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
    }
    class Pristine : BaseWorkspaceCommand
    {
        public override bool Headless { get { return true; } }
        protected override bool RunInternal(object options)
        {
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
            PopulateFilesystem(files, directories, Workspace.RootDirectory);

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
            Printer.PrintMessage("Identified #e#{0}## files and #e#{1}## directories.", filesToDelete.Count, directoriesToDelete.Count);
            if (Printer.Prompt("Continue"))
            {
                foreach (var x in filesToDelete)
                {
                    Printer.PrintMessage("#e#Purging:## {0}", x);
                    System.IO.File.SetAttributes(x, FileAttributes.Normal);
                    System.IO.File.Delete(x);
                }
                Printer.PrintMessage("#e#Removing Directories...##");
                foreach (var x in directoriesToDelete.ToArray().OrderByDescending(x => x.Length))
                {
                    System.IO.Directory.Delete(x, true);
                }
            }
            return true;
        }

        private void PopulateFilesystem(List<string> files, List<string> directories, DirectoryInfo rootDirectory)
        {
            foreach (var x in rootDirectory.GetFiles())
            {
                files.Add(x.FullName);
            }
            foreach (var x in rootDirectory.GetDirectories())
            {
                if (!x.Name.StartsWith("."))
                {
                    directories.Add(x.FullName);
                    PopulateFilesystem(files, directories, x);
                }
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
