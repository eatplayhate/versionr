using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class CommitVerbOptions : RecordVerbOptions
    {
        [Option('f', "force", HelpText = "Forces the commit to happen even if it would create a new branch head.")]
        public bool Force { get; set; }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "#q#This command will chronicle any recorded changes into the Versionr repository.",
                    "",
                    "The process will create a new #b#version#q# with its parent set to the current revision. It will then update the current branch head information to point to the newly created version.",
                    "",
                    "If you are committing from a version which is not the branch head, a new head will be created. As this is not typically a desired outcome, the commit operation will not succeed without the `#b#--force#q#` option.",
                    "",
                    "The `#b#--message#q#` option specifies the message that is associated with the new version. A message is required.",
                    "",
                    "The commit command also allows recording additional objects for inclusion in the new version, the rules for specifying these options are below.",
                }.Concat(FileCommandVerbOptions.SharedDescription).ToArray();
            }
        }
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}#q# [options] [file1, file2 ... fileN]", Verb);
            }
        }

        public override string Verb
        {
            get
            {
                return "commit";
            }
        }

        [Option('m', "message", HelpText="Commit message.")]
        public string Message { get; set; }

        [Option("message-file", HelpText = "Commit message.")]
        public string MessageFile { get; set; }

    }
    class Commit : Record
    {
		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
		{
            CommitVerbOptions localOptions = options as CommitVerbOptions;

            if (localOptions.Message == null && localOptions.MessageFile == null)
            {
                Printer.PrintError("#x#Error:## A commit message must be specified with the --message or --message-file options.");
                return false;
            }

            if (targets != null && targets.Count > 0)
            {
                ws.RecordChanges(status, targets, localOptions.Missing, false, RecordFeedback);
            }
            string message = localOptions.Message;
            if (localOptions.MessageFile != null)
            {
                using (var fs = System.IO.File.OpenText(localOptions.MessageFile))
                {
                    message = fs.ReadToEnd();
                }
            }
            else
            {
                message = message.Replace("\\\"", "\"");
                message = message.Replace("\\n", "\n");
                message = message.Replace("\\t", "\t");
            }
            if (!ws.Commit(message, localOptions.Force))
                return false;
            return true;
        }

        protected override bool RequiresTargets { get { return false; } }

	}
}
