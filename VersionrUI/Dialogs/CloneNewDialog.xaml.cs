using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VersionrUI.Commands;
using VersionrUI.ViewModels;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace VersionrUI.Dialogs
{
    /// <summary>
    /// Interaction logic for CloneNewDialog.xaml
    /// </summary>
    public partial class CloneNewDialog : Window, INotifyPropertyChanged
    {
        private bool _userTypedName = false;
        private string _pathString = "";
        private string _nameString = "";
        private string _host = "";
        private string _port = "";
        private ImageSource _imageGoodTick;
        private ImageSource _imageBadCircle;

        public CloneNewDialog(Window owner)
        {
            InitializeComponent();
            mainGrid.DataContext = this;

            try
            {
                _imageGoodTick = new BitmapImage(new Uri("pack://application:,,,/Images/GoodTick.png"));
                _imageBadCircle = new BitmapImage(new Uri("pack://application:,,,/Images/BadCircle.png"));
            }
            catch { }

            PathBrowseCommand = new DelegateCommand(PathBrowse);
            OptionChangedCommand = new DelegateCommand<AreaInitMode>(OptionChanged);
            OkCommand = new DelegateCommand(Ok, CanOk);
            RefreshRadioButtons();
        }

        public string PathString
        {
            get { return _pathString; }
            set
            {
                if (_pathString != value)
                {
                    _pathString = value;
                    if (_userTypedName == false && IsPathGood)
                    {
                        string lastBitOfPath = PathString.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).Last();
                        Regex goodChars = new Regex("[0-9a-zA-Z_-]");
                        var filteredChars = lastBitOfPath.Where(c => goodChars.IsMatch(c.ToString()));
                        NameString = new string(filteredChars.ToArray());
                    }

                    RefreshRadioButtons();
                    NotifyPropertyChanged("PathString");
                    NotifyPropertyChanged("PathStatus");
                }
            }
        }

        public string NameString
        {
            get { return _nameString; }
            set
            {
                if (_nameString != value)
                {
                    _nameString = value;
                    RefreshRadioButtons();
                    NotifyPropertyChanged("NameString");
                    NotifyPropertyChanged("NameStatus");
                }
            }
        }

        public string Host
        {
            get { return _host; }
            set
            {
                if (_host != value)
                {
                    _host = value;
                    NotifyPropertyChanged("Host");
                }
            }
        }

        public string Port
        {
            get { return _port; }
            set
            {
                if (_port != value)
                {
                    _port = value;
                    NotifyPropertyChanged("Port");
                }
            }
        }

        public AreaInitMode Result { get; private set; }

        public ImageSource PathStatus
        {
            get
            {
                if (String.IsNullOrEmpty(PathString))
                    return new BitmapImage();
                else if (IsPathGood)
                    return _imageGoodTick;
                else
                    return _imageBadCircle;
            }
        }

        public ImageSource NameStatus
        {
            get
            {
                if (String.IsNullOrEmpty(NameString))
                    return new BitmapImage();
                else if (IsNameValid)
                    return _imageGoodTick;
                else
                    return _imageBadCircle;
            }
        }

        public Visibility HostFieldVisibility { get { return (Result == AreaInitMode.Clone) ? Visibility.Visible : Visibility.Hidden; } }

        private bool IsPathGood
        {
            get
            {
                if (!String.IsNullOrEmpty(PathString))
                {
                    try
                    {
                        return Path.GetFullPath(PathString) == PathString;
                    }
                    catch { }
                }
                return false;
            }
        }

        private bool IsNameValid
        {
            get { return !String.IsNullOrEmpty(NameString) && new Regex("^[0-9a-zA-Z_-]+$").IsMatch(NameString); }
        }

        private bool IsVersionrRepo
        {
            get { return Utilities.IsVersionrPath(PathString); }
        }

        private void TextBox_Name_KeyDown(object sender, KeyEventArgs e)
        {
            _userTypedName = true;
        }

        #region Commands
        public DelegateCommand PathBrowseCommand { get; private set; }
        public DelegateCommand<AreaInitMode> OptionChangedCommand { get; private set; }
        public DelegateCommand OkCommand { get; private set; }

        private void OptionChanged(AreaInitMode newOption)
        {
            Result = newOption;

            NotifyPropertyChanged("HostFieldVisibility");
            OkCommand.RaiseCanExecuteChanged();
        }
        
        private void PathBrowse()
        {
            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.Description = "Select the folder to find, create or clone a repo";
            folderBrowserDialog.SelectedPath = Properties.Settings.Default.CloneNewDialogLastBrowsedFolder;
            if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (Directory.Exists(folderBrowserDialog.SelectedPath))
                {
                    Properties.Settings.Default.CloneNewDialogLastBrowsedFolder = folderBrowserDialog.SelectedPath;
                    Properties.Settings.Default.Save();
                    PathString = folderBrowserDialog.SelectedPath;
                }
            }
        }

        private bool CanOk()
        {
            switch (Result)
            {
                case AreaInitMode.InitNew:
                    return CanExecuteNewRepo();
                case AreaInitMode.UseExisting:
                    return CanExecuteExistingRepo();
                case AreaInitMode.Clone:
                    return CanExecuteCloneRepo();
                default:
                    return false;
            }
        }
        
        private void Ok()
        {
            DialogResult = true;
            Close();
        }
        #endregion

        private void RefreshRadioButtons()
        {
            radioNewRepo.IsEnabled = CanExecuteNewRepo();
            radioExistingRepo.IsEnabled = CanExecuteExistingRepo();
            radioCloneRepo.IsEnabled = CanExecuteCloneRepo();
        }

        private bool CanExecuteNewRepo()
        {
            return IsPathGood && !IsVersionrRepo && IsNameValid;
        }
        private bool CanExecuteExistingRepo()
        {
            return IsPathGood && IsVersionrRepo && IsNameValid;
        }

        private bool CanExecuteCloneRepo()
        {
            return IsPathGood && !IsVersionrRepo && IsNameValid;
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged(string info)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(info));
        }
        #endregion
    }
}
