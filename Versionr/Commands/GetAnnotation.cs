using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class GetAnnotationVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## -v #b#version## <key>", Verb);
            }
        }
        public override BaseCommand GetCommand()
        {
            return new GetAnnotation();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Retrieves the annotation object for a specified version.",
                    "",
                    "Annotations are blobs of data that are associated with a specific version and a specific key.",
                    "",
                    "The returned object can be saved as a file or displayed."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "get-annotation";
            }
        }
        [Option('i', "ignore-case", HelpText = "Ignore case for annotation key.")]
        public bool IgnoreCase { get; set; }
        [Option("plain", HelpText = "Don't output anything except annotation contents")]
        public bool Plain { get; set; }
        [Option('v', "version", Required = true, HelpText = "The version to retrieve an annotation from.")]
        public string Version { get; set; }
        [Option("file", Required = false, HelpText = "Specifies a filename to export the annotation object to.")]
        public string Filename { get; set; }
        [ValueOption(0)]
        public string Key { get; set; }
    }
    class GetAnnotation : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            GetAnnotationVerbOptions localOptions = options as GetAnnotationVerbOptions;
            Objects.Version ver = Workspace.GetPartialVersion(localOptions.Version);
            if (ver == null)
            {
                if (!localOptions.Plain)
                    Printer.PrintMessage("#e#Error:## can't find version #b#\"{0}\"## to retrieve annotation.", localOptions.Version);
                return false;
            }
            Objects.Annotation annotation = Workspace.GetAnnotation(ver.ID, localOptions.Key, localOptions.IgnoreCase);
            if (annotation == null)
            {
                if (!localOptions.Plain)
                    Printer.PrintMessage("#e#Error:## no annotation matching that key for the specified version.");
                return false;
            }
            if (!localOptions.Plain)
            {
                Printer.PrintMessage("Version #b#{0}## - annotation #b#{1}##", annotation.Version, annotation.Key);
                Printer.PrintMessage("Added by #b#{0}## on {1}", annotation.Author, annotation.Timestamp);
                Printer.PrintMessage("");
            }
            if (!localOptions.Plain && annotation.Flags.HasFlag(Objects.AnnotationFlags.Binary) && string.IsNullOrEmpty(localOptions.Filename))
                Printer.PrintMessage("#q#[Annotation contents is a binary blob.]##");
            else
            {
                if (string.IsNullOrEmpty(localOptions.Filename))
                {
                    Printer.PrintMessage(Printer.Escape(Workspace.GetAnnotationAsString(annotation)));
                }
                else
                {
                    using (System.IO.Stream s = Workspace.GetAnnotationStream(annotation))
                    using (System.IO.FileStream fs = System.IO.File.Open(localOptions.Filename, System.IO.FileMode.Create))
                        s.CopyTo(fs);
                    if (!localOptions.Plain)
                        Printer.PrintMessage("Wrote annotation contents to file #b#{0}##.", localOptions.Filename);
                }
            }
            return true;
        }
    }
}
