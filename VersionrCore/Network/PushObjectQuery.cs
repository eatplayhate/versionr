using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    public enum ObjectType
    {
        Branch,
        Version,
    }
    [ProtoBuf.ProtoContract]
    class PushObjectQuery
    {
        [ProtoBuf.ProtoMember(1)]
        public ObjectType Type { get; set; }

        [ProtoBuf.ProtoMember(2)]
        public string[] IDs { get; set; }
    }
    [ProtoBuf.ProtoContract]
    class PushObjectResponse
    {
        [ProtoBuf.ProtoMember(1)]
        public bool[] Recognized { get; set; }
    }

    [ProtoBuf.ProtoContract]
    class PushHead
    {
        [ProtoBuf.ProtoMember(1)]
        public Guid BranchID { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public Guid VersionID { get; set; }
    }

    [ProtoBuf.ProtoContract]
    class PushBranches
    {
        [ProtoBuf.ProtoMember(1)]
        public Objects.Branch[] Branches { get; set; }
    }

    [Compressible]
    [ProtoBuf.ProtoContract]
    class VersionPack
    {
        [ProtoBuf.ProtoMember(1)]
        public VersionInfo[] Versions { get; set; }
    }

    [ProtoBuf.ProtoContract]
    class VersionInfo
    {
        [ProtoBuf.ProtoMember(1)]
        public Objects.Version Version { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public FusedAlteration[] Alterations { get; set; }
        [ProtoBuf.ProtoMember(3)]
        public Objects.MergeInfo[] MergeInfos { get; set; }
    }

    [ProtoBuf.ProtoContract]
    class FusedAlteration
    {
        [ProtoBuf.ProtoMember(1)]
        public Objects.AlterationType Alteration { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public Objects.Record NewRecord { get; set; }
        [ProtoBuf.ProtoMember(3)]
        public Objects.Record PriorRecord { get; set; }
    }

    [Compressible]
    [ProtoBuf.ProtoContract]
    class RequestRecordParents
    {
        [ProtoBuf.ProtoMember(1)]
        public long[] RecordParents { get; set; }
    }

    [Compressible]
    [ProtoBuf.ProtoContract]
    class RequestRecordData
    {
        [ProtoBuf.ProtoMember(1)]
        public long[] Records { get; set; }
    }

    [Compressible]
    [ProtoBuf.ProtoContract]
    class RequestRecordDataUnmapped
    {
        [ProtoBuf.ProtoMember(1)]
        public string[] RecordDataKeys { get; set; }
    }

    [Compressible]
    [ProtoBuf.ProtoContract]
    class RecordParentPack
    {
        [ProtoBuf.ProtoMember(1)]
        public Objects.Record[] Parents { get; set; }
    }

    [ProtoBuf.ProtoContract]
    class DataPayload
    {
        [ProtoBuf.ProtoMember(1)]
        public byte[] Data { get; set; }
        [ProtoBuf.ProtoMember(2)]
        public bool EndOfStream { get; set; }
    }
}
