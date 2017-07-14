using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Versionr.Hooks.Filters
{
    public class ContentsFilter : IHookFilter
    {
        public static string FilterName
        {
            get
            {
                return "Modifications";
            }
        }
        public System.Text.RegularExpressions.Regex m_Match;
        public bool Accept(IHook hook)
        {
            var modifications = hook.Modifications;
            if (modifications != null)
            {
                return modifications.Any(x => m_Match.IsMatch(x.Value));
            }
            return false;
        }
        public ContentsFilter(Newtonsoft.Json.Linq.JObject param)
        {
            m_Match = new Regex(param["Path"].ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }
    }
}
