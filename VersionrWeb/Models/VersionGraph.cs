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
			"#024bfd",
			"#fd02de",
			"#02fdd3",
			"#fd8a02",
			"#4102fd",
			"#0dfd02",
			"#fd0256",
			"#029ffd",
			"#e8fd02",
			"#c802fd",
			"#02fd7f",
			"#fd3602",
			"#0217fd",
			"#61fd02",
			"#fd02aa",
			"#02f3fd",
			"#fdbe02",
			"#7402fd",
			"#02fd2b",
			"#fd0222",
			"#026bfd",
			"#b5fd02",
			"#fc02fd",
			"#02fdb3",
			"#fd6a02",
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

		private class Column
		{
			public int UsedUntilRow;
			public int Col;
		}

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
			int nextColor = 0;
			var columns = new List<Column>();
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
				var column = columns.FirstOrDefault(x => x.UsedUntilRow < node.Row);
				if (column == null)
				{
					column = new Column();
					column.Col = columns.Count;
					columns.Add(column);
				}
				node.Col = column.Col;
				column.UsedUntilRow = node.Row + 1;

				// Assign new color
				node.Color = Colors[nextColor++ % Colors.Length];
				
				// Assign direct parents into same column
				Node parent = node;
				while (parent.Parents.Count > 0 && Nodes.TryGetValue(parent.Parents[0], out parent))
				{
					column.UsedUntilRow = parent.Row + 1;
					if (parent.Col >= 0)
						break;

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

			// Links
			foreach (var node in Nodes.Values)
			{
				if (node.HasUnseenDirectChild)
				{
					DrawUnterminatedPath(sb, node, node.X, node.Y - RowHeight);
				}

				Node parent;
				if (node.Parents.Count > 0)
				{
					if (Nodes.TryGetValue(node.Parents[0], out parent))
					{
						// Direct link
						DrawPath(sb, node, parent, false);
					}
					else
					{
						DrawUnterminatedPath(sb, node, node.X, node.Y + RowHeight);
					}
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
						DrawUnterminatedPath(sb, node, node.X + ColWidth, node.Y + RowHeight);
					}
				}
			}

			// Versions
			foreach (var node in Nodes.Values)
			{
				sb.Append($"<circle cx=\"{node.X}\" cy=\"{node.Y}\" r=\"{NodeRadius}\" fill=\"{node.Color}\" />");
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
				// Direct parent
				sb.Append($"<path d=\"M{x1} {y1} L{x2} {y2}\" stroke=\"{b.Color}\" />");
			}
			else if (!isMerge)
			{
				// Branch
				sb.Append($"<path d=\"M{x1} {y1} C {x1} {y2 - RowHeight / 2} {x1} {y2} {x2} {y2}\" stroke=\"{a.Color}\" />");
			}
			else if (!b.HasChildren)
			{
				// Reintegrate
				sb.Append($"<path d=\"M{x1} {y1} C {x2} {y1} {x2} {y1 + RowHeight / 2} {x2} {y2}\" stroke=\"{b.Color}\" />");
			}
			else
			{
				// Merge
				sb.Append($"<path d=\"M{x1} {y1} C {x2} {y1} {x1} {y2} {x2} {y2}\" stroke=\"{b.Color}\" />");
			}
		}

		private void DrawUnterminatedPath(StringBuilder sb, Node a, int x2, int y2)
		{
			int x1 = a.X;
			int y1 = a.Y;
			sb.Append($"<path d=\"M{x1} {y1} L{x2} {y2}\" stroke=\"{a.Color}\" stroke-dasharray=\"15 2 5 2 3 2 1 2 1 2 1 100\" />");
		}
	}
}
