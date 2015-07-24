﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Objects
{
    [Flags]
    public enum Attributes
    {
        None = 0,
        TextANSI = 1,
        TextUTF8 = 2,
        TextUTF16 = 4,
        TextUTF16BE = 8,
        Binary = 16,

        ReadOnly = 32,
        Executable = 64,
        Hidden = 128,
		Symlink = 256,
    }
    [ProtoBuf.ProtoContract]
    public class Record
    {
        [ProtoBuf.ProtoMember(1)]
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }
        [ProtoBuf.ProtoIgnore]
        public long? Parent { get; set; }
        [ProtoBuf.ProtoMember(2)]
        [SQLite.NotNull]
        public long Size { get; set; }
        [ProtoBuf.ProtoMember(3)]
        public Attributes Attributes { get; set; }
        [ProtoBuf.ProtoMember(4)]
        [SQLite.Indexed]
        public string Fingerprint { get; set; }
        [ProtoBuf.ProtoIgnore]
        public long CanonicalNameId { get; set; }
        [ProtoBuf.ProtoMember(5)]
        public DateTime ModificationTime { get; set; }

        public bool DataEquals(Record other)
        {
            return other.Size == Size && other.Fingerprint == Fingerprint;
        }

        [ProtoBuf.ProtoMember(6)]
        [SQLite.LoadOnly]
        public string CanonicalName { get; set; }

		[SQLite.Ignore]
		public bool IsDirectory
		{
			get
			{
				return !IsSymlink && CanonicalName.EndsWith("/");
			}
		}
		[SQLite.Ignore]
		public bool IsSymlink
		{
			get
			{
				return Attributes.HasFlag(Objects.Attributes.Symlink);
			}
		}

		[ProtoBuf.ProtoIgnore]
        [SQLite.Ignore]
        public string DataIdentifier
        {
            get
            {
                return Fingerprint + "-" + Size.ToString();
            }
        }

        [ProtoBuf.ProtoIgnore]
        [SQLite.Ignore]
        public string Name
        {
            get
            {
                int index = CanonicalName.LastIndexOf('/');
                if (index == -1)
                    return CanonicalName;
                return CanonicalName.Substring(index + 1);
            }
        }

        [ProtoBuf.ProtoIgnore]
        [SQLite.Ignore]
        public string UniqueIdentifier
        {
            get
            {
                return DataIdentifier + "-" + ((int)Attributes).ToString();
            }
        }

        [SQLite.Ignore]
        public bool HasData
        {
            get
            {
                return Size > 0;
            }
        }
    }

    public class RecordIndex
    {
        [SQLite.Indexed]
        public string DataIdentifier { get; set; }
        public long Index { get; set; }
        public bool Pruned { get; set; }
    }
}
