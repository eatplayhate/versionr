using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class ViewDAGVerbOptions : VerbOptionBase
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
                return "viewdag";
            }
        }
    }
    class ViewDAG : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            ViewDAGVerbOptions localOptions = options as ViewDAGVerbOptions;
            Printer.EnableDiagnostics = false;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            var result = ws.GetDAG();
            System.Console.WriteLine("digraph {");
            int index = 0;

			int nextColourIndex = 0;
			string[] colours = new string[] { "red2", "green3", "blue", "cyan4", "darkorange1", "magenta4" };
			Dictionary<Guid, Tuple<string, string>> branchInfoMap = new Dictionary<Guid, Tuple<string, string>>();

            foreach (var x in result.Objects)
            {
                string nodename = "node" + (index++).ToString();
                string nodeattribs = string.Empty;

				Tuple<string, string> branchInfo;
				if (!branchInfoMap.TryGetValue(x.Object.Branch, out branchInfo))
				{
					string colour;
					if (x.Object.Parent == null)
						colour = "black";
					else
					{
						colour = colours[nextColourIndex];
						nextColourIndex = (nextColourIndex + 1) % colours.Length;
					}
					branchInfo = new Tuple<string, string>(colour, ws.GetBranch(x.Object.Branch).Name);
					branchInfoMap.Add(x.Object.Branch, branchInfo);
				}
				//nodeattribs += string.Format("color = {0};style = \"rounded,filled\";fontcolor = white;", branchInfo.Item1);
				nodeattribs += string.Format("color = {0};style = rounded;penwidth = 4;", branchInfo.Item1);

				string name = x.Object.ID.ToString().Substring(0, 8);
                name += string.Format("\\n{0}", x.Object.Author);
                var mappedHeads = ws.MapVersionToHeads(x.Object);
                if (mappedHeads.Count > 0)
                {
					foreach (var y in mappedHeads)
                        name += string.Format("\\nHead of \\\"{0}\\\"", y.Name);
                }
				else
					nodeattribs += "shape = box;";

				nodeattribs += string.Format("label = \"{0}\"", name);

				System.Console.WriteLine("  {0} [{1}];", nodename, nodeattribs);
                foreach (var y in x.Links)
                {
                    string sourceNode = "node" + result.Lookup[y.Source].Item2.ToString();
					string attribs = string.Format("color = {0};fontcolor = {0};penwidth = 2;", branchInfo.Item1);
					attribs += string.Format("taillabel = \"{0}\";", branchInfo.Item2);
					if (y.Merge)
						attribs += "style = dotted";

					System.Console.WriteLine("  {0} -> {1} [{2}];", sourceNode, nodename, attribs);
                }
            }
            System.Console.WriteLine("}");
            return false;
        }
    }
}
