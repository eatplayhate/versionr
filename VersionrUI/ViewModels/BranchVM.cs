using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Versionr.Objects;
using VersionrUI.Commands;
using VersionrUI.Dialogs;
using Version = Versionr.Objects.Version;

namespace VersionrUI.ViewModels
{
    public class BranchVM : NotifyPropertyChangedBase
    {
        public DelegateCommand PullCommand { get; private set; }
        public DelegateCommand PushCommand { get; private set; }
        public DelegateCommand CheckoutCommand { get; private set; }
        public DelegateCommand LogCommand { get; private set; }

        private AreaVM _areaVM;
        private Branch _branch;
        private List<VersionVM> _history = null;
        private string _searchText;
        private VersionVM _selectedVersion;
        private int _revisionLimit = 50;
        private bool _forceRefresh = false;
        private static Dictionary<int, string> _revisionLimitOptions = new Dictionary<int, string>()
        {
            { 50, "50" },
            { 100, "100" },
            { 150, "150" },
            { 200, "200" },
            { -1, "All" },
        };

        public string SearchText
        {
            get { return _searchText; }
            private set
            {
                _searchText = value;
                NotifyPropertyChanged("SearchText");
                NotifyPropertyChanged("History");
            }
        }

        public BranchVM(AreaVM areaVM, Branch branch)
        {
            _areaVM = areaVM;
            _branch = branch;

            PullCommand = new DelegateCommand(() => Load(Pull));
            PushCommand = new DelegateCommand(() => Load(Push));
            CheckoutCommand = new DelegateCommand(() => Load(Checkout), () => !IsCurrent);
            LogCommand = new DelegateCommand(Log);
        }

        public Branch Branch
        {
            get { return _branch; }
        }

        public string Name
        {
            get { return _branch.Name; }
        }

        public bool IsDeleted
        {
            get { return _branch.Terminus.HasValue; }
        }

        public bool IsCurrent
        {
            get { return _areaVM.Area.CurrentBranch.ID == _branch.ID; }
        }

        public List<VersionVM> History
        {
            get
            {
                if (_history == null || _forceRefresh)
                    Load(Refresh);
                else
                    ResolveGraph();
                if (!string.IsNullOrEmpty(SearchText))
                    return FilterHistory(_history, SearchText);
                return _history;
            }
        }

        public VersionVM SelectedVersion
        {
            get { return _selectedVersion; }
            private set
            {
                _selectedVersion = value;
                NotifyPropertyChanged("SelectedVersion");
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
                    NotifyPropertyChanged("RevisionLimit");
                    Load(Refresh);
                }
            }
        }

        public Dictionary<int, string> RevisionLimitOptions
        {
            get { return _revisionLimitOptions; }
        }

        private List<VersionVM> FilterHistory(List<VersionVM> history, string searchtext)
        {
            searchtext = searchtext.ToLower();
            List<VersionVM> results = new List<VersionVM>();
            foreach (VersionVM version in history)
            {
                if (version.Message.ToLower().Contains(searchtext) ||
                    version.ID.ToString().ToLower().Contains(searchtext) ||
                    version.Author.ToLower().Contains(searchtext) ||
                    version.Timestamp.ToString().ToLower().Contains(searchtext) ||
					version.Alterations.Any(x => x.Name.ToLower().Contains(searchtext))
				)
                {
                    results.Add(version);
                }
            }
            return results;
        }

        private static object refreshLock = new object();
        private void Refresh()
        {
            lock (refreshLock)
            {
                _forceRefresh = false;

                var headVersion = _areaVM.Area.GetBranchHeadVersion(_branch);
                int? limit = (RevisionLimit != -1) ? RevisionLimit : (int?)null;
                List<Version> versions = _areaVM.Area.GetLogicalHistory(headVersion, false, false, false, limit);
                _history = new List<VersionVM>();

                foreach (Version version in versions)
                    _history.Add(new VersionVM(version, _areaVM.Area));
                NotifyPropertyChanged("History");
            }
        }

        private void Pull()
        {
            OperationStatusDialog.Start("Pull");
            _areaVM.ExecuteClientCommand((c) => c.Pull(true, Name), "pull");
            if(IsCurrent)
                _areaVM.Area.Update(new Versionr.Area.MergeSpecialOptions());
            OperationStatusDialog.Finish();
        }

        private void Push()
        {
            OperationStatusDialog.Start("Push");
            _areaVM.ExecuteClientCommand((c) => c.Push(Name), "push", true);
            OperationStatusDialog.Finish();
        }

        private void Checkout()
        {
            if (_areaVM.Area.Status.HasModifications(false))
            {
                MessageDialogResult result = MessageDialogResult.FirstAuxiliary;
                MainWindow.Instance.Dispatcher.Invoke(async () =>
                {
                    MetroDialogSettings settings = new MetroDialogSettings()
                    {
                        AffirmativeButtonText = "Checkout (keep unversioned files)",
                        NegativeButtonText = "Checkout (purge unversioned files)",
                        FirstAuxiliaryButtonText = "Cancel",
                        ColorScheme = MainWindow.DialogColorScheme
                    };
                    result = await MainWindow.ShowMessage("Checkout", "Vault contains uncommitted changes. Do you want to force the checkout operation?", MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary, settings);
                }).Wait();

                switch (result)
                {
                    case MessageDialogResult.Affirmative:
                        DoCheckout(false);
                        break;
                    case MessageDialogResult.Negative:
                        DoCheckout(true);
                        break;
                    case MessageDialogResult.FirstAuxiliary:
                    default:
                        return;
                }
            }
            else
            {
                DoCheckout(false);
            }
        }

        private void DoCheckout(bool purge)
        {
            OperationStatusDialog.Start("Checkout");
            _areaVM.Area.Checkout(Name, purge, false, false);
            _areaVM.RefreshBranches();
            OperationStatusDialog.Finish();
        }

        private void Log()
        {
            Version headVersion = _areaVM.Area.GetBranchHeadVersion(_branch);
            LogDialog.Show(headVersion, _areaVM.Area);
        }

        public class DAG
        {
            public class Link
            {
                public Guid Source { get; set; }
                public bool Merge { get; set; }
            }
            public class ObjectAndLinks
            {
                public VersionVM Version { get; set; }
                public List<Link> Links { get; set; }

                public ObjectAndLinks(VersionVM obj)
                {
                    Version = obj;
                    Links = new List<Link>();
                }
            }

            public List<ObjectAndLinks> Objects { get; set; }
            public Dictionary<Guid, VersionVM> Lookup { get; set; }

            public DAG()
            {
                Objects = new List<ObjectAndLinks>();
                Lookup = new Dictionary<Guid, VersionVM>();
            }
        }

        private DAG GetDAG()
        {
            DAG result = new DAG();
            foreach (VersionVM version in _history)
            {
                result.Lookup[version.ID] = version;
                DAG.ObjectAndLinks initialLink = new DAG.ObjectAndLinks(version);
                result.Objects.Add(initialLink);

                if (version.Parent.HasValue)
                    initialLink.Links.Add(new DAG.Link() { Source = version.Parent.Value, Merge = false });

                IEnumerable<MergeInfo> mergeInfo = _areaVM.Area.GetMergeInfo(version.ID);
                foreach (MergeInfo info in mergeInfo)
                    initialLink.Links.Add(new DAG.Link() { Source = info.SourceVersion, Merge = true });
            }
            return result;
        }

        const int RowHeight = 25;
        const int XSpacing = 30;
        private void ResolveGraph()
        {
            var result = GetDAG();

            int index = 0;
            
            foreach (var x in result.Objects)
            {
                Tuple<Color, string, int> branchInfo = GetBranchDrawingProps(x.Version.Branch);
                x.Version.GraphNode.Color = branchInfo.Item1;
                x.Version.GraphNode.XPos = branchInfo.Item3;
                x.Version.GraphNode.YPos = index * RowHeight;

                string name = x.Version.ID.ToString().Substring(0, 8);
                name += string.Format("\n{0}", x.Version.Author);
                List<Branch> mappedHeads = _areaVM.Area.MapVersionToHeads(x.Version.ID);
                if (mappedHeads.Count > 0)
                {
                    foreach (var y in mappedHeads)
                        name += string.Format("\nHead of \"{0}\"", y.Name);
                }

                x.Version.GraphNode.Name = name;

                if (x != null)
                {
                    foreach (DAG.Link link in x.Links)
                    {
                        if (result.Lookup.ContainsKey(link.Source))
                        {
                            VersionVM sourceVM = result.Lookup[link.Source];
                            if (sourceVM != null)
                            {
                                x.Version.GraphNode.Links.Add(new Link()
                                {
                                    CurrentVersion = x.Version,
                                    SourceVersion = sourceVM,
                                    Merge = link.Merge,
                                    Color = branchInfo.Item1
                                });
                            }
                        }
                        else
                        {
                            VersionVM externalVersion = new VersionVM(_areaVM.Area.GetVersion(link.Source), _areaVM.Area);
                            Tuple<Color, string, int> externalBranchInfo = GetBranchDrawingProps(externalVersion.Branch);
                            externalVersion.GraphNode.Color = externalBranchInfo.Item1;
                            externalVersion.GraphNode.XPos = externalBranchInfo.Item3;
                            externalVersion.GraphNode.YPos = index * RowHeight;
                            externalVersion.GraphNode.Name = String.Format("{0}\n{1}\n{2}", externalVersion.ID.ToString().Substring(0, 8), externalVersion.Author, externalBranchInfo.Item2);

                            x.Version.GraphNode.ExternalVersions.Add(externalVersion);
                            x.Version.GraphNode.Links.Add(new Link()
                            {
                                CurrentVersion = x.Version,
                                SourceVersion = externalVersion,
                                Merge = link.Merge,
                                Color = externalBranchInfo.Item1
                            });
                        }
                    }
                }

                index++;
            }
        }

        private static Color[] colours = new Color[] { Colors.DarkOrange, Colors.Green, Colors.Blue, Colors.Cyan, Colors.Magenta, Colors.Red };
        private Dictionary<Guid, Tuple<Color, string, int>> branchInfoMap = new Dictionary<Guid, Tuple<Color, string, int>>();
        private Tuple<Color, string, int> GetBranchDrawingProps(Guid branchID)
        {
            Tuple<Color, string, int> branchInfo;
            if (!branchInfoMap.TryGetValue(branchID, out branchInfo))
            {
                int nextColourIndex = branchInfoMap.Count % colours.Length;
                Color colour = colours[nextColourIndex];
                branchInfo = new Tuple<Color, string, int>(colour, _areaVM.Area.GetBranch(branchID).Name, branchInfoMap.Count * XSpacing);
                branchInfoMap.Add(branchID, branchInfo);
            }
            return branchInfo;
        }
    }
}
