using System;
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
        RequestRecordUnmapped,
        Synchronized,
        Push,
        Close,
        Acknowledge,
        DataReceived,
        Error,
        Clone,
        PullVersions,
        FullClone,
        QueryBranchID,
        PushBranchJournal,
        PushInitialVersion,
        Authenticate,
        SkipAuthentication,
        AuthRetry,
        AuthFail,
        ListBranches
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
