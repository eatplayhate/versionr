using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class ListAnnotationVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## -v #b#version## [--key key]", Verb);
            }
        }
        public override BaseCommand GetCommand()
        {
            return new ListAnnotation();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Retrieves a list of annotation objects for a specified version."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "list-annotation";
            }
        }
        [Option('i', "ignore-case", HelpText = "Ignore case for annotation key.")]
        public bool IgnoreCase { get; set; }
        [Option('a', "all", HelpText = "Displays all versions of annotations for a specific key.")]
        public bool All { get; set; }
        [Option('d', "deleted", HelpText = "Shows deleted annotations.")]
        public bool Deleted { get; set; }
        [Option('v', "version", Required = true, HelpText = "The version to list annotations on.")]
        public string Version { get; set; }
        [Option("key", Required = false, HelpText = "Specifies an annotation key to check.")]
        public string Key { get; set; }
    }
    class ListAnnotation : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            ListAnnotationVerbOptions localOptions = options as ListAnnotationVerbOptions;
            List<Objects.Annotation> annotations = new List<Objects.Annotation>();
            Objects.Version ver = Workspace.GetPartialVersion(localOptions.Version);
            if (ver == null)
            {
                Printer.PrintMessage("#e#Error:## can't find version #b#\"{0}\"## to retrieve annotation list.", localOptions.Version);
                return false;
            }
            if (string.IsNullOrEmpty(localOptions.Key))
            {
                annotations.AddRange(Workspace.GetAnnotationsForVersion(ver.ID, !localOptions.Deleted));
            }
            else
            {
                annotations.AddRange(Workspace.GetAllAnnotations(ver.ID, localOptions.Key, localOptions.IgnoreCase));
            }
            if (annotations.Count == 0)
                Printer.PrintMessage("No annotations matching query.");
            else
            {
                Printer.PrintMessage("Found #b#{0}## annotations.", annotations.Count);
                HashSet<string> key = new HashSet<string>();
                for (int i = 0; i < annotations.Count; i++)
                {
                    var x = annotations[i];
                    string suffix = "";
                    if (x.Active == false)
                    {
                        if (localOptions.Deleted)
                            continue;
                        else
                            suffix = "#e#(deleted)##";
                    }
                    else
                    {
                        if (!localOptions.All)
                        {
                            if (key.Contains(x.Key))
                                continue;
                            key.Add(x.Key);
                        }
                        if (Workspace.GetAnnotation(x.Version, x.Key, false).ID == x.ID)
                            suffix = "#s#(tip)##";
                    }
                    suffix += string.Format(" #q#{0}##", Workspace.GetAnnotationPayloadSize(x));
                    if (!Workspace.HasAnnotationData(x))
                        suffix += " #w#(missing data)##";
                    Printer.PrintMessage(" #b#{1}## on version #b#{2}## by #b#{3}## on {4}{5}", i, x.Key, x.Version, x.Author, x.Timestamp.ToLocalTime(), suffix);
                }
            }
            return true;
        }
    }
}
