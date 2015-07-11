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
        Flat,
        Packed,
        Delta
    }
    class FileObjectStoreData
    {
        [SQLite.PrimaryKey]
        public string Lookup { get; set; }
        public long FileSize { get; set; }
        public long Offset { get; set; }
        public StorageMode Mode { get; set; }
        public Guid PartialStoreFile { get; set; }
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
            ObjectDatabase = new SQLite.SQLiteConnection(DataFile.FullName, SQLite.SQLiteOpenFlags.FullMutex | SQLite.SQLiteOpenFlags.Create | SQLite.SQLiteOpenFlags.ReadWrite);
            InitializeDBTypes();
        }

        private void InitializeDBTypes()
        {
            return;
        }

        public bool Open(Area owner)
        {
            Owner = owner;
            if (!DataFolder.Exists)
                return false;
            ObjectDatabase = new SQLite.SQLiteConnection(DataFile.FullName, SQLite.SQLiteOpenFlags.FullMutex | SQLite.SQLiteOpenFlags.ReadWrite);
            InitializeDBTypes();
            return true;
        }

        public bool RecordData(Record newRecord, Record priorRecord, Entry fileEntry)
        {
            throw new NotImplementedException();
        }

        public bool RestoreData(Record record, string pathOverride = null)
        {
            throw new NotImplementedException();
        }

        public bool BeginStorageTransaction()
        {
            throw new NotImplementedException();
        }

        public bool EndStorageTransaction()
        {
            throw new NotImplementedException();
        }
    }
}
