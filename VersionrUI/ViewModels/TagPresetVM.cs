using Versionr;

namespace VersionrUI.ViewModels
{
    public class TagPresetVM : NotifyPropertyChangedBase
    {
        private TagPreset m_TagPreset;
        private bool m_IsChecked;

        public TagPresetVM(TagPreset tagPreset)
        {
            m_TagPreset = tagPreset;
        }

        public string Tag
        {
            get { return m_TagPreset.Tag; }
        }

        public string Description
        {
            get { return m_TagPreset.Description; }
        }

        public bool IsChecked
        {
            get { return m_IsChecked; }
            set
            {
                m_IsChecked = value;
                NotifyPropertyChanged(nameof(IsChecked));
            }
        }
    }
}
