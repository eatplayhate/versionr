using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;
using System.Reflection;
using System.IO;

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

        [VerbOption("pristine", HelpText = "Removes all files which aren't part of the vault (skips directories which start with a '.')")]
        public Commands.PristineVerbOptions PristineVerb { get; set; }

        [VerbOption("branch", HelpText = "Creates a new branch and points it to the current version.")]
        public Commands.BranchVerbOptions BranchVerb { get; set; }

        [VerbOption("merge", HelpText = "Incorporates a sequence of changes from another branch or head.")]
        public Commands.MergeVerbOptions MergeVerb { get; set; }

        [VerbOption("server", HelpText = "Runs the server daemon.")]
        public Commands.ServerVerbOptions ServerVerb { get; set; }

        [VerbOption("push", HelpText = "Pushes version metadata and object data to a server.")]
        public Commands.PushVerbOptions PushVerb { get; set; }

        [VerbOption("set-remote", HelpText = "Used to set the remote parameters for an external vault.")]
        public Commands.SetRemoteVerbOptions RemoteVerb { get; set; }
        
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
        [VerbOption("diff-version", HelpText = "Display version differences")]
        public Commands.DiffVersionsVerbOptions DiffVersionsVerb { get; set; }

        [VerbOption("revert", HelpText = "Revert a file or files to pristine version")]
		public Commands.RevertVerbOptions RevertVerb { get; set; }
		[VerbOption("unrecord", HelpText = "Removes a file from inclusion in the next commit (undoes 'record')")]
		public Commands.UnrecordVerbOptions UnrecordVerb { get; set; }
		[VerbOption("update", HelpText = "Updates the current version to the head version of the current branch.")]
		public Commands.UpdateVerbOptions UpdateVerb { get; set; }
		[VerbOption("rename-branch", HelpText = "Rename a branch in the vault.")]
		public Commands.RenameBranchVerbOptions RenameBranchVerb { get; set; }
        [VerbOption("branch-control", HelpText = "Modify branch control data.")]
        public Commands.BranchControlVerbOptions BranchControlVerb { get; set; }
        [VerbOption("branch-list", HelpText = "Lists branches in the vault.")]
		public Commands.BranchListVerbOptions ListBranchVerb { get; set; }
		[VerbOption("delete-branch", HelpText = "Deletes a branch in the vault.")]
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

        [VerbOption("stash", HelpText = "Stashes your current changes into a patch that can be applied later and reverts the working files.")]
        public Commands.StashVerbOptions StashVerb { get; set; }

        [VerbOption("unstash", HelpText = "Applies a stash to the current working directory.")]
        public Commands.UnstashVerbOptions UnstashVerb { get; set; }

        [VerbOption("extract", HelpText = "Extracts files from a specific version.")]
        public Commands.ExtractVerbOptions ExtractVerb { get; set; }

        [VerbOption("stash-list", HelpText = "Lists available stashes.")]
        public Commands.StashListVerbOptions StashListVerb { get; set; }

        [VerbOption("stash-delete", HelpText = "Deletes stashes.")]
        public Commands.StashDeleteVerbOptions StashDeleteVerb { get; set; }

        [VerbOption("cherry-pick", HelpText = "Applies (or reverse-applies) changes from a specific version.")]
        public Commands.CherrypickVerbOptions CherrypickVerb { get; set; }

        [VerbOption("push-stash", HelpText = "Pushes one or more stashes to a server.")]
        public Commands.PushStashVerbOptions PushStashVerb { get; set; }

        [VerbOption("pull-stash", HelpText = "Pulls one or more stashes from a server.")]
        public Commands.PullStashVerbOptions PullStashVerb { get; set; }

        [VerbOption("push-records", HelpText = "Pushes file content that this node has to a server.")]
        public Commands.PushRecordsVerbOptions PushRecordsVerb { get; set; }

        [VerbOption("lock", HelpText = "Acquires an exclusive lock to a specific path on a server.")]
        public Commands.LockVerbOptions LockVerb { get; set; }

        [VerbOption("lock-list", HelpText = "Lists the locks currently held by this versionr node.")]
        public Commands.LockListVerbOptions LockListVerb { get; set; }

        [VerbOption("unlock", HelpText = "Releases a held exclusive lock on a remote.")]
        public Commands.UnlockVerbOptions UnlockVerb { get; set; }

        [VerbOption("lock-check", HelpText = "Lists or breaks locks on a remote.")]
        public Commands.LockCheckVerbOptions LockCheckVerb { get; set; }

        [VerbOption("prune", HelpText = "Cleans up old object files.")]
        public Commands.PruneVerbOptions PruneVerb { get; set; }

        [VerbOption("tag", HelpText = "Modifies tags associated with a version.")]
        public Commands.TagVerbOptions TagVerb { get; set; }

        [VerbOption("tag-find", HelpText = "Searches for versions with a specific tag.")]
        public Commands.TagFindVerbOptions TagFindVerb { get; set; }

        [VerbOption("get-annotation", HelpText = "Gets a keyed metadata blob attached to a specific version.")]
        public Commands.GetAnnotationVerbOptions GetAnnotationVerb { get; set; }

        [VerbOption("list-annotation", HelpText = "Lists metadata blobs attached to a specific version.")]
        public Commands.ListAnnotationVerbOptions ListAnnotationVerb { get; set; }

        [VerbOption("set-annotation", HelpText = "Sets a keyed metadata blob attached to a specific version.")]
        public Commands.SetAnnotationVerbOptions SetAnnotationVerb { get; set; }

        [VerbOption("delete-annotation", HelpText = "Deletes a keyed metadata blob attached to a specific version.")]
        public Commands.DeleteAnnotationVerbOptions DeleteAnnotationVerb { get; set; }

        [VerbOption("admin", HelpText = "Runs a special administration command (advanced users only).")]
        public Commands.AdminVerbOptions AdminVerb { get; set; }

        [VerbOption("apply", HelpText = "Applies a patch file.")]
        public Commands.ApplyVerbOptions ApplyVerb { get; set; }

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
            foreach (var x in GetType().GetProperties())
            {
                if (x.PropertyType.IsSubclassOf(typeof(VerbOptionBase)))
                {
                    VerbOptionBase vob = x.GetValue(this) as VerbOptionBase;
                    if (vob != null && verb == vob.Verb)
                        return vob.GetUsage();
                }
            }
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
		
        static IEnumerable<Tuple<object, Assembly>> PluginOptionsAndAssemblies
        {
            get
            {
                // Enumerate plugins
                foreach (var plugin in PluginCache.Plugins)
                {
					if (plugin.Attributes.OptionsType != null)
						yield return new Tuple<object, Assembly>(Activator.CreateInstance(plugin.Attributes.OptionsType), plugin.Assembly);
                }
            }
        }

        static IEnumerable<object> PluginOptions
		{
			get
            {
                // load assemblies
                var plugins = PluginOptionsAndAssemblies.ToList();
                yield return new Options();

                foreach (var x in plugins.Select(x => x.Item1))
                    yield return x;
            }
		}

        static void Main(string[] args)
        {
            Versionr.Utilities.Misc.StartTimer();
            try
            {   
                string workingDirectoryPath = Environment.CurrentDirectory;
                var printerStream = new Printer.PrinterStream();
                var nullstream = new System.IO.MemoryStream();
                VersionOptions initalOpts = new VersionOptions();

                CommandLine.Parser silentparser = new CommandLine.Parser(new Action<ParserSettings>(
                    (ParserSettings p) => { p.CaseSensitive = true; p.IgnoreUnknownArguments = false; p.HelpWriter = new System.IO.StreamWriter(nullstream); p.MutuallyExclusive = true; }));
                CommandLine.Parser parser = new CommandLine.Parser(new Action<ParserSettings>(
                   (ParserSettings p) => { p.CaseSensitive = true; p.IgnoreUnknownArguments = false; p.HelpWriter = printerStream; p.MutuallyExclusive = true; }));
                
                if (args.Length >= 1 && args[0] == "--version" && parser.ParseArguments(args, initalOpts) && initalOpts.Version)
                {
                    Printer.WriteLineMessage("#b#Versionr## v{0} #q#{1}{2}", System.Reflection.Assembly.GetCallingAssembly().GetName().Version, Utilities.MultiArchPInvoke.IsX64 ? "x64" : "x86", Utilities.MultiArchPInvoke.IsRunningOnMono ? " (using Mono runtime)" : "");
                    Printer.WriteLineMessage("#q#- A less hateful version control system.");
                    Printer.PushIndent();
                    Printer.WriteLineMessage("\n#b#Core version: {0}\n", Area.CoreVersion);
                    foreach (var x in Area.ComponentVersions)
                        Printer.WriteLineMessage("{0}: #b#{1}", x.Item1, x.Item2);
                    Printer.PopIndent();
                    Printer.WriteLineMessage("\n#b#Plugins:\n");
                    Printer.PushIndent();
                    foreach (var plugin in PluginCache.Plugins)
                        Printer.WriteLineMessage("#b#{1}## ({2}) #q#{0}", Path.GetFileName(plugin.Assembly.Location), plugin.Attributes.Name, plugin.Assembly.GetName().Version);
                    Printer.PopIndent();
                    Printer.RestoreDefaults();
                    return;
                }


                if (args.Length == 0)
                {
                    PrintAllOptions(args, parser, printerStream);
                    printerStream.Flush();
                    Printer.PrintMessage("\n#e#Error## - missing command.");
                    Printer.RestoreDefaults();
                    Environment.Exit(CommandLine.Parser.DefaultExitCodeFail);
                }

                // We will attempt to parse the commandline first
                object options = null;
                string invokedVerb = string.Empty;
                object invokedVerbInstance = null;
                object activatedPlugin = null;
                foreach (object pluginOptions in PluginOptions)
                {
                    if (silentparser.ParseArguments(args, pluginOptions,
						  (verb, success, subOptions) =>
						  {
                              if (subOptions != null)
                              {
                                  invokedVerb = verb;
                                  activatedPlugin = pluginOptions;
                              }
                              invokedVerbInstance = subOptions;
						  }))
					{
						options = pluginOptions;
						break;
					}
                    if (invokedVerb != string.Empty)
                        break;
                }

                if (options == null)
                {
                    if (invokedVerb != string.Empty && activatedPlugin != null)
                    {
                        // First, does the option object even support help?
                        System.Reflection.MethodInfo helpOptionVerb = GetVerbHelpMethod(activatedPlugin);
                        if (helpOptionVerb != null)
                        {
                            // We hit a verb, but the commandline parser is unhappy at us, re-run the parse
                            parser.ParseArguments(args, activatedPlugin, (verb, success, subOptions) => { });
                        }
                        else
                        {
                            if (invokedVerbInstance is VerbOptionBase)
                                printerStream.WriteLine(((VerbOptionBase)invokedVerbInstance).GetUsage());
                            else
                            {
                                Printer.PrintMessage("Warning - verb #b#{0}##: command is malformed and cannot be parsed.", invokedVerb);
                                Printer.PrintMessage("No help method defined on verb object.", invokedVerb);
                            }
                        }
                    }
                    else
                    {
                        PrintAllOptions(args, parser, printerStream);
                    }


                    printerStream.Flush();
                    Printer.RestoreDefaults();
                    Environment.Exit(CommandLine.Parser.DefaultExitCodeFail);
                }

                if (!string.IsNullOrEmpty((invokedVerbInstance as VerbOptionBase).Logfile))
                    Printer.OpenLog((invokedVerbInstance as VerbOptionBase).Logfile);
				
                Console.CancelKeyPress += Console_CancelKeyPress;

                try
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
					// because lewis broke 'lg' on purpose
					if (args.Count() > 0 && args[0] == "lg" && invokedVerbInstance is Commands.LogVerbOptions)
						((Commands.LogVerbOptions)invokedVerbInstance).Jrunting = true;
                    int bmc = 1;
                    bool bm = false;
                    if (invokedVerbInstance is VerbOptionBase)
                    {
                        bm = ((VerbOptionBase)invokedVerbInstance).Benchmark;
                        bmc = ((VerbOptionBase)invokedVerbInstance).BMC;
                    }
                    for (int i = 0; i < (bm ? bmc : 1); i++)
                    {
                        Commands.BaseCommand command = ((VerbOptionBase)invokedVerbInstance).GetCommand();
                        VerbOptionBase baseOptions = invokedVerbInstance as VerbOptionBase;
                        if (baseOptions != null)
                            Printer.NoColours = baseOptions.NoColours;
                        bool result = command.Run(new System.IO.DirectoryInfo(workingDirectoryPath), invokedVerbInstance);
                        if (!result)
                        {
                            printerStream.Flush();
                            Printer.RestoreDefaults();
                            Environment.Exit(2);
                        }
                    }
                    if (bm)
                    {
                        Printer.PrintMessage("\nOperation took #b#{0}## ms.", sw.ElapsedMilliseconds);
                    }
                    printerStream.Flush();
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

        private static void PrintAllOptions(string[] args, Parser parser, Printer.PrinterStream printerStream)
        {
            // We didn't hit a verb, print the base options and then all the plugin ones
            printerStream.WriteLine((new Options()).GetUsage());
            printerStream.Flush();

            foreach (var pluginOptionsAndAssemblies in PluginOptionsAndAssemblies)
            {
                System.Reflection.MethodInfo helpOption = GetHelpMethod(pluginOptionsAndAssemblies.Item1);
                if (helpOption != null)
                {
                    printerStream.WriteLine();
                    printerStream.WriteLine(helpOption.Invoke(pluginOptionsAndAssemblies.Item1, new object[0]));
                }
                else
                {
                    Printer.PrintMessage("Warning - plugin #b#{0}## does not have built-in help.", pluginOptionsAndAssemblies.Item2.GetName().Name);
                }
            }
        }

        private static MethodInfo GetVerbHelpMethod(object activatedPlugin)
        {
            foreach (var x in activatedPlugin.GetType().GetMethods())
            {
                if (x.GetCustomAttribute(typeof(HelpVerbOptionAttribute)) != null)
                    return x;
            }
            return null;
        }

        private static MethodInfo GetHelpMethod(object activatedPlugin)
        {
            foreach (var x in activatedPlugin.GetType().GetMethods())
            {
                if (x.GetCustomAttribute(typeof(HelpOptionAttribute)) != null)
                    return x;
            }
            return null;
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Printer.RestoreDefaults();
        }
    }
}
