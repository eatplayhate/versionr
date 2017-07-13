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
    }
}
