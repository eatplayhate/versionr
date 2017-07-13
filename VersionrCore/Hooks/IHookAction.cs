using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Hooks
{
    public interface IHookAction
    {
        bool Raise(IHook hook);
    }
}
