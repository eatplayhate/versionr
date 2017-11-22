using Graphviz4Net.Graphs;
using Graphviz4Net.WPF;
using Graphviz4Net.Dot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Graphviz4Net.Dot.AntlrParser;
using System.Windows.Media;
using Versionr.Objects;
using Version = Versionr.Objects.Version;
using System.Windows;
using System.Diagnostics;

namespace VersionrUI.ViewModels
{
    public class GraphNode
    {
        public GraphNode(Version version, string label, Color color)
        {
            Version = version;
            Label = label;
            Color = color;
        }

        public Version Version { get; }
        public string Label { get; }
        public Color Color { get; }
    }

    public class GraphLink
    {
        public GraphLink(int fromVersionID, GraphNode toVersion, string branchName, Color color, bool isMerge)
        {
            FromVersionID = fromVersionID;
            ToVersion = toVersion;
            BranchName = branchName;
            Color = color;
            IsMerge = isMerge;
        }

        public int FromVersionID { get; }
        public GraphNode FromVersion { get; set; }
        public GraphNode ToVersion { get; set; }
        public string BranchName { get; }
        public Color Color { get; }
        public bool IsMerge { get; }
    }

    public class NullArrow : GraphArrow
    {
        public NullArrow(GraphLink link)
         : base(link)
        { }
    }

    public class GraphArrow
    {
        public GraphArrow(GraphLink link)
        {
            Link = link;
        }

        public GraphLink Link { get; }
    }

    public class GraphEdge : Edge<GraphNode>
    {
        public GraphEdge(GraphLink link)
            : base(link.FromVersion, link.ToVersion, new GraphArrow(link), new NullArrow(link))
        {
            Link = link;
            // Label = link.BranchName;
        }

        public GraphLink Link { get; }
    }

    public class GraphVM : NotifyPropertyChangedBase
    {
        private AreaVM _areaVM;
        private int _revisionLimit = 50;
        private bool _forceRefresh = false;

        public GraphVM(AreaVM areaVM)
        {
            _areaVM = areaVM;
        }

        private GraphLayout _graphLayout = null;
        public GraphLayout GraphLayout
        {
            get
            {
                if (_graphLayout == null || _forceRefresh)
                    Load(Refresh);
                return _graphLayout;
            }
        }

        public int RevisionLimit
        {
            get { return _revisionLimit; }
            set
            {
                if (_revisionLimit != value)
                {
                    _revisionLimit = value;
                    _forceRefresh = true;
                    NotifyPropertyChanged(nameof(RevisionLimit));
                    Load(Refresh);
                }
            }
        }

        private static object refreshLock = new object();
        private void Refresh()
        {
            lock (refreshLock)
            {
                _forceRefresh = false;
                
                MainWindow.Instance.Dispatcher.Invoke(() =>
                {
                    _graphLayout = new GraphLayout()
                    {
                        UseContentPresenterForAllElements = true,
                        LogGraphvizOutput = true,
                        Engine = LayoutEngine.Dot,
                        DotExecutablePath = @"C:\Program Files (x86)\Graphviz2.38\bin",
                        Focusable = false,
                        Graph = CreateDotGraph()
                    };
                });

                NotifyPropertyChanged(nameof(GraphLayout));
            }
        }

        private IGraph CreateDotGraph()
        {
            List<GraphNode> definitions = new List<GraphNode>();
            List<GraphLink> links = new List<GraphLink>();

            int? limit = (RevisionLimit != -1) ? RevisionLimit : (int?)null;
            var result = _areaVM.Area.GetDAG(limit);
            
            int nextColourIndex = 0;
            Color[] colours = new Color[] { Colors.DarkOrange, Colors.Green, Colors.Blue, Colors.Cyan, Colors.Magenta, Colors.Red };
            Dictionary<Guid, Tuple<Color, Branch>> branchInfoMap = new Dictionary<Guid, Tuple<Color, Branch>>();
            
            foreach (var x in result.Objects)
            {
                Tuple<Color, Branch> branchInfo;
                if (!branchInfoMap.TryGetValue(x.Object.Branch, out branchInfo))
                {
                    Color colour;
                    if (x.Object.Parent == null)
                        colour = Colors.White;
                    else
                    {
                        colour = colours[nextColourIndex];
                        nextColourIndex = (nextColourIndex + 1) % colours.Length;
                    }
                    branchInfo = new Tuple<Color, Branch>(colour, _areaVM.Area.GetBranch(x.Object.Branch));
                    branchInfoMap.Add(x.Object.Branch, branchInfo);
                }

                Color nodeColor = branchInfo.Item1;

                string nodeLabel = x.Object.ID.ToString().Substring(0, 8);
                nodeLabel += string.Format("\n{0}", x.Object.Author);
                var mappedHeads = _areaVM.Area.MapVersionToHeads(x.Object.ID);
                if (mappedHeads.Count > 0)
                {
                    foreach (var y in mappedHeads)
                        nodeLabel += string.Format("\nHead of \"{0}\"", y.Name);
                }
                
                GraphNode node = new GraphNode(x.Object, nodeLabel, nodeColor);
                definitions.Add(node);
                if (x != null)
                {
                    foreach (var y in x.Links)
                    {
                        if (result.Lookup.ContainsKey(y.Source))
                        {
                            Tuple<Version, int> source = result.Lookup[y.Source];
                            int linkToID = source.Item2;
                            Color linkColor = branchInfo.Item1;
                            string linkLabel = $"\"{branchInfo.Item2.Name}\"";

                            GraphLink link = new GraphLink(source.Item2, node, linkLabel, linkColor, y.Merge);
                            links.Add(link);
                        }
                    }
                }
            }

            Graph<GraphNode> graph = new Graph<GraphNode>();
            definitions.ForEach(x => graph.AddVertex(x));

            links.ForEach(x =>
            {
                GraphNode fromNode = definitions[x.FromVersionID];
                x.FromVersion = fromNode;
                Debug.Assert(fromNode != x.ToVersion);
                Debug.Assert(fromNode != null);
                Debug.Assert(x.ToVersion != null);
                
                GraphEdge edge = new GraphEdge(x);
                graph.AddEdge(edge);
            });
            return graph;
        }
    }
}
