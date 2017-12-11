using System;
using System.Collections.Generic;
using System.Linq;
using Versionr;
using Versionr.Objects;
using Version = Versionr.Objects.Version;

namespace VersionrUI.ViewModels
{
    public class VersionVM : NotifyPropertyChangedBase
    {
        private readonly Version m_Version;
        private readonly Area m_Area;
        private List<AlterationVM> m_Alterations;
        private string m_SearchText;
        
        public VersionVM(Version version, Area area)
        {
            m_Version = version;
            m_Area = area;
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
    }
}
