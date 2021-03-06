﻿using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;

namespace Versionr.Commands
{
    class LogVerbOptions : FileBaseCommandVerbOptions
    {
        [Option('l', "limit", DefaultValue = -1, HelpText = "Limit number of versions to show, 10 default (0 for all).")]
        public int Limit { get; set; }
        [Option('a', "logical", DefaultValue = true, HelpText = "Show logical history (cleans up automatic merge data and can follow branches).")]
        public bool Logical { get; set; }
        [Option('f', "follow", HelpText = "Follows merges from other branches.")]
        public bool FollowBranches { get; set; }
        [Option('m', "merges", HelpText = "Shows revisions where a merge occurred when following logical history.")]
        public bool ShowMerges { get; set; }
        [Option("all-merges", HelpText = "Show all merges when following logical history, even automatic merges")]
        public bool ShowAutoMerges { get; set; }
        [Option('e', "reverse", HelpText = "Reverses the order of versions in the log.")]
        public bool Reverse { get; set; }
        [Option("indent", DefaultValue = true, HelpText = "Indents logical history to show sequencing.")]
        public bool Indent { get; set; }
        [Option("diff", DefaultValue = false, HelpText = "Displays diffs.")]
        public bool Diff { get; set; }

        public enum DetailMode
        {
            Normal,
            N = Normal,
            Concise,
            C = Concise,
            Detailed,
            D = Detailed,
            Full,
            F = Full,
            Jrunting,
            J = Jrunting,
        }

        [Option("detail", HelpText = "Set the display mode. One of (n)ormal, (c)oncise, (d)etailed, (f)ull, (j)runting", MetaValue = "<value>", MutuallyExclusiveSet = "logdetail")]
        public DetailMode Detail { get; set; }

        [Option('c', "concise", HelpText = "Uses a short log formatting style. Alias for --detail=concise", MutuallyExclusiveSet = "logdetail")]
        public bool Concise
        {
            get { return Detail == DetailMode.Concise; }
            set { if (value) Detail = DetailMode.Concise; }
        }

        [Option('j', "jrunting", HelpText = "\"This looks better\" - jrunting. Alias for --detail=jrunting", MutuallyExclusiveSet = "logdetail")]
        public bool Jrunting
        {
            get { return Detail == DetailMode.Jrunting; }
            set { if (value) Detail = DetailMode.Jrunting; }
        }


        [Option('b', "branch", HelpText = "Name of the branch to view", MutuallyExclusiveSet = "versionselect")]
        public string Branch { get; set; }
        [Option('v', "version", HelpText = "Specific version to view", MutuallyExclusiveSet = "versionselect")]
        public string Version { get; set; }

        [Option("author", HelpText = "Filter log on specific author")]
        public string Author { get; set; }

        [Option("xml", HelpText = "Generate XML output")]
        public bool Xml { get; set; }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "This command displays the history of the current branch (or a specified branch/version)."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "log";
            }
        }

        public override BaseCommand GetCommand()
        {
            return new Log();
        }
    }

    class Log : FileBaseCommand
    {
        protected override bool RequiresTargets { get { return false; } }
        protected override bool OnNoTargetsAssumeAll { get { return true; } }
        protected bool JruntingMode { get; set; }

        protected override bool ComputeTargets(FileBaseCommandVerbOptions localOptions)
        {
            return false;
        }

        class ResolvedAlteration
        {
            public Objects.Alteration Alteration { get; private set; }
            public Objects.Record Record { get; private set; }
            public ResolvedAlteration(Objects.Alteration alteration, Area ws)
            {
                Alteration = alteration;
                if (alteration.NewRecord.HasValue)
                    Record = ws.GetRecord(Alteration.NewRecord.Value);
                else if (alteration.PriorRecord.HasValue)
                    Record = ws.GetRecord(Alteration.PriorRecord.Value);
                else
                    throw new Exception("unexpected");
            }
        }

        public Log(bool jruntingMode = false)
        {
            JruntingMode = jruntingMode;
        }

        IEnumerable<ResolvedAlteration> GetAlterations(Objects.Version v)
        {
            return Workspace.GetAlterations(v).Select(x => new ResolvedAlteration(x, Workspace));
        }

        IEnumerable<KeyValuePair<bool, ResolvedAlteration>> FilterAlterations(Objects.Version v)
        {
            var enumeration = GetAlterations(v)
                .Select(x => new KeyValuePair<string, ResolvedAlteration>(x.Record.CanonicalName, x));
            return Filter(enumeration);
        }

        private Objects.Version m_Tip;
        private Dictionary<Guid, Objects.Branch> m_Branches;

        private static string XmlText(string s)
        {
            if (s == null)
                return "";
            return s
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private static string XmlAttr(string s)
        {
            if (s == null)
                return "";
            return s
                .Replace("&", "&amp;")
                .Replace("'", "&apos;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        // superdirty
        private HashSet<Guid> m_LoggedVersions;
        private void FormatLog(Tuple<Objects.Version, int> vt, IEnumerable<KeyValuePair<bool, ResolvedAlteration>> filteralt, LogVerbOptions localOptions)
        {
            Objects.Version v = vt.Item1;
            if (m_LoggedVersions == null)
                m_LoggedVersions = new HashSet<Guid>();
            m_LoggedVersions.Add(v.ID);

            Objects.Branch branch = null;
            if (!m_Branches.TryGetValue(v.Branch, out branch))
            {
                branch = Workspace.GetBranch(v.Branch);
                m_Branches[v.Branch] = branch;
            }

            if (localOptions.Xml)
            {
                Printer.PrintMessage($"  <version id='{v.ID}' parent='{v.Parent}' branch='{v.Branch}' timestamp='{v.Timestamp.ToString("o")}' author='{XmlAttr(v.Author)}' published='{v.Published}'>");
                Printer.PrintMessage($"    <message>{XmlText(v.Message)}</message>");

                foreach (var y in Workspace.GetMergeInfo(v.ID))
                {
                    var mergeParent = Workspace.GetVersion(y.SourceVersion);
                    Printer.PrintMessage($"    <merge type='{y.Type.ToString().ToLower()}' version='{mergeParent.ID}' branch='{mergeParent.Branch}' />");
                }

                if (localOptions.Detail == LogVerbOptions.DetailMode.Full)
                {
                    foreach (var y in GetAlterations(v))
                    {
                        string operationName = y.Alteration.Type.ToString().ToLower();
                        if (y.Alteration.Type == Objects.AlterationType.Copy || y.Alteration.Type == Objects.AlterationType.Move)
                        {
                            Objects.Record prior = Workspace.GetRecord(y.Alteration.PriorRecord.Value);
                            Objects.Record next = Workspace.GetRecord(y.Alteration.NewRecord.Value);
                            bool edited = (!next.IsDirectory && prior.DataIdentifier != next.DataIdentifier);
                            Printer.PrintMessage($"    <alteration type='{operationName}' path='{XmlAttr(next.CanonicalName)}' frompath='{XmlAttr(prior.CanonicalName)}' edited='{edited}' />");
                        }
                        else
                        {
                            Printer.PrintMessage($"    <alteration type='{operationName}' path='{y.Record.CanonicalName}' />");
                        }
                    }
                }
                Printer.PrintMessage("  </version>");
            }
            else if (localOptions.Jrunting)
            {
                // list of heads
                var heads = Workspace.GetHeads(v.ID);
                bool isHead = false;
                string headString = "";
                foreach (var y in heads)
                {
                    isHead = true;
                    if (headString.Length != 0)
                        headString = headString + ", ";
                    headString += Workspace.GetBranch(y.Branch).Name;
                }

                // message up to first newline
                string message = v.Message;
                if (message == null)
                    message = string.Empty;

                message = message.Replace("\r\n", "\n");

                var idx = message.IndexOf('\n');
                if (idx == -1)
                    idx = message.Length;
                message = message.Substring(0, idx);


                string mergemarker = "";
                if (Workspace.GetMergeInfo(v.ID).Count() > 0)
                {
                    var m = Workspace.GetMergeInfo(v.ID).First();
                    var heads2 = Workspace.GetHeads(m.SourceVersion);
                    if (heads2.Count > 0)
                        if (isHead)
                            mergemarker = " <- " + Workspace.GetBranch(heads2.First().Branch).Name;
                        else
                            mergemarker = "M: " + Workspace.GetBranch(heads2.First().Branch).Name;
                }

                var date = new DateTime(v.Timestamp.Ticks, DateTimeKind.Utc).ToShortDateString();

                string pattern = "* #U#{0}## - ";

                if (isHead)
                    pattern += "#Y#({4}{5})## ";
                else if (mergemarker.Length > 0)
                    pattern += "#Y#({5})## ";


                pattern += "{1} ";
                var tagList = Workspace.GetTagsForVersion(v.ID);
                if (tagList.Count > 0)
                    pattern += "#I#[" + string.Join(" ", tagList.Select(x => "\\#" + x).ToArray()) + "]## ";
                pattern += "#g#({2}, {3})##";

                Printer.PrintMessage(pattern, v.ShortName, message, v.Author, date, headString, mergemarker);
            }
            else if (localOptions.Concise)
            {
                if (vt.Item2 != 0 && localOptions.Indent)
                    Printer.Prefix = " ";
                var heads = Workspace.GetHeads(v.ID);
                bool isHead = false;
                foreach (var y in heads)
                {
                    if (y.Branch == branch.ID)
                    {
                        isHead = true;
                        break;
                    }
                }
                string message = v.Message;
                if (message == null)
                    message = string.Empty;
                string tipmarker = " ";
                if (v.ID == m_Tip.ID)
                    tipmarker = "#w#*##";
                string mergemarker = " ";
                if (Workspace.GetMergeInfo(v.ID).FirstOrDefault() != null)
                    mergemarker = "#s#M##";
                var tagList = Workspace.GetTagsForVersion(v.ID);
                string tags = "";
                if (tagList.Count > 0)
                    tags = "#s#" + string.Join(" ", tagList.Select(x => "\\#" + x).ToArray()) + "## ";
                Printer.PrintMessage($"{tipmarker}#c#{v.ShortName}:##{mergemarker}({v.Revision}/{(isHead ? "#i#" : "#b#")}{branch.Name}##)"
                    + $"{message.Replace("\r\n", " ").Replace('\n', ' ')} {tags}"
                    + $"#q#({v.Author} {new DateTime(v.Timestamp.Ticks, DateTimeKind.Utc).ToShortDateString()})##");
                Printer.Prefix = "";
            }
            else
            {
                Printer.PrintMessage("");
                if (vt.Item2 != 0 && localOptions.Indent)
                    Printer.Prefix = "| ";
                string tipmarker = "";
                if (v.ID == m_Tip.ID)
                    tipmarker = " #w#*<current>##";
                Printer.PrintMessage("({0}) #c#{1}## on branch #b#{2}##{3}", v.Revision, v.ID, branch.Name, tipmarker);

                var mergeInfo = Workspace.GetMergeInfo(v.ID);
                foreach (var y in mergeInfo)
                {
                    var mergeParent = Workspace.GetVersion(y.SourceVersion);
                    Objects.Branch mergeBranch = null;
                    if (!m_Branches.TryGetValue(mergeParent.Branch, out mergeBranch))
                    {
                        mergeBranch = Workspace.GetBranch(mergeParent.Branch);
                        m_Branches[mergeParent.Branch] = mergeBranch;
                    }
                    Printer.PrintMessage(" <- Merged from #s#{0}## on branch #b#{1}##", mergeParent.ID, mergeBranch.Name);
                }

                var heads = Workspace.GetHeads(v.ID);
                foreach (var y in heads)
                {
                    Objects.Branch headBranch = null;
                    if (!m_Branches.TryGetValue(y.Branch, out headBranch))
                    {
                        headBranch = Workspace.GetBranch(y.Branch);
                        m_Branches[y.Branch] = headBranch;
                    }
                    string branchFlags = string.Empty;
                    if (branch.Terminus.HasValue)
                        branchFlags = " #e#(deleted)##";
                    Printer.PrintMessage(" ++ #i#Head## of branch #b#{0}## (#b#\"{1}\"##){2}", headBranch.ID, headBranch.Name, branchFlags);
                }
                if (branch.Terminus == v.ID)
                    Printer.PrintMessage(" ++ #i#Terminus## of #e#deleted branch## #b#{0}## (#b#\"{1}\"##)", branch.ID, branch.Name);

                Printer.PrintMessage("#b#Author:## {0} #q# {1} ##", v.Author, v.Timestamp.ToLocalTime());
                var tagList = Workspace.GetTagsForVersion(v.ID);
                if (tagList.Count > 0)
                    Printer.PrintMessage(" #s#" + string.Join(" ", tagList.Select(x => "\\#" + x).ToArray()) + "##");
                Printer.PrintMessage("");
                Printer.PushIndent();
                Printer.PrintMessage("{0}", string.IsNullOrWhiteSpace(v.Message) ? "<none>" : Printer.Escape(v.Message));
                Printer.PopIndent();

                if (localOptions.Detail == LogVerbOptions.DetailMode.Detailed || localOptions.Detail == LogVerbOptions.DetailMode.Full)
                {
                    var alterations = localOptions.Detail == LogVerbOptions.DetailMode.Detailed ? filteralt.Select(z => z.Value) : GetAlterations(v);
                    if (localOptions.Detail == LogVerbOptions.DetailMode.Full)
                    {
                        Printer.PrintMessage("");
                        Printer.PrintMessage("#b#Alterations:##");
                        foreach (var y in alterations.OrderBy(z => z.Alteration.Type))
                        {
                            if (y.Alteration.Type == Objects.AlterationType.Move || y.Alteration.Type == Objects.AlterationType.Copy)
                            {
                                string operationName = y.Alteration.Type.ToString().ToLower();
                                Objects.Record prior = Workspace.GetRecord(y.Alteration.PriorRecord.Value);
                                Objects.Record next = Workspace.GetRecord(y.Alteration.NewRecord.Value);
                                bool isUpdate = false;
                                if (y.Alteration.Type == Objects.AlterationType.Move && !next.IsDirectory && prior.DataIdentifier != next.DataIdentifier)
                                {
                                    isUpdate = true;
                                    operationName = "refactor";
                                }
                                Printer.PrintMessage("#{2}#({0})## {1}\n  <- #q#{3}##", operationName, y.Record.CanonicalName, GetAlterationFormat(y.Alteration.Type), prior.CanonicalName);
                                if (localOptions.Diff && isUpdate)
                                    InlineDiff(prior, next);
                            }
                            else
                            {
                                Printer.PrintMessage("#{2}#({0})## {1}", y.Alteration.Type.ToString().ToLower(), y.Record.CanonicalName, GetAlterationFormat(y.Alteration.Type));
                                if (localOptions.Diff && y.Alteration.Type == Objects.AlterationType.Update)
                                    InlineDiff(Workspace.GetRecord(y.Alteration.PriorRecord.Value), Workspace.GetRecord(y.Alteration.NewRecord.Value));
                            }
                        }
                    }
                    else
                    {
                        int[] alterationCounts = new int[5];
                        foreach (var y in alterations)
                            alterationCounts[(int)y.Alteration.Type]++;
                        bool first = true;
                        string formatData = "";
                        for (int i = 0; i < alterationCounts.Length; i++)
                        {
                            if (alterationCounts[i] != 0)
                            {
                                if (!first)
                                    formatData += ", ";
                                else
                                    formatData += "  ";
                                first = false;
                                formatData += string.Format("#{2}#{0}s: {1}##", ((Objects.AlterationType)i).ToString(), alterationCounts[i], GetAlterationFormat((Objects.AlterationType)i));
                            }
                        }
                        if (formatData.Length > 0)
                        {
                            Printer.PrintMessage("");
                            Printer.PrintMessage("#b#Alterations:##");
                            Printer.PrintMessage(formatData);
                        }
                    }
                }
                else if (FilterOptions.Objects.Count != 0)
                {
                    Printer.PrintMessage("");
                    Printer.PrintMessage("#b#Alterations:##");
                    List<KeyValuePair<string, ResolvedAlteration>> altList = new List<KeyValuePair<string, ResolvedAlteration>>();
                    foreach (var y in GetAlterations(v))
                    {
                        string recName = y.Record.CanonicalName;
                        altList.Add(new KeyValuePair<string, ResolvedAlteration>(recName, y));
                    }

                    if (localOptions.Diff)
                    {
                        var records = FilterObjects(altList)
                            .SelectMany(x => new[] { x.Value.Alteration.PriorRecord, x.Value.Alteration.NewRecord })
                            .Where(x => x.HasValue)
                            .Select(x => Workspace.GetRecord(x.Value));

                        Workspace.GetMissingObjects(records, null);
                    }

                    foreach (var y in FilterObjects(altList).Select(x => x.Value))
                    {
                        if (y.Alteration.Type == Objects.AlterationType.Move || y.Alteration.Type == Objects.AlterationType.Copy)
                        {
                            string operationName = y.Alteration.Type.ToString().ToLower();
                            Objects.Record prior = Workspace.GetRecord(y.Alteration.PriorRecord.Value);
                            Objects.Record next = Workspace.GetRecord(y.Alteration.NewRecord.Value);
                            bool isUpdate = false;
                            if (y.Alteration.Type == Objects.AlterationType.Move && !next.IsDirectory && prior.DataIdentifier != next.DataIdentifier)
                            {
                                isUpdate = true;
                                operationName = "refactor";
                            }

                            Printer.PrintMessage("#{2}#({0})## {1}\n  <- #q#{3}##", operationName, y.Record.CanonicalName, GetAlterationFormat(y.Alteration.Type), prior.CanonicalName);

                            if (localOptions.Diff && isUpdate)
                                InlineDiff(prior, next);
                        }
                        else
                        {
                            Printer.PrintMessage("#{2}#({0})## {1}", y.Alteration.Type.ToString().ToLower(), y.Record.CanonicalName, GetAlterationFormat(y.Alteration.Type));
                            if (localOptions.Diff && y.Alteration.Type == Objects.AlterationType.Update)
                                InlineDiff(Workspace.GetRecord(y.Alteration.PriorRecord.Value), Workspace.GetRecord(y.Alteration.NewRecord.Value));
                        }
                    }
                }

                Printer.Prefix = "";
                // Same-branch merge revisions. This only sort-of respects the limit :(
                //foreach (var y in mergeInfo)
                //{
                //	var mergeParent = Workspace.GetVersion(y.SourceVersion);
                //	if (mergeParent.Branch == v.Branch)
                //	{
                //		Printer.PushIndent();
                //		Printer.PrintMessage("---- Merged versions ----");

                //		List<Objects.Version> mergedVersions = new List<Objects.Version>();

                //		var p = mergeParent;
                //		do
                //		{
                //			mergedVersions.Add(p);
                //			if (p.Parent.HasValue && !m_LoggedVersions.Contains(p.Parent.Value))
                //				p = Workspace.GetVersion(p.Parent.Value);
                //			else
                //				p = null;
                //		} while (p != null);

                //		foreach (var a in ApplyHistoryFilter(mergedVersions, localOptions))
                //			FormatLog(a.Item1, a.Item2, localOptions);

                //		Printer.PrintMessage("-------------------------");
                //		Printer.PopIndent();
                //	}
                //}
            }
        }

        private void InlineDiff(Objects.Record old, Objects.Record newRecord)
        {
            if (old.Size > 10 * 1024 * 1024)
                return;
            string tmpOld = Utilities.DiffTool.GetTempFilename();
            string tmpNew = Utilities.DiffTool.GetTempFilename();
            Workspace.RestoreRecord(old, DateTime.Now, Path.GetFullPath(tmpOld));
            try
            {
                if (Utilities.FileClassifier.Classify(new FileInfo(tmpOld)) != Utilities.FileEncoding.Binary)
                {
                    try
                    {
                        Workspace.RestoreRecord(newRecord, DateTime.Now, Path.GetFullPath(tmpNew));

                        List<string> messages = Utilities.DiffFormatter.Run(Path.GetFullPath(tmpOld), Path.GetFullPath(tmpNew), old.Name, newRecord.Name, true, true);
                        foreach (var x in messages)
                            Printer.PrintMessage(x);
                    }
                    finally
                    {
                        System.IO.File.Delete(tmpNew);
                    }
                }
            }
            finally
            {
                System.IO.File.Delete(tmpOld);
            }
        }

        private IEnumerable<Tuple<Tuple<Objects.Version, int>, IEnumerable<KeyValuePair<bool, ResolvedAlteration>>>> ApplyHistoryFilter(IEnumerable<Tuple<Objects.Version, int>> history, LogVerbOptions localOptions)
        {
            if (!string.IsNullOrEmpty(localOptions.Author))
                history = history.Where(x => x.Item1.Author.Equals(localOptions.Author, StringComparison.OrdinalIgnoreCase));

            var enumeration = history
                .Select(x => new Tuple<Tuple<Objects.Version, int>, IEnumerable<KeyValuePair<bool, ResolvedAlteration>>>(x, FilterAlterations(x.Item1)))
                .Where(x => x.Item2.Any() || (x.Item1.Item1.Parent == null || localOptions.Objects.Count == 0));

            if (localOptions.Limit != 0)
                enumeration = enumeration.Take(localOptions.Limit);

            if (!(localOptions.Jrunting ^ localOptions.Reverse))
                enumeration = enumeration.Reverse();

            return enumeration;
        }

        protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
        {
            LogVerbOptions localOptions = options as LogVerbOptions;
            if (JruntingMode)
                localOptions.Detail = LogVerbOptions.DetailMode.Jrunting;

            if (localOptions.ShowAutoMerges)
                localOptions.ShowMerges = true;

            if (localOptions.FollowBranches || localOptions.ShowMerges)
            {
                if (!localOptions.Logical)
                {
                    Printer.PrintError("#e#Error:## Following branches and specifically enabling display or merges are only valid options when showing the #b#--logical## history.");
                    return false;
                }
            }

            Printer.EnableDiagnostics = localOptions.Verbose;

            bool targetedBranch = false;
            Objects.Version version = null;
            if (!string.IsNullOrEmpty(localOptions.Branch))
            {
                bool multipleBranches = false;
                var branch = ws.GetBranchByPartialName(localOptions.Branch, out multipleBranches);
                if (branch == null || multipleBranches)
                {
                    Printer.PrintError("No unique branch found for {0}", localOptions.Branch);
                    return false;
                }
                version = ws.GetBranchHeadVersion(branch);
                targetedBranch = true;
            }
            else if (!string.IsNullOrEmpty(localOptions.Version))
            {
                version = ws.GetPartialVersion(localOptions.Version);
                if (version == null)
                {
                    Printer.PrintError("Couldn't find matching version for {0}", localOptions.Version);
                    return false;
                }
            }

            bool versionAutoSelected = false;
            bool lastResortVersionSelection = false;
            List<Objects.Head> targetHeadObjects = null;

            if (localOptions.Limit == -1)
                localOptions.Limit = (version == null || targetedBranch) ? 10 : 1;
            if (version == null)
            {
                versionAutoSelected = true;
                targetHeadObjects = ws.GetBranchHeads(ws.CurrentBranch);
                if (targetHeadObjects.Count == 1)
                    version = ws.GetVersion(targetHeadObjects[0].Version);
                else
                {
                    var guid = ws.Version.ID;
                    foreach (var head in targetHeadObjects)
                    {
                        if (head.Version == guid)
                        {
                            version = ws.Version;
                            break;
                        }
                    }
                    if (version == null)
                    {
                        foreach (var head in targetHeadObjects)
                        {
                            var temphistory = ws.GetHistory(ws.GetVersion(head.Version), null);
                            foreach (var h in temphistory)
                            {
                                if (h.ID == guid)
                                {
                                    version = ws.GetVersion(head.Version);
                                    break;
                                }
                            }
                        }
                    }
                    if (version == null)
                    {
                        lastResortVersionSelection = true;
                        version = ws.Version;
                    }
                }
            }

            int? nullableLimit = localOptions.Limit;
            if (nullableLimit.Value <= 0)
                nullableLimit = null;

            if (localOptions.Xml)
            {
                Printer.PrintMessage("<?xml version='1.0'?>");
                Printer.PrintMessage($"<vsrlog>");
                var branch = ws.GetBranch(version.Branch);
                Printer.PrintMessage($"  <branch id='{branch.ID}' name='{XmlAttr(branch.Name)}'>");
                foreach (var head in ws.GetBranchHeads(branch))
                    Printer.PrintMessage($"    <head version='{head.Version}' />");
                Printer.PrintMessage("  </branch>");
            }

            var history = (localOptions.Logical ? ws.GetLogicalHistorySequenced(version, localOptions.FollowBranches, localOptions.ShowMerges, localOptions.ShowAutoMerges, nullableLimit) : ws.GetHistory(version, nullableLimit).Select(x => new Tuple<Objects.Version, int>(x, 0))).AsEnumerable();

            m_Tip = Workspace.Version;
            Objects.Version last = null;
            m_Branches = new Dictionary<Guid, Objects.Branch>();
            bool anything = false;
            ws.BeginDatabaseTransaction();
            foreach (var x in ApplyHistoryFilter(history, localOptions))
            {
                last = x.Item1.Item1;
                FormatLog(x.Item1, x.Item2, localOptions);
                anything = true;
            }
            ws.CommitDatabaseTransaction();

            if (localOptions.Xml)
            {
                Printer.PrintMessage("</vsrlog>");
            }
            else
            {
                if (!localOptions.Jrunting)
                {
                    if (last != null && last.ID != m_Tip.ID)
                    {
                        var branch = Workspace.CurrentBranch;
                        var heads = Workspace.GetBranchHeads(branch);
                        bool isHead = heads.Any(x => x.Version == m_Tip.ID);
                        bool isOnlyHead = heads.Count == 1;
                        if (!isHead)
                            Printer.PrintMessage("\nCurrent version #b#{0}## is #e#not the head## of branch #b#{1}## (#b#\"{2}\"##)", m_Tip.ShortName, branch.ShortID, branch.Name);
                        else if (!isOnlyHead)
                            Printer.PrintMessage("\nCurrent version #b#{0}## is #w#not only the head## of branch #b#{1}## (#b#\"{2}\"##)", m_Tip.ShortName, branch.ShortID, branch.Name);
                    }
                    if (!anything)
                    {
                        if (!nullableLimit.HasValue || nullableLimit.Value <= 0)
                            Printer.PrintMessage("\nNo versions matched your history/filter query (searched #b#all## revisions).");
                        else
                            Printer.PrintMessage("\nNo versions matched your history/filter query (searched #b#{0}## revisions).\n\nTry setting #b#--limit## to a larger value (or #b#0## for all revisions).", nullableLimit.Value);
                    }
                }
                if (versionAutoSelected)
                {
                    if (targetHeadObjects.Count > 1)
                    {
                        Printer.WriteLineMessage("\n #w#Warning:## Target branch has multiple heads.");

                        Printer.WriteLineMessage("\n Heads of #b#\"{0}\"##:", ws.CurrentBranch.Name);
                        foreach (var x in targetHeadObjects)
                        {
                            var v = Workspace.GetVersion(x.Version);
                            Printer.WriteLineMessage("   #b#{0}##: {1} by {2}", v.ShortName, v.Timestamp.ToLocalTime(), v.Author);
                        }
                    }
                }
            }
            return true;
        }

        private string GetAlterationFormat(Objects.AlterationType code)
        {
            switch (code)
            {
                case Objects.AlterationType.Add:
                case Objects.AlterationType.Copy:
                    return "s";
                case Objects.AlterationType.Update:
                    return "w";
                case Objects.AlterationType.Move:
                    return "c";
                case Objects.AlterationType.Delete:
                    return "e";
                case Objects.AlterationType.Discard:
                    return "M";
                default:
                    throw new Exception("Unknown alteration type");
            }
        }
    }
}
