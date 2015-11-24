namespace VersionrUI.ViewModels
{
    public class StatusEntryVM
    {
        private Versionr.Status.StatusEntry _statusEntry;

        public StatusEntryVM(Versionr.Status.StatusEntry statusEntry)
        {
            _statusEntry = statusEntry;
        }

        public Versionr.StatusCode Code
        {
            get { return _statusEntry.Code; }
        }

        public bool Staged
        {
            get { return _statusEntry.Staged; }
        }

        public string Name
        {
            get { return _statusEntry.Name; }
        }

        public bool IsDirectory
        {
            get { return _statusEntry.IsDirectory; }
        }
    }
}
