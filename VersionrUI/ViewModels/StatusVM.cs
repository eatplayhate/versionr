using MahApps.Metro.Controls.Dialogs;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Versionr;
using VersionrUI.Commands;
using VersionrUI.Dialogs;
using System;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace VersionrUI.ViewModels
{
    public class StatusVM : NotifyPropertyChangedBase
    {
        public DelegateCommand RefreshCommand { get; private set; }
        public DelegateCommand CommitCommand { get; private set; }
        public DelegateCommand CreateBranchCommand { get; private set; }
        public DelegateCommand<TextBox> AddTagCommand { get; private set; }
        public DelegateCommand<string> RemoveTagCommand { get; private set; }

        private Status _status;
        private AreaVM _areaVM;
        private List<StatusEntryVM> _elements;
        private List<TagPresetVM> _tagPresets;
        private bool _pushOnCommit;
        private string _commitMessage;

        public StatusVM(AreaVM areaVM)
        {
            _areaVM = areaVM;

            CustomTags = new ObservableCollection<CustomTagVM>();

            RefreshCommand = new DelegateCommand(() => Load(Refresh));
            CommitCommand = new DelegateCommand(() => Load(Commit), CanCommit);
            CreateBranchCommand = new DelegateCommand(() => _areaVM.CreateBranch());
            AddTagCommand = new DelegateCommand<TextBox>(AddTag, CanAddTag);
            RemoveTagCommand = new DelegateCommand<string>(RemoveTag);
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

        public List<TagPresetVM> TagPresets
        {
            get
            {
                if (_tagPresets == null)
                    Load(Refresh);
                return _tagPresets;
            }
        }

        public ObservableCollection<CustomTagVM> CustomTags { get; private set; }

        private List<string> GetCommitTags()
        {
            return TagPresets.Where(x => x.IsChecked).Select(x => x.Tag)
                .Union(CustomTags.Select(x => x.Tag)).ToList();
        }

        private static object refreshLock = new object();
        public void Refresh()
        {
            if (_areaVM.IsValid)
            {
                lock (refreshLock)
                {
                    _status = _areaVM.Area.GetStatus(_areaVM.Area.Root);
                    _elements = new List<StatusEntryVM>();
                    
                    _tagPresets = new List<TagPresetVM>();
                    if(_areaVM.Area.Directives.TagPresets != null)
                        _tagPresets.AddRange(_areaVM.Area.Directives.TagPresets.Select(x => new TagPresetVM(x)));

                    foreach (Status.StatusEntry statusEntry in Status.Elements.OrderBy(x => x.CanonicalName))
                    {
                        if (statusEntry.Code != StatusCode.Ignored &&
                            statusEntry.Code != StatusCode.Excluded &&
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
                    NotifyPropertyChanged("TagPresets");
                    MainWindow.Instance.Dispatcher.Invoke(() => CommitCommand.RaiseCanExecuteChanged());
                }
            }
        }

        private void StatusVM_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsStaged")
            {
                NotifyPropertyChanged("AllStaged");
                MainWindow.Instance.Dispatcher.Invoke(() => CommitCommand.RaiseCanExecuteChanged());
            }
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
                    MainWindow.Instance.Dispatcher.Invoke(() => CommitCommand.RaiseCanExecuteChanged());
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
            MainWindow.Instance.Dispatcher.Invoke(() => CommitCommand.RaiseCanExecuteChanged());
        }

        private bool CanCommit()
        {
            return _elements != null && _elements.Count(x => x.IsStaged) > 0;
        }

        private void Commit()
        {
            if (string.IsNullOrEmpty(CommitMessage))
            {
                MainWindow.Instance.Dispatcher.Invoke(() =>
                {
                    MainWindow.ShowMessage("Not so fast...", "Please provide a commit message", MessageDialogStyle.Affirmative);
                });
                return;
            }

            OperationStatusDialog.Start("Commit");
            bool commitSuccessful = _areaVM.Area.Commit(CommitMessage, false, GetCommitTags());
            
            if (commitSuccessful && PushOnCommit)
                _areaVM.ExecuteClientCommand((c) => c.Push(), "push", true);
            OperationStatusDialog.Finish();

            CommitMessage = string.Empty;
            TagPresets.ForEach(x => x.IsChecked = false);
            MainWindow.Instance.Dispatcher.Invoke(() => CustomTags.Clear());

            Refresh();
        }

        private bool CanAddTag(TextBox textbox)
        {
            string tag = textbox?.Text;
            return ValidateTag(tag) &&
                TagPresets != null &&
                !TagPresets.Any(x => x.Tag == tag) &&
                !CustomTags.Any(x => x.Tag == tag);
        }

        private void AddTag(TextBox textbox)
        {
            if(CanAddTag(textbox))
            {
                string tag = textbox?.Text;
                CustomTags.Add(new CustomTagVM(tag));
                textbox.Text = String.Empty;
            }
        }

        private void RemoveTag(string tag)
        {
            List<CustomTagVM> itemsToRemove = CustomTags.Where(x => x.Tag == tag).ToList();

            foreach (CustomTagVM itemToRemove in itemsToRemove)
                CustomTags.Remove(itemToRemove);
        }

        private bool ValidateTag(string tag)
        {
            return !String.IsNullOrEmpty(tag) &&
                tag[0] == '#' &&
                !tag.Contains(' ') &&
                !tag.Contains('\t');
        }
    }
}
