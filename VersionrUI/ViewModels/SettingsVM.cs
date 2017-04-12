using Versionr;
using VersionrUI.Commands;

namespace VersionrUI.ViewModels
{
    public enum SettingsViewMode
    {
        Meta = 0,
        UserGlobal = 1,
        User = 2,
        Effective = 3,
    }

    public class SettingsVM : NotifyPropertyChangedBase
    {
        public DelegateCommand<SettingsViewMode> OptionChangedCommand { get; private set; }
        
        private Area _area;
        private SettingsViewMode _viewMode = SettingsViewMode.Effective;
        private SubSettingsVM[] _subSettings = new SubSettingsVM[4];
        private SubSettingsVM _selectedSubSettings;

        public SettingsVM(Area area)
        {
            OptionChangedCommand = new DelegateCommand<SettingsViewMode>(OptionChanged);
            _area = area;
            Refresh();
        }

        public string Name
        {
            get { return "Settings"; }
        }

        public string SettingsHeader
        {
            get
            {
                switch(ViewMode)
                {
                    case SettingsViewMode.Meta:
                        return "Edit default settings for all users of this repo (.vrmeta)";
                    case SettingsViewMode.UserGlobal:
                        return "Edit global user settings that will be applied to all repos";
                    case SettingsViewMode.User:
                        return "Edit user defined settings for this repo (.vruser)";
                    case SettingsViewMode.Effective:
                        return "The effective settings applied to this repo";
                    default:
                        return string.Empty;
                }
            }
        }

        public SubSettingsVM[] SubSettingsArray
        {
            get { return _subSettings; }
        }

        public SubSettingsVM SelectedSubSettings
        {
            get { return _selectedSubSettings; }
            set
            {
                if (_selectedSubSettings != value)
                {
                    _selectedSubSettings = value;
                    NotifyPropertyChanged("SelectedSubSettings");
                }
            }
        }





        public SubSettingsVM SubSettings
        {
            get { return _subSettings[(int)ViewMode]; }
        }

        private SettingsViewMode ViewMode
        {
            get { return _viewMode; }
            set
            {
                if (_viewMode != value)
                {
                    _viewMode = value;
                    NotifyPropertyChanged("ViewMode");
                    NotifyPropertyChanged("SettingsHeader");
                    NotifyPropertyChanged("SubSettings");
                }
            }
        }

        public void Refresh()
        {
            _subSettings[0] = new SubSettingsVM(_area, SettingsViewMode.Meta);
            _subSettings[1] = new SubSettingsVM(_area, SettingsViewMode.UserGlobal);
            _subSettings[2] = new SubSettingsVM(_area, SettingsViewMode.User);
            _subSettings[3] = new SubSettingsVM(_area, SettingsViewMode.Effective);
        }

        private void OptionChanged(SettingsViewMode newOption)
        {
            ViewMode = newOption;
        }
    }
}
