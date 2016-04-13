﻿using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Versionr;
using Versionr.LocalState;
using Versionr.Network;
using Versionr.Objects;
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
        public DelegateCommand RefreshCommand { get; private set; }
        public DelegateCommand<NotifyPropertyChangedBase> SelectViewCommand { get; private set; }
        public DelegateCommand OpenInExplorerCommand { get; private set; }

        private Area _area;
        private string _name;
        private List<BranchVM> _branches;
        private List<RemoteConfig> _remotes;
        private StatusVM _status;
        public SettingsVM _settings;
        public NotifyPropertyChangedBase _selectedVM = null;
        public BranchVM _selectedBranch = null;
        public RemoteConfig _selectedRemote = null;

        public AreaVM(string path, string name, AreaInitMode areaInitMode, string host = null, int port = 0)
        {
            RefreshCommand = new DelegateCommand(() => Load(RefreshAll));
            SelectViewCommand = new DelegateCommand<NotifyPropertyChangedBase>((x) => SelectedVM = x);
            OpenInExplorerCommand = new DelegateCommand(OpenInExplorer);

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
                        MainWindow.ShowMessage("Clone Failed", String.Format("Couldn't connect to {0}:{1}", host, port));
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
                        MainWindow.ShowMessage("Init Failed", String.Format("Couldn't create subdirectory \"{0}\"", dir.FullName));
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

        public NotifyPropertyChangedBase SelectedVM
        {
            get { return _selectedVM; }
            set
            {
                if (_selectedVM != value)
                {
                    _selectedVM = value;
                    NotifyPropertyChanged("SelectedVM");
                    NotifyPropertyChanged("IsStatusSelected");
                    NotifyPropertyChanged("IsHistorySelected");
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

                    NotifyPropertyChanged("SelectedBranch");
                }
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
                    NotifyPropertyChanged("SelectedRemote");
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

                NotifyPropertyChanged("Status");

                // Make sure something is displayed
                if (SelectedVM == null)
                    SelectedVM = _status;
            }
        }

        private static object refreshSettingsLock = new object();
        public void RefreshSettings()
        {
            lock (refreshSettingsLock)
            {
                _settings = new SettingsVM(_area);
                NotifyPropertyChanged("Settings");
            }
        }

        private static object refreshBranchesLock = new object();
        public void RefreshBranches()
        {
            lock (refreshBranchesLock)
            {
                _branches = _area.Branches.Select(x => new BranchVM(this, x)).OrderBy(x => !x.IsCurrent).ThenBy(x => x.IsDeleted).ThenBy(x => x.Name).ToList();
                
                NotifyPropertyChanged("Branches");

                // Make sure something is displayed
                if (SelectedBranch == null)
                    SelectedBranch = Branches.First();
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

                NotifyPropertyChanged("Remotes");

                if (SelectedRemote == null || !_remotes.Contains(SelectedRemote))
                    SelectedRemote = _remotes.FirstOrDefault();
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
            if (SelectedRemote != null)
            {
                Client client = new Client(_area);
                try
                {
                    if (client.Connect(SelectedRemote.Host, SelectedRemote.Port, SelectedRemote.Module, requiresWriteAccess))
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
    }
}
