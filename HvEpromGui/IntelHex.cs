using System;
using System.Text;

namespace HvEpromGui
{
    public static class IntelHex
    {
        public const int MaxImage = 16 * 1024 * 1024;

        /// <summary>Reads Intel HEX into a fixed-size image (empty = 0xFF); addresses beyond the size are an error.</summary>
        public static byte[] Parse(string text, int size)
        {
            var img = new byte[size];
            Array.Fill(img, (byte)0xFF);
            Walk(text, (a, v) =>
            {
                if (a >= size)
                    throw new FormatException($"Address 0x{a:X} is outside the device (0x{size:X} bytes).");
                img[a] = v;
            });
            return img;
        }

        /// <summary>Reads Intel HEX into an image that extends to the highest used address (gaps = 0xFF).</summary>
        public static byte[] ParseAuto(string text)
        {
            int max = -1;
            Walk(text, (a, v) =>
            {
                if (a >= MaxImage)
                    throw new FormatException($"Address 0x{a:X} exceeds the maximum image size ({MaxImage / (1024 * 1024)} MB).");
                if (a > max) max = a;
            });
            if (max < 0) throw new FormatException("The HEX file contains no data.");

            var img = new byte[max + 1];
            Array.Fill(img, (byte)0xFF);
            Walk(text, (a, v) => img[a] = v);
            return img;
        }

        private static void Walk(string text, Action<int, byte> sink)
        {
            int upper = 0;
            int lineNo = 0;

            foreach (var raw in text.Split('\n'))
            {
                lineNo++;
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] != ':')
                    throw new FormatException($"Line {lineNo}: does not start with ':'.");
                if (line.Length < 11 || (line.Length % 2) == 0)
                    throw new FormatException($"Line {lineNo}: invalid length.");

                var b = new byte[(line.Length - 1) / 2];
                for (int i = 0; i < b.Length; i++)
                    b[i] = Convert.ToByte(line.Substring(1 + 2 * i, 2), 16);

                int len = b[0];
                if (b.Length != len + 5)
                    throw new FormatException($"Line {lineNo}: length field does not match.");

                int sum = 0;
                foreach (byte x in b) sum += x;
                if ((sum & 0xFF) != 0)
                    throw new FormatException($"Line {lineNo}: checksum error.");

                int addr = (b[1] << 8) | b[2];
                switch (b[3])
                {
                    case 0x00:
                        for (int i = 0; i < len; i++)
                            sink(upper + addr + i, b[4 + i]);
                        break;
                    case 0x01:
                        return;
                    case 0x02:
                        upper = ((b[4] << 8) | b[5]) << 4;
                        break;
                    case 0x04:
                        upper = ((b[4] << 8) | b[5]) << 16;
                        break;
                }
            }
        }

        public static string Write(byte[] data) { return Write(data, 0, data.Length); }

        /// <summary>Writes a range; addresses in the file equal the buffer offsets.</summary>
        public static string Write(byte[] data, int start, int length)
        {
            var sb = new StringBuilder();
            int end = start + length;
            int curUpper = 0;

            for (int off = start; off < end; off += 16)
            {
                int upper = off >> 16;
                if (upper != curUpper)
                {
                    curUpper = upper;
                    int s = 2 + 0 + 0 + 4 + ((upper >> 8) & 0xFF) + (upper & 0xFF);
                    sb.Append(":02000004").Append(upper.ToString("X4"))
                      .Append(((0x100 - (s & 0xFF)) & 0xFF).ToString("X2")).Append("\r\n");
                }

                int n = Math.Min(16, end - off);
                // a record must not cross the 64K boundary
                n = Math.Min(n, 0x10000 - (off & 0xFFFF));
                int a16 = off & 0xFFFF;
                int sum = n + ((a16 >> 8) & 0xFF) + (a16 & 0xFF);
                sb.Append(':').Append(n.ToString("X2")).Append(a16.ToString("X4")).Append("00");
                for (int i = 0; i < n; i++)
                {
                    sb.Append(data[off + i].ToString("X2"));
                    sum += data[off + i];
                }
                sb.Append(((0x100 - (sum & 0xFF)) & 0xFF).ToString("X2")).Append("\r\n");
                off += n - 16;   // compensate the loop increment (+16) when n < 16
            }
            sb.Append(":00000001FF\r\n");
            return sb.ToString();
        }
    }
}
