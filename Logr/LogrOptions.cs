using CommandLine;
using CommandLine.Text;
using System;
using System.Linq;

namespace Logr
{
    public enum BuildStatus
    {
        Pending,
        Building,
        Passed,
        Failed,
    }

    public class LogrOptions
    {
        [ParserState]
        public IParserState LastParserState { get; set; }

        [Option('r', "repo", Required = true, HelpText = "Path to the versionr repository")]
        public string Repo { get; set; }

        [Option('l', "logfile", Required = true, HelpText = "The output log file path. If an existing file is specified, its entries will be updated.")]
        public string LogFile { get; set; }

        [Option("limit", Required = false, HelpText = "The max number of log entries to output.")]
        public int Limit { get; set; }

        public BuildStatus Status { get; set; }
        public string BuildVersionID { get; set; }

        [Option('s', "start", HelpText = "Marks any 'Pending' entries up to the given version as 'Building'", MetaValue = "<versionID>", MutuallyExclusiveSet = "buildstatus")]
        public string BuildStarted
        {
            get { return (Status == BuildStatus.Building) ? BuildVersionID : null; }
            set { BuildVersionID = value; Status = BuildStatus.Building; }
        }

        [Option('p', "pass", HelpText = "Marks any 'Building' entries up to the given version as 'Passed'", MetaValue = "<versionID>", MutuallyExclusiveSet = "buildstatus")]
        public string BuildPassed
        {
            get { return (Status == BuildStatus.Passed) ? BuildVersionID : null; }
            set { BuildVersionID = value; Status = BuildStatus.Passed; }
        }

        [Option('f', "fail", HelpText = "Marks any 'Building' entries up to the given version as 'Failed'", MetaValue = "<versionID>", MutuallyExclusiveSet = "buildstatus")]
        public string BuildFailed
        {
            get { return (Status == BuildStatus.Passed) ? BuildVersionID : null; }
            set { BuildVersionID = value; Status = BuildStatus.Failed; }
        }

        public LogrOptions()
        {
        }

        [HelpOption]
        public string GetUsage()
        {
            var help = new HelpText
            {
                Heading = new HeadingInfo("#b#Logr## #q#- Retrieves Versionr logs and updates continuous integration status.##"),
                AddDashesToOption = true,
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
            help.AddPreOptionsLine("Usage: #b#logr## -r repo -l logfile #q#[options]## \n\n#b#Options:");
            help.AddOptions(this);
            return help;
        }

        private void Help_FormatOptionHelpText(object sender, FormatOptionHelpTextEventArgs e)
        {
            e.Option.HelpText = "##" + e.Option.HelpText + "#b#";
        }
    }
}
