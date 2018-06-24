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
        private string m_Author;
        private string m_Pattern;
        private List<VersionVM> m_History;
        private int m_RevisionLimit;
        private static readonly Dictionary<int, string> m_RevisionLimitOptions = new Dictionary<int, string>()
        {
            { 50, "50" },
            { 100, "100" },
            { 150, "150" },
            { 200, "200" },
            { 500, "500" },
            { 1000, "1000" },
            { 2000, "2000" },
            { -1, "All" },
        };

        private GridViewColumnHeader m_LastHeaderClicked = null;
        private ListSortDirection m_LastDirection = ListSortDirection.Ascending;

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
            if (!(o is LogDialog))
                return;
            LogDialog logDialog = (LogDialog)o;
            logDialog.History.ForEach(x =>
                x.SearchText = logDialog.ApplyFilterToResults ? logDialog.Pattern : string.Empty);
        }

        // Reusing this dialog to display the results of a search
        public static void FindResultDialog(Version version, Area area)
        {
            IsUsedAsALogDialog = false;
            new LogDialog(version, area).ShowDialog();
        }

        public static void Show(Version version, Area area, string pattern = null)
        {
            // Showing modal for now because sqlite dies a horrible death if multiple windows access the db.
            // If that ever gets fixed, uncomment the code below so we can have multiple log windows.
            IsUsedAsALogDialog = true;
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
            Area = area;
            m_Pattern = pattern;

            m_RevisionLimit = RevisionLimitOptions.First().Key;
            
            InitializeComponent();
            mainGrid.DataContext = this;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                    Close();
            };
            Owner = MainWindow.Instance;
            if (!IsUsedAsALogDialog)
                Title = "Find Version using GUID";
        }

        public static bool IsUsedAsALogDialog { get; private set; } = true;
        public Version Version { get; private set; }
        public Area Area { get; }

        public string Author
        {
            get => m_Author;
            set
            {
                if (m_Author == value) return;
                m_Author = value;
                NotifyPropertyChanged(nameof(Author));
                Load(RefreshHistory);
            }
        }

        public Dictionary<int, string> RevisionLimitOptions => m_RevisionLimitOptions;

        public int RevisionLimit
        {
            get => m_RevisionLimit;
            set
            {
                if (m_RevisionLimit == value) return;
                m_RevisionLimit = value;
                NotifyPropertyChanged(nameof(RevisionLimit));
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
            get => m_Pattern;
            set
            {
                if (m_Pattern == value) return;
                m_Pattern = value;
                NotifyPropertyChanged(nameof(Pattern));
                NotifyPropertyChanged(nameof(Regex));
                NotifyPropertyChanged(nameof(ApplyFilterToResults));
                Load(RefreshHistory);
            }
        }

        public List<VersionVM> History
        {
            get
            {
                if (m_History == null)
                    Load(RefreshHistory);
                return m_History;
            }
        }

        private static readonly object refreshLock = new object();
        private void RefreshHistory()
        {
            lock (refreshLock)
            {
                m_History = new List<VersionVM>();
                if (IsUsedAsALogDialog)
                {
                    int? limit = (RevisionLimit != -1) ? RevisionLimit : (int?) null;
                    IEnumerable<Version> versions =
                        ApplyHistoryFilter(Area.GetLogicalHistory(Version, false, false, false, limit));
                    
                    foreach (Version ver in versions)
                        m_History.Add(new VersionVM(ver, Area));
                }
                else
                {
                    m_History.Add(new VersionVM(Version, Area));
                }
                NotifyPropertyChanged(nameof(History));
            }
        }

        IEnumerable<ResolvedAlteration> GetAlterations(Version v)
        {
            return Area.GetAlterations(v).Select(x => new ResolvedAlteration(x, Area));
        }

        private IEnumerable<Version> ApplyHistoryFilter(IEnumerable<Version> history)
        {
            IEnumerable<Version> enumeration = history;

            if (!string.IsNullOrEmpty(Author))
                enumeration = enumeration.Where(x => x.Author.IndexOf(Author, StringComparison.OrdinalIgnoreCase) >= 0);

            enumeration = enumeration.Where(HasAlterationMatchingFilter);

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

            if (!(e.OriginalSource is GridViewColumnHeader headerClicked))
                return;
            if (headerClicked.Role == GridViewColumnHeaderRole.Padding)
                return;
            ListSortDirection direction;
            if (headerClicked != m_LastHeaderClicked)
            {
                direction = ListSortDirection.Ascending;
            }
            else
            {
                if (m_LastDirection == ListSortDirection.Ascending)
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
            if (m_LastHeaderClicked != null && m_LastHeaderClicked != headerClicked)
                m_LastHeaderClicked.Column.HeaderTemplate = null;

            m_LastHeaderClicked = headerClicked;
            m_LastDirection = direction;
        }

        #region Loading
        private bool m_IsLoading = false;
        public bool IsLoading
        {
            get => m_IsLoading;
            set
            {
                if (m_IsLoading == value)
                    return;
                m_IsLoading = value;
                NotifyPropertyChanged(nameof(IsLoading));
                NotifyPropertyChanged(nameof(LogOpacity));
            }
        }

        public float LogOpacity => IsLoading ? 0.3f : 1.0f;

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(info));
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

            if (!(values[0] is LogDialog dialog) || !(values[1] is AlterationVM alteration))
                return false;
            if (String.IsNullOrEmpty(dialog.Pattern) || dialog.Pattern.Equals(".*"))
                return false;

            ResolvedAlteration resolved = new ResolvedAlteration(alteration.Alteration, dialog.Area);
            return dialog.Regex.IsMatch(resolved.Record.CanonicalName);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
