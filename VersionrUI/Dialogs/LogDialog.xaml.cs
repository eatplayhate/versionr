using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Versionr;
using Versionr.Objects;
using VersionrUI.Controls;
using VersionrUI.ViewModels;
using Version = Versionr.Objects.Version;

namespace VersionrUI.Dialogs
{
    /// <summary>
    /// Interaction logic for LogDialog.xaml
    /// </summary>
    public partial class LogDialog : INotifyPropertyChanged
    {
        private readonly Area _area;
        private string _author;
        private string _pattern;
        private List<VersionVM> _history;
        private int _revisionLimit;
        private static readonly Dictionary<int, string> _revisionLimitOptions = new Dictionary<int, string>()
        {
            { 50, "50" },
            { 100, "100" },
            { 200, "200" },
            { 500, "500" },
            { -1, "All" },
        };

        private GridViewColumnHeader _lastHeaderClicked = null;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        public static DependencyProperty ApplyFilterToResultsProperty =
            DependencyProperty.Register("ApplyFilterToResults", typeof(bool), typeof(LogDialog),
                new UIPropertyMetadata(false, ApplyFilterToResultChanged));

        public bool ApplyFilterToResults
        {
            get => (bool)GetValue(ApplyFilterToResultsProperty);
            set => SetValue(ApplyFilterToResultsProperty, value);
        }

        private static void ApplyFilterToResultChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            if (!(o is LogDialog logDialog)) return;
            logDialog.History.ForEach(x =>
                x.SearchText = logDialog.ApplyFilterToResults ? logDialog.Pattern : string.Empty);
        }

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
        public Area Area => _area;

        public string Author
        {
            get => _author;
            set
            {
                if (_author == value) return;
                _author = value;
                NotifyPropertyChanged("Author");
                Load(RefreshHistory);
            }
        }

        public Dictionary<int, string> RevisionLimitOptions => _revisionLimitOptions;

        public int RevisionLimit
        {
            get => _revisionLimit;
            set
            {
                if (_revisionLimit == value) return;
                _revisionLimit = value;
                NotifyPropertyChanged("RevisionLimit");
                Load(RefreshHistory);
            }
        }

        public Regex Regex
        {
            get
            {
                if (string.IsNullOrEmpty(Pattern))
                    return new Regex(".*");

                // TODO: Needs more work...
                string escaped = Regex.Escape(Pattern).Replace(@"\*", ".*").Replace(@"\?", ".");
                return new Regex(escaped, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            }
        }

        public string Pattern
        {
            get => _pattern;
            set
            {
                if (_pattern == value) return;
                _pattern = value;
                NotifyPropertyChanged("Pattern");
                NotifyPropertyChanged("Regex");
                Load(RefreshHistory);
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

        private static readonly object refreshLock = new object();
        private void RefreshHistory()
        {
            lock (refreshLock)
            {
                int? limit = (RevisionLimit != -1) ? RevisionLimit : (int?)null;
                IEnumerable<Version> versions = ApplyHistoryFilter(_area.GetLogicalHistory(Version, false, false, false, limit));
                
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

        private void listViewHeader_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is ListView))
                return;

            GridViewColumnHeader headerClicked = e.OriginalSource as GridViewColumnHeader;
            ListSortDirection direction;
            if (headerClicked != null)
            {
                if (headerClicked.Role != GridViewColumnHeaderRole.Padding)
                {
                    if (headerClicked != _lastHeaderClicked)
                    {
                        direction = ListSortDirection.Ascending;
                    }
                    else
                    {
                        if (_lastDirection == ListSortDirection.Ascending)
                            direction = ListSortDirection.Descending;
                        else
                            direction = ListSortDirection.Ascending;
                    }

                    string header = headerClicked.Column.Header as string;
                    VersionrPanel.Sort(CollectionViewSource.GetDefaultView(((ListView)sender).ItemsSource), header, direction);

                    if (direction == ListSortDirection.Ascending)
                        headerClicked.Column.HeaderTemplate = Resources["HeaderTemplateArrowUp"] as DataTemplate;
                    else
                        headerClicked.Column.HeaderTemplate = Resources["HeaderTemplateArrowDown"] as DataTemplate;

                    // Remove arrow from previously sorted header
                    if (_lastHeaderClicked != null && _lastHeaderClicked != headerClicked)
                        _lastHeaderClicked.Column.HeaderTemplate = null;

                    _lastHeaderClicked = headerClicked;
                    _lastDirection = direction;
                }
            }
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
