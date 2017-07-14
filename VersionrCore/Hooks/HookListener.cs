using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Hooks
{
    public class HookListener
    {
        public string Event { get; set; }
        public Type EventType { get; set; }
        public IHookAction Action { get; set; }
        public List<KeyValuePair<string, IHookFilter>> Filters { get; set; }
        public bool Raise(IHook hook)
        {
            if (Filters == null || Filters.Count == 0)
            {
                return Action.Raise(hook, null);
            }
            else
            {
                foreach (var x in Filters)
                {
                    if (x.Value.Accept(hook))
                    {
                        return Action.Raise(hook, x.Key);
                    }
                }
                return true;
            }
        }
        public HookListener(List<KeyValuePair<string, IHookFilter>> hookFilters)
        {
            if (hookFilters != null && hookFilters.Count != 0)
                Filters = hookFilters;
        }
    }
}
