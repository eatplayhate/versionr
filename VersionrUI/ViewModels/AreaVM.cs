using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Versionr;
using Versionr.LocalState;
using Versionr.Network;
using VersionrUI.Commands;
using VersionrUI.Controls;
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
        public DelegateCommand<ObservableCollection<AreaVM>> RemoveRepoFromListCommand { get; private set; }

        private string m_Name;
        private List<BranchVM> m_Branches;
        private List<RemoteConfig> m_Remotes;
        private StatusVM m_Status;
        private GraphVM m_Graph;
        public SettingsVM m_Settings;
        public NotifyPropertyChangedBase m_SelectedVM = null;
        public BranchVM m_SelectedBranch = null;
        public RemoteConfig m_SelectedRemote = null;

        private AreaVM(string name)
        {
            RefreshCommand = new DelegateCommand(() => Load(RefreshAll));
            SelectViewCommand = new DelegateCommand<NotifyPropertyChangedBase>((x) => SelectedVM = x);
            OpenInExplorerCommand = new DelegateCommand(OpenInExplorer);
            RemoveRepoFromListCommand = new DelegateCommand<ObservableCollection<AreaVM>>(RemoveRepoFromList);
            m_Name = name;
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
                                Area = Area.Load(client.Workspace.Root);
                                Area.Checkout(null, false, false);
                            }
                        }
                        else
                        {
                            title = "Clone Failed";
                            message = $"Couldn't connect to {host}:{port}";
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
                            message = $"Couldn't create subdirectory \"{dir.FullName}\"";
                            break;
                        }
                        Area = Area.Init(dir, m_Name);
                        break;
                    case AreaInitMode.UseExisting:
                        // Add it to settings and refresh UI, get status etc.
                        Area = Area.Load(dir);
                        if(Area == null)
                        {
                            title = "Missing workspace";
                            message = $"Failed to load \"{m_Name}\". The location {path} may be have been removed.";
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
            get { return m_SelectedVM; }
            set
            {
                if (m_SelectedVM == value)
                    return;
                m_SelectedVM = value;
                NotifyPropertyChanged(nameof(SelectedVM));
                NotifyPropertyChanged(nameof(IsStatusSelected));
                NotifyPropertyChanged(nameof(IsHistorySelected));
                NotifyPropertyChanged(nameof(IsGraphSelected));
            }
        }

        public BranchVM SelectedBranch
        {
            get { return m_SelectedBranch; }
            set
            {
                if (m_SelectedBranch == value)
                    return;
                m_SelectedBranch = value;

                if (IsHistorySelected)
                    SelectedVM = m_SelectedBranch;
                    
                NotifyPropertyChanged(nameof(SelectedBranch));
            }
        }

        public Area Area { get; private set; }

        public bool IsValid => Area != null;

        public DirectoryInfo Directory => Area?.Root;

        public string Name
        {
            get { return m_Name; }
            set
            {
                if (m_Name == value)
                    return;
                m_Name = value;
                NotifyPropertyChanged(nameof(Name));
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
            set
            {
                if (!GraphVM.IsGraphVizInstalled())
                {
                    MainWindow.Instance.Dispatcher.Invoke(() =>
                    {
                        MainWindow.ShowMessage("GraphViz Version 2.38 Missing",
                            @"Please install GraphViz from https://graphviz.gitlab.io/ to use this feature. Please ensure that is added to PATH",
                            MessageDialogStyle.Affirmative);
                    });
                    return;
                }
                SelectedVM = Graph;
            }
        }

        public List<RemoteConfig> Remotes
        {
            get
            {
                if (m_Remotes == null)
                    Load(RefreshRemotes);
                return m_Remotes;
            }
        }

        public RemoteConfig SelectedRemote
        {
            get { return m_SelectedRemote; }
            set
            {
                if (m_SelectedRemote == value)
                    return;
                m_SelectedRemote = value;
                NotifyPropertyChanged(nameof(SelectedRemote));
            }
        }

        public IEnumerable<BranchVM> Branches
        {
            get
            {
                if (m_Branches == null)
                    Load(RefreshBranches);
                return m_Branches;
            }
        }

        public StatusVM Status
        {
            get
            {
                if (m_Status == null)
                    Load(RefreshStatus);
                return m_Status;
            }
        }

        public GraphVM Graph
        {
            get
            {
                if (m_Graph == null)
                    Load(RefreshGraph);
                return m_Graph;
            }
        }

        public SettingsVM Settings
        {
            get
            {
                if (m_Settings == null)
                    Load(RefreshSettings);
                return m_Settings;
            }
        }

        private static readonly object refreshStatusLock = new object();
        public void RefreshStatus()
        {
            lock (refreshStatusLock)
            {
                if (m_Status == null)
                    m_Status = new StatusVM(this);
                else
                    m_Status.Refresh();

                NotifyPropertyChanged(nameof(Status));

                // Make sure something is displayed
                if (SelectedVM == null)
                    SelectedVM = m_Status;
            }
        }

        private static readonly object refreshSettingsLock = new object();
        public void RefreshSettings()
        {
            if (Area == null)
                return;
            lock (refreshSettingsLock)
            {
                m_Settings = new SettingsVM(Area);
                NotifyPropertyChanged(nameof(Settings));
            }
        }

        private static readonly object refreshBranchesLock = new object();
        public void RefreshBranches()
        {
            if (Area == null)
                return;
            lock (refreshBranchesLock)
            {
                m_Branches = Area.Branches.Select(x => new BranchVM(this, x)).OrderBy(x => !x.IsCurrent)
                    .ThenBy(x => x.IsDeleted).ThenBy(x => x.Name).ToList();

                /*
                     *  By default WPF compares SelectedItem to each item in the ItemsSource by reference, 
                     *  meaning that unless the SelectedItem points to the same item in memory as the ItemsSource item, 
                     *  it will decide that the item doesn’t exist in the ItemsSource and so no item gets selected.
                     *  The next line is a workaround for that
                     */
                SelectedBranch = m_Branches.FirstOrDefault(x => SelectedBranch?.Name == x.Name);

                NotifyPropertyChanged(nameof(Branches));

                // Make sure something is displayed
                if (SelectedBranch == null)
                    SelectedBranch = Branches.First();
            }
        }

        private static readonly object refreshGraphLock = new object();
        public void RefreshGraph()
        {
            if (Area == null)
                return;
            lock (refreshGraphLock)
            {
                if(m_Graph == null)
                    m_Graph = new GraphVM(this);

                // Make sure something is displayed
                if (SelectedVM == null)
                    SelectedVM = m_Graph;

                NotifyPropertyChanged(nameof(Graph));
            }
        }

        private static readonly object refreshRemotesLock = new object();
        public void RefreshRemotes()
        {
            if (Area == null)
                return;
            lock (refreshRemotesLock)
            {
                // Refresh remotes
                List<RemoteConfig> remotes = Area.GetRemotes();
                m_Remotes = new List<RemoteConfig>();

                foreach (RemoteConfig remote in remotes)
                    m_Remotes.Add(remote);

                if (SelectedRemote == null || !m_Remotes.Contains(SelectedRemote))
                    SelectedRemote = m_Remotes.FirstOrDefault();

                NotifyPropertyChanged(nameof(Remotes));
            }
        }

        private void RefreshAll()
        {
            RefreshRemotes();
            RefreshStatus();
            RefreshSettings();
            RefreshBranches();
            RefreshGraph();
        }

        private void OpenInExplorer()
        {
            ProcessStartInfo si = new ProcessStartInfo("explorer");
            si.Arguments = "/e /root,\"" + Directory.FullName + "\"";
            Process.Start(si);
        }

        private void RemoveRepoFromList(ObservableCollection<AreaVM> areas)
        {
            AreaVM areaToRemove = areas?.FirstOrDefault(area => area.Directory.FullName == Directory.FullName);
            if (areaToRemove != null)
                areas.Remove(areaToRemove);
            // Update on disk
            VersionrPanel.SaveOpenAreas(areas);
        }

        public void ExecuteClientCommand(Action<Client> action, string command, bool requiresWriteAccess = false)
        {
            if (Area != null && SelectedRemote != null)
            {
                Client client = new Client(Area);
                try
                {
                    if (client.Connect(Client.ToVersionrURL(SelectedRemote.Host, SelectedRemote.Port, SelectedRemote.Module), requiresWriteAccess))
                        action.Invoke(client);
                    else
                        OperationStatusDialog.Write(
                            $"Couldn't connect to remote {SelectedRemote.Host}:{SelectedRemote.Port} while processing {command} command!");
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
            m_Status?.SetStaged(entries, staged);
        }
        
        internal async void CreateBranch()
        {
            if (Area == null)
                return;
            MetroDialogSettings dialogSettings = new MetroDialogSettings() { ColorScheme = MainWindow.DialogColorScheme };

            string branchName = await MainWindow.Instance.ShowInputAsync("Branching from " + Area.CurrentBranch.Name, "Enter a name for the new branch", dialogSettings);

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
            Area.Branch(branchName);
            RefreshAll();
            SelectedBranch = Branches.First();
            //    OperationStatusDialog.Finish();
            //});
        }
    }
}
