using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class TagVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## -v #b#version## [--add #b#\\#tag1##;#b#\\#tag2##] [--remove#b#\\#tag3##;#b#\\#tag4##]", Verb);
            }
        }
        public override BaseCommand GetCommand()
        {
            return new Tag();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Adds or removes tags associated with a version. Tags must consist of one word prefixed with a hash (#b#\\###) character.",
                    "",
                    "To specific multiple tags to add or remove, use a semicolon to separate values. Tags specified as a list are not permitted to have spaces between them."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "tag";
            }
        }
        [OptionList('a', "add", ';', HelpText = "Adds tags to a specified version")]
        public IEnumerable<string> TagsToAdd { get; set; }
        [OptionList('r', "remove", ';', HelpText = "Adds tags to a specified version")]
        public IEnumerable<string> TagsToRemove { get; set; }
        [Option('v', "version", Required = true, HelpText = "The version to tag or untag")]
        public string Version { get; set; }
    }
    class Tag : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            TagVerbOptions localOptions = options as TagVerbOptions;
            Objects.Version ver = Workspace.GetPartialVersion(localOptions.Version);
            if (ver == null)
            {
                Printer.PrintMessage("#e#Error:## can't find version #b#\"{0}\"## for tag alteration.", localOptions.Version);
                return false;
            }
            if (localOptions.TagsToAdd != null)
            {
                foreach (var x in localOptions.TagsToAdd)
                {
                    if (!x.StartsWith("#"))
                    {
                        Printer.PrintMessage("Can't add tag #b#{0}##. Tag is required to start with a \"\\#\"", x);
                        return false;
                    }
                    if (x.Length == 1)
                    {
                        Printer.PrintMessage("Can't add an empty tag.");
                        return false;
                    }
                }
            }
            if (localOptions.TagsToRemove != null)
            {
                foreach (var x in localOptions.TagsToRemove)
                {
                    if (!x.StartsWith("#"))
                    {
                        Printer.PrintMessage("Can't remove tag #b#{0}##. Tag is required to start with a \"\\#\"", x);
                        return false;
                    }
                    if (x.Length == 1)
                    {
                        Printer.PrintMessage("Can't remove an empty tag.");
                        return false;
                    }
                }
            }
            if (localOptions.TagsToAdd != null)
            {
                foreach (var x in localOptions.TagsToAdd)
                    Workspace.AddTag(ver.ID, x.Substring(1));
            }
            if (localOptions.TagsToRemove != null)
            {
                foreach (var x in localOptions.TagsToRemove)
                    Workspace.RemoveTag(ver.ID, x.Substring(1));
            }
            return true;
        }
    }
}
