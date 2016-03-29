using System;
using System.Collections.Generic;
using System.Linq;
using Versionr;

namespace VersionrUI.ViewModels
{
    public class IgnoresVM : NotifyPropertyChangedBase
    {
        public class NameArrayPair
        {
            public string Name { get; set; }
            public string[] Array { get; set; }
        }

        public event EventHandler ListChanged;

        private Ignores _ignores;
        private List<NameArrayPair> _ignoreLists;
        private NameArrayPair _selectedPair;

        public IgnoresVM(Ignores ignores)
        {
            _ignores = ignores;
            _ignoreLists = new List<NameArrayPair>();

            // TODO: Can't use ignores?.DirectoryPatterns here because we need to set to the original property
            // and we also need to handle the _ignores being null

            AddEntry("Directory Patterns", ignores?.DirectoryPatterns);
            AddEntry("File Patterns", ignores?.FilePatterns);
            AddEntry("Directories", ignores?.Directories);
            AddEntry("Extensions", ignores?.Extensions);
            AddEntry("Patterns", ignores?.Patterns);
            
            SelectedPair = _ignoreLists.FirstOrDefault();
        }

        public List<NameArrayPair> IgnoreLists
        {
            get { return _ignoreLists; }
        }

        public NameArrayPair SelectedPair
        {
            get { return _selectedPair; }
            set
            {
                _selectedPair = value;
                NotifyPropertyChanged("SelectedPair");
                NotifyPropertyChanged("SelectedList");
            }
        }

        public string[] SelectedList
        {
            get { return _selectedPair.Array; }
            set
            {
                _selectedPair.Array = value;
                if(ListChanged != null)
                    ListChanged(this, new EventArgs());
                NotifyPropertyChanged("SelectedList");
            }
        }

        private void AddEntry(string name, string[] collection)
        {
            _ignoreLists.Add(new NameArrayPair()
            {
                Name = name,
                Array = (collection != null) ? collection : new string[0]
            });
        }
    }
}
