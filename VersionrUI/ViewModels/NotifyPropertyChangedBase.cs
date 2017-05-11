using System;
using System.ComponentModel;

namespace VersionrUI.ViewModels
{
    public class NotifyPropertyChangedBase : INotifyPropertyChanged
    {
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
                    NotifyPropertyChanged("Opacity");
                }
            }
        }

        public float Opacity
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

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged(string info)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(info));
        }
    }
}
