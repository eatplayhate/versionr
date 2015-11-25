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
		public override string[] Description
		{
			get
			{
				return new string[]
				{
					"Diff a file"
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
                                if (localOptions.External)
                                {
                                    tempFiles.Add(tmp);
                                    bool nonblocking = Workspace.Directives.NonBlockingDiff.HasValue && Workspace.Directives.NonBlockingDiff.Value;
                                    var t = Utilities.LimitedTaskDispatcher.Factory.StartNew(() =>
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
                                        RunInternalDiff(tmp, System.IO.Path.Combine(Workspace.RootDirectory.FullName, Workspace.GetLocalCanonicalName(x.VersionControlRecord)));
                                    }
                                    finally
                                    {
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
                            var t = Utilities.LimitedTaskDispatcher.Factory.StartNew(() =>
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
                                RunInternalDiff(tmpParent, tmpVersion);
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

        class Region
        {
            public int Start1;
            public int End1;
            public int Start2;
            public int End2;
        }
        private void RunInternalDiff(string file1, string file2, bool processTabs = true)
        {
            List<string> lines1 = new List<string>();
            List<string> lines2 = new List<string>();
            using (var fs = new System.IO.FileInfo(file1).OpenText())
            {
                while (true)
                {
                    if (fs.EndOfStream)
                        break;
                    string line = fs.ReadLine();
                    if (processTabs)
                        line = line.Replace("\t", "    ");
                    lines1.Add(line);
                }
            }
            using (var fs = new System.IO.FileInfo(file2).OpenText())
            {
                while (true)
                {
                    if (fs.EndOfStream)
                        break;
                    string line = fs.ReadLine();
                    if (processTabs)
                        line = line.Replace("\t", "    ");
                    lines2.Add(line);
                }
            }

            List<Utilities.Diff.commonOrDifferentThing> diff = null;
            diff = Versionr.Utilities.Diff.diff_comm2(lines1.ToArray(), lines2.ToArray(), true);
            int line0 = 0;
            int line1 = 0;
            Printer.PrintMessage("--- a/{0}", file1);
            Printer.PrintMessage("+++ b/{0}", file2);
            List<Region> regions = new List<Region>();
            Region openRegion = null;
            Region last = null;
            // cleanup step
            bool doCleanup = true;
            if (!doCleanup)
                goto Display;
            for (int i = 1; i < diff.Count - 1; i++)
            {
                if (diff[i - 1].common == null || diff[i - 1].common.Count == 0)
                    continue;
                if (diff[i + 1].common == null || diff[i + 1].common.Count == 0)
                    continue;
                int cf0 = diff[i].file1 == null ? 0 : diff[i].file1.Count;
                int cf1 = diff[i].file2 == null ? 0 : diff[i].file2.Count;
                if ((cf0 == 0) ^ (cf1 == 0)) // insertion
                {
                    List<string> target = cf0 == 0 ? diff[i].file2 : diff[i].file1;
                    List<string> receiver = diff[i - 1].common;
                    List<string> source = diff[i + 1].common;

                    int copied = 0;
                    for (int j = 0; j < target.Count && j < source.Count; j++)
                    {
                        if (target[j] == source[j])
                            copied++;
                        else
                            break;
                    }

                    if (copied > 0)
                    {
                        target.AddRange(source.Take(copied));
                        source.RemoveRange(0, copied);
                        receiver.AddRange(target.Take(copied));
                        target.RemoveRange(0, copied);
                    }
                }
            }
            for (int i = 0; i < diff.Count - 1; i++)
            {
                if (diff[i].common != null)
                    continue;
                if (diff[i + 1].common == null)
                {
                    var next = diff[i + 1];
                    diff.RemoveAt(i + 1);
                    foreach (var x in next.file1)
                    {
                        diff[i].file1.Add(x);
                    }
                    foreach (var x in next.file2)
                    {
                        diff[i].file2.Add(x);
                    }
                    i--;
                    continue;
                }
                if (diff[i + 1].common == null || diff[i + 1].common.Count == 0)
                    continue;
                bool isWhitespace = true;
                bool isShort = false;
                bool isBrace = false;
                if (diff[i + 1].common.Count * 2 <= diff[i].file1.Count &&
                    diff[i + 1].common.Count * 2 <= diff[i].file2.Count)
                    isShort = true;
                foreach (var x in diff[i + 1].common)
                {
                    if (x.Trim().Length != 0)
                    {
                        isWhitespace = false;
                        break;
                    }
                }
                if (diff[i + 1].common.Count == 1 || (diff[i + 1].common.Count == 1 && (diff[i + 1].common[0].Trim() == "{" || diff[i + 1].common[0].Trim() == "}")))
                {
                    if (i < diff.Count - 2 && (diff[i + 2].common == null || diff[i + 2].common.Count == 0))
                        isBrace = true;
                }
                if ((isWhitespace && isShort) || isShort || isBrace)
                {
                    var next = diff[i + 1];
                    if (isBrace && next.common.Count > 1)
                    {
                        // currently disabled
                        diff[i].file1.Add(next.common[0]);
                        diff[i].file2.Add(next.common[0]);
                        next.common.RemoveAt(0);
                    }
                    else
                    {
                        diff.RemoveAt(i + 1);
                        foreach (var x in next.common)
                        {
                            diff[i].file1.Add(x);
                            diff[i].file2.Add(x);
                        }
                        i--;
                    }
                }
            }
        Display:
            for (int i = 0; i < diff.Count; i++)
            {
                if (regions.Count > 0)
                    last = regions[regions.Count - 1];
                if (diff[i].common != null)
                {
                    foreach (var x in diff[i].common)
                    {
                        line0++;
                        line1++;
                    }
                }
                int cf0 = diff[i].file1 == null ? 0 : diff[i].file1.Count;
                int cf1 = diff[i].file2 == null ? 0 : diff[i].file2.Count;
                for (int j = 1; j <= cf0 || j <= cf1; j++)
                {
                    if (openRegion == null)
                    {
                        int s1 = System.Math.Max(1, line0 - 2);
                        int s2 = System.Math.Max(1, line1 - 2);
                        if (last != null && (last.End1 + 3 > s1 || last.End2 + 3 > s2))
                            openRegion = last;
                        else
                            openRegion = new Region() { Start1 = s1, Start2 = s2 };
                    }
                    openRegion.End1 = System.Math.Min(line0 + 4, lines1.Count + 1);
                    openRegion.End2 = System.Math.Min(line1 + 4, lines2.Count + 1);
                    if (j <= cf0)
                    {
                        line0++;
                    }
                    if (j <= cf1)
                    {
                        line1++;
                    }
                }
                if (openRegion != null && (openRegion.End1 < line0 && openRegion.End2 < line1))
                {
                    if (regions.Count == 0 || regions[regions.Count - 1] != openRegion)
                        regions.Add(openRegion);
                    openRegion = null;
                }
            }
            if (openRegion != null && openRegion != last)
            {
                if (regions.Count == 0 || regions[regions.Count - 1] != openRegion)
                    regions.Add(openRegion);
            }
            int activeRegion = 0;
            while (activeRegion < regions.Count)
            {
                Region reg = regions[activeRegion];
                line0 = 0;
                line1 = 0;
                Printer.PrintMessage("#c#@@ -{0},{1} +{2},{3} @@", reg.Start1, reg.End1 - reg.Start1, reg.Start2, reg.End2 - reg.Start2);
                for (int i = 0; i < diff.Count; i++)
                {
                    if ((line0 > reg.End1) || (line1 > reg.End2))
                    {
                        break;
                    }
                    if (diff[i].common != null)
                    {
                        foreach (var x in diff[i].common)
                        {
                            line0++;
                            line1++;
                            if ((line0 >= reg.Start1 && line0 <= reg.End1) || (line1 >= reg.Start2 && line1 <= reg.End2))
                                Printer.PrintMessage(" {0}", Printer.Escape(x));
                        }
                    }
                    int cf0 = diff[i].file1 == null ? 0 : diff[i].file1.Count;
                    int cf1 = diff[i].file2 == null ? 0 : diff[i].file2.Count;
                    for (int j = 1; j <= cf0; j++)
                    {
                        line0++;
                        if (line0 >= reg.Start1 && line0 <= reg.End1)
                            Printer.PrintMessage("#e#-{0}", Printer.Escape(diff[i].file1[j - 1]));
                    }
                    for (int j = 1; j <= cf1; j++)
                    {
                        line1++;
                        if (line1 >= reg.Start2 && line1 <= reg.End2)
                            Printer.PrintMessage("#s#+{0}", Printer.Escape(diff[i].file2[j - 1]));
                    }
                }
                activeRegion++;
            }
        }
    }
}
