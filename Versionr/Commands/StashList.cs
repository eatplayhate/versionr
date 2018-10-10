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
                return string.Format("#b#versionr #i#{0}## #q#[options]## [stash name/key/guid]", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Lists the stashes in a vault or extracts details from a stash object.",
                    "",
                    "When specifying a specific stash, you may also generate a patch file from any text patches in the stashed object."
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

        [Option('d', "diff", HelpText = "Show text file differences.")]
        public bool DisplayDiff { get; set; }

        [Option('p', "patch", HelpText = "Resulting patch file to generate.")]
        public string PatchFile { get; set; }
        [Option('r', "remote", Required = false, HelpText = "Specifies the remote URL or saved name to query stashes on a remote server.")]
        public string Remote { get; set; }

        [ValueList(typeof(List<string>))]
        public List<string> Name { get; set; }

        public override BaseCommand GetCommand()
        {
            return new StashList();
        }
    }
    class StashList : BaseCommand
    {
        static public Area.StashInfo LookupStash(Area ws, string name, bool expectNone = false)
        {
            bool ambiguous;
            return LookupStash(ws, name, out ambiguous, expectNone);
        }
        static public Area.StashInfo LookupStash(Area ws, string name, out bool ambiguous, bool expectNone = false)
        {
            var stashes = ws.FindStash(name);
            Area.StashInfo stash = null;
            if (stashes.Count == 1)
                stash = stashes[0];

            ambiguous = false;

            if (stashes.Count == 0 && !expectNone)
                Printer.PrintMessage("#e#Error:## Couldn't find a stash matching a name/key/ID of \"{0}\".", name);
            else if (stashes.Count > 1)
            {
                ambiguous = true;
                Printer.PrintMessage("#e#Error:## Ambiguous stash \"{0}\", could be:", name);
                foreach (var x in stashes)
                {
                    Printer.PrintMessage(" #b#{0}##: {5} - #q#{4}##\n    {1} - by {2} on #q#{3}##", x.Author + "-" + x.Key, string.IsNullOrEmpty(x.Name) ? "(no name)" : ("\"" + x.Name + "\""), x.Author, x.Time.ToLocalTime(), x.GUID, Versionr.Utilities.Misc.FormatSizeFriendly(x.File.Length));
                }
            }

            return stash;
        }
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            StashListVerbOptions localOptions = options as StashListVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            if (localOptions.Remote != null)
            {
                if (localOptions.Remote == string.Empty)
                    localOptions.Remote = "default";
                LocalState.RemoteConfig config = null;
                if (ws != null)
                {
                    config = ws.GetRemote(string.IsNullOrEmpty(localOptions.Remote) ? "default" : localOptions.Remote);
                }
                if (config != null)
                    localOptions.Remote = config.URL;
                Printer.PrintMessage("Querying stashes on remote #b#{0}##.", localOptions.Remote);
                Network.IRemoteClient client = ws.Connect(localOptions.Remote, false);
                var stashes = client.ListStashes(localOptions.Name);
                if (stashes.Count == 0)
                    Printer.PrintMessage("Remote vault has no stashes{0}.", (localOptions.Name == null || localOptions.Name.Count == 0) ? "" : " matching the query name.");
                else
                {
                    Printer.PrintMessage("Remote vault has #b#{0}## stash{1} {2}:", stashes.Count, stashes.Count == 1 ? "" : "es", (localOptions.Name == null || localOptions.Name.Count == 0) ? "available" : " matching the query name.");
                    foreach (var x in Enumerable.Reverse(stashes))
                    {
                        Printer.PrintMessage(" #b#{0}##: #q#{4}##\n    {1} - by {2} on #q#{3}##", x.Author + "-" + x.Key, string.IsNullOrEmpty(x.Name) ? "(no name)" : ("\"" + x.Name + "\""), x.Author, x.Time.ToLocalTime(), x.GUID);
                    }
                }
                client.Close();
                return true;
            }
            if (localOptions.Name == null || localOptions.Name.Count == 0)
            {
                var stashes = ws.ListStashes();
                if (stashes.Count == 0)
                    Printer.PrintMessage("Vault has no stashes.");
                else
                {
                    Printer.PrintMessage("Vault has #b#{0}## stash{1} available:", stashes.Count, stashes.Count == 1 ? "" : "es");
                    foreach (var x in Enumerable.Reverse(stashes))
                    {
                        Printer.PrintMessage(" #b#{0}##: {5} - #q#{4}##\n    {1} - by {2} on #q#{3}##", x.Author + "-" + x.Key, string.IsNullOrEmpty(x.Name) ? "(no name)" : ("\"" + x.Name + "\""), x.Author, x.Time.ToLocalTime(), x.GUID, Versionr.Utilities.Misc.FormatSizeFriendly(x.File.Length));
                    }
                }
            }
            else
            {
                foreach (var n in localOptions.Name)
                {
                    var stash = LookupStash(ws, n);
                    if (stash != null)
                    {
                        if (localOptions.DisplayDiff)
                            Printer.PrintMessage("{1} patch for stash #b#{2}## ({0})", stash.GUID, string.IsNullOrEmpty(localOptions.PatchFile) ? "Showing" : "Generating", stash.Key);
                        else
                            Printer.PrintMessage("Showing stash #b#{1}## ({0})", stash.GUID, stash.Key);

                        Stream baseStream = null;
                        if (string.IsNullOrEmpty(localOptions.PatchFile))
                            baseStream = new MemoryStream();
                        else
                            baseStream = File.Open(localOptions.PatchFile, FileMode.Create, FileAccess.ReadWrite);

                        ws.DisplayStashOperations(stash);
                        if (localOptions.DisplayDiff)
                        {
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
                }
            }
            return true;
        }
    }
}
