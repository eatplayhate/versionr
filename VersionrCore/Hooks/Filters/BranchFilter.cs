using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Versionr.Hooks.Filters
{
    public class BranchFilter : IHookFilter
    {
        public static string FilterName
        {
            get
            {
                return "Branch";
            }
        }
        public System.Text.RegularExpressions.Regex m_Match;
        public bool Accept(IHook hook)
        {
            var branch = hook.Branch;
            if (branch != null)
            {
                return m_Match.IsMatch(branch.Name) || m_Match.IsMatch(branch.ID.ToString());
            }
            return false;
        }
        public BranchFilter(Newtonsoft.Json.Linq.JObject param)
        {
            m_Match = new Regex(param["Name"].ToString(), RegexOptions.Singleline);
        }
    }
}
