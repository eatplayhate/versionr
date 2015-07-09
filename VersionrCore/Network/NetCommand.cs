﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    public enum NetCommandType
    {
        PushObjectQuery,
        PushBranch,
        PushVersions,
        SynchronizeRecords,
        PushHead,
        AcceptPush,
        RejectPush,
        RequestRecordParents,
        RequestRecord,
        Synchronized,
        Push,
        Close,
        Acknowledge,
        DataReceived,
        Error,
    }
    [ProtoBuf.ProtoContract]
    class NetCommand
    {
        [ProtoBuf.ProtoMember(1)]
        public NetCommandType Type { get; set; }

        [ProtoBuf.ProtoMember(2)]
        public string AdditionalPayload { get; set; }

        [ProtoBuf.ProtoMember(3)]
        public long Identifier { get; set; }
    }
}
