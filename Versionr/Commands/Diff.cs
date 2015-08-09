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

        [Option('x', "external", HelpText = "Use external diffing tool")]
        public bool External { get; set; }

		[Option('v', "version", HelpText = "Show changes made at a particular version")]
		public string Version { get; set; }
	}

	class Diff : FileCommand
	{
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

            if (version == null)
            {
                foreach (var x in targets)
                {
                    if (x.VersionControlRecord != null && !x.IsDirectory && x.FilesystemEntry != null && x.Code == StatusCode.Modified)
                    {
                        // Displaying local modifications
                        string tmp = Utilities.DiffTool.GetTempFilename();
                        if (Workspace.ExportRecord(x.CanonicalName, Workspace.Version, tmp))
                        {
                            try
                            {
                                Printer.PrintMessage("Displaying changes for file: #b#{0}", x.CanonicalName);
                                if (localOptions.External)
                                    Utilities.DiffTool.Diff(tmp, x.Name + "-base", x.CanonicalName, x.Name);
                                else
                                    RunInternalDiff(tmp, x.CanonicalName);
                            }
                            finally
                            {
                                System.IO.File.Delete(tmp);
                            }
                        }
                    }
                }
            }
            else
            {
                List<KeyValuePair<string, Objects.Record>> updates = ws.GetAlterations(version)
                    .Where(x => x.Type == Objects.AlterationType.Update)
                    .Select(x => ws.GetRecord(x.NewRecord.Value))
                    .Select(x => new KeyValuePair<string, Objects.Record>(x.CanonicalName, x)).ToList();
                foreach (var rec in Filter(updates))
                {
                    string tmpVersion = Utilities.DiffTool.GetTempFilename();
                    bool exportedVersion = Workspace.ExportRecord(rec.CanonicalName, version, tmpVersion);

                    string tmpParent = Utilities.DiffTool.GetTempFilename();
                    bool exportedParent = Workspace.ExportRecord(rec.CanonicalName, parent, tmpParent);

                    try
                    {
                        if (exportedParent && exportedVersion)
                        {
                            Printer.PrintMessage("Displaying changes for file: #b#{0}", rec.CanonicalName);
                            if (localOptions.External)
                                Utilities.DiffTool.Diff(tmpParent, rec.Name + "-" + parent.ShortName, tmpVersion, rec.Name + "-" + version.ShortName);
                            else
                                RunInternalDiff(tmpParent, tmpVersion);
                        }
                    }
                    finally
                    {
                        if (exportedVersion)
                            System.IO.File.Delete(tmpVersion);
                        if (exportedParent)
                            System.IO.File.Delete(tmpParent);
                    }
                }
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
                if (isShort)
                {
                    if (diff[i + 1].common.Count == 1 && (diff[i + 1].common[0].Trim() == "{" || diff[i + 1].common[0].Trim() == "}"))
                        isBrace = true;
                }
                if ((isWhitespace || isBrace) && isShort)
                {
                    var next = diff[i + 1];
                    diff.RemoveAt(i + 1);
                    foreach (var x in next.common)
                    {
                        diff[i].file1.Add(x);
                        diff[i].file2.Add(x);
                    }
                    i--;
                }
            }
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
                    regions.Add(openRegion);
                    openRegion = null;
                }
            }
            if (openRegion != null && openRegion != last)
                regions.Add(openRegion);
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
