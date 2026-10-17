using System;

namespace HvEpromGui
{
    /// <summary>
    /// Simple sums and CRCs. Check values for "123456789":
    /// CRC-8 = F4, CCITT-FALSE = 29B1, XMODEM = 31C3, ARC = BB3D, CRC-32 = CBF43926.
    /// </summary>
    public static class Checksums
    {
        private static readonly uint[] Crc32Table = BuildCrc32Table();

        private static uint[] BuildCrc32Table()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                t[i] = c;
            }
            return t;
        }

        public static byte Sum8(byte[] d, int o, int n)
        {
            uint s = 0;
            for (int i = 0; i < n; i++) s += d[o + i];
            return (byte)(s & 0xFF);
        }

        public static ushort Sum16(byte[] d, int o, int n)
        {
            uint s = 0;
            for (int i = 0; i < n; i++) s += d[o + i];
            return (ushort)(s & 0xFFFF);
        }

        /// <summary>Two's complement of the 8-bit sum (the sum including this byte is 0).</summary>
        public static byte Sum8TwosComplement(byte[] d, int o, int n)
        {
            return (byte)((0x100 - Sum8(d, o, n)) & 0xFF);
        }

        public static byte Xor8(byte[] d, int o, int n)
        {
            int x = 0;
            for (int i = 0; i < n; i++) x ^= d[o + i];
            return (byte)x;
        }

        public static byte Crc8(byte[] d, int o, int n)
        {
            int crc = 0;
            for (int i = 0; i < n; i++)
            {
                crc ^= d[o + i];
                for (int k = 0; k < 8; k++)
                    crc = (crc & 0x80) != 0 ? ((crc << 1) ^ 0x07) & 0xFF : (crc << 1) & 0xFF;
            }
            return (byte)crc;
        }

        public static ushort Crc16(byte[] d, int o, int n, int poly, int init)
        {
            int crc = init;
            for (int i = 0; i < n; i++)
            {
                crc ^= d[o + i] << 8;
                for (int k = 0; k < 8; k++)
                    crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ poly) & 0xFFFF : (crc << 1) & 0xFFFF;
            }
            return (ushort)crc;
        }

        public static ushort Crc16CcittFalse(byte[] d, int o, int n) { return Crc16(d, o, n, 0x1021, 0xFFFF); }

        public static ushort Crc16Xmodem(byte[] d, int o, int n) { return Crc16(d, o, n, 0x1021, 0x0000); }

        public static ushort Crc16Arc(byte[] d, int o, int n)
        {
            int crc = 0;
            for (int i = 0; i < n; i++)
            {
                crc ^= d[o + i];
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1;
            }
            return (ushort)crc;
        }

        public static uint Crc32(byte[] d, int o, int n)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < n; i++)
                crc = Crc32Table[(crc ^ d[o + i]) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }

        public static (string Name, string Value)[] Compute(byte[] d, int o, int n)
        {
            return new (string Name, string Value)[]
            {
                ("Sum-8 (mod 256)",          "0x" + Sum8(d, o, n).ToString("X2")),
                ("Sum-8 two's complement",   "0x" + Sum8TwosComplement(d, o, n).ToString("X2")),
                ("Sum-16",                   "0x" + Sum16(d, o, n).ToString("X4")),
                ("XOR-8",                    "0x" + Xor8(d, o, n).ToString("X2")),
                ("CRC-8 (Poly 0x07)",        "0x" + Crc8(d, o, n).ToString("X2")),
                ("CRC-16/CCITT-FALSE",       "0x" + Crc16CcittFalse(d, o, n).ToString("X4")),
                ("CRC-16/XMODEM",            "0x" + Crc16Xmodem(d, o, n).ToString("X4")),
                ("CRC-16/ARC (IBM)",         "0x" + Crc16Arc(d, o, n).ToString("X4")),
                ("CRC-32 (IEEE 802.3)",      "0x" + Crc32(d, o, n).ToString("X8")),
            };
        }
    }
}
