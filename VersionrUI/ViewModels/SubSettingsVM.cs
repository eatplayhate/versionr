using System;
using System.Windows;
using Versionr;
using Versionr.Utilities;
using VersionrUI.Commands;

namespace VersionrUI.ViewModels
{
    public class SubSettingsVM : NotifyPropertyChangedBase
    {
        public DelegateCommand SaveCommand { get; private set; }

        private Area _area;
        private SettingsViewMode _viewMode;
        private Directives _directives;
        private IgnoresVM _ignores;
        private IgnoresVM _includes;
        private string _error;
        private bool _isDirty = false;

        public SubSettingsVM(Area area, SettingsViewMode viewMode)
        {
            SaveCommand = new DelegateCommand(Save, () => IsDirty);

            _area = area;
            _viewMode = viewMode;

            string error = null;
            switch (_viewMode)
            {
                case SettingsViewMode.Meta:
                    _directives = DirectivesUtils.LoadVRMeta(_area, out error);
                    break;
                case SettingsViewMode.UserGlobal:
                    _directives = DirectivesUtils.LoadGlobalVRUser(out error);
                    break;
                case SettingsViewMode.User:
                    _directives = DirectivesUtils.LoadVRUser(_area, out error);
                    break;
                case SettingsViewMode.Effective:
                default:
                    _directives = _area.Directives;
                    IsReadOnly = true;
                    break;
            }
            Error = (String.IsNullOrEmpty(error)) ? String.Empty : String.Format("{0}\nSaving changes will create a new file", error);

            if (_directives == null)
                _directives = new Directives();

            _ignores = new IgnoresVM(_directives.Ignore, IsReadOnly);
            _ignores.Dirtied += (s, e) => IsDirty = true;

            _includes = new IgnoresVM(_directives.Include, IsReadOnly);
            _includes.Dirtied += (s, e) => IsDirty = true;
        }

        public bool IsDirty
        {
            get { return _isDirty; }
            private set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    NotifyPropertyChanged("IsDirty");
                    SaveCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsReadOnly { get; private set; }

        public string Error
        {
            get { return _error; }
            private set
            {
                if (_error != value)
                {
                    _error = value;
                    NotifyPropertyChanged("Error");
                }
            }
        }

        public string FilePath
        {
            get
            {
                switch (_viewMode)
                {
                    case SettingsViewMode.Meta:
                        return DirectivesUtils.GetVRMetaPath(_area);
                    case SettingsViewMode.UserGlobal:
                        return DirectivesUtils.GetGlobalVRUserPath();
                    case SettingsViewMode.User:
                        return DirectivesUtils.GetVRUserPath(_area);
                    case SettingsViewMode.Effective:
                    default:
                        return string.Empty;
                }
            }
        }

        public Visibility ErrorVisibility
        {
            get { return (String.IsNullOrEmpty(Error)) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility SaveButtonVisibility
        {
            get { return (_viewMode == SettingsViewMode.Effective) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public string UserNameWatermark
        {
            get { return Environment.UserName; }
        }

        public string UserName
        {
            get { return _directives.UserName; }
            set
            {
                if (_directives.UserName != value)
                {
                    IsDirty = true;
                    _directives.UserName = value;
                    if (String.IsNullOrEmpty(_directives.UserName))
                        _directives.UserName = null;
                    NotifyPropertyChanged("UserName");
                }
            }
        }

        public string ExternalDiff
        {
            get { return _directives.ExternalDiff; }
            set
            {
                if (_directives.ExternalDiff != value)
                {
                    IsDirty = true;
                    _directives.ExternalDiff = value;
                    if (String.IsNullOrEmpty(_directives.ExternalDiff))
                        _directives.ExternalDiff = null;
                    NotifyPropertyChanged("ExternalDiff");
                }
            }
        }

        public string ExternalMerge
        {
            get { return _directives.ExternalMerge; }
            set
            {
                if (_directives.ExternalMerge != value)
                {
                    IsDirty = true;
                    _directives.ExternalMerge = value;
                    if (String.IsNullOrEmpty(_directives.ExternalMerge))
                        _directives.ExternalMerge = null;
                    NotifyPropertyChanged("ExternalMerge");
                }
            }
        }

        public string ExternalMerge2Way
        {
            get { return _directives.ExternalMerge2Way; }
            set
            {
                if (_directives.ExternalMerge2Way != value)
                {
                    IsDirty = true;
                    _directives.ExternalMerge2Way = value;
                    if (String.IsNullOrEmpty(_directives.ExternalMerge2Way))
                        _directives.ExternalMerge2Way = null;
                    NotifyPropertyChanged("ExternalMerge2Way");
                }
            }
        }

        public IgnoresVM Ignores
        {
            get { return _ignores; }
        }

        public IgnoresVM Includes
        {
            get { return _includes; }
        }

        private void Save()
        {
            _directives.Ignore = (_ignores.IsEmpty) ? null : _ignores.Ignores;
            _directives.Include = (_includes.IsEmpty) ? null : _includes.Ignores;

            bool success = false;
            switch (_viewMode)
            {
                case SettingsViewMode.Meta:
                    success = DirectivesUtils.WriteVRMeta(_area, _directives);
                    break;
                case SettingsViewMode.UserGlobal:
                    success = DirectivesUtils.WriteGlobalVRUser(_directives);
                    break;
                case SettingsViewMode.User:
                    success = DirectivesUtils.WriteVRUser(_area, _directives);
                    break;
                case SettingsViewMode.Effective:
                default:
                    break;
            }

            if (success)
            {
                IsDirty = false;
                Error = String.Empty;
            }
            else
            {
                Error = String.Format("Failed to write to {0}\n. This could be because the file is already in use.", FilePath);
            }
        }
    }
}
