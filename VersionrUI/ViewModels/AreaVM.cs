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
    public class AreaVM : NotifyPropertyChangedBase
    {
        private Area _area;
        private string _name;

        public AreaVM(Area area, string name)
        {
            _area = area;
            _name = name;
            Branches = new ObservableCollection<BranchVM>();
            Remotes = new ObservableCollection<RemoteConfig>();

            PullCommand = new DelegateCommand(Pull);
            PushCommand = new DelegateCommand(Push);

            RefreshBranches();

            foreach (RemoteConfig remote in _area.GetRemotes())
                Remotes.Add(remote);
            SelectedRemote = Remotes.FirstOrDefault();
        }

        private void RefreshBranches()
        {
            Branches.Clear();

            foreach (Versionr.Objects.Branch branch in _area.Branches)
            {
                Branches.Add(VersionrVMFactory.GetBranchVM(_area, branch));
            }
        }

        public Area Area { get { return _area; } }

        public DirectoryInfo Directory
        {
            get { return _area.Root; }
        }
        
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

        public ObservableCollection<BranchVM> Branches { get; private set; }
        public ObservableCollection<RemoteConfig> Remotes { get; private set; }
        
        public RemoteConfig SelectedRemote { get; set; }

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

        public StatusVM GetStatus()
        {
            // Assume the active directory is the root of the Area
            DirectoryInfo activeDirectory = _area.Root;
            return VersionrVMFactory.GetStatusVM(_area.GetStatus(activeDirectory), this);
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
