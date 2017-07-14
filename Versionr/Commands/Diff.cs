using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
	class DiffVerbOptions : FileCommandVerbOptions
    {
        public override BaseCommand GetCommand()
        {
            return new Diff();
        }
        public override string[] Description
		{
			get
			{
				return new string[]
				{
					"Displays diffs for one or more objects. If a version is specified, it will display the diffs from that version, otherwise it will compare objects with the workspace's current version."
				};
			}
		}

		public override string Verb
		{
			get
			{
				return "diff";
			}
        }

        [Option('d', "direct", HelpText = "Specify two files to compare that may not be under version control.")]
        public bool Direct { get; set; }

        [Option('x', "external", HelpText = "Use external diffing tool")]
        public bool External { get; set; }

		[Option('v', "version", HelpText = "Show changes made at a particular version")]
		public string Version { get; set; }

        [Option('l', "local", HelpText = "Compare with working copy")]
        public bool Local { get; set; }

        [Option('c', "recorded", HelpText = "Matches only files that are recorded")]
        public bool Recorded { get; set; }

        [Option('y', "externalnb", HelpText = "Use external diffing tool, non blocking")]
        public bool ExternalNonBlocking { get; set; }
    }

	class Diff : FileCommand
	{
        public override bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            DiffVerbOptions localOptions = options as DiffVerbOptions;
            if (localOptions.Direct)
            {
                if (localOptions.Objects.Count != 2)
                {
                    Printer.PrintError("Direct diff requires two files to compare.");
                    return false;
                }
                System.IO.FileInfo f1 = new System.IO.FileInfo(localOptions.Objects[0]);
                System.IO.FileInfo f2 = new System.IO.FileInfo(localOptions.Objects[1]);
                if (!f1.Exists)
                {
                    Printer.PrintError("Can't locate file: \"{0}\".", f1.FullName);
                    return false;
                }
                if (!f2.Exists)
                {
                    Printer.PrintError("Can't locate file: \"{0}\".", f2.FullName);
                    return false;
                }
                if (localOptions.External)
                {
                    Utilities.DiffTool.Diff(f1.FullName, f1.FullName, f2.FullName, f2.FullName, Workspace.Directives.ExternalDiff, false);
                }
                else
                {
                    RunInternalDiff(f1.FullName, f2.FullName);
                }
                return true;
            }
            return base.Run(workingDirectory, options);
        }
        protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
		{
            DiffVerbOptions localOptions = options as DiffVerbOptions;

			Objects.Version version = null;
			Objects.Version parent = null;
			if (!string.IsNullOrEmpty(localOptions.Version))
			{
				version = Workspace.GetPartialVersion(localOptions.Version);
				if (version == null)
				{
					Printer.PrintError("No version found matching {0}", localOptions.Version);
					return false;
				}
				if (version.Parent.HasValue)
					parent = Workspace.GetVersion(version.Parent.Value);

				if (parent == null)
				{
					Printer.PrintMessage("Version {0} has no parent", version.ID);
					return true;
				}
                Printer.PrintMessage("Showing changes for version #c#{0}", version.ID);
			}

            bool showUnchangedObjects = localOptions.Objects.Count != 0;

			List<Task> tasks = new List<Task>();
            List<string> tempFiles = new List<string>();
            List<System.Diagnostics.Process> diffProcesses = new List<System.Diagnostics.Process>();
            try
            {
                if (version == null)
                {
                    foreach (var x in targets)
                    {
                        if (x.VersionControlRecord != null && !x.IsDirectory && x.FilesystemEntry != null && x.Code == StatusCode.Modified)
                        {
                            if (localOptions.Recorded && x.Staged == false)
                                continue;

                            if (Utilities.FileClassifier.Classify(x.FilesystemEntry.Info) == Utilities.FileEncoding.Binary)
                            {
                                Printer.PrintMessage("File: #b#{0}## is binary #w#different##.", x.CanonicalName);
                                continue;
                            }
                            // Displaying local modifications
                            string tmp = Utilities.DiffTool.GetTempFilename();
                            if (Workspace.ExportRecord(x.CanonicalName, Workspace.Version, tmp))
                            {
                                Printer.PrintMessage("Displaying changes for file: #b#{0}", x.CanonicalName);
                                if (localOptions.External || localOptions.ExternalNonBlocking)
                                {
                                    tempFiles.Add(tmp);
                                    bool nonblocking = Workspace.Directives.NonBlockingDiff.HasValue && Workspace.Directives.NonBlockingDiff.Value;
                                    nonblocking |= localOptions.ExternalNonBlocking;
                                    var t = GetTaskFactory(options).StartNew(() =>
                                    {
                                        var diffResult = Utilities.DiffTool.Diff(tmp, x.Name + "-base", System.IO.Path.Combine(Workspace.RootDirectory.FullName, Workspace.GetLocalCanonicalName(x.VersionControlRecord)), x.Name, ws.Directives.ExternalDiff, nonblocking);
                                        if (diffResult != null)
                                        {
                                            lock (diffProcesses)
                                            {
                                                diffProcesses.Add(diffResult);
                                            }
                                        }
                                    });
                                    if (nonblocking)
                                        tasks.Add(t);
                                    else
                                        t.Wait();
                                }
                                else
                                {
                                    try
                                    {
                                        RunInternalDiff(tmp, System.IO.Path.Combine(Workspace.RootDirectory.FullName, Workspace.GetLocalCanonicalName(x.VersionControlRecord)), true, Workspace.GetLocalCanonicalName(x.VersionControlRecord));
                                    }
                                    finally
                                    {
                                        new System.IO.FileInfo(tmp).IsReadOnly = false;
                                        System.IO.File.Delete(tmp);
                                    }
                                }
                            }
                        }
                        else if (x.Code == StatusCode.Unchanged && showUnchangedObjects && !x.IsDirectory)
                        {
                            var filter = Filter(new KeyValuePair<string, Objects.Record>[] { new KeyValuePair<string, Objects.Record>(x.CanonicalName, x.VersionControlRecord) }).FirstOrDefault();
                            if (filter.Value != null && filter.Key == true) // check if the file was really specified
                                Printer.PrintMessage("Object: #b#{0}## is #s#unchanged##.", x.CanonicalName);
                        }
                        else if (x.VersionControlRecord == null && showUnchangedObjects)
                        {
                            var filter = Filter(new KeyValuePair<string, bool>[] { new KeyValuePair<string, bool>(x.CanonicalName, true) }).FirstOrDefault();
                            if (filter.Value != false && filter.Key == true) // check if the file was really specified
                                Printer.PrintMessage("Object: #b#{0}## is #c#unversioned##.", x.CanonicalName);
                        }
                    }
                }
                else
                {
                    if (localOptions.Local)
                    {
                        var records = ws.GetRecords(version);
                        Dictionary<string, Objects.Record> recordMap = new Dictionary<string, Objects.Record>();
                        foreach (var x in records)
                            recordMap[x.CanonicalName] = x;
                        foreach (var x in targets)
                        {
                            if (localOptions.Recorded && x.Staged == false)
                                continue;

                            Objects.Record otherRecord = null;
                            if (recordMap.TryGetValue(x.CanonicalName, out otherRecord))
                            {
                                if (x.VersionControlRecord != null && x.VersionControlRecord.DataIdentifier == otherRecord.DataIdentifier)
                                    continue;
                                if (Utilities.FileClassifier.Classify(x.FilesystemEntry.Info) == Utilities.FileEncoding.Binary)
                                {
                                    Printer.PrintMessage("File: #b#{0}## is binary #w#different##.", x.CanonicalName);
                                    continue;
                                }
                                string tmp = Utilities.DiffTool.GetTempFilename();
                                if (Workspace.ExportRecord(x.CanonicalName, version, tmp))
                                {
                                    Printer.PrintMessage("Displaying changes for file: #b#{0}", x.CanonicalName);
                                    if (localOptions.External)
                                    {
                                        tempFiles.Add(tmp);
                                        bool nonblocking = Workspace.Directives.NonBlockingDiff.HasValue && Workspace.Directives.NonBlockingDiff.Value;
                                        var t = GetTaskFactory(options).StartNew(() =>
                                        {
                                            var diffResult = Utilities.DiffTool.Diff(tmp, x.Name + "-base", Workspace.GetLocalCanonicalName(x.VersionControlRecord), x.Name, ws.Directives.ExternalDiff, nonblocking);
                                            if (diffResult != null)
                                            {
                                                lock (diffProcesses)
                                                {
                                                    diffProcesses.Add(diffResult);
                                                }
                                            }
                                        });
                                        if (nonblocking)
                                            tasks.Add(t);
                                        else
                                            t.Wait();
                                    }
                                    else
                                    {
                                        try
                                        {
                                            RunInternalDiff(tmp, System.IO.Path.Combine(Workspace.RootDirectory.FullName, Workspace.GetLocalCanonicalName(x.VersionControlRecord)), true, Workspace.GetLocalCanonicalName(x.VersionControlRecord));
                                        }
                                        finally
                                        {
                                            System.IO.File.Delete(tmp);
                                        }
                                    }
                                }
                            }
                            else
                                Printer.PrintMessage("File: #b#{0}## is not in other version.", x.CanonicalName);
                        }
                    }
                    else
                    {
                        List<KeyValuePair<string, Objects.Record>> updates = ws.GetAlterations(version)
                            .Where(x => x.Type == Objects.AlterationType.Update)
                            .Select(x => ws.GetRecord(x.NewRecord.Value))
                            .Select(x => new KeyValuePair<string, Objects.Record>(x.CanonicalName, x)).ToList();
                        foreach (var pair in Filter(updates))
                        {
                            Objects.Record rec = pair.Value;
                            string tmpVersion = Utilities.DiffTool.GetTempFilename();
                            if (!Workspace.ExportRecord(rec.CanonicalName, version, tmpVersion))
                                continue;

                            string tmpParent = Utilities.DiffTool.GetTempFilename();
                            if (!Workspace.ExportRecord(rec.CanonicalName, parent, tmpParent))
                            {
                                System.IO.File.Delete(tmpVersion);
                                continue;
                            }

                            Printer.PrintMessage("Displaying changes for file: #b#{0}", rec.CanonicalName);
                            if (localOptions.External)
                            {
                                tempFiles.Add(tmpVersion);
                                tempFiles.Add(tmpParent);
                                bool nonblocking = Workspace.Directives.NonBlockingDiff.HasValue && Workspace.Directives.NonBlockingDiff.Value;
                                var t = GetTaskFactory(options).StartNew(() =>
                                {
                                    var diffResult = Utilities.DiffTool.Diff(tmpParent, rec.Name + "-" + parent.ShortName, tmpVersion, rec.Name + "-" + version.ShortName, ws.Directives.ExternalDiff, nonblocking);
                                    if (diffResult != null)
                                    {
                                        lock (diffProcesses)
                                        {
                                            diffProcesses.Add(diffResult);
                                        }
                                    }
                                });
                                if (nonblocking)
                                    tasks.Add(t);
                                else
                                    t.Wait();
                            }
                            else
                            {
                                try
                                {
                                    RunInternalDiff(tmpParent, tmpVersion, true, rec.CanonicalName);
                                }
                                finally
                                {
                                    System.IO.File.Delete(tmpVersion);
                                    System.IO.File.Delete(tmpParent);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                Task.WaitAll(tasks.ToArray());
                foreach (var x in diffProcesses)
                    x.WaitForExit();
                foreach (var x in tempFiles)
                    System.IO.File.Delete(x);
            }
            return true;
        }

        protected override bool OnNoTargetsAssumeAll
        {
            get
            {
                return true;
            }
        }
        private void RunInternalDiff(string file1, string file2, bool processTabs = true, string filenameOverride = null)
        {
            List<string> messages = Utilities.DiffFormatter.Run(file1, file2, filenameOverride, filenameOverride, processTabs, true);
            foreach (var x in messages)
                Printer.PrintMessage(x);
        }
    }
}
