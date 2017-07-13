using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;

namespace Versionr.Hooks
{
    public abstract class WorkspaceHook : IHook
    {
        DateTime m_Time = DateTime.Now;
        public override DateTime Timestamp => m_Time;
        public override bool IsServerHook => false;
        Area m_Area;
        public override Area Workspace => m_Area;

        public string m_Username;
        public override string Username => m_Username;
        public override Guid? VersionID => m_Version.ID;
        public override Objects.Version Version => m_Version;
        public override Guid? PreviousVersionID => m_OldVersion == null ? null : (Guid?)m_OldVersion.ID;
        public override Objects.Version PreviousVersion => m_OldVersion;
        public override Guid? BranchID => m_Branch.ID;
        public override Objects.Branch Branch => m_Branch;

        Objects.Version m_Version;
        Objects.Version m_OldVersion;
        Objects.Branch m_Branch;
        public WorkspaceHook(Area area, string username, Objects.Branch branch, Objects.Version v, Objects.Version old = null)
            : base()
        {
            m_Area = area;
            m_Username = username;
            m_Branch = branch;
            m_Version = v;
            m_OldVersion = old;
        }
    }
    public class PreCommitHook : WorkspaceHook
    {
        public static string EventName
        {
            get
            {
                return "PreCommit";
            }
        }
        public override string Name => EventName;
        public override List<KeyValuePair<AlterationType, string>> Modifications => m_Modifications;
        public override string Message => m_Message;
        string m_Message;
        List<KeyValuePair<AlterationType, string>> m_Modifications;
        public PreCommitHook(Area ws, string username, Objects.Branch branch, Objects.Version version, string message, List<KeyValuePair<AlterationType, string>> modifications)
            : base(ws, username, branch, version)
        {
            m_Modifications = modifications;
            m_Message = message;
        }
    }
    public class PostCommitHook : WorkspaceHook
    {
        public static string EventName
        {
            get
            {
                return "PostCommit";
            }
        }
        public override string Name => EventName;
        public override List<KeyValuePair<AlterationType, string>> Modifications => m_Modifications;
        public override string Message => Version.Message;
        string m_Message;
        List<KeyValuePair<AlterationType, string>> m_Modifications;
        public PostCommitHook(Area ws, string username, Objects.Branch branch, Objects.Version version, Objects.Version old, List<KeyValuePair<AlterationType, string>> modifications)
            : base(ws, username, branch, version, old)
        {
            m_Modifications = modifications;
        }
    }
}
