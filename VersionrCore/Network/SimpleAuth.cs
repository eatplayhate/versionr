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
        Write = 2,
        Create = 4
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

        internal bool CheckUser(string name, string password)
        {
            AuthEntry e = null;
            if (!Users.TryGetValue(name, out e))
                e = Users.Where(x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).FirstOrDefault();
            if (e != null)
                return e.Password == password;
            return false;
        }
    }
}
