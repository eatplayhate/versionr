using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Data;
using Versionr;

namespace VersionrUI.ViewModels
{
    public class AreaVM : NotifyPropertyChangedBase
    {
        private Area _area;
        private string _name;
        private ObservableCollection<BranchVM> _branches;

        public AreaVM(Area area, string name)
        {
            _area = area;
            _name = name;
            _branches = new ObservableCollection<BranchVM>();

            RefreshBranches();
        }

        private void RefreshBranches()
        {
            _branches.Clear();

            foreach (Versionr.Objects.Branch branch in _area.Branches)
            {
                _branches.Add(VersionrVMFactory.GetBranchVM(_area, branch));
            }
        }

        public string Name
        {
            // TODO:
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    NotifyPropertyChanged("Name");
                }
            }
        }

        public ObservableCollection<BranchVM> Branches
        {
            get { return _branches; }
        }

        public CompositeCollection Children
        {
            get
            {
                CompositeCollection collection = new CompositeCollection();
                collection.Add(GetStatus());
                collection.Add(new NamedCollection("Branches", Branches));
                return collection;
            }
        }

        public StatusVM GetStatus()
        {
            // Assume the active directory is the root of the Area
            DirectoryInfo activeDirectory = _area.Root;
            return VersionrVMFactory.GetStatusVM(_area.GetStatus(activeDirectory), _area);
        }
    }
}
