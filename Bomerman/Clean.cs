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
    public class BomCleanOptions : FileCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Cleans whitespace changes from a set of files."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "bom-clean";
            }
        }

        [Option("newlines", DefaultValue = true, HelpText = "Cleans newline-only changes.")]
        public bool Newlines { get; set; }

        [Option("bom", DefaultValue = true, HelpText = "Reverts BOM-only changes.")]
        public bool BOM { get; set; }

        [Option("whitespace", DefaultValue = true, HelpText = "Reverts whitespace-only changes.")]
        public bool WS { get; set; }

        [Option('c', "recorded", HelpText = "Matches only files that are recorded")]
        public bool Recorded { get; set; }

        public override BaseCommand GetCommand()
        {
            return new BomClean();
        }
    }

    class CleanStats
    {
        public int BOMFixes { get; set; } = 0;
        public int LEFixes { get; set; } = 0;
    }

    class BomClean : FileCommand
    {
        BomCleanOptions LocalOptions;
        protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
        {
            BomCleanOptions localOptions = options as BomCleanOptions;
            LocalOptions = localOptions;
            try
            {
                List<Versionr.Status.StatusEntry> realTargets = new List<Status.StatusEntry>();
                foreach (var x in targets)
                {
                    if (x.VersionControlRecord != null && !x.IsDirectory && x.FilesystemEntry != null && x.Code == StatusCode.Modified)
                    {
                        if (localOptions.Recorded && x.Staged == false)
                            continue;
                        realTargets.Add(x);
                    }
                }
                Printer.PrintMessage("{0} files in initial list...", realTargets.Count);
                Dictionary<string, Versionr.Objects.Record> recordMap = new Dictionary<string, Versionr.Objects.Record>();
                foreach (var x in ws.GetRecords(Workspace.Version))
                    recordMap[x.CanonicalName] = x;
                Printer.PrintMessage("Original version: #s#{0}##", Workspace.Version.ID);

                CleanStats cs = new CleanStats();

                List<Task> tasks = new List<Task>();
                foreach (var x in realTargets)
                {
                    tasks.Add(Versionr.Utilities.LimitedTaskDispatcher.Factory.StartNew(() =>
                    {
                        var newFileType = Versionr.Utilities.FileClassifier.Classify(x.FilesystemEntry.Info);
                        if (newFileType == Versionr.Utilities.FileEncoding.Binary)
                        {
                            return;
                        }
                        // Displaying local modifications
                        string tmp = Versionr.Utilities.DiffTool.GetTempFilename();
                        Versionr.Objects.Record rec;
                        if (recordMap.TryGetValue(x.CanonicalName, out rec))
                        {
                            Encoding encoding = VSREncodingToEncoding(newFileType);
                            string messages = string.Format("File #b#{0}##:", x.CanonicalName);
                            ws.RestoreRecord(rec, DateTime.Now, tmp);
                            var oldFileType = Versionr.Utilities.FileClassifier.Classify(new System.IO.FileInfo(tmp));
                            bool fixBOM = false;
                            bool fixLines = false;
                            if (oldFileType != newFileType && localOptions.BOM)
                            {
                                bool allowFix = true;
                                if ((oldFileType == FileEncoding.ASCII || oldFileType == FileEncoding.Latin1) &&
                                    (newFileType == FileEncoding.ASCII || newFileType == FileEncoding.Latin1))
                                    allowFix = false;
                                if (allowFix)
                                {
                                    fixBOM = true;
                                    lock (cs)
                                    {
                                        cs.BOMFixes++;
                                    }
                                    messages += string.Format("\n - Fixing changed encoding: {0} -> {1}", oldFileType, newFileType);
                                }
                            }
                            var fullText = new Lazy<string>(() =>
                            {
                                using (var fs = x.FilesystemEntry.Info.OpenRead())
                                using (var sr = new System.IO.StreamReader(fs, encoding))
                                {
                                    return sr.ReadToEnd();
                                }
                            });
                            var oldFullText = new Lazy<string>(() =>
                            {
                                using (var fs = new System.IO.FileInfo(tmp).OpenRead())
                                using (var sr = new System.IO.StreamReader(fs, VSREncodingToEncoding(oldFileType)))
                                {
                                    return sr.ReadToEnd();
                                }
                            });
                            if (localOptions.Newlines)
                            {
                                // Fast version - all the same, except the newlines changed
                                string resultString;
                                bool lcheckResult = NewLineCheckFast(fullText.Value, oldFullText.Value, ref messages, out resultString);
                                if (lcheckResult == true)
                                {
                                    if (resultString != null)
                                    {
                                        lock (cs)
                                        {
                                            cs.LEFixes++;
                                        }
                                        fixLines = true;
                                        fullText = new Lazy<string>(() => { return resultString; });
                                    }
                                }
                                // Slow version - need to use diff engine
                            }
                            if (fixBOM || fixLines)
                            {
                                Encoding targetEncoding = VSREncodingToEncoding(oldFileType);
                                x.FilesystemEntry.Info.IsReadOnly = false;
                                using (var fs = x.FilesystemEntry.Info.Open(System.IO.FileMode.Create))
                                using (var sw = new System.IO.StreamWriter(fs, targetEncoding))
                                {
                                    sw.Write(fullText.Value);
                                }
                                Printer.PrintMessage(messages);
                            }
                            new System.IO.FileInfo(tmp).IsReadOnly = false;
                            System.IO.File.Delete(tmp);
                        }
                    }));
                    if (System.Diagnostics.Debugger.IsAttached)
                        tasks[tasks.Count - 1].Wait();
                }
                Task.WaitAll(tasks.ToArray());
                if (cs.BOMFixes > 0)
                    Printer.PrintMessage("Updated encoding/BOM for {0} files.", cs.BOMFixes);
                if (cs.LEFixes > 0)
                    Printer.PrintMessage("Updated line endings for {0} files.", cs.LEFixes);
            }
            finally
            {

            }
            return true;
        }

        internal enum LineEndingType
        {
            CRLF = 0,
            LF = 1,
            CR = 2,
            EOF
        }

        internal class LineEndingCursor
        {
            public LineEndingCursor(string s)
            {
                Source = s;
                SourceChars = s.ToCharArray();
            }
            public char[] SourceChars { get; private set; }
            public string Source { get; private set; }
            public int CurrentLineStart { get; set; } = 0;
            public int CurrentLineEnd { get; set; } = 0;
            public int NextStart { get; set; } = 0;
            public ArraySegment<char> Line
            {
                get
                {
                    return new ArraySegment<char>(SourceChars, CurrentLineStart, CurrentLineEnd - CurrentLineStart + 1);
                }
            }
            public LineEndingType LineEndingType
            {
                get
                {
                    if (CurrentLineEnd + 1 == SourceChars.Length)
                        return LineEndingType.EOF;
                    if (SourceChars[CurrentLineEnd + 1] == '\x0A')
                    {
                        return LineEndingType.LF;
                    }
                    if (SourceChars[CurrentLineEnd + 1] == '\x0D')
                    {
                        if (CurrentLineEnd + 2 < SourceChars.Length && SourceChars[CurrentLineEnd + 2] == '\x0A')
                            return LineEndingType.CRLF;
                        return LineEndingType.CR;
                    }
                    throw new Exception();
                }
            }
            public bool NextLine()
            {
                int seek = NextStart;
                if (NextStart == SourceChars.Length)
                    return false;
                CurrentLineStart = NextStart;
                CurrentLineEnd = NextStart;
                while (true)
                {
                    if (seek == SourceChars.Length)
                    {
                        CurrentLineEnd = seek - 1;
                        NextStart = seek;
                        return true;
                    }
                    else if (SourceChars[seek] == '\x0D')
                    {
                        CurrentLineEnd = seek - 1;
                        NextStart = LineEndingType == LineEndingType.CRLF ? seek + 2 : seek + 1;
                        return true;
                    }
                    else if (SourceChars[seek] == '\x0A')
                    {
                        CurrentLineEnd = seek - 1;
                        NextStart = seek + 1;
                        return true;
                    }
                    seek++;
                }
            }
        }

        private bool NewLineCheckFast(string newString, string oldString, ref string messages, out string resultString)
        {
            resultString = null;
            StringBuilder sb = new StringBuilder();

            int[] oldTypes = new int[3];
            int[] newTypes = new int[3];

            bool updatedLines = false;

            LineEndingCursor oldCursor = new LineEndingCursor(oldString);
            LineEndingCursor newCursor = new LineEndingCursor(newString);

            bool wsChange = false;

            while (oldCursor.NextLine())
            {
                if (!newCursor.NextLine())
                    return false;

                var oldLE = oldCursor.LineEndingType;
                var newLE = newCursor.LineEndingType;

                if (oldLE != LineEndingType.EOF)
                    oldTypes[(int)oldLE]++;
                if (newLE != LineEndingType.EOF)
                    newTypes[(int)newLE]++;

                var oldLine = oldCursor.Line;
                var newLine = newCursor.Line;
                if (oldLine.Count != newLine.Count)
                {
                    wsChange = true;
                    if (!LocalOptions.WS || !CompareWSInvariant(oldLine, newLine))
                        return false;
                }

                if (oldLine.Count == newLine.Count)
                {
                    for (int i = 0; i < oldLine.Count; i++)
                    {
                        if (oldLine.Array[oldLine.Offset + i] != newLine.Array[newLine.Offset + i])
                        {
                            if (LocalOptions.WS && CompareWSInvariant(oldLine, newLine))
                            {
                                wsChange = true;
                                break;
                            }
                            return false;
                        }
                    }
                }

                if (oldLE != newLE)
                {
                    updatedLines = true;
                }

                sb.Append(oldLine.Array, oldLine.Offset, oldLine.Count);
                if (oldLE == LineEndingType.CR)
                    sb.Append('\x0D');
                else if (oldLE == LineEndingType.CRLF)
                {
                    sb.Append('\x0D');
                    sb.Append('\x0A');
                }
                else if (oldLE == LineEndingType.LF)
                    sb.Append('\x0A');
            }
            if (updatedLines || wsChange)
            {
                resultString = sb.ToString();
            }
            if (updatedLines)
            {
                string oldLEFormat = LETypesToString(oldTypes);
                string newLEFormat = LETypesToString(newTypes);
                messages += string.Format("\n - Updated line endings ({0}) -> ({1})", oldLEFormat, newLEFormat);
            }
            if (wsChange)
                messages += string.Format("\n - Reverted whitespace changes.");
            return true;
        }

        private bool CompareWSInvariant(ArraySegment<char> oldLine, ArraySegment<char> newLine)
        {
            string oldString = WSInvariantString(oldLine);
            string newString = WSInvariantString(newLine);
            return oldString == newString;
        }

        private string WSInvariantString(ArraySegment<char> line)
        {
            StringBuilder sb = new StringBuilder();
            bool ws = false;
            for (int i = 0; i < line.Count; i++)
            {
                if (line.Array[line.Offset + i] == ' ' || line.Array[line.Offset + i] == '\t')
                {
                    if (!ws)
                    {
                        ws = true;
                        sb.Append(' ');
                    }
                }
                else
                {
                    ws = false;
                    sb.Append(line.Array[line.Offset + i]);
                }
            }
            string s = sb.ToString();
            return s.TrimEnd(new char[] { ' ', '\t' });
        }

        public static string LETypesToString(int[] letypes)
        {
            string result = string.Empty;
            if (letypes[(int)LineEndingType.CR] > 0)
            {
                if (result != string.Empty)
                    result += ", ";
                result += string.Format("CR: {0}", letypes[(int)LineEndingType.CR]);
            }
            if (letypes[(int)LineEndingType.CRLF] > 0)
            {
                if (result != string.Empty)
                    result += ", ";
                result += string.Format("CRLF: {0}", letypes[(int)LineEndingType.CRLF]);
            }
            if (letypes[(int)LineEndingType.LF] > 0)
            {
                if (result != string.Empty)
                    result += ", ";
                result += string.Format("LF: {0}", letypes[(int)LineEndingType.LF]);
            }
            return result;
        }

        internal static Encoding VSREncodingToEncoding(FileEncoding newFileType)
        {
            if (newFileType == Versionr.Utilities.FileEncoding.ASCII)
                return Encoding.ASCII;
            else if (newFileType == Versionr.Utilities.FileEncoding.Latin1)
                return Encoding.Default;
            else if (newFileType == Versionr.Utilities.FileEncoding.UTF8_BOM)
                return Encoding.UTF8;
            else if (newFileType == Versionr.Utilities.FileEncoding.UTF8)
                return new UTF8Encoding(false);
            else if (newFileType == Versionr.Utilities.FileEncoding.UTF7_BOM)
                return Encoding.UTF7;
            else if (newFileType == Versionr.Utilities.FileEncoding.UTF7)
                return new UTF7Encoding(false);
            else if (newFileType == Versionr.Utilities.FileEncoding.UTF16_LE_BOM)
                return Encoding.Unicode;
            else if (newFileType == Versionr.Utilities.FileEncoding.UTF16_LE)
                return new UnicodeEncoding(false, false);
            else if (newFileType == Versionr.Utilities.FileEncoding.UTF16_BE_BOM)
                return Encoding.BigEndianUnicode;
            else if (newFileType == Versionr.Utilities.FileEncoding.UTF16_BE)
                return new UnicodeEncoding(true, false);
            else if (newFileType == Versionr.Utilities.FileEncoding.UCS4_LE_BOM)
                return Encoding.UTF32;
            else if (newFileType == Versionr.Utilities.FileEncoding.UCS4_LE)
                return new UTF32Encoding(false, false);
            else if (newFileType == Versionr.Utilities.FileEncoding.UCS4_BE_BOM)
                return new UTF32Encoding(true, false);
            else if (newFileType == Versionr.Utilities.FileEncoding.UCS4_BE)
                return new UTF32Encoding(true, false);
            else
                throw new Exception("Unsupported file encoding");
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
