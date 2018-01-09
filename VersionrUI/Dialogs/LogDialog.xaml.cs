﻿using System;
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
        private readonly Area m_Area;
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

        private GridViewColumnHeader _lastHeaderClicked = null;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        public static DependencyProperty ApplyFilterToResultsProperty =
            DependencyProperty.Register("ApplyFilterToResults", typeof(bool), typeof(LogDialog),
                new UIPropertyMetadata(false, ApplyFilterToResultChanged));

        public bool ApplyFilterToResults
        {
            get { return (bool)GetValue(ApplyFilterToResultsProperty); }
            set { SetValue(ApplyFilterToResultsProperty, value); }
        }

        private static void ApplyFilterToResultChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            if (!(o is LogDialog))
                return;
            LogDialog logDialog = (LogDialog)o;
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
            m_Area = area;
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
        }

        public Version Version { get; private set; }
        public Area Area => m_Area;

        public string Author
        {
            get { return m_Author; }
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
            get { return m_RevisionLimit; }
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
            get { return m_Pattern; }
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
                int? limit = (RevisionLimit != -1) ? RevisionLimit : (int?)null;
                IEnumerable<Version> versions =
                    ApplyHistoryFilter(m_Area.GetLogicalHistory(Version, false, false, false, limit));
                
                m_History = new List<VersionVM>();
                foreach (Version ver in versions)
                    m_History.Add(new VersionVM(ver, m_Area));
                NotifyPropertyChanged(nameof(History));
            }
        }

        IEnumerable<ResolvedAlteration> GetAlterations(Version v)
        {
            return m_Area.GetAlterations(v).Select(x => new ResolvedAlteration(x, m_Area));
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

            var headerClicked = e.OriginalSource as GridViewColumnHeader;
            if (headerClicked == null)
                return;
            if (headerClicked.Role == GridViewColumnHeaderRole.Padding)
                return;
            ListSortDirection direction;
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

        #region Loading
        private bool _isLoading = false;
        public bool IsLoading
        {
            get { return _isLoading; }
            set
            {
                if (_isLoading == value)
                    return;
                _isLoading = value;
                NotifyPropertyChanged(nameof(IsLoading));
                NotifyPropertyChanged(nameof(LogOpacity));
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

            var dialog = values[0] as LogDialog;
            var alteration = values[1] as AlterationVM;
            if (dialog == null || alteration == null)
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
