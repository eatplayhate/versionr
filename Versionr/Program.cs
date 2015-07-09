using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace Versionr
{
    class CommandLineBase
    {
        public string Name { get; set; }
        public string ShortName { get; set; }
        public string Description { get; set; }
    };
    class Command : CommandLineBase
    {
        public Commands.BaseCommand Processor { get; set; }
    }
    abstract class VerbOptionBase
    {
        [ParserState]
        public IParserState LastParserState { get; set; }
        public abstract string Verb { get; }
        public abstract string[] Description { get; }
        [HelpOption]
        public string GetUsage()
        {
            var help = new HelpText
            {
                Heading = new HeadingInfo("Versionr - the less hateful version control system."),
                AddDashesToOption = true
            };

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
            help.AddPreOptionsLine(string.Format("\n{0}\n", Usage));
            help.AddPreOptionsLine(string.Format("The `{0}` Command:", Verb));
            foreach (var x in Description)
                help.AddPreOptionsLine("  " + x);
            help.AddPreOptionsLine("\nOptions:");
            help.AddOptions(this);
            return help;
        }

        [Option('v', "verbose", DefaultValue = true, HelpText = "Display verbose diagnostics information.")]
        public bool Verbose { get; set; }

        public virtual string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [options] <arguments>", Verb);
            }
        }
    }
    class VersionOptions
    {
        [Option("version", DefaultValue = false)]
        public bool Version { get; set; }
    }
    class Options
    {
        [ParserState]
        public IParserState LastParserState { get; set; }

        [VerbOption("init", HelpText = "Initializes a Versionr repository here.")]
        public Commands.InitVerbOptions InitVerb { get; set; }

        [VerbOption("commit", HelpText = "Records any changes in the reposity to a new version.")]
        public Commands.CommitVerbOptions CommitVerb { get; set; }

        [VerbOption("status", HelpText = "Displays the status of the current repository.")]
        public Commands.StatusVerbOptions StatusVerb { get; set; }

        [VerbOption("record", HelpText = "Records changes into the versionr control system.")]
        public Commands.RecordVerbOptions RecordVerb { get; set; }

        [VerbOption("checkout", HelpText = "Checks out a specific branch or revision from the Versionr repository.")]
        public Commands.CheckoutVerbOptions CheckoutVerb { get; set; }

        [VerbOption("branch", HelpText = "blah blah")]
        public Commands.BranchVerbOptions BranchVerb { get; set; }

        [VerbOption("merge", HelpText = "blah blah")]
        public Commands.MergeVerbOptions MergeVerb { get; set; }

        [VerbOption("server", HelpText = "blah blah")]
        public Commands.ServerVerbOptions ServerVerb { get; set; }

        [VerbOption("push", HelpText = "blah blah")]
        public Commands.PushVerbOptions PushVerb { get; set; }

        [VerbOption("remote", HelpText = "Used to set the remote parameters for an external vault.")]
        public Commands.RemoteVerbOptions RemoteVerb { get; set; }
        
		[VerbOption("log", HelpText = "Print a log")]
		public Commands.LogVerbOptions LogVerb { get; set; }
        
		[VerbOption("viewdag", HelpText = "Output dag info")]
		public Commands.ViewDAGVerbOptions ViewDAGVerb { get; set; }

        [VerbOption("behead", HelpText = "Forcefully removes a head from a branch.")]
        public Commands.BeheadVerbOptions BeheadVerb { get; set; }

        [VerbOption("clone", HelpText = "Clones an initial revision from a remote server.")]
        public Commands.CloneVerbOptions CloneVerb { get; set; }

        [HelpOption]
        public string GetUsage()
        {
            var help = new HelpText
            {
                Heading = new HeadingInfo("Versionr - the less hateful version control system."),
                AddDashesToOption = false
            };

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
            help.AddPreOptionsLine("Usage: versionr COMMAND [options] <arguments>\n\nCommands:");
            help.AddOptions(this);
            help.AddPostOptionsLine("For additional help, use: versionr COMMAND --help\nTo display version information, use the `--version` option.\n");
            return help;
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
            return GetUsage();
        }
    }
    class Program
    {
        static void Main(string[] args)
        {
            string workingDirectoryPath = Environment.CurrentDirectory;

            VersionOptions initalOpts = new VersionOptions();
            CommandLine.Parser.Default.ParseArguments(args, initalOpts);
            if (initalOpts.Version)
            {
                System.Console.WriteLine("Versionr v{0} {1}{2}", System.Reflection.Assembly.GetCallingAssembly().GetName().Version, Utilities.MultiArchPInvoke.IsX64 ? "x64" : "x86", Utilities.MultiArchPInvoke.IsRunningOnMono ? " (using Mono runtime)" : "");
                System.Console.WriteLine("  - A less hateful version control system.");
                return;
            }

            var options = new Options();
            string invokedVerb = string.Empty;
            object invokedVerbInstance = null;
            if (!CommandLine.Parser.Default.ParseArguments(args, options,
                  (verb, subOptions) =>
                  {
                      invokedVerb = verb;
                      invokedVerbInstance = subOptions;
                  }))
            {
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

            Commands.BaseCommand command = null;
            if (!commands.TryGetValue(invokedVerb, out command))
            {
                System.Console.WriteLine("Couldn't invoke action: {0}", invokedVerb);
                Environment.Exit(10);
            }
            try
            {
                if (!command.Run(new System.IO.DirectoryInfo(workingDirectoryPath), invokedVerbInstance))
                    Environment.Exit(2);
                return;
            }
            catch (Exception e)
            {
                System.Console.WriteLine("Error processing action:\n{0}", e.ToString());
                Environment.Exit(20);
            }

            return;
        }

		private static void ListBranches(Area ws)
        {
            List<Objects.Branch> branches = ws.Branches;
            Printer.PrintMessage("Listing Branches:");
            foreach (var x in branches)
            {
                Printer.PrintMessage(" {0}", x.Name);
                var heads = ws.GetBranchHeads(x);
                if (heads.Count > 1)
                    Printer.PrintMessage("Warning: Branch has {0} heads!", heads.Count);
                foreach (var y in heads)
                    Printer.PrintMessage("  - Head {0}", y.Version);
            }
        }

        private static string ShortStatusCode(StatusCode code)
        {
            switch (code)
            {
                case StatusCode.Missing:
                    return "%";
                case StatusCode.Deleted:
                    return "D";
                case StatusCode.Modified:
                    return "M";
                case StatusCode.Added:
                    return "+";
                case StatusCode.Unchanged:
                    return "-";
                case StatusCode.Unversioned:
                    return "?";
                case StatusCode.Renamed:
                    return "R";
                case StatusCode.Copied:
                    return "C";
                case StatusCode.Conflict:
                    return "!";
            }
            throw new Exception();
        }
    }
}
