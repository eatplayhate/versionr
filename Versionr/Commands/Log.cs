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
		[Option('l', "limit", DefaultValue = -1, HelpText = "Limit number of versions to show, 10 default (0 for all).")]
		public int Limit { get; set; }
        [Option('m', "shormerged", DefaultValue = true, HelpText = "Show logical history (cleans up automatic merge data).")]
        public bool ShowMerged { get; set; }
        [Option('e', "reverse", HelpText = "Reverses the order of versions in the log.")]
        public bool Reverse { get; set; }

        public enum DetailMode
		{
			Normal,
			N = Normal,
			Concise,
			C = Concise,
			Detailed,
			D = Detailed,
			Full,
			Jrunting,
			J = Jrunting,
			F = Full
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

		// superdirty
		private HashSet<Guid> m_LoggedVersions;
		private void FormatLog(Objects.Version v, IEnumerable<KeyValuePair<bool, ResolvedAlteration>> filteralt, LogVerbOptions localOptions)
		{
			if (m_LoggedVersions == null)
				m_LoggedVersions = new HashSet<Guid>();
			m_LoggedVersions.Add(v.ID);

            Objects.Branch branch = null;
			if (!m_Branches.TryGetValue(v.Branch, out branch))
			{
				branch = Workspace.GetBranch(v.Branch);
				m_Branches[v.Branch] = branch;
			}

			if (localOptions.Jrunting)
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

				pattern += "{1} #g#({2}, {3})##";
				Printer.PrintMessage(pattern, v.ShortName, message, v.Author, date, headString, mergemarker);
			}
			else if (localOptions.Concise)
			{
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
				Printer.PrintMessage("{6}#c#{0}:##{7}({4}/{8}{5}##) {1} #q#({2} {3})##", v.ShortName, message.Replace('\n', ' '), v.Author, new DateTime(v.Timestamp.Ticks, DateTimeKind.Utc).ToShortDateString(), v.Revision, branch.Name, tipmarker, mergemarker, isHead ? "#i#" : "#b#");
			}
			else
			{
				string tipmarker = "";
				if (v.ID == m_Tip.ID)
					tipmarker = " #w#*<current>##";
				Printer.PrintMessage("\n({0}) #c#{1}## on branch #b#{2}##{3}", v.Revision, v.ID, branch.Name, tipmarker);

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

				Printer.PrintMessage("#b#Author:## {0} #q# {1} ##\n", v.Author, v.Timestamp.ToLocalTime());
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
                                if (y.Alteration.Type == Objects.AlterationType.Move && !next.IsDirectory && prior.DataIdentifier != next.DataIdentifier)
                                    operationName = "refactor";
                                Printer.PrintMessage("#{2}#({0})## {1}\n  <- #q#{3}##", operationName, y.Record.CanonicalName, GetAlterationFormat(y.Alteration.Type), prior.CanonicalName);
                            }
                            else
                            {
                                Printer.PrintMessage("#{2}#({0})## {1}", y.Alteration.Type.ToString().ToLower(), y.Record.CanonicalName, GetAlterationFormat(y.Alteration.Type));
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

		private IEnumerable<Tuple<Objects.Version, IEnumerable<KeyValuePair<bool, ResolvedAlteration>>>> ApplyHistoryFilter(IEnumerable<Objects.Version> history, LogVerbOptions localOptions)
		{
			if (!string.IsNullOrEmpty(localOptions.Author))
				history = history.Where(x => x.Author.Equals(localOptions.Author, StringComparison.OrdinalIgnoreCase));

			var enumeration = history
				.Select(x => new Tuple<Objects.Version, IEnumerable<KeyValuePair<bool, ResolvedAlteration>>>(x, FilterAlterations(x)))
				.Where(x => x.Item2.Any() || (x.Item1.Parent == null || localOptions.Objects.Count == 0));

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

			if (localOptions.Limit == -1)
				localOptions.Limit = (version == null || targetedBranch) ? 10 : 1;
            if (version == null)
                version = ws.Version;

            int? nullableLimit = localOptions.Limit;
            if (nullableLimit.Value <= 0)
                nullableLimit = null;
            
            var history = (localOptions.ShowMerged ? ws.GetLogicalHistory(version, nullableLimit) : ws.GetHistory(version, nullableLimit)).AsEnumerable();

			m_Tip = Workspace.Version;
			Objects.Version last = null;
			m_Branches = new Dictionary<Guid, Objects.Branch>();
			foreach (var x in ApplyHistoryFilter(history, localOptions))
			{
				last = x.Item1;
				FormatLog(x.Item1, x.Item2, localOptions);
			}

			if (!localOptions.Jrunting && last != null && last.ID == m_Tip.ID && version == null)
			{
				var branch = Workspace.CurrentBranch;
				var heads = Workspace.GetBranchHeads(branch);
				bool isHead = heads.Any(x => x.Version == last.ID);
				bool isOnlyHead = heads.Count == 1;
				if (!isHead)
					Printer.PrintMessage("\nCurrent version #b#{0}## is #e#not the head## of branch #b#{1}## (#b#\"{2}\"##)", m_Tip.ShortName, branch.ShortID, branch.Name);
				else if (!isOnlyHead)
					Printer.PrintMessage("\nCurrent version #b#{0}## is #w#not only the head## of branch #b#{1}## (#b#\"{2}\"##)", m_Tip.ShortName, branch.ShortID, branch.Name);
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
