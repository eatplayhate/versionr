using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Utilities
{
    public enum FileEncoding
    {
        Binary,
        ASCII,
        Latin1,
        UTF7,
        UTF7_BOM,
        UTF8,
        UTF8_BOM,
        UTF16_BE,
        UTF16_BE_BOM,
        UTF16_LE,
        UTF16_LE_BOM,
        UCS4_BE,
        UCS4_BE_BOM,
        UCS4_LE,
        UCS4_LE_BOM,
    }
    public class FileClassifier
    {
        static public FileEncoding Classify(System.IO.FileInfo info, int sampleSize = 8192)
        {
            byte[] data = new byte[(int)System.Math.Min(sampleSize, info.Length)];
            using (var fs = info.OpenRead())
                fs.Read(data, 0, data.Length);

            int bomLength;
            FileEncoding? BOM = CheckBOM(data, out bomLength);
            if (BOM.HasValue)
            {
                if (ValidateBOM(BOM.Value, data, bomLength))
                    return BOM.Value;
            }
            if (PossiblyASCIIText(data, 0))
                return FileEncoding.ASCII;
            if (PossiblyLatinText(data, 0))
                return FileEncoding.Latin1;
            if (PossiblyUTF8(data, 0))
                return FileEncoding.UTF8;
            if (PossiblyUTF16(data, 0, true))
                return FileEncoding.UTF16_BE;
            if (PossiblyUTF16(data, 0, false))
                return FileEncoding.UTF16_LE;
            if (PossiblyUCS4(data, 0, true))
                return FileEncoding.UCS4_BE;
            if (PossiblyUCS4(data, 0, false))
                return FileEncoding.UCS4_LE;
            return FileEncoding.Binary;
        }

        private static bool PossiblyASCIIText(byte[] data, int v)
        {
            for (int i = v; i < data.Length; i++)
            {
                byte c = data[i];
                if (InPrintableSet(c))
                    continue;
                return false;
            }
            return true;
        }

        private static bool PossiblyLatinText(byte[] data, int v)
        {
            for (int i = v; i < data.Length; i++)
            {
                byte c = data[i];
                if (InExtendedSet(c))
                    continue;
                return false;
            }
            return true;
        }

        private static bool InExtendedSet(byte c)
        {
            if (!InPrintableSet(c))
            {
                if (c >= 0xa0 && c <= 0xff)
                    return true;
                return false;
            }
            return true;
        }

        private static bool InPrintableSet(byte c)
        {
            if (c >= 0x20 && c <= 0x7E)
                return true;
            if (c == 0x09 || /* HT */
                c == 0x0A || /* LF */
                c == 0x0D)   /* CR */
                return true;
            return false;
        }

        private static bool PossiblyUTF8(byte[] data, int v)
        {
            int count = 0;
            for (int i = v; i < data.Length; i++)
            {
                byte c = data[i];
                if (c >= 0x80)
                {
                    c <<= 1;
                    if (c >= 0x80)
                    {
                        if (count != 0)
                            return false;
                        do
                        {
                            count++;
                            c <<= 1;
                        } while (c >= 0x80);
                    }
                    else
                        count--;
                    continue;
                }
                else
                {
                    if (count != 0)
                        return false;
                    if (InPrintableSet(c))
                        continue;
                }
                return false;
            }
            return true;
        }

        private static bool PossiblyUTF16(byte[] data, int v, bool bigEndian)
        {
            int count = 0;
            for (int i = v; i < data.Length - 1; i += 2)
            {
                ushort c = BitConverter.ToUInt16(data, i);
                if (bigEndian)
                    c = (ushort)((c & 0xFF) << 8 | ((c >> 8) & 0xFF));
                if (c >= 0xD800 && c <= 0xDBFF)
                {
                    if (count != 0)
                        return false;
                    count = 1;
                    continue;
                }
                else if (c >= 0xDC00 && c <= 0xDFFF)
                {
                    if (count != 1)
                        return false;
                    count = 0;
                    continue;
                }
                else
                {
                    if (count != 0)
                        return false;
                    if (c < 0xFF)
                    {
                        if (InExtendedSet((byte)c))
                            continue;
                    }
                    else if (c == 0x0A00)
                        return false;
                    else if (c == 0x0D00)
                        return false;
                    else if (c <= 0xD7FF)
                        continue;
                    else if (c <= 0xFFFF && c >= 0xE000)
                        continue;
                }
                return false;
            }
            return true;
        }

        private static bool PossiblyUCS4(byte[] data, int v, bool bigEndian)
        {
            int count = 0;
            for (int i = v; i < data.Length - 3; i += 4)
            {
                uint c = BitConverter.ToUInt32(data, i);
                if (bigEndian)
                {
                    c = (uint)(
                        (c & 0xFF) << 24 |
                        (c & 0xFF00) << 8 |
                        (c & 0xFF0000) >> 8 |
                        ((c >> 24) & 0xFF)
                    );
                }
                if (c >= 0x10FFFF)
                    return false;
                if (c < 0xFF)
                {
                    if (!InExtendedSet((byte)c))
                        return false;
                }
            }
            return true;
        }

        private static bool ValidateBOM(FileEncoding value, byte[] data, int bomLength)
        {
            if (value == FileEncoding.UTF8_BOM)
                return PossiblyUTF8(data, bomLength);
            else if (value == FileEncoding.UTF16_LE_BOM)
                return PossiblyUTF16(data, bomLength, false);
            else if (value == FileEncoding.UTF16_BE_BOM)
                return PossiblyUTF16(data, bomLength, true);
            else if (value == FileEncoding.UCS4_LE_BOM)
                return PossiblyUCS4(data, bomLength, false);
            else if (value == FileEncoding.UCS4_BE_BOM)
                return PossiblyUCS4(data, bomLength, true);
            return true;
        }

        private static FileEncoding? CheckBOM(byte[] data, out int bomlength)
        {
            if (data.Length >= 2)
            {
                bomlength = 2;
                if (data[0] == 0xFF && data[1] == 0xFE)
                    return FileEncoding.UTF16_LE_BOM;
                if (data[0] == 0xFE && data[1] == 0xFF)
                    return FileEncoding.UTF16_BE_BOM;

                if (data.Length >= 3)
                {
                    bomlength = 3;
                    if (data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                        return FileEncoding.UTF8_BOM;

                    if (data.Length >= 4)
                    {
                        if (data[0] == 0x2B && data[1] == 0x2F && data[2] == 0x76)
                        {
                            bomlength = 4;
                            if (data[3] == 0x38 || data[3] == 0x39 || data[3] == 0x2B || data[3] == 0x2F)
                            {
                                if (data.Length >= 5 && data[3] == 0x38 && data[4] == 0x2D)
                                    bomlength = 5;
                                return FileEncoding.UTF7_BOM;
                            }
                        }
                        if (data[0] == 0xFF && data[1] == 0xFE && data[2] == 0x00 && data[3] == 0x00)
                            return FileEncoding.UCS4_LE_BOM;
                        if (data[0] == 0x00 && data[1] == 0x00 && data[2] == 0xFE && data[3] == 0xFF)
                            return FileEncoding.UCS4_BE_BOM;
                    }
                }
            }
            bomlength = 0;
            return null;
        }
    }
}
