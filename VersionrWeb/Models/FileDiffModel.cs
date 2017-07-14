using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace VersionrWeb.Models
{
    public class FileDiffModel
    {
        public string RawLink;

        public string Name;
        public long Size;

        public bool IsContentImage;
        public bool IsContentText;
        public string ContentText;

        public string HighlightClass;

        public FileDiffModel(Area area, Versionr.Objects.Version version, Versionr.Objects.Record record)
        {
            Name = Path.GetFileName(record.CanonicalName);
            HighlightClass = "text";
            if (record.IsDirectory)
            {
                IsContentText = true;
                ContentText = "(directory)";
                return;
            }
            Guid oldVersionID = Guid.Empty;
            Size = record.Size;
            Versionr.Objects.Record oldRecord = null;
            if (version.Parent != null)
            {
                Versionr.Objects.Version previous = area.GetVersion(version.Parent.Value);
                oldVersionID = previous.ID;
                oldRecord = area.GetRecords(previous).Where(x => x.CanonicalName == record.CanonicalName).First();
            }
            if (oldRecord == null)
            {
                IsContentText = true;
                ContentText = "(no previous version)";
                return;
            }
            area.GetMissingObjects(new Versionr.Objects.Record[] { record, oldRecord }, null);
            using (var stream = area.ObjectStore.GetRecordStream(record))
            using (var streamOld = area.ObjectStore.GetRecordStream(oldRecord))
            {
                if (Versionr.Utilities.FileClassifier.Classify(stream) == Versionr.Utilities.FileEncoding.Binary)
                {
                    IsContentText = true;
                    ContentText = "(binary differences)";
                    return;
                }
                HighlightClass = "patch";
                stream.Position = 0;
                IsContentText = true;
                var diffLines = Versionr.Utilities.DiffFormatter.Run(stream, streamOld, record.CanonicalName + "@" + version.ID, record.CanonicalName + "@" + oldVersionID, true, false);
                StringBuilder sb = new StringBuilder();
                foreach (var x in diffLines)
                    sb.AppendLine(x);
                ContentText = sb.ToString();
            }
        }

        private static Encoding GuessEncoding(Stream stream)
        {
            // Read Unicode byte order mark and reset stream
            byte[] bom = new byte[4];
            stream.Read(bom, 0, 4);
            stream.Position = 0;

            if (bom[0] == 0x2b && bom[1] == 0x2f && bom[2] == 0x76) return Encoding.UTF7;
            if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf) return Encoding.UTF8;
            if (bom[0] == 0xff && bom[1] == 0xfe) return Encoding.Unicode; //UTF-16LE
            if (bom[0] == 0xfe && bom[1] == 0xff) return Encoding.BigEndianUnicode; //UTF-16BE
            if (bom[0] == 0 && bom[1] == 0 && bom[2] == 0xfe && bom[3] == 0xff) return Encoding.UTF32;

            // No BOM, check if first four bytes are ASCII printable
            foreach (byte b in bom)
            {
                if ((b < 32 || b > 127) && (b != 0xD && b != 0xA))
                    return null;
            }

            // Assume UTF-8
            return Encoding.UTF8;
        }

    }
}
