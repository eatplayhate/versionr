using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Utilities
{
    public static class DiffFormatter
    {
        public static List<string> Run(System.IO.Stream f1, System.IO.Stream f2, string f1name, string f2name, bool processTabs, bool emitColours)
        {
            List<string> lines1 = new List<string>();
            List<string> lines2 = new List<string>();
            using (var fs = new System.IO.StreamReader(f1))
            {
                while (true)
                {
                    if (fs.EndOfStream)
                        break;
                    string line = fs.ReadLine();
                    if (processTabs)
                        line = line.Replace("\t", "    ");
                    lines1.Add(line);
                }
            }
            using (var fs = new System.IO.StreamReader(f2))
            {
                while (true)
                {
                    if (fs.EndOfStream)
                        break;
                    string line = fs.ReadLine();
                    if (processTabs)
                        line = line.Replace("\t", "    ");
                    lines2.Add(line);
                }
            }

            return RunDiffLines(lines1, lines2, f1name, f2name, emitColours);
        }
        public static List<string> Run(string file1, string file2, string f1name, string f2name, bool processTabs, bool emitColours)
        {
            List<string> lines1 = new List<string>();
            List<string> lines2 = new List<string>();
            using (var fs = new System.IO.FileInfo(file1).OpenText())
            {
                while (true)
                {
                    if (fs.EndOfStream)
                        break;
                    string line = fs.ReadLine();
                    if (processTabs)
                        line = line.Replace("\t", "    ");
                    lines1.Add(line);
                }
            }
            using (var fs = new System.IO.FileInfo(file2).OpenText())
            {
                while (true)
                {
                    if (fs.EndOfStream)
                        break;
                    string line = fs.ReadLine();
                    if (processTabs)
                        line = line.Replace("\t", "    ");
                    lines2.Add(line);
                }
            }

            return RunDiffLines(lines1, lines2, f1name != null ? f1name : file1, f2name != null ? f2name : file2, emitColours);
        }

        class Region
        {
            public int Start1;
            public int End1;
            public int Start2;
            public int End2;
        }

        public static List<string> RunDiffLines(List<string> lines1, List<string> lines2, string file1, string file2, bool emitColours)
        {
            List<string> results = new List<string>();
            List<Utilities.Diff.commonOrDifferentThing> diff = null;
            diff = Versionr.Utilities.Diff.diff_comm2(lines1.ToArray(), lines2.ToArray(), true);
            int line0 = 0;
            int line1 = 0;
            results.Add(string.Format("--- {0}", file1));
            results.Add(string.Format("+++ {0}", file2));
            List<Region> regions = new List<Region>();
            Region openRegion = null;
            Region last = null;
            // cleanup step
            bool doCleanup = true;
            if (!doCleanup)
                goto Display;
            for (int i = 1; i < diff.Count - 1; i++)
            {
                if (diff[i - 1].common == null || diff[i - 1].common.Count == 0)
                    continue;
                if (diff[i + 1].common == null || diff[i + 1].common.Count == 0)
                    continue;
                int cf0 = diff[i].file1 == null ? 0 : diff[i].file1.Count;
                int cf1 = diff[i].file2 == null ? 0 : diff[i].file2.Count;
                if ((cf0 == 0) ^ (cf1 == 0)) // insertion
                {
                    List<string> target = cf0 == 0 ? diff[i].file2 : diff[i].file1;
                    List<string> receiver = diff[i - 1].common;
                    List<string> source = diff[i + 1].common;

                    int copied = 0;
                    for (int j = 0; j < target.Count && j < source.Count; j++)
                    {
                        if (target[j] == source[j])
                            copied++;
                        else
                            break;
                    }

                    if (copied > 0)
                    {
                        target.AddRange(source.Take(copied));
                        source.RemoveRange(0, copied);
                        receiver.AddRange(target.Take(copied));
                        target.RemoveRange(0, copied);
                    }
                }
            }
            for (int i = 0; i < diff.Count - 1; i++)
            {
                if (diff[i].common != null)
                    continue;
                if (diff[i + 1].common == null)
                {
                    var next = diff[i + 1];
                    diff.RemoveAt(i + 1);
                    foreach (var x in next.file1)
                    {
                        diff[i].file1.Add(x);
                    }
                    foreach (var x in next.file2)
                    {
                        diff[i].file2.Add(x);
                    }
                    i--;
                    continue;
                }
                if (diff[i + 1].common == null || diff[i + 1].common.Count == 0)
                    continue;
                bool isWhitespace = true;
                bool isShort = false;
                bool isBrace = false;
                if (diff[i + 1].common.Count * 2 <= diff[i].file1.Count &&
                    diff[i + 1].common.Count * 2 <= diff[i].file2.Count)
                    isShort = true;
                foreach (var x in diff[i + 1].common)
                {
                    if (x.Trim().Length != 0)
                    {
                        isWhitespace = false;
                        break;
                    }
                }
                if (diff[i + 1].common.Count == 1 || (diff[i + 1].common.Count == 1 && (diff[i + 1].common[0].Trim() == "{" || diff[i + 1].common[0].Trim() == "}")))
                {
                    if (i < diff.Count - 2 && (diff[i + 2].common == null || diff[i + 2].common.Count == 0))
                        isBrace = true;
                }
                if ((isWhitespace && isShort) || isShort || isBrace)
                {
                    var next = diff[i + 1];
                    if (isBrace && next.common.Count > 1)
                    {
                        // currently disabled
                        diff[i].file1.Add(next.common[0]);
                        diff[i].file2.Add(next.common[0]);
                        next.common.RemoveAt(0);
                    }
                    else
                    {
                        diff.RemoveAt(i + 1);
                        foreach (var x in next.common)
                        {
                            diff[i].file1.Add(x);
                            diff[i].file2.Add(x);
                        }
                        i--;
                    }
                }
            }
            Display:
            for (int i = 0; i < diff.Count; i++)
            {
                if (regions.Count > 0)
                    last = regions[regions.Count - 1];
                if (diff[i].common != null)
                {
                    line0 += diff[i].common.Count;
                    line1 += diff[i].common.Count;
                }
                int cf0 = diff[i].file1 == null ? 0 : diff[i].file1.Count;
                int cf1 = diff[i].file2 == null ? 0 : diff[i].file2.Count;
                for (int j = 1; j <= cf0 || j <= cf1; j++)
                {
                    if (openRegion == null)
                    {
                        int s1 = System.Math.Max(1, line0 - 2);
                        int s2 = System.Math.Max(1, line1 - 2);
                        if (last != null && (last.End1 + 3 > s1 || last.End2 + 3 > s2))
                            openRegion = last;
                        else
                            openRegion = new Region() { Start1 = s1, Start2 = s2 };
                    }
                    if (j <= cf0)
                    {
                        line0++;
                    }
                    if (j <= cf1)
                    {
                        line1++;
                    }
                    openRegion.End1 = System.Math.Min(line0 + 3, lines1.Count + 1);
                    openRegion.End2 = System.Math.Min(line1 + 3, lines2.Count + 1);
                }
                if (openRegion != null && (openRegion.End1 < line0 && openRegion.End2 < line1))
                {
                    if (regions.Count == 0 || regions[regions.Count - 1] != openRegion)
                        regions.Add(openRegion);
                    openRegion = null;
                }
            }
            if (openRegion != null && openRegion != last)
            {
                if (regions.Count == 0 || regions[regions.Count - 1] != openRegion)
                    regions.Add(openRegion);
            }
            int activeRegion = 0;
            while (activeRegion < regions.Count)
            {
                Region reg = regions[activeRegion];
                line0 = 0;
                line1 = 0;
                results.Add(string.Format("{4}@@ -{0},{1} +{2},{3} @@", reg.Start1, reg.End1 - reg.Start1 + 1, reg.Start2, reg.End2 - reg.Start2 + 1, emitColours ? "#c#" : ""));
                for (int i = 0; i < diff.Count; i++)
                {
                    if ((line0 > reg.End1) || (line1 > reg.End2))
                    {
                        break;
                    }
                    if (diff[i].common != null)
                    {
                        foreach (var x in diff[i].common)
                        {
                            line0++;
                            line1++;
                            if ((line0 >= reg.Start1 && line0 <= reg.End1) || (line1 >= reg.Start2 && line1 <= reg.End2))
                                results.Add(string.Format(" {0}", emitColours ? Printer.Escape(x) : x));
                        }
                    }
                    int cf0 = diff[i].file1 == null ? 0 : diff[i].file1.Count;
                    int cf1 = diff[i].file2 == null ? 0 : diff[i].file2.Count;
                    for (int j = 1; j <= cf0; j++)
                    {
                        line0++;
                        if (line0 >= reg.Start1 && line0 <= reg.End1)
                            results.Add(string.Format("{1}-{0}", emitColours ? Printer.Escape(diff[i].file1[j - 1]) : diff[i].file1[j - 1], emitColours ? "#e#" : ""));
                    }
                    for (int j = 1; j <= cf1; j++)
                    {
                        line1++;
                        if (line1 >= reg.Start2 && line1 <= reg.End2)
                            results.Add(string.Format("{1}+{0}", emitColours ? Printer.Escape(diff[i].file2[j - 1]) : diff[i].file2[j - 1], emitColours ? "#s#" : ""));
                    }
                }
                activeRegion++;
            }
            return results;
        }
    }
}
