using System;
using System.Collections.Generic;
using System.Linq;
using Versionr;
using Versionr.Objects;
using VersionrUI.Commands;
using Version = Versionr.Objects.Version;

namespace VersionrUI.ViewModels
{
    public class VersionVM : NotifyPropertyChangedBase
    {
        private readonly Version m_Version;
        private readonly Area m_Area;
        private List<AlterationVM> m_Alterations;
        private string m_SearchText;
        public DelegateCommand CopyInfoCommand { get; private set; }
        public DelegateCommand GeneratePatchFileCommand { get; private set; }
        public DelegateCommand CreateReviewCommand { get; private set; }
        private AlterationVM m_SelectedAlteration;


        public VersionVM(Version version, Area area)
        {
            m_Version = version;
            m_Area = area;
            CopyInfoCommand = new DelegateCommand(CopyInfo);
            GeneratePatchFileCommand = new DelegateCommand(GeneratePatchFile);
            CreateReviewCommand = new DelegateCommand(CreateReview);
        }

        public Guid ID
        {
            get { return m_Version.ID; }
        }

        public string ShortName
        {
            get { return m_Version.ShortName; }
        }

        public string Author
        {
            get { return m_Version.Author; }
        }

        public string Message
        {
            get { return m_Version.Message; }
        }

        public DateTime Timestamp
        {
            get { return m_Version.Timestamp.ToLocalTime(); }
        }

        public bool IsCurrent
        {
            get { return m_Version.ID == m_Area.Version.ID; }
        }

        public uint Revision
        {
            get { return m_Version.Revision; }
        }

        public Guid? Parent
        {
            get { return m_Version.Parent; }
        }

        public Guid Branch
        {
            get { return m_Version.Branch; }
        }

        public string SearchText
        {
            get { return m_SearchText; }
            set
            {
                m_SearchText = value;
                NotifyPropertyChanged(nameof(SearchText));
                NotifyPropertyChanged(nameof(Alterations));
            }
        }

        public List<AlterationVM> Alterations
        {
            get
            {
                if (m_Alterations == null)
                    Refresh();
                return !string.IsNullOrEmpty(m_SearchText) ? FilterAlterations(m_SearchText, m_Alterations) : m_Alterations;
            }
        }

        public AlterationVM SelectedAlteration
        {
            get { return m_SelectedAlteration; }
            set
            {
                m_SelectedAlteration = value;
                NotifyPropertyChanged(nameof(SelectedAlteration));
            }
        }

        public List<AlterationVM> FilterAlterations(string searchtext, List<AlterationVM> alterations)
        {
            searchtext = searchtext.ToLower();
            return alterations.Where(alteration => alteration.Name.ToLower().Contains(searchtext)).ToList();
        }
        
        private static readonly object refreshLock = new object();
        private void Refresh()
        {
            lock (refreshLock)
            {
                List<Alteration> alterations = m_Area.GetAlterations(m_Version);
                m_Alterations = new List<AlterationVM>();
                
                List<AlterationVM> unordered = new List<AlterationVM>(alterations.Count);
                unordered.AddRange(alterations.Select(alteration => new AlterationVM(alteration, m_Area, m_Version)));

                foreach (AlterationVM vm in unordered.OrderBy(x => x.Name))
                    m_Alterations.Add(vm);

                NotifyPropertyChanged(nameof(Alterations));
            }
        }

        private void CopyInfo()
        {
            var versionInfo =
                $"ID: {ID}\nAuthor: {Author}\nMessage: {Message}\nDate/Time: {Timestamp.ToString()}\n\nFile(s) changed: \n\n";
            versionInfo = Alterations.Aggregate(versionInfo, (current, alteration) => current + $"{alteration.Name}\t{alteration.AlterationType}\n");
            System.Windows.Clipboard.SetDataObject(versionInfo);
        }

        private void GeneratePatchFile()
        {
            if (!Alterations.Any())
                return;
            List<string> allfiles = Alterations.Select(x => x.Name).ToList();
            // Diff with the previous version
            Utilities.GeneratePatchFile(allfiles, m_Area.Root.FullName, m_Version.ID.ToString());
        }

        private void CreateReview()
        {
            string url = $"https://bucktr.ea.com:44320/pullrequests/Create?repo={Utilities.RepoName}&version={ID}";
            System.Diagnostics.Process.Start(url);
        }
    }
}
