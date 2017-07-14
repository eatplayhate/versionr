using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;

namespace Versionr.Network
{
    public abstract class ServerHook : Hooks.IHook
    {
        DateTime m_Time = DateTime.Now;
        public override DateTime Timestamp => m_Time;

        public override bool IsServerHook => true;
    }

    public abstract class ServerWorkspaceHook : ServerHook
    {
        Area m_Area;
        public ServerWorkspaceHook(Area area, string username)
            : base()
        {
            m_Area = area;
            m_Username = username;
        }
        public override Area Workspace => m_Area;

        public string m_Username;
        public override string Username => m_Username;
    }

    public class PushHeadHook : ServerWorkspaceHook
    {
        public static string EventName
        {
            get
            {
                return "PushHead";
            }
        }

        public override string Name => EventName;

        public Lazy<List<KeyValuePair<Objects.AlterationType, string>>> m_Modifications;
        public override List<KeyValuePair<Objects.AlterationType, string>> Modifications => m_Modifications.Value;
        public override Guid? VersionID => m_NewHead.ID;
        public override Objects.Version Version => m_NewHead;
        public override Guid? PreviousVersionID => m_OldHead.ID;
        public override Objects.Version PreviousVersion => m_OldHead;
        public override Guid? BranchID => m_Branch.ID;
        public override Objects.Branch Branch => m_Branch;
        public override List<Objects.Version> AdditionalVersions => m_AdditionalVersions.Value;

        Objects.Version m_NewHead;
        Objects.Version m_OldHead;
        Objects.Branch m_Branch;
        Lazy<List<Objects.Version>> m_AdditionalVersions;

        public PushHeadHook(Area ws, string username, Objects.Branch branch, Objects.Version newHead, Objects.Version oldHead, List<Objects.Version> consumedVersions)
            : base(ws, username)
        {
            m_Branch = branch;
            m_NewHead = newHead;
            m_OldHead = oldHead;
            m_AdditionalVersions = new Lazy<List<Objects.Version>>(() => { return consumedVersions.Distinct().ToList(); });
            m_Modifications = new Lazy<List<KeyValuePair<Objects.AlterationType, string>>>(() =>
            {
                Dictionary<string, string> oldRecords = new Dictionary<string, string>();
                HashSet<string> newRecords = new HashSet<string>();
                if (oldHead != null)
                {
                    foreach (var rec in ws.GetRecords(ws.GetVersion(oldHead.ID)))
                    {
                        oldRecords.Add(rec.CanonicalName, rec.Fingerprint);
                    }
                }
                List<KeyValuePair<Objects.AlterationType, string>> results = new List<KeyValuePair<Objects.AlterationType, string>>();
                if (newHead != null)
                {
                    foreach (var rec in ws.GetRecords(ws.GetVersion(newHead.ID)))
                    {
                        string oldfp;
                        if (oldRecords.TryGetValue(rec.CanonicalName, out oldfp))
                        {
                            if (oldfp != rec.Fingerprint)
                                results.Add(new KeyValuePair<Objects.AlterationType, string>(Objects.AlterationType.Update, rec.CanonicalName));
                        }
                        else
                            results.Add(new KeyValuePair<Objects.AlterationType, string>(Objects.AlterationType.Add, rec.CanonicalName));
                        newRecords.Add(rec.CanonicalName);
                    }
                }
                foreach (var x in oldRecords)
                {
                    if (!newRecords.Contains(x.Key))
                        results.Add(new KeyValuePair<Objects.AlterationType, string>(Objects.AlterationType.Delete, x.Key));
                }
                return results;
            });
        }
    }
}
