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
		public string RawLink;

		public string Name;
		public long Size;

		public bool IsContentImage;
		public bool IsContentText;
        public bool IsContentPDF = false;
        public string ContentText;

		public string HighlightClass;

		public FileBrowseModel(Area area, Versionr.Objects.Version version, Versionr.Objects.Record record)
		{
			Name = Path.GetFileName(record.CanonicalName);
			Size = record.Size;

			area.GetMissingObjects(new Versionr.Objects.Record[] { record }, null);
			using (var stream = area.ObjectStore.GetRecordStream(record))
			{
				stream.Position = 0;
                var fileEncoding = Versionr.Utilities.FileClassifier.Classify(stream);

                if (fileEncoding == Versionr.Utilities.FileEncoding.Binary)
				{
					IsContentText = false;
					IsContentImage = GuessIsContentImage(record.CanonicalName);
				}
				else
				{
					IsContentText = true;
					byte[] bytes = new byte[stream.Length];
					stream.Read(bytes, 0, bytes.Length);
					ContentText = Versionr.Utilities.FileClassifier.GetEncoding(fileEncoding).GetString(bytes);
					HighlightClass = GuessHighlightClass(record.CanonicalName);
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
				if ((b < 32 || b > 127) && (b != 0xD && b != 0xA))
					return null;
			}

			// Assume UTF-8
			return Encoding.UTF8;
		} 

		private static string GuessHighlightClass(string filename)
		{
			// Hint only; highlight.js will guess from content if we return nothing.
			switch (Path.GetExtension(filename).ToLower())
			{
				case ".cs":
					return "cs";
				case ".c":
				case ".cpp":
				case ".h":
					return "cpp";
				case ".m":
				case ".mm":
					return "objc";
				case ".lua":
					return "lua";
				case ".java":
					return "java";
				case ".js":
					return "js";
				case ".css":
					return "css";
				case ".php":
					return "php";
				case ".diff":
				case ".patch":
					return "patch";
				case ".cshtml":
				case ".htm":
				case ".html":
				case ".xml":
				case ".xaml":
					return ".xml";
				case ".md":
					return "markdown";
				case ".sh":
					return "bash";
				case ".py":
					return "python";
				case ".ps1":
					return "powershell";
				default: return "";
			}
		}

		private static bool GuessIsContentImage(string filename)
		{
			switch (Path.GetExtension(filename).ToLower())
			{
				case ".png":
				case ".jpg":
				case ".jpeg":
					return true;
			}

			return false;
		}

	}
}
