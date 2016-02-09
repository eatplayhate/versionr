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

		[VerbOption("lg", HelpText = "Prints a log of versions.")]
		public Commands.LogVerbOptions LgVerb { get; set; }

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
		[VerbOption("renamebranch", HelpText = "Rename a branch in the vault.")]
		public Commands.RenameBranchVerbOptions RenameBranchVerb { get; set; }
		[VerbOption("listbranch", HelpText = "Lists branches in the vault.")]
		public Commands.ListBranchVerbOptions ListBranchVerb { get; set; }
		[VerbOption("deletebranch", HelpText = "Deletes a branch in the vault.")]
		public Commands.DeleteBranchVerbOptions DeleteBranchVerb { get; set; }
		[VerbOption("stats", HelpText = "Displays statistics.")]
        public Commands.StatsVerbOptions StatsVerb { get; set; }
        [VerbOption("expunge", HelpText = "Deletes a version from the vault and rolls back history.")]
        public Commands.ExpungeVerbOptions ExpungeVerb { get; set; }

        [VerbOption("mergeinfo", HelpText = "Identify branches where a commit has been merged into.")]
        public Commands.MergeInfoVerbOptions MergeInfoVerb { get; set; }

        [VerbOption("rebase", HelpText = "Rebase the current node in the history to a new branch of the version DAG.")]
        public Commands.RebaseVerbOptions RebaseVerb { get; set; }

        [VerbOption("info", HelpText = "Shows the current version and branch information.")]
        public Commands.InfoVerbOptions InfoVerb { get; set; }

        [VerbOption("ahead", HelpText = "Checks if you are ahead or behind a particular remote.")]
        public Commands.AheadVerbOptions AheadVerb { get; set; }

        [VerbOption("resolve", HelpText = "Resolves conflicts by removing conlict markers on files.")]
        public Commands.ResolveVerbOptions ResolveVerb { get; set; }

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
			else if (verb == "lg")
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
            else if (verb == "renamebranch")
                return RenameBranchVerb.GetUsage();
            else if (verb == "listbranch")
                return ListBranchVerb.GetUsage();
            else if (verb == "deletebranch")
                return DeleteBranchVerb.GetUsage();
            else if (verb == "stats")
                return StatsVerb.GetUsage();
            else if (verb == "expunge")
                return ExpungeVerb.GetUsage();
            else if (verb == "mergeinfo")
                return MergeInfoVerb.GetUsage();
            else if (verb == "rebase")
                return RebaseVerb.GetUsage();
            else if (verb == "info")
                return InfoVerb.GetUsage();
            else if (verb == "ahead")
                return AheadVerb.GetUsage();
            else if (verb == "resolve")
                return ResolveVerb.GetUsage();
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
            try
            {
                string workingDirectoryPath = Environment.CurrentDirectory;
                var printerStream = new Printer.PrinterStream();
                VersionOptions initalOpts = new VersionOptions();
                CommandLine.Parser parser = new CommandLine.Parser(new Action<ParserSettings>(
                    (ParserSettings p) => { p.CaseSensitive = false; p.IgnoreUnknownArguments = false; p.HelpWriter = printerStream; p.MutuallyExclusive = true; }));
                if (parser.ParseArguments(args, initalOpts) && initalOpts.Version)
                {
                    Printer.WriteLineMessage("#b#Versionr## v{0} #q#{1}{2}", System.Reflection.Assembly.GetCallingAssembly().GetName().Version, Utilities.MultiArchPInvoke.IsX64 ? "x64" : "x86", Utilities.MultiArchPInvoke.IsRunningOnMono ? " (using Mono runtime)" : "");
                    Printer.WriteLineMessage("#q#  - A less hateful version control system.");
                    Printer.PushIndent();
                    Printer.WriteLineMessage("\n#b#Core version: {0}\n", Area.CoreVersion);
                    foreach (var x in Area.ComponentVersions)
                        Printer.WriteLineMessage("{0}: #b#{1}", x.Item1, x.Item2);
                    Printer.PopIndent();
                    Printer.RestoreDefaults();
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
                    Printer.RestoreDefaults();
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
                commands["lg"] = new Commands.Log(true);
                commands["remote"] = new Commands.Remote();
                commands["behead"] = new Commands.Behead();
                commands["viewdag"] = new Commands.ViewDAG();
                commands["info"] = new Commands.Info();
                commands["clone"] = new Commands.Clone();
                commands["pull"] = new Commands.Pull();
                commands["syncrecords"] = new Commands.SyncRecords();
                commands["diff"] = new Commands.Diff();
                commands["revert"] = new Commands.Revert();
                commands["unrecord"] = new Commands.Unrecord();
                commands["update"] = new Commands.Update();
                commands["renamebranch"] = new Commands.RenameBranch();
                commands["listbranch"] = new Commands.ListBranch();
                commands["deletebranch"] = new Commands.DeleteBranch();
                commands["stats"] = new Commands.Stats();
                commands["expunge"] = new Commands.Expunge();
                commands["mergeinfo"] = new Commands.MergeInfo();
                commands["rebase"] = new Commands.Rebase();
                commands["ahead"] = new Commands.Ahead();
                commands["resolve"] = new Commands.Resolve();

                Console.CancelKeyPress += Console_CancelKeyPress;

                Commands.BaseCommand command = null;
                Console.CancelKeyPress += Console_CancelKeyPress;
                if (!commands.TryGetValue(invokedVerb, out command))
                {
                    command = commands.Where(x => x.Key.Equals(invokedVerb, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).FirstOrDefault();
                    if (command == null)
                    {
                        printerStream.Flush();
                        System.Console.WriteLine("Couldn't invoke action: {0}", invokedVerb);
                        Printer.RestoreDefaults();
                        Environment.Exit(10);
                    }
                }
                try
                {
                    VerbOptionBase baseOptions = invokedVerbInstance as VerbOptionBase;
                    if (baseOptions != null)
                        Printer.NoColours = baseOptions.NoColours;
                    if (!command.Run(new System.IO.DirectoryInfo(workingDirectoryPath), invokedVerbInstance))
                    {
                        Printer.RestoreDefaults();
                        printerStream.Flush();
                        Environment.Exit(2);
                    }
                    Printer.RestoreDefaults();
                    return;
                }
                catch (Exception e)
                {
                    printerStream.Flush();
                    System.Console.WriteLine("Error processing action:\n{0}", e.ToString());
                    Printer.RestoreDefaults();
                    Environment.Exit(20);
                }
                Printer.RestoreDefaults();
            }
            catch
            {
                Environment.Exit(100);
            }

            return;
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Printer.RestoreDefaults();
        }
    }
}
