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

        static string InternalProtocol
        {
            get
            {
                return "Versionr/Protocol:2.8";
            }
        }

        public bool Valid
        {
            get
            {
                return VersionrProtocol == InternalProtocol;
            }
        }
        public static Handshake Create()
        {
            return new Handshake() { VersionrProtocol = InternalProtocol };
        }
    }
}
