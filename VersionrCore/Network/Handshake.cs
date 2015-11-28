using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProtoBuf;

namespace Versionr.Network
{
    [ProtoContract]
    class Handshake
    {
        [ProtoMember(1)]
        public string VersionrProtocol { get; set; }

        [ProtoMember(2)]
        public string RequestedModule { get; set; }

        public static Dictionary<SharedNetwork.Protocol, string> Protocols;
        
        static Handshake()
        {
            Protocols = new Dictionary<SharedNetwork.Protocol, string>();
            Protocols[SharedNetwork.Protocol.Versionr281] = "Versionr/Protocol:2.8.1";
            Protocols[SharedNetwork.Protocol.Versionr29] = "Versionr/Protocol:2.9";
            Protocols[SharedNetwork.Protocol.Versionr3] = "Versionr/Protocol:3.0";
            Protocols[SharedNetwork.Protocol.Versionr31] = "Versionr/Protocol:3.1";
            Protocols[SharedNetwork.Protocol.Versionr32] = "Versionr/Protocol:3.2";
        }

        public static string GetProtocolString(SharedNetwork.Protocol protocol)
        {
            return Protocols[protocol];
        }

        public SharedNetwork.Protocol? CheckProtocol()
        {
            foreach (var x in Protocols)
            {
                if (VersionrProtocol == x.Value)
                    return x.Key;
            }
            return null;
        }
        public static Handshake Create(SharedNetwork.Protocol protocol)
        {
            return new Handshake() { VersionrProtocol = GetProtocolString(protocol) };
        }
    }
}
