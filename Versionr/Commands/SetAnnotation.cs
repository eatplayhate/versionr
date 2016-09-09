using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class SetAnnotationVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## -v #b#version## --key <key> <contents>\n" +
                    "#b#versionr #i#{0}## -v #b#version## --key <key> --file <filename>", Verb);
            }
        }
        public override BaseCommand GetCommand()
        {
            return new SetAnnotation();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Stores a keyed annotation object for a specified version.",
                    "",
                    "Annotations are blobs of data that are associated with a specific version and a specific key."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "set-annotation";
            }
        }
        [Option("precise", HelpText = "Skip checking for an annotation with a similar but differently-cased key.")]
        public bool Precise { get; set; }
        [Option("no-overwrite", HelpText = "Don't overwrite annotation contents if it is already present.")]
        public bool NoOverwrite { get; set; }
        [Option('v', "version", Required = true, HelpText = "The version to retrieve assign the annotation to.")]
        public string Version { get; set; }
        [Option('k', "key", Required = true, HelpText = "The version key to associate this annotation with.")]
        public string Key { get; set; }
        [Option("file", Required = false, HelpText = "Specifies a filename to import the annotation object from.")]
        public string Filename { get; set; }
        [ValueList(typeof(List<string>))]
        public List<string> StringData { get; set; }
    }
    class SetAnnotation : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            SetAnnotationVerbOptions localOptions = options as SetAnnotationVerbOptions;

            Objects.Version ver = Workspace.GetPartialVersion(localOptions.Version);
            if (ver == null)
            {
                Printer.PrintMessage("#e#Error:## can't find version #b#\"{0}\"## to assign annotation to.", localOptions.Version);
                return false;
            }

            Objects.Annotation annotation = Workspace.GetAnnotation(ver.ID, localOptions.Key, false);
            if (annotation != null && localOptions.NoOverwrite)
            {
                Printer.PrintMessage("Annotation already exists. Overwriting is currently disabled.");
                return false;
            }
            if (!localOptions.Precise)
            {
                annotation = Workspace.GetSimilarAnnotation(ver.ID, localOptions.Key);
                if (annotation != null)
                {
                    Printer.PrintMessage("A similar annotation (with key #b#\"{0}\"##) exists. Delete this annotation or enable #b#--precise## mode to continue.", annotation.Key);
                    return false;
                }
            }

            if (string.IsNullOrEmpty(localOptions.Filename))
            {
                if (localOptions.StringData == null || localOptions.StringData.Count == 0)
                {
                    Printer.PrintMessage("#e#Error:## Data for annotation is empty.");
                    return false;
                }
                return Workspace.SetAnnotation(ver.ID, localOptions.Key, string.Join(" ", localOptions.StringData.ToArray()));
            }
            else
            {
                System.IO.FileInfo info = new System.IO.FileInfo(localOptions.Filename);
                if (!info.Exists)
                {
                    Printer.PrintMessage("#e#Error:## Payload file #b#\"{0}\"## does not exist.", info.FullName);
                    return false;
                }
                using (var s = info.OpenRead())
                    return Workspace.SetAnnotation(ver.ID, localOptions.Key, Utilities.FileClassifier.Classify(info) == Utilities.FileEncoding.Binary, s);
            }
        }
    }
}
