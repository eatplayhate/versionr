using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Hooks
{
    public interface IHookFilter
    {
        bool Accept(IHook hook);
    }
}
