using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;
using Versionr.Commands;
using Versionr.Utilities;

namespace Bomerman
{
    public class BomSetEOLOptions : FileCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Sets the line-ending style for a file. If no line ending style is chosen, it will use the most popular type in the file."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "set-eol";
            }
        }

        [Option("crlf", DefaultValue = false, HelpText = "Sets the line ending type to CRLF (Windows).", MutuallyExclusiveSet = "LET")]
        public bool CRLF { get; set; }

        [Option("lf", DefaultValue = false, HelpText = "Sets the line ending type to NF (Unix/OSX).", MutuallyExclusiveSet = "LET")]
        public bool LF { get; set; }

        [Option("cr", DefaultValue = false, HelpText = "Sets the line ending type to CR (old OSX).", MutuallyExclusiveSet = "LET")]
        public bool CR { get; set; }

        [Option('c', "recorded", HelpText = "Matches only files that are recorded")]
        public bool Recorded { get; set; }

        public override BaseCommand GetCommand()
        {
            return new BomSetEOL();
        }
    }

    class EOLStats
    {
        public int LEFixes { get; set; } = 0;
    }

    class BomSetEOL : FileCommand
    {
        BomSetEOLOptions LocalOptions;
        protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
        {
            BomSetEOLOptions localOptions = options as BomSetEOLOptions;
            LocalOptions = localOptions;
            try
            {
                List<Versionr.Status.StatusEntry> realTargets = new List<Status.StatusEntry>();
                foreach (var x in targets)
                {
                    if (!x.IsDirectory && x.FilesystemEntry != null)
                    {
                        if (localOptions.Recorded && x.Staged == false)
                            continue;
                        realTargets.Add(x);
                    }
                }
                Printer.PrintMessage("{0} files in initial list...", realTargets.Count);

                EOLStats cs = new EOLStats();

                List<Task> tasks = new List<Task>();
                foreach (var x in realTargets)
                {
                    tasks.Add(GetTaskFactory(options).StartNew(() =>
                    {
                        var newFileType = Versionr.Utilities.FileClassifier.Classify(x.FilesystemEntry.Info);
                        if (newFileType == Versionr.Utilities.FileEncoding.Binary)
                        {
                            return;
                        }
                        // Displaying local modifications
                        Encoding encoding = BomClean.VSREncodingToEncoding(newFileType);
                        string fullText;
                        using (var fs = x.FilesystemEntry.Info.OpenRead())
                        using (var sr = new System.IO.StreamReader(fs, encoding))
                        {
                            fullText = sr.ReadToEnd();
                        }
                        string resultString;
                        BomClean.LineEndingType let;
                        if (UnifyLineEndings(fullText, localOptions, out resultString, out let))
                        {
                            x.FilesystemEntry.Info.IsReadOnly = false;
                            using (var fs = x.FilesystemEntry.Info.Open(System.IO.FileMode.Create))
                            using (var sw = new System.IO.StreamWriter(fs, encoding))
                            {
                                sw.Write(resultString);
                            }
                            cs.LEFixes++;
                            Printer.PrintMessage("#b#{0}##: => #s#{1}", x.CanonicalName, let == BomClean.LineEndingType.CR ? "CR" : let == BomClean.LineEndingType.CRLF ? "CRLF" : "LF");
                        }
                    }));
                    if (System.Diagnostics.Debugger.IsAttached)
                        tasks[tasks.Count - 1].Wait();
                }
                Task.WaitAll(tasks.ToArray());
                if (cs.LEFixes > 0)
                    Printer.PrintMessage("Updated line endings for {0} files.", cs.LEFixes);
            }
            finally
            {

            }
            return true;
        }

        private bool UnifyLineEndings(string fullText, BomSetEOLOptions localOptions, out string resultString, out BomClean.LineEndingType let)
        {
            resultString = null;
            StringBuilder sb = new StringBuilder();

            int[] leTypes = new int[3];

            bool updatedLines = false;

            BomClean.LineEndingCursor cursor = new BomClean.LineEndingCursor(fullText);

            if (localOptions.CRLF)
                let = BomClean.LineEndingType.CRLF;
            else if (localOptions.CR)
                let = BomClean.LineEndingType.CR;
            else if (localOptions.LF)
                let = BomClean.LineEndingType.LF;
            else
            {
                while (cursor.NextLine())
                {
                    var leType = cursor.LineEndingType;

                    if (leType != BomClean.LineEndingType.EOF)
                        leTypes[(int)leType]++;
                }
                if (leTypes[0] > leTypes[1])
                {
                    if (leTypes[0] > leTypes[2])
                        let = (BomClean.LineEndingType)0;
                    else
                        let = (BomClean.LineEndingType)2;
                }
                else
                {
                    if (leTypes[1] > leTypes[2])
                        let = (BomClean.LineEndingType)1;
                    else
                        let = (BomClean.LineEndingType)2;
                }
                if (leTypes.Where(x => x > 0).Count() < 2)
                    return false;
                cursor = new BomClean.LineEndingCursor(fullText);
            }

            while (cursor.NextLine())
            {
                var le = cursor.LineEndingType;

                var line = cursor.Line;

                sb.Append(line.Array, line.Offset, line.Count);
                if (le == BomClean.LineEndingType.EOF)
                    break;

                if (le != let)
                {
                    updatedLines = true;
                }
                if (let == BomClean.LineEndingType.CR)
                    sb.Append('\x0D');
                else if (let == BomClean.LineEndingType.CRLF)
                {
                    sb.Append('\x0D');
                    sb.Append('\x0A');
                }
                else if (let == BomClean.LineEndingType.LF)
                    sb.Append('\x0A');
            }
            if (updatedLines)
            {
                resultString = sb.ToString();
                return true;
            }
            return false;
        }

        protected override bool OnNoTargetsAssumeAll
        {
            get
            {
                return true;
            }
        }
    }


}
