using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Hooks
{
    public abstract class IHook
    {
        public abstract string Name { get; }
        public abstract string Username { get; }
        public abstract DateTime Timestamp { get; }
        public abstract bool IsServerHook { get; }
        public virtual Guid? VersionID
        {
            get
            {
                return null;
            }
        }
        public virtual Guid? PreviousVersionID
        {
            get
            {
                return null;
            }
        }
        public virtual Guid? BranchID
        {
            get
            {
                return null;
            }
        }
        public virtual Objects.Version Version
        {
            get
            {
                if (VersionID != null && Workspace != null)
                    return Workspace.GetVersion(VersionID.Value);
                return null;
            }
        }
        public virtual Objects.Version PreviousVersion
        {
            get
            {
                if (VersionID != null && Workspace != null)
                    return Workspace.GetVersion(PreviousVersionID.Value);
                return null;
            }
        }
        public virtual List<Objects.Version> AdditionalVersions
        {
            get
            {
                return null;
            }
        }
        public virtual Objects.Branch Branch
        {
            get
            {
                if (BranchID != null && Workspace != null)
                    return Workspace.GetBranch(BranchID.Value);
                return null;
            }
        }
        public virtual Area Workspace
        {
            get
            {
                return null;
            }
        }
        public virtual List<KeyValuePair<Objects.AlterationType, string>> Modifications
        {
            get
            {
                return null;
            }
        }
        public virtual string Message
        {
            get
            {
                return null;
            }
        }
        public IEnumerable<Objects.Version> AllVersions
        {
            get
            {
                if (Version != null)
                    yield return Version;
                var associated = AdditionalVersions;
                if (associated != null)
                {
                    foreach (var x in associated)
                        yield return x;
                }
            }
        }
        public IEnumerable<string> AllTags
        {
            get
            {
                if (Workspace == null)
                    return new string[0];
                return AllVersions.SelectMany(z => Workspace.GetTagsForVersion(z.ID));
            }
        }
    }
}
