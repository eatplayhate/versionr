using System.Collections;

namespace VersionrUI.ViewModels
{
    public class NamedCollection : NotifyPropertyChangedBase
    {
        public NamedCollection(string name, IEnumerable items)
        {
            Name = name;
            Items = items;
        }
        public string Name { get; private set; }
        public IEnumerable Items { get; private set; }

        private bool _isExpanded = false;
        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    NotifyPropertyChanged("IsExpanded");
                }
            }
        }
    }
}
