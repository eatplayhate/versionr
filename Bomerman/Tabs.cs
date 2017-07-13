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
    public abstract class BomTabOptions : FileCommandVerbOptions
    {
        [Option("tabsize", DefaultValue = 4, HelpText = "Sets the number of spaces which are equivalent to one tab.")]
        public int TabSize { get; set; }

        [Option("trim", DefaultValue = true, HelpText = "Trims whitespace from the end of line.")]
        public bool Trim { get; set; }
    }
    public class BomEntabOptions : BomTabOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Converts spaces to tabs."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "entab";
            }
        }
        public override BaseCommand GetCommand()
        {
            return new BomEntab();
        }
    }
    public class BomDetabOptions : BomTabOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Converts tabs to spaces."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "detab";
            }
        }
        public override BaseCommand GetCommand()
        {
            return new BomDetab();
        }
    }
    abstract class BomTabProcessor : FileCommand
    {
        protected abstract bool AddTabs { get; }
        class TabStats
        {
            public int Files { get; set; }
            public int Insertions { get; set; }
        }
        protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
        {
            BomTabOptions localOptions = options as BomTabOptions;

            try
            {
                List<Versionr.Status.StatusEntry> realTargets = new List<Status.StatusEntry>();
                foreach (var x in targets)
                {
                    if (x.VersionControlRecord != null && !x.IsDirectory && x.FilesystemEntry != null)
                    {
                        realTargets.Add(x);
                    }
                }
                Printer.PrintMessage("Found {0} files for processing...", realTargets.Count);
                TabStats ts = new TabStats();
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
                        ProcessFile(AddTabs, localOptions.Trim, localOptions.TabSize, x, ts);
                    }));
                    if (System.Diagnostics.Debugger.IsAttached)
                        tasks[tasks.Count - 1].Wait();
                }
                Task.WaitAll(tasks.ToArray());
                if (ts.Files > 0)
                {
                    if (AddTabs)
                        Printer.PrintMessage("Updated {0} files and inserted {1} tabs.", ts.Files, ts.Insertions);
                    else
                        Printer.PrintMessage("Updated {0} files and inserted {1} spaces.", ts.Files, ts.Insertions * localOptions.TabSize);
                }
            }
            finally
            {

            }
            return true;
        }

        private void ProcessFile(bool addTabs, bool trim, int tabSize, Status.StatusEntry x, TabStats ts)
        {
            byte[] data = null;
            using (var fs = System.IO.File.OpenRead(x.FilesystemEntry.Info.FullName))
            {
                data = new byte[fs.Length];
                fs.Read(data, 0, data.Length);
            }
            System.IO.MemoryStream ms = new System.IO.MemoryStream(data.Length * 2);
            int count = 0;
            bool newline = false;
            bool trimmed = false;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == (byte)'\n')
                {
                    newline = true;
                    ms.WriteByte((byte)'\n');
                }
                else if (data[i] == (byte)'\r')
                {
                    newline = true;
                    ms.WriteByte((byte)'\r');
                }
                else if (data[i] == (byte)' ')
                {
                    if (trim && !newline)
                    {
                        int trimcount = 1;
                        for (int j = i + 1; j < data.Length; j++)
                        {
                            if (data[j] == (byte)'\n' || data[j] == (byte)'\r')
                                break;
                            if (data[j] != (byte)' ' && data[j] != (byte)'\t')
                            {
                                trimcount = 0;
                                break;
                            }
                            trimcount++;
                        }
                        if (trimcount != 0)
                        {
                            trimmed = true;
                            i += trimcount - 1;
                            continue;
                        }
                        else
                        {
                            ms.WriteByte(data[i]);
                            newline = false;
                        }
                    }
                    else if (addTabs && newline)
                    {
                        int span = 1;
                        for (int j = i + 1; j < i + tabSize && j < data.Length; j++)
                        {
                            if (data[j] != (byte)' ')
                                break;
                            span++;
                        }
                        if (span == tabSize)
                        {
                            count++;
                            ms.WriteByte((byte)'\t');
                        }
                        else
                        {
                            for (int j = 0; j < span; j++)
                                ms.WriteByte((byte)' ');
                        }
                        i += span - 1;
                    }
                    else
                        ms.WriteByte((byte)' ');
                }
                else if (data[i] == (byte)'\t')
                {
                    if (trim && !newline)
                    {
                        int trimcount = 1;
                        for (int j = i + 1; j < data.Length; j++)
                        {
                            if (data[j] == (byte)'\n' || data[j] == (byte)'\r')
                                break;
                            if (data[j] != (byte)' ' && data[j] != (byte)'\t')
                            {
                                trimcount = 0;
                                break;
                            }
                            trimcount++;
                        }
                        if (trimcount != 0)
                        {
                            trimmed = true;
                            i += trimcount - 1;
                            continue;
                        }
                        else
                        {
                            ms.WriteByte(data[i]);
                            newline = false;
                        }
                    }
                    else if (!addTabs && newline)
                    {
                        for (int j = 0; j < tabSize; j++)
                            ms.WriteByte((byte)' ');
                        count++;
                    }
                    else
                        ms.WriteByte((byte)'\t');
                }
                else
                {
                    ms.WriteByte(data[i]);
                    newline = false;
                }
            }
            if (count > 0 || trimmed)
            {
                using (var fs = System.IO.File.Create(x.FilesystemEntry.Info.FullName))
                {
                    data = ms.ToArray();
                    fs.Write(data, 0, data.Length);
                }

                string mods = "";
                if (count > 0)
                {
                    if (addTabs)
                        mods = string.Format("added #b#{0}## tabs", count);
                    else
                        mods = string.Format("added #b#{0}## spaces", count * tabSize);
                }
                if (trimmed)
                {
                    if (!string.IsNullOrEmpty(mods))
                        mods += ", ";
                    mods += "trimmed trailing whitespace";
                }

                Printer.PrintMessage("#s#{0}## - {1}.", x.CanonicalName, mods);

                lock (ts)
                {
                    ts.Files++;
                    ts.Insertions += count;
                }
            }
        }
    }
    class BomEntab : BomTabProcessor
    {
        protected override bool AddTabs
        {
            get
            {
                return true;
            }
        }
    }
    class BomDetab : BomTabProcessor
    {
        protected override bool AddTabs
        {
            get
            {
                return false;
            }
        }
    }
}
