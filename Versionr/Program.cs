using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace Versionr
{
    class VersionOptions
    {
        [Option("version", DefaultValue = false)]
        public bool Version { get; set; }
    }
    class Options
    {
        [ParserState]
        public IParserState LastParserState { get; set; }

        [VerbOption("init", HelpText = "Initializes a Versionr vault in the current directory.")]
        public Commands.InitVerbOptions InitVerb { get; set; }

        [VerbOption("commit", HelpText = "Chronicles recorded changes into the vault.")]
        public Commands.CommitVerbOptions CommitVerb { get; set; }

        [VerbOption("status", HelpText = "Displays the status of the current vault and objects.")]
        public Commands.StatusVerbOptions StatusVerb { get; set; }

        [VerbOption("record", HelpText = "Marks changes/objects for inclusion in the next commit.")]
        public Commands.RecordVerbOptions RecordVerb { get; set; }

        [VerbOption("checkout", HelpText = "Checks out a specific branch or revision from the vault.")]
        public Commands.CheckoutVerbOptions CheckoutVerb { get; set; }

        [VerbOption("branch", HelpText = "Creates a new branch and points it to the current version.")]
        public Commands.BranchVerbOptions BranchVerb { get; set; }

        [VerbOption("merge", HelpText = "Incorporates a sequence of changes from another branch or head.")]
        public Commands.MergeVerbOptions MergeVerb { get; set; }

        [VerbOption("server", HelpText = "Runs the server daemon.")]
        public Commands.ServerVerbOptions ServerVerb { get; set; }

        [VerbOption("push", HelpText = "Pushes version metadata and object data to a server.")]
        public Commands.PushVerbOptions PushVerb { get; set; }

        [VerbOption("remote", HelpText = "Used to set the remote parameters for an external vault.")]
        public Commands.RemoteVerbOptions RemoteVerb { get; set; }
        
		[VerbOption("log", HelpText = "Prints a log of versions.")]
		public Commands.LogVerbOptions LogVerb { get; set; }
        
		[VerbOption("viewdag", HelpText = "Outputs a directed acyclic graph of version metadata.")]
		public Commands.ViewDAGVerbOptions ViewDAGVerb { get; set; }

        [VerbOption("behead", HelpText = "Forcefully removes a head from a branch.")]
        public Commands.BeheadVerbOptions BeheadVerb { get; set; }

        [VerbOption("clone", HelpText = "Clones an initial revision from a remote server.")]
        public Commands.CloneVerbOptions CloneVerb { get; set; }

        [VerbOption("pull", HelpText = "Retreives changes from a remote vault.")]
		public Commands.PullVerbOptions PullVerb { get; set; }

        [VerbOption("syncrecords", HelpText = "Retreives changes from a remote vault.")]
        public Commands.SyncRecordsOptions SyncRecordsVerb { get; set; }
        
		[VerbOption("diff", HelpText = "Display file differences")]
		public Commands.DiffVerbOptions DiffVerb { get; set; }

		[VerbOption("revert", HelpText = "Revert a file or files to pristine version")]
		public Commands.RevertVerbOptions RevertVerb { get; set; }
		[VerbOption("unrecord", HelpText = "Removes a file from inclusion in the next commit (undoes 'record')")]
		public Commands.UnrecordVerbOptions UnrecordVerb { get; set; }
		[VerbOption("update", HelpText = "Updates the current version to the head version of the current branch.")]
		public Commands.UpdateVerbOptions UpdateVerb { get; set; }

		[HelpOption]
        public string GetUsage()
        {
            var help = new HelpText
            {
                Heading = new HeadingInfo("#b#Versionr## #q#- the less hateful version control system.##"),
                AddDashesToOption = false,
            };
            help.FormatOptionHelpText += Help_FormatOptionHelpText;

            // ...
            if (LastParserState != null && LastParserState.Errors.Any())
            {
                var errors = help.RenderParsingErrorsText(this, 2); // indent with two spaces

                if (!string.IsNullOrEmpty(errors))
                {
                    help.AddPreOptionsLine(string.Concat(Environment.NewLine, "Invalid command:"));
                    help.AddPreOptionsLine(errors);
                }
            }
            help.AddPreOptionsLine("Usage: #b#versionr## COMMAND #q#[options]## <arguments>\n\n#b#Commands:");
            help.AddOptions(this);
            help.AddPostOptionsLine("##For additional help, use: #b#versionr## COMMAND #b#--help##.");
            help.AddPostOptionsLine("To display version information, use the `#b#--version##` option.\n");
            return help;
        }

        private void Help_FormatOptionHelpText(object sender, FormatOptionHelpTextEventArgs e)
        {
            e.Option.HelpText = "##" + e.Option.HelpText + "#b#";
        }

        // Remainder omitted
        [HelpVerbOption]
        public string GetUsage(string verb)
        {
			if (verb == "init")
				return InitVerb.GetUsage();
			else if (verb == "commit")
				return CommitVerb.GetUsage();
			else if (verb == "status")
				return StatusVerb.GetUsage();
			else if (verb == "record")
				return RecordVerb.GetUsage();
			else if (verb == "checkout")
				return CheckoutVerb.GetUsage();
			else if (verb == "branch")
				return BranchVerb.GetUsage();
			else if (verb == "server")
				return ServerVerb.GetUsage();
			else if (verb == "merge")
				return MergeVerb.GetUsage();
			else if (verb == "push")
				return PushVerb.GetUsage();
			else if (verb == "remote")
				return RemoteVerb.GetUsage();
			else if (verb == "log")
				return LogVerb.GetUsage();
			else if (verb == "behead")
				return BeheadVerb.GetUsage();
			else if (verb == "viewdag")
				return ViewDAGVerb.GetUsage();
			else if (verb == "clone")
				return CloneVerb.GetUsage();
			else if (verb == "pull")
				return PullVerb.GetUsage();
			else if (verb == "syncrecords")
				return SyncRecordsVerb.GetUsage();
			else if (verb == "diff")
				return DiffVerb.GetUsage();
			else if (verb == "revert")
				return RevertVerb.GetUsage();
			else if (verb == "unrecord")
				return UnrecordVerb.GetUsage();
			else if (verb == "update")
				return UpdateVerb.GetUsage();
			return GetUsage();
        }
    }
    class Program
    {
        class Region
        {
            public int Start1;
            public int End1;
            public int Start2;
            public int End2;
        }
        static void Main(string[] args)
        {
            string workingDirectoryPath = Environment.CurrentDirectory;

            if (args.Length > 1 && (args[0] == "hht"))
            {
                System.Console.WriteLine("{0} is {1}", args[1], Utilities.FileClassifier.Classify(new System.IO.FileInfo(args[1])).ToString());
                return;
            }
            if (args.Length > 2 && (args[0] == "hax" || args[0] == "hax2" || args[0] == "hax3"))
            {
                bool mode = args[0] == "hax";
                bool fancy = args[0] == "hax3";
                List<string> lines1 = new List<string>();
                List<string> lines2 = new List<string>();
                using (var fs = new System.IO.FileInfo(args[1]).OpenText())
                {
                    while (true)
                    {
                        if (fs.EndOfStream)
                            break;
                        lines1.Add(fs.ReadLine());
                    }
                }
                using (var fs = new System.IO.FileInfo(args[2]).OpenText())
                {
                    while (true)
                    {
                        if (fs.EndOfStream)
                            break;
                        lines2.Add(fs.ReadLine());
                    }
                }

                List<Utilities.Diff.commonOrDifferentThing> diff = null;
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                if (mode)
                    diff = Versionr.Utilities.Diff.diff_comm(lines1.ToArray(), lines2.ToArray());
                else
                    diff = Versionr.Utilities.Diff.diff_comm2(lines1.ToArray(), lines2.ToArray(), fancy);
                System.Console.WriteLine("Diff time: {0}", sw.ElapsedTicks);
                int line0 = 0;
                int line1 = 0;
                int minDisplayedLine0 = 0;
                int minDisplayedLine1 = 0;
                Printer.PrintMessage("--- a/{0}", args[1]);
                Printer.PrintMessage("+++ b/{0}", args[2]);
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
                    if (diff[i + 1].common.Count * 2 < diff[i].file1.Count &&
                        diff[i + 1].common.Count * 2 < diff[i].file2.Count)
                        isShort = true;
                    foreach (var x in diff[i + 1].common)
                    {
                        if (x.Trim().Length != 0)
                        {
                            isWhitespace = false;
                            break;
                        }
                    }
                    if (isWhitespace)
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
                    if (openRegion != null && openRegion != last && (openRegion.End1 < line0 && openRegion.End2 < line1))
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
                return;       
            }

            var printerStream = new Printer.PrinterStream();
            VersionOptions initalOpts = new VersionOptions();
            CommandLine.Parser parser = new CommandLine.Parser(new Action<ParserSettings>(
                (ParserSettings p) => { p.CaseSensitive = false; p.IgnoreUnknownArguments = false; p.HelpWriter = printerStream; p.MutuallyExclusive = true; }));
            parser.ParseArguments(args, initalOpts);
            if (initalOpts.Version)
            {
                Printer.WriteLineMessage("#b#Versionr## v{0} #q#{1}{2}", System.Reflection.Assembly.GetCallingAssembly().GetName().Version, Utilities.MultiArchPInvoke.IsX64 ? "x64" : "x86", Utilities.MultiArchPInvoke.IsRunningOnMono ? " (using Mono runtime)" : "");
                Printer.WriteLineMessage("#q#  - A less hateful version control system.");
                Printer.PushIndent();
                Printer.WriteLineMessage("\n#b#Core version: {0}\n", Area.CoreVersion);
                foreach (var x in Area.ComponentVersions)
                    Printer.WriteLineMessage("{0}: #b#{1}", x.Item1, x.Item2);
                Printer.PopIndent();
                return;
            }

            var options = new Options();
            string invokedVerb = string.Empty;
            object invokedVerbInstance = null;
            if (!parser.ParseArguments(args, options,
                  (verb, subOptions) =>
                  {
                      invokedVerb = verb;
                      invokedVerbInstance = subOptions;
                  }))
            {
                printerStream.Flush();
                Environment.Exit(CommandLine.Parser.DefaultExitCodeFail);
            }

            Dictionary<string, Commands.BaseCommand> commands = new Dictionary<string, Commands.BaseCommand>();
            commands["init"] = new Commands.Init();
            commands["commit"] = new Commands.Commit();
            commands["status"] = new Commands.Status();
            commands["record"] = new Commands.Record();
            commands["checkout"] = new Commands.Checkout();
            commands["branch"] = new Commands.Branch();
            commands["server"] = new Commands.Server();
            commands["push"] = new Commands.Push();
            commands["merge"] = new Commands.Merge();
			commands["log"] = new Commands.Log();
            commands["remote"] = new Commands.Remote();
            commands["behead"] = new Commands.Behead();
            commands["viewdag"] = new Commands.ViewDAG();
            commands["clone"] = new Commands.Clone();
            commands["pull"] = new Commands.Pull();
            commands["syncrecords"] = new Commands.SyncRecords();
			commands["diff"] = new Commands.Diff();
			commands["revert"] = new Commands.Revert();
			commands["unrecord"] = new Commands.Unrecord();
            commands["update"] = new Commands.Update();

            Commands.BaseCommand command = null;
            if (!commands.TryGetValue(invokedVerb, out command))
            {
                printerStream.Flush();
                System.Console.WriteLine("Couldn't invoke action: {0}", invokedVerb);
                Environment.Exit(10);
            }
            try
            {
                if (!command.Run(new System.IO.DirectoryInfo(workingDirectoryPath), invokedVerbInstance))
                {
                    printerStream.Flush();
                    Environment.Exit(2);
                }
                return;
            }
            catch (Exception e)
            {
                printerStream.Flush();
                System.Console.WriteLine("Error processing action:\n{0}", e.ToString());
                Environment.Exit(20);
            }

            return;
        }
    }
}
