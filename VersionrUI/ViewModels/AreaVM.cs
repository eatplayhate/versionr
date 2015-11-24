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

            // TODO consider retaining BranchVM instances
            foreach (Versionr.Objects.Branch branch in _area.Branches)
            {
                _branches.Add(new BranchVM(_area, branch));
            }
        }

        public string Name
        {
            // TODO:
            get { return _name; }
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
            return new StatusVM(_area.GetStatus(activeDirectory));
        }
    }
}
