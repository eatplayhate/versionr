using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Objects;

namespace Versionr.Commands
{
    class ExtractVerbOptions : PatternMatchCommandVerbOptions
    {
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## -v <version> <file patterns> [--output path] [--keep-folders]", Verb);
            }
        }
        public override BaseCommand GetCommand()
        {
            return new Extract();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "This command will extract one or more files from a specific version. If no output path is specified, it will extract the files to the current folder. Specify #b#--keep-folders## to preserve the internal folder structure."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "extract";
            }
        }

        [Option("keep-folders", DefaultValue = false, HelpText = "Preserves the internal folder structure when outputting")]
        public bool KeepFolders { get; set; }

        [Option("flatten", DefaultValue = false, HelpText = "Removes folder information and outputs all files together")]
        public bool Flatten { get; set; }

        [Option('o', "output", HelpText = "Specify folder to output data")]
        public string Output { get; set; }

        [Option('v', "version", HelpText = "Show changes made at a particular version", Required = true)]
        public string Version { get; set; }
    }

    class Extract : PatternMatchCommand
    {
        protected override bool OnNoTargetsAssumeAll
        {
            get
            {
                return true;
            }
        }
        protected override bool RunInternal(object options)
        {
            ExtractVerbOptions localOptions = options as ExtractVerbOptions;

            FilterOptions = localOptions;

            Objects.Version version = Workspace.GetPartialVersion(localOptions.Version);
            if (version == null)
            {
                Printer.PrintError("No version found matching {0}", localOptions.Version);
                return false;
            }

            IList<Versionr.Objects.Record> targets = null;

            if (ComputeTargets(localOptions))
                ApplyFilters(Workspace.GetRecords(version), localOptions, ref targets);
            else
                targets = Workspace.GetRecords(version);

            if ((targets != null && targets.Count > 0))
                return RunInternal(Workspace, targets, localOptions);

            Printer.PrintWarning("No files selected for {0}", localOptions.Verb);
            return false;
        }

        private void ApplyFilters(List<Objects.Record> list, ExtractVerbOptions localOptions, ref IList<Objects.Record> targets)
        {
            var partialResult = FilterObjects(list.Select(x => new KeyValuePair<string, Objects.Record>(x.CanonicalName, x))).Select(x => x.Value).ToList();
            if (localOptions.Recursive)
            {
                List<Objects.Record> additional = new List<Objects.Record>();
                additional.AddRange(partialResult);
                HashSet<string> considered = new HashSet<string>();
                foreach (var x in partialResult)
                    considered.Add(x.CanonicalName);
                foreach (var x in list.OrderBy(x => x.CanonicalName))
                {
                    int lastIndex = x.CanonicalName.LastIndexOf('/', x.IsDirectory ? x.CanonicalName.Length - 2 : x.CanonicalName.Length - 1);
                    if (lastIndex == -1)
                        continue;
                    string immediateParent = x.CanonicalName.Substring(0, lastIndex + 1);
                    if (considered.Contains(immediateParent))
                    {
                        additional.Add(x);
                        if (x.IsDirectory)
                            considered.Add(x.CanonicalName);
                    }
                }
                targets = additional;
            }
            else
                targets = partialResult;
        }

        protected bool RunInternal(Area ws, IList<Versionr.Objects.Record> targets, ExtractVerbOptions options)
        {
            var output = targets.OrderBy(x => x.CanonicalName).ToList();
            System.IO.DirectoryInfo outFolder = new System.IO.DirectoryInfo(options.Output ?? ".");

            bool hasFiles = output.Any(x => !x.IsDirectory);
            string commonFolderRoot = (output.Count > 1 && output[0].IsDirectory && output[output.Count - 1].CanonicalName.StartsWith(output[0].CanonicalName)) ? output[0].CanonicalName : null;
            bool hasSubfolders = output.Any(x => x.IsDirectory && x.CanonicalName != output[0].CanonicalName);

            if (!hasFiles)
            {
                Printer.PrintMessage("No file objects matching the specified filter were found for this version.");
                return false;
            }

            if (options.KeepFolders)
            {
                foreach (var x in targets)
                {
                    if (x.IsDirectory)
                        continue;
                    System.IO.DirectoryInfo folder = new System.IO.DirectoryInfo(System.IO.Path.Combine(outFolder.FullName, System.IO.Path.GetDirectoryName(x.CanonicalName)));
                    folder.Create();
                    Printer.PrintMessage("Extracting #b#{0}##...", x.CanonicalName);
                    ws.RestoreRecord(x, x.ModificationTime, System.IO.Path.Combine(folder.FullName, System.IO.Path.GetFileName(x.CanonicalName)));
                }
            }
            else
            {
                if (options.Flatten || !hasSubfolders)
                {
                    foreach (var x in targets)
                    {
                        if (x.IsDirectory)
                            continue;
                        System.IO.DirectoryInfo folder = outFolder;
                        folder.Create();
                        Printer.PrintMessage("Extracting #b#{0}## => {1}...", x.CanonicalName, System.IO.Path.GetFileName(x.CanonicalName));
                        ws.RestoreRecord(x, x.ModificationTime, System.IO.Path.Combine(folder.FullName, System.IO.Path.GetFileName(x.CanonicalName)));
                    }
                }
                else
                {
                    string reducedCommonFolder = commonFolderRoot == null ? null : System.IO.Path.GetDirectoryName(commonFolderRoot);
                    foreach (var x in targets)
                    {
                        if (x.IsDirectory)
                            continue;
                        string objectFolder = System.IO.Path.GetDirectoryName(x.CanonicalName);
                        if (commonFolderRoot != null)
                        {
                            if (!x.CanonicalName.StartsWith(commonFolderRoot))
                                throw new InvalidOperationException();
                            objectFolder = objectFolder.Substring(reducedCommonFolder.Length);
                            if (objectFolder.Length > 0 && objectFolder[0] == '\\')
                                objectFolder = objectFolder.Substring(1);
                        }
                        System.IO.DirectoryInfo folder = objectFolder.Length == 0 ? outFolder : new System.IO.DirectoryInfo(System.IO.Path.Combine(outFolder.FullName, objectFolder));
                        Printer.PrintMessage("Extracting #b#{0}## => {1}...", x.CanonicalName, objectFolder.Length == 0 ? System.IO.Path.GetFileName(x.CanonicalName) : System.IO.Path.Combine(objectFolder, System.IO.Path.GetFileName(x.CanonicalName)));
                        folder.Create();
                        ws.RestoreRecord(x, x.ModificationTime, System.IO.Path.Combine(folder.FullName, System.IO.Path.GetFileName(x.CanonicalName)));
                    }
                }
            }

            return true;
        }
    }
}
