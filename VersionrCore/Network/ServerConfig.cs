using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    class ServerConfig
    {
        public SimpleAuth SimpleAuthentication { get; set; }
        public Utilities.ChecksumCodec ChecksumType { get; set; }
        public bool Encrypted { get; set; }
        public int AuthenticationAttempts { get; set; }
        public bool AllowUnauthenticatedRead { get; set; }
        public bool AllowUnauthenticatedWrite { get; set; }
        public Dictionary<string, string> Domains { get; set; }
        public bool? IncludeRoot { get; set; }
        public bool AllowVaultCreation { get; set; }
        public string AutoDomains { get; set; }
        public WebConfig WebService { get; set; }
        public bool RequiresAuthentication
        {
            get
            {
                return SimpleAuthentication != null;
            }
        }

        public bool SupportsSimpleAuthentication
        {
            get
            {
                return SimpleAuthentication != null;
            }
        }

        public ServerConfig()
        {
            ChecksumType = Utilities.ChecksumCodec.Default;
            Encrypted = true;
            AuthenticationAttempts = 3;
            Domains = new Dictionary<string, string>();
        }

        internal AuthEntry GetSimpleLogin(string identifierToken)
        {
            AuthEntry result = null;
            if (SimpleAuthentication != null && SimpleAuthentication.Users.TryGetValue(identifierToken, out result))
                return result;
            return SimpleAuthentication.Users.Where(x => x.Key.Equals(identifierToken, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).FirstOrDefault();
        }
    }
}
