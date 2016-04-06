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

        public bool IsEmpty
        {
            get
            {
                return DirectoryPatterns == null &&
                   FilePatterns == null &&
                   Directories == null &&
                   Extensions == null &&
                   Patterns == null;
            }
        }

        public string[] DirectoryPatterns
        {
            get { return _ignores.DirectoryPatterns; }
            set
            {
                _ignores.DirectoryPatterns = value;
                if (_ignores.DirectoryPatterns != null && _ignores.DirectoryPatterns.Length == 0)
                    _ignores.DirectoryPatterns = null;
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
                if (_ignores.FilePatterns != null && _ignores.FilePatterns.Length == 0)
                    _ignores.FilePatterns = null;
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
                if (_ignores.Directories != null && _ignores.Directories.Length == 0)
                    _ignores.Directories = null;
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
                if (_ignores.Extensions != null && _ignores.Extensions.Length == 0)
                    _ignores.Extensions = null;
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
                if (_ignores.Patterns != null && _ignores.Patterns.Length == 0)
                    _ignores.Patterns = null;
                if (Dirtied != null)
                    Dirtied(this, new EventArgs());
                NotifyPropertyChanged("Patterns");
            }
        }
    }
}
