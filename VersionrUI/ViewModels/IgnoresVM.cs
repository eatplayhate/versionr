using System;
using Versionr;

namespace VersionrUI.ViewModels
{
    public class IgnoresVM : NotifyPropertyChangedBase
    {
        private Ignores _ignores;
        
        public event EventHandler Dirtied;

        public IgnoresVM(Ignores ignores, bool isReadOnly)
        {
            _ignores = ignores;
            if (_ignores == null)
                _ignores = new Ignores();

            IsReadOnly = isReadOnly;
        }

        public Ignores Ignores
        {
            get { return _ignores; }
        }

        public bool IsReadOnly { get; private set; }

        public string[] DirectoryPatterns
        {
            get { return _ignores.DirectoryPatterns; }
            set
            {
                _ignores.DirectoryPatterns = value;
                if (Dirtied != null)
                    Dirtied(this, new EventArgs());
                NotifyPropertyChanged("DirectoryPatterns");
            }
        }

        public string[] FilePatterns
        {
            get { return _ignores.FilePatterns; }
            set
            {
                _ignores.FilePatterns = value;
                if (Dirtied != null)
                    Dirtied(this, new EventArgs());
                NotifyPropertyChanged("FilePatterns");
            }
        }

        public string[] Directories
        {
            get { return _ignores.Directories; }
            set
            {
                _ignores.Directories = value;
                if (Dirtied != null)
                    Dirtied(this, new EventArgs());
                NotifyPropertyChanged("Directories");
            }
        }

        public string[] Extensions
        {
            get { return _ignores.Extensions; }
            set
            {
                _ignores.Extensions = value;
                if (Dirtied != null)
                    Dirtied(this, new EventArgs());
                NotifyPropertyChanged("Extensions");
            }
        }

        public string[] Patterns
        {
            get { return _ignores.Patterns; }
            set
            {
                _ignores.Patterns = value;
                if (Dirtied != null)
                    Dirtied(this, new EventArgs());
                NotifyPropertyChanged("Patterns");
            }
        }
    }
}
