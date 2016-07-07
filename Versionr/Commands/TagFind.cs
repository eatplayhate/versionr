using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class TagFindVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## <tag name>", Verb);
            }
        }
        public override BaseCommand GetCommand()
        {
            return new TagFind();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Searches for versions with one or more tags."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "tag-find";
            }
        }
        [Option('a', "all", HelpText = "Search for versions which only have all of the specified tags (rather than any).")]
        public bool All { get; set; }
        [Option('i', "ignore-case", HelpText = "Ignore case for tag values.")]
        public bool IgnoreCase { get; set; }
        [ValueList(typeof(List<string>))]
        public List<string> Tags { get; set; }
    }
    class TagFind : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            TagFindVerbOptions localOptions = options as TagFindVerbOptions;
            if (localOptions.Tags == null || localOptions.Tags.Count == 0)
            {
                Printer.PrintMessage("#e#Error:## no tag specified to search for!");
                return false;
            }
            bool first = true;
            List<Guid> results = new List<Guid>();
            foreach (var x in localOptions.Tags)
            {
                if (!x.StartsWith("#"))
                {
                    Printer.PrintMessage("#e#Error:## tag {0} doesn't start with #b#\\###!", x);
                    return false;
                }
                IEnumerable<Guid> getTags = Workspace.FindTags(x.Substring(1), localOptions.IgnoreCase).Select(y => y.Version);
                if (!localOptions.All || first)
                    results = results.Concat(getTags).Distinct().ToList();
                else
                    results = results.Intersect(getTags).ToList();
                first = false;
            }
            Printer.PrintMessage("Matched {0} versions.", results.Count);
            foreach (var x in results)
            {
                Objects.Version ver = Workspace.GetVersion(x);
                Printer.PrintMessage(" - #b#{0}## on #c#{1}## ##[#s#{4}##] #q#by {2} at {3}", ver.ID, Workspace.GetBranch(ver.Branch).Name, ver.Author, ver.Timestamp.ToLocalTime(), string.Join(" ", Workspace.GetTagsForVersion(ver.ID).Select(t => "\\#" + t).ToArray()));
            }
            return true;
        }
    }
}
