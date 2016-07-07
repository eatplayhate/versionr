using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class DeleteAnnotationVerbOptions : GetAnnotationVerbOptions
    {
        public override BaseCommand GetCommand()
        {
            return new DeleteAnnotation();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Deletes the annotation object for a specified version.",
                    "",
                    "Annotations are blobs of data that are associated with a specific version and a specific key.",
                    "",
                    "Deleted annotations may still be recovered as they remain in the vault."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "delete-annotation";
            }
        }
    }
    class DeleteAnnotation : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            DeleteAnnotationVerbOptions localOptions = options as DeleteAnnotationVerbOptions;
            Objects.Annotation annotation = null;
            if (string.IsNullOrEmpty(localOptions.Key))
            {
                if (!localOptions.Plain)
                    Printer.PrintMessage("#e#Error:## an annotation key must be specified.");
                return false;
            }
            if (!string.IsNullOrEmpty(localOptions.Version))
            {
                Objects.Version ver = Workspace.GetPartialVersion(localOptions.Version);
                if (ver == null)
                {
                    if (!localOptions.Plain)
                        Printer.PrintMessage("#e#Error:## can't find version #b#\"{0}\"## to remove annotation.", localOptions.Version);
                    return false;
                }
                annotation = Workspace.GetAnnotation(ver.ID, localOptions.Key, localOptions.IgnoreCase);
                if (annotation == null)
                {
                    if (!localOptions.Plain)
                        Printer.PrintMessage("#e#Error:## no annotation matching that key for the specified version.");
                    return false;
                }
            }
            else
            {
                var annotations = Workspace.GetPartialAnnotation(localOptions.Key);
                if (annotations.Count == 0)
                {
                    if (!localOptions.Plain)
                        Printer.PrintMessage("#e#Error:## no annotation matching that ID.");
                    return false;
                }
                else if (annotations.Count > 1)
                {
                    if (!localOptions.Plain)
                    {
                        Printer.PrintMessage("#e#Error:## found #b#{0}## multiple matching annotations:", annotations.Count);
                        for (int i = 0; i < annotations.Count; i++)
                        {
                            var x = annotations[i];
                            string suffix = "";
                            if (x.Active == false)
                                suffix = "#e#(deleted)##";
                            else
                            {
                                if (Workspace.GetAnnotation(x.Version, x.Key, false).ID == x.ID)
                                    suffix = "#s#(tip)##";
                            }
                            suffix += string.Format(" #q#{0}##", Workspace.GetAnnotationPayloadSize(x));
                            Printer.PrintMessage(" [{0}]: #b#{1}## on version #b#{2}## {5}\n   by #b#{3}## on {4} #q#(ID: {6})##", i, x.Key, x.Version, x.Author, x.Timestamp.ToLocalTime(), suffix, x.ID);
                        }
                    }
                    return false;
                }
                else
                    annotation = annotations[0];
            }
            if (annotation.Active != false)
            {
                Workspace.DeleteAnnotation(annotation);
                Printer.PrintMessage("Deleted.");
                return true;
            }
            return false;
        }
    }
}
