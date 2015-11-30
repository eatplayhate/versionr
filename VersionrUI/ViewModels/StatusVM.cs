using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Versionr;
using VersionrUI.Commands;

namespace VersionrUI.ViewModels
{
    public class StatusVM : NotifyPropertyChangedBase
    {
        public DelegateCommand RefreshCommand { get; private set; }
        public DelegateCommand CommitCommand { get; private set; }

        private Status _status;
        private AreaVM _areaVM;
        private ObservableCollection<StatusEntryVM> _elements;
        private bool _pushOnCommit;
        private string _commitMessage;

        public StatusVM(AreaVM areaVM)
        {
            _areaVM = areaVM;

            RefreshCommand = new DelegateCommand(Refresh);
            CommitCommand = new DelegateCommand(Commit);
        }

        public Status Status
        {
            get
            {
                if (_status == null)
                    Load(() => Refresh());
                return _status;
            }
        }

        public bool PushOnCommit
        {
            get { return _pushOnCommit; }
            set
            {
                if (_pushOnCommit != value)
                {
                    _pushOnCommit = value;
                    NotifyPropertyChanged("PushOnCommit");
                }
            }
        }

        public string CommitMessage
        {
            get { return _commitMessage; }
            set
            {
                if (_commitMessage != value)
                {
                    _commitMessage = value;
                    NotifyPropertyChanged("CommitMessage");
                }
            }
        }

        private object refreshLock = new object();
        public void Refresh()
        {
            lock (refreshLock)
            {
                _status = _areaVM.Area.GetStatus(_areaVM.Area.Root);

                MainWindow.Instance.Dispatcher.Invoke(() =>
                {
                    if (_elements == null)
                        _elements = new ObservableCollection<StatusEntryVM>();
                    else
                        _elements.Clear();

                    foreach (Status.StatusEntry statusEntry in Status.Elements)
                    {
                        if (statusEntry.Code != StatusCode.Masked &&
                            statusEntry.Code != StatusCode.Ignored &&
                            statusEntry.Code != StatusCode.Unchanged)
                            _elements.Add(VersionrVMFactory.GetStatusEntryVM(statusEntry, this, _areaVM.Area));
                    }

                    NotifyPropertyChanged("Status");
                    NotifyPropertyChanged("Elements");
                });
            }
        }

        public ObservableCollection<StatusEntryVM> Elements
        {
            get
            {
                if (_elements == null)
                    Load(() => Refresh());
                return _elements;
            }
        }

        private void Commit()
        {
            if (string.IsNullOrEmpty(CommitMessage))
            {
                MessageBox.Show("Please provide a commit message", "Denied", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!_areaVM.Area.Commit(CommitMessage, false))
            {
                MessageBox.Show("Could not commit as it would create a new head.", "Commit failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (PushOnCommit)
                _areaVM.ExecuteClientCommand((c) => c.Push(), "push", true);
            
            CommitMessage = string.Empty;
            Refresh();
        }
    }
}
