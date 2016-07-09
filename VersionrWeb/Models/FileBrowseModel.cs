using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace VersionrWeb.Models
{
	public class FileBrowseModel
	{
		public string Name;
		public long Size;

		public bool IsContentText;
		public string ContentText;

		public string HighlightClass;

		public FileBrowseModel(Area area, Versionr.Objects.Version version, Versionr.Objects.Record record)
		{
			Name = Path.GetFileName(record.CanonicalName);
			Size = record.Size;

			area.GetMissingRecords(new Versionr.Objects.Record[] { record });
			using (var stream = area.ObjectStore.GetRecordStream(record))
			{
				stream.Position = 0;
				var encoding = GuessEncoding(stream);
				if (encoding == null)
				{
					IsContentText = false;
				}
				else
				{
					IsContentText = true;
					byte[] bytes = new byte[stream.Length];
					stream.Read(bytes, 0, bytes.Length);
					ContentText = encoding.GetString(bytes);
					HighlightClass = GuessHighlightClass(record.CanonicalName, ContentText);
				}
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
				if (b < 32 || b > 127)
					return null;
			}

			// Assume UTF-8
			return Encoding.UTF8;
		} 

		private static string GuessHighlightClass(string filename, string text)
		{
			switch (Path.GetExtension(filename).ToLower())
			{
				case ".cs":	return "cs";
				default: return "";
			}
		}


	}
}
