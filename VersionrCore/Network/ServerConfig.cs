﻿using System;
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
            ChecksumType = Utilities.ChecksumCodec.MurMur3;
            Encrypted = true;
            AuthenticationAttempts = 3;
        }

        internal AuthEntry GetSimpleLogin(string identifierToken)
        {
            AuthEntry result = null;
            if (SimpleAuthentication != null && SimpleAuthentication.Users.TryGetValue(identifierToken, out result))
                return result;
            return null;
        }
    }
}