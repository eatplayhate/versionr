using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security;
using Versionr;
using Versionr.Commands;

namespace Svgdag
{
    public class SvgdagVerbOptions : VerbOptionBase
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Outputs a directed acyclic graph of version metadata in svg format."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "svgdag";
            }
        }

        [Option('l', "limit", HelpText = "Limit number of versions to include; use 0 for all.")]
        public int? Limit { get; set; }

        public override BaseCommand GetCommand()
        {
            return new Svgdag();
        }
    }

    class Svgdag : BaseWorkspaceCommand
    {
        class BranchDeets
        {
            internal string color;
            internal float column = 0.0f;
            internal int rowStart = int.MaxValue;
            internal int rowEnd = int.MinValue;
            internal void RowSeen(int row)
            {
                rowStart = Math.Min(row, rowStart);
                rowEnd = Math.Max(row, rowStart);
            }
            internal bool Overlaps(BranchDeets other)
            {
                if (rowStart > other.rowEnd)
                    return false;
                if (rowEnd < other.rowStart)
                    return false;
                return true;
            }
        }

        class VersionDeets
        {
            internal float X;
            internal float Y;
            internal string Color;
        }

        protected override bool RunInternal(object options)
        {
            SvgdagVerbOptions localOptions = options as SvgdagVerbOptions;

            float startLine = 20.0f;
            float deltaLine = 20.0f;

            // STUFF
            int limit = !localOptions.Limit.HasValue ? 50 : localOptions.Limit.Value;
            var dag = Workspace.GetDAG(limit);
            string[] colors = new string[] { "green", "blue", "orange", "darkturquoise", "purple", "pink", "olive", "cornflowerblue", "coral", "teal", "blueviolet", "brown", "grey", "maroon", "indigo" };   // add more
            Dictionary<Guid, BranchDeets> branchDeets = new Dictionary<Guid, BranchDeets>();
            branchDeets.Add(Workspace.CurrentBranch.ID, new BranchDeets { color = "red" }); // no one else gets red
            int nextColorIndex = 0;

            // Collect branches and assign colors.
            int row = 0;
            foreach (var version in dag.Objects)
            {
                BranchDeets bd;
                if (!branchDeets.TryGetValue(version.Object.Branch, out bd))
                {
                    int useColorIndex = nextColorIndex % colors.Length;
                    branchDeets.Add(version.Object.Branch, bd = new BranchDeets { color = colors[useColorIndex] });
                    nextColorIndex++;
                }

                bd.RowSeen(row);

                ++row;
            }

            float documentLength = startLine + (float)(row + 1) * deltaLine;

            Console.WriteLine(@"<?xml version=""1.0"" standalone=""no""?><!DOCTYPE svg PUBLIC ""-//W3C//DTD SVG 1.1//EN"" ""http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd"">");
            Console.WriteLine(@"<svg width=""1024pt"" height=""{0}pt"" viewBox=""0 0 1024 {0}"" xmlns=""http://www.w3.org/2000/svg"" xmlns:xlink=""http://www.w3.org/1999/xlink"" version=""1.1"">", documentLength);
            Console.WriteLine(@"<desc>Versionr svg output</desc>");
            // CSS Style town
            Console.WriteLine(@"
  <defs>
    <style type=""text/css""><![CDATA[
      text {
        font-family: Verdana;
        font-size: 9px;
        dominant-baseline: central;
      }
      text.u {
        fill: blue;
      }
      text.g {
        font-family: Courier;
        fill: green;
      }
      circle {
        stroke: black;
        stroke-width: 1px;
      }
      path {
        stroke-width: 1px;
        fill: none;
      }
      path.m {
        stroke-dasharray: 3;
      }
    ]]></style>
  </defs>
            ");


            // Assign columns to branches.
            List<List<BranchDeets>> columnSlot = new List<List<BranchDeets>>();
            columnSlot.Add(new List<BranchDeets>());
            BranchDeets currentBD = branchDeets[Workspace.CurrentBranch.ID];
            columnSlot[0].Add(currentBD);    // current branch always column 0

            foreach (var bd in branchDeets.Values)
            {
                if (bd == currentBD)
                    continue;

                bool inserted = false;
                foreach (var column in columnSlot)
                {
                    bool overlap = false;
                    foreach (var columnBD in column)
                    {
                        if (bd.Overlaps(columnBD))
                        {
                            overlap = true;
                            break;
                        }
                    }

                    if (overlap == false)
                    {
                        column.Add(bd);
                        inserted = true;
                        break;
                    }
                }

                if (inserted == false)
                {
                    columnSlot.Add(new List<BranchDeets>());
                    columnSlot.Last().Add(bd);
                }
            }

            float columnValue = 0.0f;
            foreach (var column in columnSlot)
            {
                foreach (var bd in column)
                {
                    bd.column = columnValue;
                }
                columnValue += 1.0f;
            }

            float guidX = (columnValue + 1.0f) * deltaLine;
            float branchX = guidX + 75.0f;
            float userX = branchX + 100.0f;
            float messageX = userX + 75.0f;
            float dotColumnStartX = 20.0f;

            float line = startLine;
            StringBuilder branchBlurb = new StringBuilder(1024 * 1024);
            branchBlurb.AppendFormat(@"<g transform=""translate({0}, 0)"">", branchX);
            StringBuilder guidBlurb = new StringBuilder(1024 * 1024);
            guidBlurb.AppendFormat(@"<g transform=""translate({0}, 0)"">", guidX);
            StringBuilder userBlurb = new StringBuilder(1024 * 1024);
            userBlurb.AppendFormat(@"<g transform=""translate({0}, 0)"">", userX);
            StringBuilder messageBlurb = new StringBuilder(1024 * 1024);
            messageBlurb.AppendFormat(@"<g transform=""translate({0}, 0)"">", messageX);
            foreach (var version in dag.Objects)
            {
                BranchDeets bd;
                if (!branchDeets.TryGetValue(version.Object.Branch, out bd))
                {
                    int useColorIndex = nextColorIndex % colors.Length;
                    branchDeets.Add(version.Object.Branch, bd = new BranchDeets { color = colors[useColorIndex] });
                    nextColorIndex++;
                }

                branchBlurb.AppendFormat(@"<text y=""{0}"" fill=""{1}"">{2}</text>", line, bd.color, SecurityElement.Escape(Workspace.GetBranch(version.Object.Branch).Name));
                guidBlurb.AppendFormat(@"<text y=""{0}"" class=""g"">{1}</text>", line, version.Object.ID.ToString().Substring(0, 8));
                userBlurb.AppendFormat(@"<text y=""{0}"" class=""u"">{1}</text>", line, SecurityElement.Escape(version.Object.Author));
                messageBlurb.AppendFormat(@"<text y=""{0}"">{1}</text>", line, SecurityElement.Escape(version.Object.Message));

                line += deltaLine;
            }

            branchBlurb.Append("</g>");
            guidBlurb.Append("</g>");
            userBlurb.Append("</g>");
            messageBlurb.Append("</g>");

            Console.Write(branchBlurb);
            Console.Write(guidBlurb);
            Console.Write(userBlurb);
            Console.Write(messageBlurb);

            Dictionary<Guid, VersionDeets> versionDeets = new Dictionary<Guid, VersionDeets>();

            // Draw branch dots (but postpone output)
            StringBuilder dotsBlurb = new StringBuilder(1024 * 1024);
            line = startLine;
            float dotRadius = deltaLine / 4.0f;
            foreach (var version in dag.Objects)
            {
                BranchDeets bd = branchDeets[version.Object.Branch];
                float x = dotColumnStartX + bd.column * deltaLine; // using deltaLine for width as well as height for dots, so they're on a square grid
                dotsBlurb.AppendFormat(@"<circle fill=""{0}"" cx=""{1}"" cy=""{2}"" r=""{3}""/>", bd.color, x, line, dotRadius);
                // Save off its position
                versionDeets.Add(version.Object.ID, new VersionDeets { X = x, Y = line, Color = bd.color });

                line += deltaLine;
            }

            // Draw connecting lines
            foreach (var version in dag.Objects)
            {
                VersionDeets currentDot = versionDeets[version.Object.ID];
                foreach (var link in version.Links)
                {
                    if (dag.Lookup.ContainsKey(link.Source))
                    {
                        VersionDeets otherDot = versionDeets[dag.Lookup[link.Source].Item1.ID];
                        string lineClass = "n";
                        if (link.Merge)
                            lineClass = "m";
                        if (currentDot.X != otherDot.X)
                            Console.WriteLine(@"<path d=""M {0} {1} Q {2} {1} {2} {3}"" class=""{4}"" stroke=""{5}""/>", currentDot.X, currentDot.Y, otherDot.X, otherDot.Y, lineClass, otherDot.Color);
                        else
                            Console.WriteLine(@"<path d=""M {0} {1} L {0} {2}"" class=""{3}"" stroke=""{4}""/>", currentDot.X, currentDot.Y, otherDot.Y, lineClass, otherDot.Color);
                    }
                }
            }

            Console.Write(dotsBlurb);

            Console.WriteLine(@"</svg>");
            return true;
        }
    }


    public class SvgdagOptions
    {
        [HelpOption]
        public string GetUsage()
        {
            var help = new CommandLine.Text.HelpText
            {
                Heading = new CommandLine.Text.HeadingInfo("Plugin: #b#Svgdag##"),
                AddDashesToOption = false,
            };
            help.AddPreOptionsLine("Outputs a directed acyclic graph of version metadata in svg format.\n\n#b#Commands:");
            help.ShowHelpOption = false;
            help.AddOptions(this);
            return help;
        }

        [VerbOption("svgdag", HelpText = "Outputs a directed acyclic graph of version metadata in svg format.")]
        public SvgdagVerbOptions blookityblarg { get; set; }
    }
}
