using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    public enum AuthenticationMode
    {
        Simple,
        LDAP,
        Guest
    }

    [ProtoBuf.ProtoContract]
    class AuthenticationChallenge
    {
        [ProtoBuf.ProtoMember(1)]
        public List<AuthenticationMode> AvailableModes { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public string Salt { get; set; }
    }

    [ProtoBuf.ProtoContract]
    class AuthenticationResponse
    {
        [ProtoBuf.ProtoMember(1)]
        public AuthenticationMode Mode { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public string IdentifierToken { get; set; }
        [ProtoBuf.ProtoMember(3)]
        public byte[] Payload { get; set; }
    }
}
