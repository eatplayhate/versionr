using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Versionr;
using Versionr.LocalState;
using Versionr.Network;
using VersionrUI.Commands;
using VersionrUI.Dialogs;

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
        public static AreaVM Create(string name, string path, Action<AreaVM, string, string> afterInit, AreaInitMode areaInitMode, string host = null, int port = 0)
        {
            AreaVM area = new AreaVM(name);
            area.Init(path, areaInitMode, host, port, afterInit);
            return area;
        }

        public DelegateCommand RefreshCommand { get; private set; }
        public DelegateCommand<NotifyPropertyChangedBase> SelectViewCommand { get; private set; }
        public DelegateCommand OpenInExplorerCommand { get; private set; }

        private Area _area;
        private string _name;
        private List<BranchVM> _branches;
        private List<RemoteConfig> _remotes;
        private StatusVM _status;
        private GraphVM _graph;
        public SettingsVM _settings;
        public NotifyPropertyChangedBase _selectedVM = null;
        public BranchVM _selectedBranch = null;
        public RemoteConfig _selectedRemote = null;

        private AreaVM(string name)
        {
            RefreshCommand = new DelegateCommand(() => Load(RefreshAll));
            SelectViewCommand = new DelegateCommand<NotifyPropertyChangedBase>((x) => SelectedVM = x);
            OpenInExplorerCommand = new DelegateCommand(OpenInExplorer);
            _name = name;
        }

        public void Init(string path, AreaInitMode areaInitMode, string host, int port, Action<AreaVM, string, string> afterInit)
        {
            Load(() =>
            {
                DirectoryInfo dir = new DirectoryInfo(path);
                string title = String.Empty;
                string message = String.Empty;
                switch (areaInitMode)
                {
                    case AreaInitMode.Clone:
                        OperationStatusDialog.Start("Clone");
                        Client client = new Client(dir);
                    	if (client.Connect(Client.ToVersionrURL(host, port, null), true))
                        {
                            bool result = client.Clone(true);
                            if (!result)
                                result = client.Clone(false);
                            if (result)
                            {
                                string remoteName = "default";
                            	client.Workspace.SetRemote(Client.ToVersionrURL(client.Host, client.Port, client.Module), remoteName);
                                client.Pull(false, client.Workspace.CurrentBranch.ID.ToString());
                                _area = Area.Load(client.Workspace.Root);
                                _area.Checkout(null, false, false);
                            }
                        }
                        else
                        {
                            title = "Clone Failed";
                            message = String.Format("Couldn't connect to {0}:{1}", host, port);
                        }
                        client.Close();
                        OperationStatusDialog.Finish();
                        break;
                    case AreaInitMode.InitNew:
                        // Tell versionr to initialize at path
                        try
                        {
                            dir.Create();
                        }
                        catch
                        {
                            title = "Init Failed";
                            message = String.Format("Couldn't create subdirectory \"{0}\"", dir.FullName);
                            break;
                        }
                        _area = Area.Init(dir, _name);
                        break;
                    case AreaInitMode.UseExisting:
                        // Add it to settings and refresh UI, get status etc.
                        _area = Area.Load(dir);
                        if(_area == null)
                        {
                            title = "Missing workspace";
                            message = String.Format("Failed to load \"{0}\". The location {1} may be have been removed.", _name, path);
                        }
                        break;
                }
                RefreshAll();
                NotifyPropertyChanged(nameof(Directory));
                NotifyPropertyChanged(nameof(IsValid));
                NotifyPropertyChanged(nameof(Remotes));
                NotifyPropertyChanged(nameof(Status));
                NotifyPropertyChanged(nameof(Settings));
                NotifyPropertyChanged(nameof(Branches));
                MainWindow.Instance.Dispatcher.Invoke(afterInit, this, title, message);
            });
        }

        public NotifyPropertyChangedBase SelectedVM
        {
            get { return _selectedVM; }
            set
            {
                if (_selectedVM != value)
                {
                    _selectedVM = value;
                    NotifyPropertyChanged(nameof(SelectedVM));
                    NotifyPropertyChanged(nameof(IsStatusSelected));
                    NotifyPropertyChanged(nameof(IsHistorySelected));
                    NotifyPropertyChanged(nameof(IsGraphSelected));
                }
            }
        }

        public BranchVM SelectedBranch
        {
            get { return _selectedBranch; }
            set
            {
                if (_selectedBranch != value)
                {
                    _selectedBranch = value;

                    if (IsHistorySelected)
                        SelectedVM = _selectedBranch;

                    NotifyPropertyChanged(nameof(SelectedBranch));
                }
            }
        }

        public Area Area { get { return _area; } }

        public bool IsValid { get { return _area != null; } }

        public DirectoryInfo Directory { get { return _area?.Root; } }

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    NotifyPropertyChanged(nameof(Name));
                }
            }
        }

        public bool IsStatusSelected
        {
            get { return SelectedVM == Status; }
            set { SelectedVM = Status; }
        }

        public bool IsHistorySelected
        {
            get { return SelectedVM is BranchVM; }
            set { SelectedVM = SelectedBranch; }
        }

        public bool IsGraphSelected
        {
            get { return SelectedVM == Graph; }
            set { SelectedVM = Graph; }
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

        public RemoteConfig SelectedRemote
        {
            get { return _selectedRemote; }
            set
            {
                if (_selectedRemote != value)
                {
                    _selectedRemote = value;
                    NotifyPropertyChanged(nameof(SelectedRemote));
                }
            }
        }

        public IEnumerable<BranchVM> Branches
        {
            get
            {
                if (_branches == null)
                    Load(RefreshBranches);
                return _branches;
            }
        }

        public StatusVM Status
        {
            get
            {
                if (_status == null)
                    Load(RefreshStatus);
                return _status;
            }
        }

        public GraphVM Graph
        {
            get
            {
                if (_graph == null)
                    Load(RefreshGraph);
                return _graph;
            }
        }

        public SettingsVM Settings
        {
            get
            {
                if (_settings == null)
                    Load(RefreshSettings);
                return _settings;
            }
        }

        private static object refreshStatusLock = new object();
        public void RefreshStatus()
        {
            lock (refreshStatusLock)
            {
                if (_status == null)
                    _status = new StatusVM(this);
                else
                    _status.Refresh();

                NotifyPropertyChanged(nameof(Status));

                // Make sure something is displayed
                if (SelectedVM == null)
                    SelectedVM = _status;
            }
        }

        private static object refreshSettingsLock = new object();
        public void RefreshSettings()
        {
            if (_area != null)
            {
                lock (refreshSettingsLock)
                {
                    _settings = new SettingsVM(_area);
                    NotifyPropertyChanged(nameof(Settings));
                }
            }
        }

        private static readonly object refreshBranchesLock = new object();
        public void RefreshBranches()
        {
            if (_area != null)
            {
                lock (refreshBranchesLock)
                {
                    _branches = _area.Branches.Select(x => new BranchVM(this, x)).OrderBy(x => !x.IsCurrent)
                        .ThenBy(x => x.IsDeleted).ThenBy(x => x.Name).ToList();

                    /*
                     *  By default WPF compares SelectedItem to each item in the ItemsSource by reference, 
                     *  meaning that unless the SelectedItem points to the same item in memory as the ItemsSource item, 
                     *  it will decide that the item doesn’t exist in the ItemsSource and so no item gets selected.
                     *  The next line is a workaround for that
                     */
                    SelectedBranch = _branches.FirstOrDefault(x => SelectedBranch?.Name == x.Name);

                    NotifyPropertyChanged(nameof(Branches));

                    // Make sure something is displayed
                    if (SelectedBranch == null)
                        SelectedBranch = Branches.First();
                }
            }
        }

        private static readonly object refreshGraphLock = new object();
        public void RefreshGraph()
        {
            if (_area != null)
            {
                lock (refreshGraphLock)
                {
                    if(_graph == null)
                        _graph = new GraphVM(this);

                    // Make sure something is displayed
                    if (SelectedVM == null)
                        SelectedVM = _graph;

                    NotifyPropertyChanged(nameof(Graph));
                }
            }
        }

        private static readonly object refreshRemotesLock = new object();
        public void RefreshRemotes()
        {
            if (_area != null)
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

                    NotifyPropertyChanged(nameof(Remotes));
                }
            }
        }

        private void RefreshAll()
        {
            RefreshRemotes();
            RefreshStatus();
            RefreshSettings();
            RefreshBranches();
        }

        private void OpenInExplorer()
        {
            ProcessStartInfo si = new ProcessStartInfo("explorer");
            si.Arguments = "/e /root,\"" + Directory.FullName + "\"";
            Process.Start(si);
        }

        public void ExecuteClientCommand(Action<Client> action, string command, bool requiresWriteAccess = false)
        {
            if (_area != null && SelectedRemote != null)
            {
                Client client = new Client(_area);
                try
                {
                    if (client.Connect(Client.ToVersionrURL(SelectedRemote.Host, SelectedRemote.Port, SelectedRemote.Module), requiresWriteAccess))
                        action.Invoke(client);
                    else
                        OperationStatusDialog.Write(String.Format("Couldn't connect to remote {0}:{1} while processing {2} command!", SelectedRemote.Host, SelectedRemote.Port, command));
                }
                catch { }
                finally
                {
                    client.Close();
                }
            }
            else
            {
                OperationStatusDialog.Write("No remote selected");
            }
        }

        internal void SetStaged(List<StatusEntryVM> entries, bool staged)
        {
            if(_status != null)
                _status.SetStaged(entries, staged);
        }
        
        internal async void CreateBranch()
        {
            if (_area != null)
            {
                MetroDialogSettings dialogSettings = new MetroDialogSettings() { ColorScheme = MainWindow.DialogColorScheme };

                string branchName = await MainWindow.Instance.ShowInputAsync("Branching from " + _area.CurrentBranch.Name, "Enter a name for the new branch", dialogSettings);

                if (branchName == null) // User pressed cancel
                    return;

                if (!System.Text.RegularExpressions.Regex.IsMatch(branchName, "^\\w+$"))
                {
                    await MainWindow.ShowMessage("Couldn't create branch", "Branch name is invalid");
                    return;
                }

                // Branching is quick, so we probably don't need status messages. Commented for now...

                //Load(() =>
                //{
                //    OperationStatusDialog.Start("Creating Branch");
                _area.Branch(branchName);
                RefreshAll();
                SelectedBranch = Branches.First();
                //    OperationStatusDialog.Finish();
                //});
            }
        }
    }
}
