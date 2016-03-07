using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Versionr;
using VersionrUI.Commands;
using System.Linq;
using System.Collections.Generic;

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
                    {
                        _elements = new ObservableCollection<StatusEntryVM>();
                        _elements.CollectionChanged += _elements_CollectionChanged;
                    }
                    else
                        _elements.Clear();

                    foreach (Status.StatusEntry statusEntry in Status.Elements.OrderBy(x => x.CanonicalName))
                    {
                        if (statusEntry.Code != StatusCode.Masked &&
                            statusEntry.Code != StatusCode.Ignored &&
                            statusEntry.Code != StatusCode.Unchanged)
                        {
                            StatusEntryVM statusEntryVM = new StatusEntryVM(statusEntry, this, _areaVM.Area);
                            if (statusEntryVM != null)
                            {
                                _elements.Add(statusEntryVM);
                                statusEntryVM.PropertyChanged += StatusVM_PropertyChanged;
                            }
                        }
                    }

                    NotifyPropertyChanged("Status");
                    NotifyPropertyChanged("Elements");
                });
            }
        }

        private void StatusVM_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsStaged")
                NotifyPropertyChanged("AllStaged");
        }

        private void _elements_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
           NotifyPropertyChanged("AllStaged");
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

        public bool? AllStaged
        {
            get
            {
                if (_elements == null)
                    return false;   // whatever
                int stagedCount = _elements.Count(x => x.IsStaged);
                if (stagedCount == 0)
                    return false;
                else if (stagedCount == _elements.Count)
                    return true;
                else
                    return null;
            }
            set
            {
                bool useValue = true;
                if (!value.HasValue || value == false)
                    useValue = false;
                if (AllStaged != useValue)
                {
                    foreach (var st in _elements)
                    {
                        st.IsStaged = useValue;
                    }
                    NotifyPropertyChanged("AllStaged");
                }
            }
        }

        public void SetStaged(List<StatusEntryVM> statusEntries, bool staged)
        {   
            if (staged)
                _areaVM.Area.RecordChanges(_status, statusEntries.Select(x => x.StatusEntry).ToList(), true, false, (se, code, b) => { se.Code = code; se.Staged = true; });
            else
                _areaVM.Area.Revert(statusEntries.Select(x => x.StatusEntry).ToList(), false, false, false, (se, code) => { se.Code = code; se.Staged = false; });

            statusEntries.ForEach(x =>
            {
                x.NotifyPropertyChanged("IsStaged");
                x.NotifyPropertyChanged("Code");
            });

            NotifyPropertyChanged("AllStaged");
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
