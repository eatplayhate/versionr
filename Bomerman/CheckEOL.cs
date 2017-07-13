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
    public class BomCheckEOLOptions : FileCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Displays the line-ending style for a file."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "check-eol";
            }
        }

        [Option('c', "recorded", HelpText = "Matches only files that are recorded")]
        public bool Recorded { get; set; }

        [Option('m', "ambiguous", HelpText = "Only show results for files with ambiguous line endings.", MutuallyExclusiveSet = "Display")]
        public bool Ambigious { get; set; }

        [Option("lfonly", HelpText = "Only show files with LF endings.", MutuallyExclusiveSet = "Display")]
        public bool LFOnly { get; set; }

        [Option("cronly", HelpText = "Only show files with CR endings.", MutuallyExclusiveSet = "Display")]
        public bool CROnly { get; set; }

        [Option("crlfonly", HelpText = "Only show files with CRLF endings.", MutuallyExclusiveSet = "Display")]
        public bool CRLFOnly { get; set; }

        public override BaseCommand GetCommand()
        {
            return new BomCheckEOL();
        }
    }

    class CheckEOLStats
    {
        public int CRLFFiles { get; set; } = 0;
        public int CRFiles { get; set; } = 0;
        public int LFFiles { get; set; } = 0;
        public int MixedFiles { get; set; } = 0;
    }

    class BomCheckEOL : FileCommand
    {
        BomCheckEOLOptions LocalOptions;
        protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
        {
            BomCheckEOLOptions localOptions = options as BomCheckEOLOptions;
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

                CheckEOLStats cs = new CheckEOLStats();

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
                        IdentifyLineEndings(x.CanonicalName, fullText, localOptions, cs);
                    }));
                    if (System.Diagnostics.Debugger.IsAttached)
                        tasks[tasks.Count - 1].Wait();
                }
                Task.WaitAll(tasks.ToArray());
                if ((cs.CRLFFiles + cs.CRFiles + cs.LFFiles + cs.MixedFiles) > 1)
                {
                    Printer.PrintMessage("Final Count:");
                    if (cs.CRLFFiles > 0)
                        Printer.PrintMessage("\t#b#{0}## CRLF", cs.CRLFFiles);
                    if (cs.LFFiles > 0)
                        Printer.PrintMessage("\t#b#{0}## LF", cs.LFFiles);
                    if (cs.CRFiles > 0)
                        Printer.PrintMessage("\t#b#{0}## CR", cs.CRFiles);
                    if (cs.MixedFiles > 0)
                        Printer.PrintMessage("\t#w#{0}## Mixed", cs.MixedFiles);
                }
            }
            finally
            {

            }
            return true;
        }

        private void IdentifyLineEndings(string canonicalName, string fullText, BomCheckEOLOptions localOptions, CheckEOLStats cs)
        {
            StringBuilder sb = new StringBuilder();

            int[] leTypes = new int[3];

            BomClean.LineEndingCursor cursor = new BomClean.LineEndingCursor(fullText);

            bool specific = (localOptions.CRLFOnly || localOptions.CROnly || localOptions.LFOnly);

            while (cursor.NextLine())
            {
                var leType = cursor.LineEndingType;

                if (leType != BomClean.LineEndingType.EOF)
                    leTypes[(int)leType]++;
            }
            if (leTypes.Where(x => x > 0).Count() > 1)
            {
                if (!specific)
                    Printer.PrintMessage("#b#{0}##: #w#Mixed## #q#({1})##", canonicalName, BomClean.LETypesToString(leTypes));
                lock (cs)
                    cs.MixedFiles++;
            }
            else if (leTypes[0] > 0)
            {
                if (localOptions.CRLFOnly || (!specific && !localOptions.Ambigious))
                    Printer.PrintMessage("#b#{0}##: #s#CRLF##", canonicalName);
                lock (cs)
                    cs.CRLFFiles++;
            }
            else if (leTypes[1] > 0)
            {
                if (localOptions.LFOnly || (!specific && !localOptions.Ambigious))
                    Printer.PrintMessage("#b#{0}##: #s#LF##", canonicalName);
                lock (cs)
                    cs.LFFiles++;
            }
            else if (leTypes[2] > 0)
            {
                if (localOptions.CROnly || (!specific && !localOptions.Ambigious))
                    Printer.PrintMessage("#b#{0}##: #w#CR##", canonicalName);
                lock (cs)
                    cs.CRFiles++;
            }
            else if (!specific && !localOptions.Ambigious)
                Printer.PrintMessage("#b#{0}##: #c#(No line endings)##", canonicalName);
            return;
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
