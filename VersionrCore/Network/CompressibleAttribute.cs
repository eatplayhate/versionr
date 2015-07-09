using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    internal sealed class CompressibleAttribute : Attribute
    {
        public CompressibleAttribute()
        {
        }
    }
}
