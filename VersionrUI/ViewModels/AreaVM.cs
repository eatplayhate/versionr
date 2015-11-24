using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Versionr;

namespace VersionrUI.ViewModels
{
    public class AreaVM : NotifyPropertyChangedBase
    {
        private Area _area;
        private string _name;
        private ObservableCollection<BranchVM> _branches;
        private BranchVM _selectedBranch = null;

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

            if (!_branches.Contains(_selectedBranch))
                _selectedBranch = _branches.FirstOrDefault();
        }

        public string Name
        {
            // TODO:
            get { return _name; }
            set { _name = value; }
        }

        public BranchVM SelectedBranch
        {
            get { return _selectedBranch; }
            set
            {
                if (_selectedBranch != value)
                {
                    _selectedBranch = value;
                    NotifyPropertyChanged("SelectedBranch");
                }
            }
        }

        public ObservableCollection<BranchVM> Branches
        {
            get { return _branches; }
        }
        public StatusVM GetStatus()
        {
            // Assume the active directory is the root of the Area
            DirectoryInfo activeDirectory = _area.Root;
            return VersionrVMFactory.GetStatusVM(_area.GetStatus(activeDirectory));
        }

    }
}
