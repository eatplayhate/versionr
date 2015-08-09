using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    [Flags]
    public enum Rights
    {
        Read = 1,
        Write = 2
    }
    public class AuthEntry
    {
        public string Password { get; set; }
        public Rights Access { get; set; }

        public AuthEntry()
        {
            Access = Rights.Read | Rights.Write;
        }
    }
    public class SimpleAuth
    {
        public Dictionary<string, AuthEntry> Users { get; set; }
        public SimpleAuth()
        {
            Users = new Dictionary<string, AuthEntry>();
        }
    }
}
