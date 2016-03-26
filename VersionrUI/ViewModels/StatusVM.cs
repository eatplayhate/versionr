using MahApps.Metro.Controls.Dialogs;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Versionr;
using VersionrUI.Commands;
using VersionrUI.Dialogs;

namespace VersionrUI.ViewModels
{
    public class StatusVM : NotifyPropertyChangedBase
    {
        public DelegateCommand RefreshCommand { get; private set; }
        public DelegateCommand CommitCommand { get; private set; }

        private Status _status;
        private AreaVM _areaVM;
        private List<StatusEntryVM> _elements;
        private bool _pushOnCommit;
        private string _commitMessage;

        public StatusVM(AreaVM areaVM)
        {
            _areaVM = areaVM;

            RefreshCommand = new DelegateCommand(() => Load(Refresh));
            CommitCommand = new DelegateCommand(Commit);
        }

        public Status Status
        {
            get
            {
                if (_status == null)
                    Load(Refresh);
                return _status;
            }
        }
        
        public string Name
        {
            get { return "Status"; }
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

        private static object refreshLock = new object();
        public void Refresh()
        {
            lock (refreshLock)
            {
                _status = _areaVM.Area.GetStatus(_areaVM.Area.Root);
                _elements = new List<StatusEntryVM>();

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
                NotifyPropertyChanged("AllStaged");
            }
        }

        private void StatusVM_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsStaged")
                NotifyPropertyChanged("AllStaged");
        }

        public List<StatusEntryVM> Elements
        {
            get
            {
                if (_elements == null)
                    Load(Refresh);
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
                MetroDialogSettings settings = new MetroDialogSettings()
                {
                    ColorScheme = MainWindow.DialogColorScheme
                };
                MainWindow.Instance.ShowMessageAsync("Not so fast...", "Please provide a commit message", MessageDialogStyle.Affirmative, settings);
                return;
            }

            OperationStatusDialog.Start("Commit");
            bool commitSuccessful = _areaVM.Area.Commit(CommitMessage, false);
            
            if (commitSuccessful && PushOnCommit)
                _areaVM.ExecuteClientCommand((c) => c.Push(), "push", true);
            OperationStatusDialog.Finish();

            CommitMessage = string.Empty;
            Refresh();
        }
    }
}
