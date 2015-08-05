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
		[Option('l', "alterations", HelpText = "Display a listing of alterations.", MutuallyExclusiveSet = "concise")]
		public bool Alterations { get; set; }
		[Option('t', "limit", DefaultValue = 10, HelpText = "Limit number of versions to show (0 for all).")]
		public int Limit { get; set; }
        [Option('c', "concise", HelpText = "Uses a short log formatting style.", MutuallyExclusiveSet = "alterations")]
        public bool Concise { get; set; }

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
		protected override void Start()
		{
			Printer.WriteLineMessage("Version #b#{0}## on branch \"#b#{1}##\" (rev {2})", Workspace.Version.ID, Workspace.CurrentBranch.Name, Workspace.Version.Revision);
		}

		protected override bool RequiresTargets { get { return false; } }

		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
		{
			LogVerbOptions localOptions = options as LogVerbOptions;
			Printer.EnableDiagnostics = localOptions.Verbose;

            var enumeration = ws.History.Where(y => HasAlterationForTarget(y, targets));
            if (localOptions.Limit != 0)
                enumeration = enumeration.Take(localOptions.Limit);

            foreach (var x in enumeration.Reverse())
			{
                if (localOptions.Concise)
                {
                    string message = x.Message;
                    if (message == null)
                        message = string.Empty;
                    Printer.PrintMessage("({4}) #c#{0}:## {1} #q#({2} {3})##", x.ShortName, message.Replace('\n', ' '), x.Author, new DateTime(x.Timestamp.Ticks, DateTimeKind.Utc).ToShortDateString(), x.Revision);
                }
                else
                {
                    Printer.PrintMessage("\n({0}) #c#{1}## on branch #b#{2}##", x.Revision, x.ID, ws.GetBranch(x.Branch).Name);

                    foreach (var y in ws.GetMergeInfo(x.ID))
                    {
                        var mergeParent = ws.GetVersion(y.SourceVersion);
                        Printer.PrintMessage(" <- Merged from {0} on branch {1}", mergeParent.ID, ws.GetBranch(mergeParent.Branch).Name);
                    }

                    Printer.PrintMessage("#b#Author:## {0} #q# {1} ##", x.Author, x.Timestamp.ToLocalTime());
                    Printer.PrintMessage("#b#Message:##\n{0}", string.IsNullOrWhiteSpace(x.Message) ? "<none>" : Printer.Escape(x.Message));

                    if (localOptions.Alterations)
                    {
                        Printer.PrintMessage("#b#Alterations:##");
                        foreach (var y in ws.GetAlterations(x).OrderBy(z => z.Type))
                        {
							Objects.Record rec = y.NewRecord.HasValue ? ws.GetRecord(y.NewRecord.Value) : y.PriorRecord.HasValue ? ws.GetRecord(y.PriorRecord.Value) : null;
							if (rec != null && IsTarget(rec, targets))
								Printer.PrintMessage("#{2}#({0})## {1}", y.Type.ToString().ToLower(), rec.CanonicalName, GetAlterationFormat(y.Type));
						}
					}
                }
			}

			return true;
		}

		private bool HasAlterationForTarget(Objects.Version v, IList<Versionr.Status.StatusEntry> targets)
		{
			if (targets == null || targets.Count == 0)
				return true;

			foreach (var x in Workspace.GetAlterations(v))
			{
				if (x.NewRecord.HasValue)
				{
					if (IsTarget(Workspace.GetRecord(x.NewRecord.Value), targets))
						return true;
				}
				else if (x.PriorRecord.HasValue)
				{
					if (IsTarget(Workspace.GetRecord(x.PriorRecord.Value), targets))
						return true;
				}
			}
			return false;
		}

		private bool IsTarget(Objects.Record rec, IList<Versionr.Status.StatusEntry> targets)
		{
			if (targets == null || targets.Count == 0)
				return true;

			if (rec == null)
				return false;

			return targets.Where(x => x.CanonicalName == rec.CanonicalName).FirstOrDefault() != null;
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
