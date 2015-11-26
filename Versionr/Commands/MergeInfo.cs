﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class MergeInfoVerbOptions : VerbOptionBase
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "abandon all ships"
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "merge";
            }
        }

        [ValueList(typeof(List<string>))]
        public IList<string> Target { get; set; }
    }
    class MergeInfo : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            MergeInfoVerbOptions localOptions = options as MergeInfoVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            foreach (var x in localOptions.Target)
            {
                Objects.Version v = ws.GetPartialVersion(x);
                if (v != null)
                {
                    Printer.PrintMessage("Merge information for version #c#{0}##.", v.ID);
                    var mergeInfos = ws.GetMergeList(v.ID);
                    HashSet<Guid> directMerges = new HashSet<Guid>();
                    foreach (var m in mergeInfos)
                    {
                        Objects.Branch b = ws.GetBranch(m.Branch);
                        directMerges.Add(b.ID);
                        Printer.PrintMessage(" - Merged into #b#{0}## on branch #b#\"{1}\"## ({2})", m.ShortName, b.Name, b.ShortID);
                    }
                    Printer.PrintMessage("Branch relationships:");
                    foreach (var b in ws.GetBranches(false))
                    {
                        string result = "#w#unrelated";
                        int relationshipCode = 0;
                        foreach (var h in ws.GetBranchHeads(b))
                        {
                            var headVersion = ws.GetVersion(h.Version);
                            if (relationshipCode < 4 && headVersion.ID == v.ID)
                            {
                                relationshipCode = 4;
                                result = "#s#a branch head";
                            }
                            else if (relationshipCode < 3 && ws.GetHistory(headVersion).Any(z => z.ID == v.ID))
                            {
                                relationshipCode = 3;
                                result = "#s#a direct parent";
                            }
                            else if (relationshipCode < 2 && directMerges.Contains(b.ID))
                            {
                                relationshipCode = 2;
                                result = "#s#a merge parent";
                            }
                            else if (relationshipCode < 1 && ws.GetParentGraph(headVersion).ContainsKey(v.ID))
                            {
                                relationshipCode = 1;
                                result = "#c#an indirect ancestor";
                            }
                        }
                        Printer.PrintMessage(" - #b#{0}## ({1}): version is {2}##.", b.Name, b.ShortID, result);
                    }
                }
                else
                    Printer.PrintError("#e#Can't locate version with ID #b#\"{0}\"#e#.", x);
            }
            return true;
        }
    }
}
