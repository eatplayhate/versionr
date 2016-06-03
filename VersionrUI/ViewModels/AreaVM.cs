using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Versionr;
using Versionr.LocalState;
using Versionr.Network;
using Versionr.Objects;
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
        private List<BranchVM> _branches;
        private List<RemoteConfig> _remotes;
        private StatusVM _status;

        public AreaVM(string path, string name, AreaInitMode areaInitMode, string host = null, int port = 0)
        {
            RefreshCommand = new DelegateCommand(() => Load(() => { RefreshRemotes(); RefreshChildren(); }));
            PullCommand = new DelegateCommand(() => Load(Pull));
            PushCommand = new DelegateCommand(() => Load(Push));

            _name = name;

            DirectoryInfo dir = new DirectoryInfo(path);
            switch (areaInitMode)
            {
                case AreaInitMode.Clone:
                    // Spawn another dialog for the source (or put it in the Clone New button)
                    Client client = new Client(dir);
                    if (client.Connect(host, port, null, true))
                    {
                        bool result = client.Clone(true);
                        if (!result)
                            result = client.Clone(false);
                        if (result)
                        {
                            string remoteName = "default";
                            client.Workspace.SetRemote(client.Host, client.Port, client.Module, remoteName);
                            client.Pull(false, client.Workspace.CurrentBranch.ID.ToString());
                            _area = Area.Load(client.Workspace.Root);
                            _area.Checkout(null, false, false);
                            client.SyncRecords();
                        }
                    }
                    else
                    {
                        MessageBox.Show(string.Format("Couldn't connect to {0}:{1}", host, port), "Clone Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    client.Close();
                    break;
                case AreaInitMode.InitNew:
                    // Tell versionr to initialize at path
                    try
                    {
                        dir.Create();
                    }
                    catch
                    {
                        MessageBox.Show("Error - couldn't create subdirectory \"{0}\"", dir.FullName);
                        break;
                    }
                    _area = Area.Init(dir, name);
                    break;
                case AreaInitMode.UseExisting:
                    // Add it to settings and refresh UI, get status etc.
                    _area = Area.Load(dir);
                    break;
            }
        }

        public Area Area { get { return _area; } }

        public bool IsValid { get { return _area != null; } }

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

        public List<RemoteConfig> Remotes
        {
            get
            {
                if (_remotes == null)
                    Load(RefreshRemotes);
                return _remotes;
            }
        }

        public RemoteConfig SelectedRemote { get; set; }

        public CompositeCollection Children
        {
            get
            {
                if (_branches == null || _status == null)
                    Load(RefreshChildren);

                CompositeCollection collection = new CompositeCollection();
                collection.Add(_status);
                collection.Add(new NamedCollection("Branches", _branches));
                return collection;
            }
        }

        #region Commands
        public DelegateCommand RefreshCommand { get; private set; }
        public DelegateCommand PullCommand { get; private set; }
        public DelegateCommand PushCommand { get; private set; }

        private void Pull()
        {
            ExecuteClientCommand((c) => c.Pull(true, null), "pull");
            _area.Update(new Area.MergeSpecialOptions());
        }

        private void Push()
        {
            ExecuteClientCommand((c) => c.Push(), "push", true);
        }

        private static object refreshChildrenLock = new object();
        public void RefreshChildren()
        {
            lock (refreshChildrenLock)
            {
                // Refresh status
                if (_status == null)
                {
                    // Assume the active directory is the root of the Area
                    _status = new StatusVM(this);
                }
                else
                {
                    _status.Refresh();
                }


                // Refresh branches
                IEnumerable<Branch> branches = _area.Branches.OrderBy(x => x.Terminus.HasValue).ThenBy(x => x.Name);
                _branches = new List<BranchVM>();

                foreach (Branch branch in branches)
                    _branches.Add(new BranchVM(this, branch));

                NotifyPropertyChanged("Children");
            }
        }

        private static object refreshRemotesLock = new object();
        public void RefreshRemotes()
        {
            lock (refreshRemotesLock)
            {
                // Refresh remotes
                List<RemoteConfig> remotes = _area.GetRemotes();
                _remotes = new List<RemoteConfig>();
                
                foreach (RemoteConfig remote in remotes)
                    _remotes.Add(remote);

                if (SelectedRemote == null || !_remotes.Contains(SelectedRemote))
                    SelectedRemote = _remotes.FirstOrDefault();

                NotifyPropertyChanged("Remotes");
                NotifyPropertyChanged("SelectedRemote");
            }
        }
        #endregion

        public void ExecuteClientCommand(Action<Client> action, string command, bool requiresWriteAccess = false)
        {
            if (SelectedRemote != null)
            {
                Client client = new Client(_area);
                if (client.Connect(SelectedRemote.Host, SelectedRemote.Port, SelectedRemote.Module, requiresWriteAccess))
                {
                    action.Invoke(client);
                }
                else
                {
                    MainWindow.Instance.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(string.Format("Couldn't connect to remote {0} while processing {1} command!", SelectedRemote.Host, command), "Command Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                client.Close();
            }
        }

        internal void SetStaged(List<StatusEntryVM> entries, bool staged)
        {
            if(_status != null)
                _status.SetStaged(entries, staged);
        }
    }
}
