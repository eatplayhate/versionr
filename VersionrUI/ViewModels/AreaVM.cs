using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Versionr;
using Versionr.LocalState;
using Versionr.Network;
using VersionrUI.Commands;

namespace VersionrUI.ViewModels
{
    public enum AreaInitMode
    {
        InitNew,
        UseExisting,
        Clone,
    }

    public class AreaVM : NotifyPropertyChangedBase
    {

        private Area _area;
        private string _name;
        private ObservableCollection<BranchVM> _branches;
        private ObservableCollection<RemoteConfig> _remotes;
        private StatusVM _status;

        public AreaVM(string path, string name, AreaInitMode areaInitMode)
        {
            PullCommand = new DelegateCommand(Pull);
            PushCommand = new DelegateCommand(Push);

            _name = name;

            switch (areaInitMode)
            {
                case AreaInitMode.Clone:
                    // Spawn another dialog for the source (or put it in the Clone New button)
                    throw new NotImplementedException("// TODO: AreaInitMode.Clone");
                // break;
                case AreaInitMode.InitNew:
                    // Tell versionr to initialize at path
                    _area = Area.Init(new DirectoryInfo(path), name);
                    break;
                case AreaInitMode.UseExisting:
                    // Add it to settings and refresh UI, get status etc.
                    _area = Area.Load(new DirectoryInfo(path));
                    break;
            }
        }

        public Area Area { get { return _area; } }

        public DirectoryInfo Directory { get { return _area.Root; } }

        public string Name
        {
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

        public ObservableCollection<RemoteConfig> Remotes
        {
            get
            {
                if (_remotes == null)
                    Load(() => RefreshRemotes());
                return _remotes;
            }
        }

        public RemoteConfig SelectedRemote { get; set; }

        public CompositeCollection Children
        {
            get
            {
                CompositeCollection collection = new CompositeCollection();
                collection.Add(Status);
                collection.Add(new NamedCollection("Branches", Branches));
                return collection;
            }
        }

        #region Commands
        public DelegateCommand PullCommand { get; private set; }
        public DelegateCommand PushCommand { get; private set; }

        private void Pull()
        {
            ExecuteClientCommand((c) => c.Pull(true, null), "pull");
            _area.Update();
        }

        private void Push()
        {
            ExecuteClientCommand((c) => c.Push(), "push", true);
        }
        #endregion

        private ObservableCollection<BranchVM> Branches
        {
            get
            {
                if (_branches == null)
                    Load(() => RefreshStatusAndBranches());
                return _branches;
            }
        }

        private StatusVM Status
        {
            get
            {
                if (_status == null)
                    Load(() => RefreshStatusAndBranches());
                return _status;
            }
        }

        private void RefreshStatusAndBranches()
        {
            if (_status == null)
            {
                // Assume the active directory is the root of the Area
                _status = VersionrVMFactory.GetStatusVM(this);
            }

            if (_branches == null)
                _branches = new ObservableCollection<BranchVM>();
            else
                _branches.Clear();
            foreach (Versionr.Objects.Branch branch in _area.Branches)
                _branches.Add(VersionrVMFactory.GetBranchVM(_area, branch));

            NotifyPropertyChanged("Children");
        }

        private void RefreshRemotes()
        {
            if (_remotes == null)
                _remotes = new ObservableCollection<RemoteConfig>();
            else
                _remotes.Clear();
            foreach (RemoteConfig remote in _area.GetRemotes())
                _remotes.Add(remote);

            if (SelectedRemote == null || !_remotes.Contains(SelectedRemote))
                SelectedRemote = _remotes.FirstOrDefault();

            NotifyPropertyChanged("Remotes");
            NotifyPropertyChanged("SelectedRemote");
        }

        public void ExecuteClientCommand(Action<Client> action, string command, bool requiresWriteAccess = false)
        {
            if (SelectedRemote != null)
            {
                Client client = new Client(_area);
                if (client.Connect(SelectedRemote.Host, SelectedRemote.Port, SelectedRemote.Module, requiresWriteAccess))
                    client.Pull(true, null);
                else
                    MessageBox.Show(string.Format("Couldn't connect to remote {0} while processing {1} command!", SelectedRemote.Host, command), "Command Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                client.Close();
            }
        }
    }
}
