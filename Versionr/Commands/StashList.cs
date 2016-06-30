using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Commands
{
    class StashListVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [stash name or guid]", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Lists the stashes in a vault or extracts details from a stash object."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "stash-list";
            }
        }

        [Option('p', "patch", HelpText = "Resulting patch file")]
        public string PatchFile { get; set; }

        [ValueList(typeof(List<string>))]
        public List<string> Name { get; set; }
    }
    class StashList : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            StashListVerbOptions localOptions = options as StashListVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            if (localOptions.Name == null || localOptions.Name.Count == 0)
            {
                var stashes = ws.ListStashes();
                if (stashes.Count == 0)
                    Printer.PrintMessage("Vault has no stashes.");
                else
                {
                    Printer.PrintMessage("Vault has #b#{0}## stash{1} available:", stashes.Count, stashes.Count == 1 ? "" : "es");
                    foreach (var x in stashes)
                    {
                        Printer.PrintMessage(" #b#{0}##: - #q#{4}##\n    {1} - by {2} on #q#{3}##", x.Author + "-" + x.Key, string.IsNullOrEmpty(x.Name) ? "(no name)" : ("\"" + x.Name + "\""), x.Author, x.Time.ToLocalTime(), x.GUID);
                    }
                }
            }
            else
            {
                Area.StashInfo stash = ws.FindStash(localOptions.Name[0]);

                Printer.PrintMessage("{1} patch for stash {0}", stash.GUID, string.IsNullOrEmpty(localOptions.PatchFile) ? "Showing" : "Generating");

                if (stash == null)
                    Printer.PrintMessage("#e#Error:## Couldn't find a stash matching a name/key/ID of \"{0}\".", localOptions.Name);
                else
                {
                    Stream baseStream = null;
                    if (string.IsNullOrEmpty(localOptions.PatchFile))
                        baseStream = new MemoryStream();
                    else
                        baseStream = File.Open(localOptions.PatchFile, FileMode.Create, FileAccess.ReadWrite);

                    using (StreamWriter sw = new StreamWriter(baseStream))
                    {
                        ws.StashToPatch(sw, stash);
                        sw.Flush();
                        baseStream.Position = 0;
                        if (string.IsNullOrEmpty(localOptions.PatchFile))
                        {
                            using (TextReader tr = new StreamReader(baseStream))
                            {
                                string[] patchLines = tr.ReadToEnd().Split('\n');
                                foreach (var x in patchLines)
                                {
                                    if (x.StartsWith("@@"))
                                        Printer.PrintMessage("#c#{0}##", Printer.Escape(x));
                                    else if (x.StartsWith("+++"))
                                        Printer.PrintMessage("#b#{0}##", Printer.Escape(x));
                                    else if (x.StartsWith("---"))
                                        Printer.PrintMessage("#b#{0}##", Printer.Escape(x));
                                    else if (x.StartsWith("-"))
                                        Printer.PrintMessage("#e#{0}##", Printer.Escape(x));
                                    else if (x.StartsWith("+"))
                                        Printer.PrintMessage("#s#{0}##", Printer.Escape(x));
                                    else
                                        Printer.PrintMessage(Printer.Escape(x));
                                }
                            }
                        }
                    }
                }
            }
            return true;
        }
    }
}
