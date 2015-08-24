using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Commands
{
	class LogVerbOptions : FileBaseCommandVerbOptions
	{
		[Option('t', "limit", DefaultValue = -1, HelpText = "Limit number of versions to show, 10 default (0 for all).")]
		public int Limit { get; set; }

		public enum DetailMode
		{
			Normal,
			N = Normal,
			Concise,
			C = Concise,
			Detailed,
			D = Detailed,
			Full,
			F = Full
		}
		[Option("detail", HelpText = "Set the display mode. One of (n)ormal, (c)oncise, (d)etailed, (f)ull", MetaValue = "<value>", MutuallyExclusiveSet = "logdetail")]
		public DetailMode Detail { get; set; }
		[Option('c', "concise", HelpText = "Uses a short log formatting style. Alias for --detail=concise", MutuallyExclusiveSet = "logdetail")]
		public bool Concise
		{
			get { return Detail == DetailMode.Concise; }
			set { if (value) Detail = DetailMode.Concise; }
		}


		[Option('b', "branch", HelpText = "Name of the branch to view", MutuallyExclusiveSet = "versionselect")]
		public string Branch { get; set; }
		[Option('v', "version", HelpText = "Specific version to view", MutuallyExclusiveSet = "versionselect")]
		public string Version { get; set; }

		[Option("author", HelpText = "Filter log on specific author")]
		public string Author { get; set; }


		public override string[] Description
		{
			get
			{
				return new string[]
				{
					"get a log"
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

	}
	class Log : FileBaseCommand
	{
		protected override bool RequiresTargets { get { return false; } }
		protected override bool OnNoTargetsAssumeAll { get { return true; } }

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

		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
		{
			LogVerbOptions localOptions = options as LogVerbOptions;
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

			var history = (version == null ? ws.History : ws.GetHistory(version)).AsEnumerable();

			if (!string.IsNullOrEmpty(localOptions.Author))
				history = history.Where(x => x.Author.Equals(localOptions.Author, StringComparison.OrdinalIgnoreCase));

			var enumeration = history
				.Select(x => new Tuple<Objects.Version, IEnumerable<KeyValuePair<bool, ResolvedAlteration>>>(x, FilterAlterations(x)))
				.Where(x => x.Item2.Any() || (x.Item1.Parent == null && localOptions.Objects.Count == 0));

			if (localOptions.Limit == -1)
                localOptions.Limit = (version == null || targetedBranch) ? 10 : 1;

            if (localOptions.Limit != 0)
                enumeration = enumeration.Take(localOptions.Limit);

            Objects.Version tip = Workspace.Version;
            Objects.Version last = null;
            Dictionary<Guid, Objects.Branch> branches = new Dictionary<Guid, Objects.Branch>();
            foreach (var x in enumeration.Reverse())
			{
				Objects.Version v = x.Item1;
                last = v;
                Objects.Branch branch = null;
                if (!branches.TryGetValue(v.Branch, out branch))
                {
                    branch = ws.GetBranch(v.Branch);
                    branches[v.Branch] = branch;
                }
                if (localOptions.Concise)
                {
                    string message = v.Message;
                    if (message == null)
                        message = string.Empty;
                    string tipmarker = " ";
                    if (v.ID == tip.ID)
                        tipmarker = "#w#*##";
                    string mergemarker = " ";
                    if (ws.GetMergeInfo(v.ID).FirstOrDefault() != null)
                        mergemarker = "#s#M##";
                    Printer.PrintMessage("{6}#c#{0}:##{7}({4}/#b#{5}##) {1} #q#({2} {3})##", v.ShortName, message.Replace('\n', ' '), v.Author, new DateTime(v.Timestamp.Ticks, DateTimeKind.Utc).ToShortDateString(), v.Revision, branch.Name, tipmarker, mergemarker);
                }
                else
                {
                    string tipmarker = "";
                    if (v.ID == tip.ID)
                        tipmarker = " #w#*<current>##";
                    Printer.PrintMessage("\n({0}) #c#{1}## on branch #b#{2}##{3}", v.Revision, v.ID, branch.Name, tipmarker);

                    foreach (var y in ws.GetMergeInfo(v.ID))
                    {
                        var mergeParent = ws.GetVersion(y.SourceVersion);
                        Objects.Branch mergeBranch = null;
                        if (!branches.TryGetValue(mergeParent.Branch, out mergeBranch))
                        {
                            mergeBranch = ws.GetBranch(mergeParent.Branch);
                            branches[mergeParent.Branch] = mergeBranch;
                        }
                        Printer.PrintMessage(" <- Merged from #s#{0}## on branch #b#{1}##", mergeParent.ID, mergeBranch.Name);
                    }

                    var heads = Workspace.GetHeads(v.ID);
                    foreach (var y in heads)
                    {
                        Objects.Branch headBranch = null;
                        if (!branches.TryGetValue(y.Branch, out headBranch))
                        {
                            headBranch = ws.GetBranch(y.Branch);
                            branches[y.Branch] = headBranch;
                        }
                        string branchFlags = string.Empty;
                        if (branch.Terminus.HasValue)
                            branchFlags = " #e#(deleted)##";
                        Printer.PrintMessage(" ++ #i#Head## of branch #b#{0}## (#b#\"{1}\"##){2}", headBranch.ID, headBranch.Name, branchFlags);
                    }
                    if (branch.Terminus == v.ID)
                        Printer.PrintMessage(" ++ #i#Terminus## of #e#deleted branch## #b#{0}## (#b#\"{1}\"##)", branch.ID, branch.Name);

                    Printer.PrintMessage("#b#Author:## {0} #q# {1} ##\n", v.Author, v.Timestamp.ToLocalTime());
                    Printer.PushIndent();
                    Printer.PrintMessage("{0}", string.IsNullOrWhiteSpace(v.Message) ? "<none>" : Printer.Escape(v.Message));
                    Printer.PopIndent();

                    if (localOptions.Detail == LogVerbOptions.DetailMode.Detailed || localOptions.Detail == LogVerbOptions.DetailMode.Full)
					{
						Printer.PrintMessage("#b#Alterations:##");
						var alterations = localOptions.Detail == LogVerbOptions.DetailMode.Detailed ? x.Item2.Select(z => z.Value) : GetAlterations(v);
						foreach (var y in alterations.OrderBy(z => z.Alteration.Type))
						{
							Printer.PrintMessage("#{2}#({0})## {1}", y.Alteration.Type.ToString().ToLower(), y.Record.CanonicalName, GetAlterationFormat(y.Alteration.Type));
						}
					}
				}
			}
            if (last.ID == tip.ID && version == null)
            {
                var branch = Workspace.CurrentBranch;
                var heads = Workspace.GetBranchHeads(branch);
                bool isHead = heads.Any(x => x.Version == last.ID);
                bool isOnlyHead = heads.Count == 1;
                if (!isHead)
                    Printer.PrintMessage("\nCurrent version #b#{0}## is #e#not the head## of branch #b#{1}## (#b#\"{2}\"##)", tip.ShortName, branch.ShortID, branch.Name);
                else if (!isOnlyHead)
                    Printer.PrintMessage("\nCurrent version #b#{0}## is #w#not only the head## of branch #b#{1}## (#b#\"{2}\"##)", tip.ShortName, branch.ShortID, branch.Name);
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
				default:
					throw new Exception("Unknown alteration type");
			}
		}
	}
}
