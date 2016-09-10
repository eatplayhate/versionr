using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace VersionrWeb.Models
{
	public class VersionGraph
	{
		public string Svg { get; private set; }

		public int Width { get; private set; }
		public int Height { get; private set; }

		public const int PaddingLeft = 8;
		public const int PaddingRight = 4;

		public const int NodeRadius = 4;

		public const int RowHeight = 50;
		public const int ColWidth = 16;

		public string[] Colors =
		{
			"#009800",
			"#e11d21",
			"#006b75",
			"#207de5",
			"#0052cc",
			"#5319e7",
			"#f7c6c7",
			"#fad8c7",
			"#fef2c0",
			"#bfe5bf",
			"#c7def8",
			"#bfdadc",
			"#bfd4f2",
			"#d4c5f9",
			"#cccccc",
			"#84b6eb",
			"#e6e6e6",
			"#cc317c"
		};

		private class Node
		{
			public Versionr.Objects.Version Version;
			public int Row;
			public int Col;
			public List<Guid> Parents = new List<Guid>();
			public string Color = null;

			public bool HasChildren;
			public bool HasUnseenDirectChild;

			public int X => PaddingLeft + Col * ColWidth + ColWidth / 2;
			public int Y => Row * RowHeight + RowHeight / 2;
		}

		private Dictionary<Guid, Node> Nodes = new Dictionary<Guid, Node>();
		private List<Node> OrderedNodes = new List<Node>();

		public VersionGraph(Area area, IEnumerable<Versionr.Objects.Version> versions)
		{
			// Create nodes
			foreach (var version in versions)
			{
				var node = new Node();
				node.Version = version;
				if (version.Parent.HasValue)
					node.Parents.Add(version.Parent.Value);
				foreach (var mergeinfo in area.GetMergeInfo(node.Version.ID))
					node.Parents.Add(mergeinfo.SourceVersion);
				node.Row = Nodes.Count;
				node.Col = -1;
				Nodes[version.ID] = node;
				OrderedNodes.Add(node);
			}

			// Allocate columns
			int nextCol = 0;
			int nextColor = 0;
			foreach (var node in OrderedNodes)
			{
				// Already allocated?
				if (node.Col >= 0)
					continue;

				if (area.GetDirectChildren(node.Version).Count > 0)
				{
					node.HasUnseenDirectChild = true;
					node.HasChildren = true;
				}
				
				// Assign to next free column
				node.Col = nextCol++;
				node.Color = Colors[nextColor++ % Colors.Length];

				// Assign direct parents into same column
				Node parent = node;
				while (parent.Parents.Count > 0 && Nodes.TryGetValue(parent.Parents[0], out parent))
				{
					parent.Col = node.Col;
					parent.Color = node.Color;
					parent.HasChildren = true;
				}
			}
			
			var sb = new StringBuilder();
			Width = (Nodes.Values.Max(x => x.Col) + 1) * ColWidth + PaddingLeft + PaddingRight;
			Height = Nodes.Count * RowHeight;
			sb.Append($"<svg width=\"{Width}\" height=\"{Height}\">");
			sb.Append("<style>");
			sb.Append("  path { fill: none; stroke-width: 2 }");
			sb.Append("  circle { stroke: none }");
			sb.Append("</style>");

			foreach (var node in Nodes.Values)
			{
				sb.Append($"<circle cx=\"{node.X}\" cy=\"{node.Y}\" r=\"{NodeRadius}\" fill=\"{node.Color}\" />");
			}

			// Links
			foreach (var node in Nodes.Values)
			{
				if (node.HasUnseenDirectChild)
				{
					// Gradient path up
					sb.Append($"<path d=\"M{node.X} {node.Y} L{node.X} {node.Y - RowHeight}\" stroke=\"{node.Color}\" />");
				}

				Node parent;
				if (Nodes.TryGetValue(node.Parents[0], out parent))
				{
					// Direct link
					DrawPath(sb, node, parent, false);
				}
				else
				{
					// Gradient path down
					sb.Append($"<path d=\"M{node.X} {node.Y} L{node.X} {node.Y + RowHeight}\" stroke=\"{node.Color}\" />");
				}

				foreach (var parentId in node.Parents.Skip(1))
				{
					if (Nodes.TryGetValue(parentId, out parent))
					{
						// Merge link
						DrawPath(sb, node, parent, true);
					}
					else
					{
						// Gradient path merge
					}
				}
			}

			sb.Append($"</svg>");
			Svg = sb.ToString();
		}

		private void DrawPath(StringBuilder sb, Node a, Node b, bool isMerge)
		{
			int x1 = a.X;
			int y1 = a.Y;
			int x2 = b.X;
			int y2 = b.Y;

			if (a.Col == b.Col)
			{
				sb.Append($"<path d=\"M{x1} {y1} L{x2} {y2}\" stroke=\"{a.Color}\" />");
			}
			else if (!isMerge)
			{
				sb.Append($"<path d=\"M{x1} {y1} C {x1} {y2 - RowHeight / 2} {x1} {y2} {x2} {y2}\" stroke=\"{a.Color}\" />");
			}
			else if (!b.HasChildren)
			{
				sb.Append($"<path d=\"M{x1} {y1} C {x2} {y1} {x2} {y1 + RowHeight / 2} {x2} {y2}\" stroke=\"{a.Color}\" />");
			}
			else
			{
				sb.Append($"<path d=\"M{x1} {y1} C {x2} {y1} {x1} {y2} {x2} {y2}\" stroke=\"{a.Color}\" />");
			}
		}
	}
}
