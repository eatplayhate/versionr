using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;

namespace Versionr.ObjectStore
{
    enum StorageMode
    {
        Legacy,
        Flat,
        Packed,
        Delta
    }
    class PackfileObject
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public Guid ID { get; set; }
    }
    class FileObjectStoreData
    {
        [SQLite.PrimaryKey]
        public string Lookup { get; set; }
        public long FileSize { get; set; }
        public long Offset { get; set; }
        public StorageMode Mode { get; set; }
        public bool HasSignatureData { get; set; }
        public Guid? PackFileID { get; set; }
    }
    class StandardObjectStoreMetadata
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public int Id { get; set; }
        public int Version { get; set; }
    }
    public class StandardObjectStore : ObjectStoreBase
    {
        Area Owner { get; set; }
        SQLite.SQLiteConnection ObjectDatabase { get; set; }
        System.IO.DirectoryInfo DataFolder
        {
            get
            {
                return new DirectoryInfo(Path.Combine(Owner.AdministrationFolder.FullName, "objects"));
            }
        }
        System.IO.FileInfo DataFile
        {
            get
            {
                return new FileInfo(Path.Combine(DataFolder.FullName, "store.db"));
            }
        }
        public void Create(Area owner)
        {
            Owner = owner;
            DataFolder.Create();
            ObjectDatabase = new SQLite.SQLiteConnection(DataFile.FullName, SQLite.SQLiteOpenFlags.NoMutex | SQLite.SQLiteOpenFlags.Create | SQLite.SQLiteOpenFlags.ReadWrite);
            InitializeDBTypes();

            var meta = new StandardObjectStoreMetadata();
            meta.Version = 1;
            ObjectDatabase.Insert(meta);
        }

        private void InitializeDBTypes()
        {
            ObjectDatabase.EnableWAL = true;
            ObjectDatabase.CreateTable<FileObjectStoreData>();
            ObjectDatabase.CreateTable<PackfileObject>();
            ObjectDatabase.CreateTable<StandardObjectStoreMetadata>();
        }

        public bool Open(Area owner)
        {
            Owner = owner;
            if (!DataFolder.Exists)
                return false;
            ObjectDatabase = new SQLite.SQLiteConnection(DataFile.FullName, SQLite.SQLiteOpenFlags.Create | SQLite.SQLiteOpenFlags.NoMutex | SQLite.SQLiteOpenFlags.ReadWrite);
            InitializeDBTypes();

            var version = ObjectDatabase.Table<StandardObjectStoreMetadata>().FirstOrDefault();
            if (version == null)
            {
                ObjectDatabase.BeginExclusive();
                Printer.PrintMessage("Upgrading object store database...");
                var records = owner.GetAllRecords();
                foreach (var x in records)
                {
                    ImportRecordFromFlatStore(x);
                }
                var meta = new StandardObjectStoreMetadata();
                meta.Version = 1;
                ObjectDatabase.Insert(meta);
                ObjectDatabase.Commit();
            }
            return true;
        }

        private void ImportRecordFromFlatStore(Record x)
        {
            if (x.HasData)
            {
                var recordData = new FileObjectStoreData();
                recordData.FileSize = x.Size;
                recordData.HasSignatureData = false;
                recordData.Lookup = x.DataIdentifier;
                recordData.Mode = StorageMode.Legacy;
                recordData.Offset = 0;
                recordData.PackFileID = null;
                try
                {
                    ObjectDatabase.Insert(recordData);
                }
                catch (SQLite.SQLiteException e)
                {
                    if (e.Result != SQLite.SQLite3.Result.Constraint)
                        throw;
                }
            }
        }

        public bool RecordData(Record newRecord, Record priorRecord, Entry fileEntry)
        {
            throw new NotImplementedException();
        }

        public bool RestoreData(Record record, string pathOverride = null)
        {
            throw new NotImplementedException();
        }

        ObjectStoreTransaction ObjectStoreBase.BeginStorageTransaction()
        {
            throw new NotImplementedException();
        }

        public bool HasData(Record recordInfo)
        {
            throw new NotImplementedException();
        }

        public bool EndStorageTransaction(ObjectStoreTransaction transaction)
        {
            throw new NotImplementedException();
        }
    }
}
