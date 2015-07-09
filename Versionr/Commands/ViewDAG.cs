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
            foreach (var x in result.Objects)
            {
                string nodename = "node" + (index++).ToString();
                string name = x.Object.ID.ToString().Substring(0, 8);
                name += string.Format("\\n{0}", x.Object.Author);
                string nodeattribs = string.Empty;
                var mappedHeads = ws.MapVersionToHeads(x.Object);
                if (mappedHeads.Count > 0)
                {
                    nodeattribs += "shape = box;";
                    foreach (var y in mappedHeads)
                        name += string.Format("\\nHead of \\\"{0}\\\"", y.Name);
                }
                nodeattribs += string.Format("label = \"{0}\"", name);
                System.Console.WriteLine("  {0} [{1}];", nodename, nodeattribs);
                foreach (var y in x.Links)
                {
                    string sourceNode = "node" + result.Lookup[y.Source].Item2.ToString();
                    string attribs = string.Empty;
                    if (y.Merge)
                        attribs = " [style = dotted]";
                    System.Console.WriteLine("  {0} -> {1}{2};", sourceNode, nodename, attribs);
                }
            }
            System.Console.WriteLine("}");
            return false;
        }
    }
}
