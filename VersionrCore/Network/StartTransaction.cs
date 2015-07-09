using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    [ProtoBuf.ProtoContract]
    class StartTransaction
    {
        [ProtoBuf.ProtoMember(1)]
        public Handshake ServerHandshake { get; set; }

        [ProtoBuf.ProtoMember(2)]
        public string Domain { get; set; }
        
        [ProtoBuf.ProtoMember(3)]
        public string PublicKeyJSON { get; set; }

        [ProtoBuf.ProtoIgnore]
        public System.Security.Cryptography.RSAParameters RSAKey
        {
            get
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<System.Security.Cryptography.RSAParameters>(PublicKeyJSON);
            }
            set
            {
                PublicKeyJSON = Newtonsoft.Json.JsonConvert.SerializeObject(value);
            }
        }

        public static StartTransaction Create(string domain, System.Security.Cryptography.RSAParameters publicKey)
        {
            return new StartTransaction() { ServerHandshake = Handshake.Create(), Domain = domain, RSAKey = publicKey };
        }
    }
}
