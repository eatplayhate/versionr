using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
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
    public partial class LogDialog : Window, INotifyPropertyChanged
    {
        private Area _area;
        private string _author;
        private string _pattern;
        private Tuple<int?, string> _limit;
        private ObservableCollection<VersionVM> _history;

        public LogDialog(Version version, Area area, string pattern = null)
        {
            Version = version;
            _area = area;
            _pattern = pattern;

            LimitOptions = new List<Tuple<int?, string>>();
            LimitOptions.Add(new Tuple<int?, string>(50, "50"));
            LimitOptions.Add(new Tuple<int?, string>(100, "100"));
            LimitOptions.Add(new Tuple<int?, string>(200, "200"));
            LimitOptions.Add(new Tuple<int?, string>(null, "All"));
            _limit = LimitOptions.First();

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
                    RefreshHistory();
                }
            }
        }

        public List<Tuple<int?, string>> LimitOptions { get; private set; }
        public Tuple<int?, string> Limit
        {
            get { return _limit; }
            set
            {
                if (_limit != value)
                {
                    _limit = value;
                    NotifyPropertyChanged("Limit");
                    RefreshHistory();
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
                    RefreshHistory();
                }
            }
        }

        public ObservableCollection<VersionVM> History
        {
            get
            {
                if (_history == null)
                {
                    IsLoading = true;

                    BackgroundWorker worker = new BackgroundWorker();
                    worker.DoWork += new DoWorkEventHandler((obj, args) =>
                    {
                        RefreshHistory();
                        IsLoading = false;
                    });
                    worker.RunWorkerAsync();
                }
                return _history;
            }
        }

        private object refreshLock = new object();
        private void RefreshHistory()
        {
            IsLoading = true;

            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += new DoWorkEventHandler((obj, args) =>
            {
                lock (refreshLock)
                {
                    IEnumerable<Version> versions = ApplyHistoryFilter(_area.GetHistory(Version));
                    
                    MainWindow.Instance.Dispatcher.Invoke(() =>
                    {
                        if (_history == null)
                            _history = new ObservableCollection<VersionVM>();
                        else
                            _history.Clear();
                        foreach (Version ver in versions)
                            _history.Add(new VersionVM(ver, _area));
                        NotifyPropertyChanged("History");
                    });
                }
                IsLoading = false;
            });
            worker.RunWorkerAsync();
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
            
            if (Limit.Item1.HasValue)
                enumeration = enumeration.Take(Limit.Item1.Value);

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
                    NotifyPropertyChanged("Visibility");
                }
            }
        }

        public Visibility GridVisibility
        {
            get { return IsLoading ? Visibility.Collapsed : Visibility.Visible; }
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
