using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Input;
using Versionr;
using Versionr.Objects;
using VersionrUI.ViewModels;
using Version = Versionr.Objects.Version;

namespace VersionrUI.Dialogs
{
    /// <summary>
    /// Interaction logic for LogDialog.xaml
    /// </summary>
    public partial class LogDialog : INotifyPropertyChanged
    {
        private Area _area;
        private string _author;
        private string _pattern;
        private List<VersionVM> _history;
        private int _revisionLimit;
        private static Dictionary<int, string> _revisionLimitOptions = new Dictionary<int, string>()
        {
            { 50, "50" },
            { 100, "100" },
            { 150, "150" },
            { 200, "200" },
            { -1, "All" },
        };

        public static void Show(Version version, Area area, string pattern = null)
        {
            // Showing modal for now because sqlite dies a horrible death if multiple windows access the db.
            // If that ever gets fixed, uncomment the code below so we can have multiple log windows.
            new LogDialog(version, area, pattern).ShowDialog();

            //Thread newWindowThread = new Thread(() =>
            //{
            //    new LogDialog(version, area, pattern).Show();
            //    System.Windows.Threading.Dispatcher.Run();
            //});
            //newWindowThread.SetApartmentState(ApartmentState.STA);
            //newWindowThread.IsBackground = true;
            //newWindowThread.Start();
        }

        private LogDialog(Version version, Area area, string pattern = null)
        {
            Version = version;
            _area = area;
            _pattern = pattern;

            _revisionLimit = RevisionLimitOptions.First().Key;

            InitializeComponent();
            mainGrid.DataContext = this;

            this.PreviewKeyDown += new KeyEventHandler((s, e) =>
            {
                if (e.Key == Key.Escape)
                    Close();
            });
        }

        public Version Version { get; private set; }
        public Area Area { get { return _area; } }

        public string Author
        {
            get { return _author; }
            set
            {
                if (_author != value)
                {
                    _author = value;
                    NotifyPropertyChanged("Author");
                    Load(RefreshHistory);
                }
            }
        }

        public Dictionary<int, string> RevisionLimitOptions
        {
            get { return _revisionLimitOptions; }
        }
        public int RevisionLimit
        {
            get { return _revisionLimit; }
            set
            {
                if (_revisionLimit != value)
                {
                    _revisionLimit = value;
                    NotifyPropertyChanged("RevisionLimit");
                    Load(RefreshHistory);
                }
            }
        }

        public Regex Regex
        {
            get
            {
                if (String.IsNullOrEmpty(Pattern))
                    return new Regex(".*");

                // TODO: Needs more work...
                string escaped = Regex.Escape(Pattern).Replace(@"\*", ".*").Replace(@"\?", ".");
                return new Regex(escaped, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            }
        }

        public string Pattern
        {
            get { return _pattern; }
            set
            {
                if (_pattern != value)
                {
                    _pattern = value;
                    NotifyPropertyChanged("Pattern");
                    NotifyPropertyChanged("Regex");
                    Load(RefreshHistory);
                }
            }
        }

        public List<VersionVM> History
        {
            get
            {
                if (_history == null)
                    Load(RefreshHistory);
                return _history;
            }
        }

        private static object refreshLock = new object();
        private void RefreshHistory()
        {
            lock (refreshLock)
            {
                int? limit = (RevisionLimit != -1) ? RevisionLimit : (int?)null;
                IEnumerable<Version> versions = ApplyHistoryFilter(_area.GetLogicalHistory(Version, limit));
                
                _history = new List<VersionVM>();
                foreach (Version ver in versions)
                    _history.Add(new VersionVM(ver, _area));
                NotifyPropertyChanged("History");
            }
        }

        IEnumerable<ResolvedAlteration> GetAlterations(Version v)
        {
            return _area.GetAlterations(v).Select(x => new ResolvedAlteration(x, _area));
        }

        private IEnumerable<Version> ApplyHistoryFilter(IEnumerable<Version> history)
        {
            IEnumerable<Version> enumeration = history;

            if (!string.IsNullOrEmpty(Author))
                enumeration = enumeration.Where(x => x.Author.IndexOf(Author, StringComparison.OrdinalIgnoreCase) >= 0);

            enumeration = enumeration.Where(x => HasAlterationMatchingFilter(x));

            if (RevisionLimit != -1)
                enumeration = enumeration.Take(RevisionLimit);

            return enumeration;
        }

        private bool HasAlterationMatchingFilter(Version v)
        {
            IEnumerable<ResolvedAlteration> alterations = GetAlterations(v);
            return alterations.Any(x => Regex.IsMatch(x.Record.CanonicalName));
        }

        #region Loading
        private bool _isLoading = false;
        public bool IsLoading
        {
            get { return _isLoading; }
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    NotifyPropertyChanged("IsLoading");
                    NotifyPropertyChanged("LogOpacity");
                }
            }
        }

        public float LogOpacity
        {
            get { return IsLoading ? 0.3f : 1.0f; }
        }

        protected void Load(Action action)
        {
            IsLoading = true;

            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += new DoWorkEventHandler((obj, args) =>
            {
                action.Invoke();
                IsLoading = false;
            });
            worker.RunWorkerAsync();
        }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged(string info)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(info));
        }
        #endregion
    }
    class ResolvedAlteration
    {
        public Alteration Alteration { get; private set; }
        public Record Record { get; private set; }
        public ResolvedAlteration(Alteration alteration, Area ws)
        {
            Alteration = alteration;
            if (alteration.NewRecord.HasValue)
                Record = ws.GetRecord(Alteration.NewRecord.Value);
            else if (alteration.PriorRecord.HasValue)
                Record = ws.GetRecord(Alteration.PriorRecord.Value);
            else
                throw new Exception("unexpected");
        }
    }

    public class MatchRegexConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 2)
                return false;

            LogDialog dialog = values[0] as LogDialog;
            AlterationVM alteration = values[1] as AlterationVM;

            if (dialog != null && alteration != null)
            {
                if (String.IsNullOrEmpty(dialog.Pattern) || dialog.Pattern.Equals(".*"))
                    return false;

                ResolvedAlteration resolved = new ResolvedAlteration(alteration.Alteration, dialog.Area);
                return dialog.Regex.IsMatch(resolved.Record.CanonicalName);
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
