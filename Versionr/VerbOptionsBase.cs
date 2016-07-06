using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace Versionr
{
    public abstract class VerbOptionBase
    {
        [ParserState]
        public IParserState LastParserState { get; set; }
        public abstract string Verb { get; }
        public abstract Commands.BaseCommand GetCommand();
        public abstract string[] Description { get; }
        [HelpOption]
        public string GetUsage()
        {
            var help = new HelpText
            {
                Heading = new HeadingInfo("#b#Versionr## #q#- the less hateful version control system.##"),
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
            string[] usageOpts = Usage.Split(new char[] { '\n' });
            help.MaximumDisplayWidth += 16;
            help.AddPreOptionsLine(string.Format("\nUsage:\n   {0}", usageOpts[0]));
            for (int i = 1; i < usageOpts.Length; i++)
                help.AddPreOptionsLine(string.Format("Or:\n   {0}", usageOpts[i]));
            help.AddPreOptionsLine("\n");
            help.MaximumDisplayWidth -= 16;
            help.AddPreOptionsLine(string.Format("##The `#b#{0}##` Command:", Verb));
            foreach (var x in Description)
                help.AddPreOptionsLine("  " + x);
            help.AddPreOptionsLine("\n##Options:#b#");
            help.AddOptions(this);
            return help;
        }

        private void Help_FormatOptionHelpText(object sender, FormatOptionHelpTextEventArgs e)
        {
            e.Option.HelpText = "##" + e.Option.HelpText + "#b#";
        }

        [Option("verbose", HelpText = "Display verbose diagnostics information.")]
        public bool Verbose { get; set; }

        [Option('q', "quiet", HelpText = "Disable output of all messages except errors and warnings.")]
        public bool Quiet { get; set; }

        [Option("nocolours", HelpText = "Disable coloured output.")]
        public bool NoColours { get; set; }

        [Option("logfile", HelpText = "Specify a log file for versionr.")]
        public string Logfile { get; set; }

        public virtual string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## #q#[options]##", Verb);
            }
        }
    }
}
