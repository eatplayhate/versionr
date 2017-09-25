using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using System.Windows.Shapes;
using Versionr;
using Versionr.Objects;
using Version = Versionr.Objects.Version;

namespace VersionrUI.ViewModels
{
    public class Link
    {
        private Color _color;

        public VersionVM CurrentVersion { get; set; }
        public VersionVM SourceVersion { get; set; }
        public bool Merge { get; set; }
        public Color Color
        {
            get { return _color; }
            set
            {
                if (_color != value)
                {
                    _color = value;
                    Brush = new SolidColorBrush(_color);
                }
            }
        }
        public Brush Brush { get; private set; }
        public int XPos { get { return CurrentVersion.GraphNode.XPos + 6; } }
        public int SourceXOffset { get { return SourceVersion.GraphNode.XPos + 6; } }
        public int SourceYOffset { get { return SourceVersion.GraphNode.YPos - CurrentVersion.GraphNode.YPos + 6; } }
    }

    public class NodeDescrption
    {
        private Color _color;

        public NodeDescrption()
        {
            Links = new ObservableCollection<Link>();
            ExternalVersions = new ObservableCollection<VersionVM>();
        }

        public string Name { get; set; }
        public Color Color
        {
            get { return _color; }
            set
            {
                if(_color != value)
                {
                    _color = value;
                    Brush = new SolidColorBrush(_color);
                }
            }
        }
        public Brush Brush { get; private set; }
        public Color FontColor { get; set; }
        public int XPos { get; set; }
        public int YPos { get; set; }
        public ObservableCollection<Link> Links { get; private set; }
        public ObservableCollection<VersionVM> ExternalVersions { get; private set; }
    }

    public class VersionVM : NotifyPropertyChangedBase
    {
        private Version _version;
        private Area _area;
        private List<AlterationVM> _alterations;
        private string _searchText;
        
        public VersionVM(Version version, Area area)
        {
            _version = version;
            _area = area;

            GraphNode = new NodeDescrption();
        }

        public Guid ID
        {
            get { return _version.ID; }
        }

        public string ShortName
        {
            get { return _version.ShortName; }
        }

        public string Author
        {
            get { return _version.Author; }
        }

        public string Message
        {
            get { return _version.Message; }
        }

        public DateTime Timestamp
        {
            get { return _version.Timestamp.ToLocalTime(); }
        }

        public bool IsCurrent
        {
            get { return _version.ID == _area.Version.ID; }
        }

        public uint Revision
        {
            get { return _version.Revision; }
        }

        public Guid? Parent
        {
            get { return _version.Parent; }
        }

        public Guid Branch
        {
            get { return _version.Branch; }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                _searchText = value;
                NotifyPropertyChanged("SearchText");
                NotifyPropertyChanged("Alterations");
            }
        }

        public List<AlterationVM> Alterations
        {
            get
            {
                if (_alterations == null)
                    Refresh();
                if (!string.IsNullOrEmpty(_searchText))
                    return FilterAlterations(_searchText, _alterations);
                return _alterations;
            }
        }

        public List<AlterationVM> FilterAlterations(string searchtext, List<AlterationVM> alterations)
        {
            searchtext = searchtext.ToLower();
            List<AlterationVM> results = new List<AlterationVM>();
            foreach (AlterationVM alteration in alterations)
            {
                if (alteration.Name.ToLower().Contains(searchtext))
                    results.Add(alteration);
            }
            return results;
        }

        public NodeDescrption GraphNode { get; private set; }

        private static readonly object refreshLock = new object();
        private void Refresh()
        {
            lock (refreshLock)
            {
                List<Alteration> alterations = _area.GetAlterations(_version);
                _alterations = new List<AlterationVM>();
                
                List<AlterationVM> unordered = new List<AlterationVM>(alterations.Count);
                foreach (Alteration alteration in alterations)
                    unordered.Add(new AlterationVM(alteration, _area, _version));

                foreach (AlterationVM vm in unordered.OrderBy(x => x.Name))
                    _alterations.Add(vm);

                NotifyPropertyChanged("Alterations");
            }
        }
    }
}
