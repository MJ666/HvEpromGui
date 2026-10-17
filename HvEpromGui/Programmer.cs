// Protokoll-Implementierung portiert aus "hvepromcmd" (C) Matthew Millman,
// GNU General Public License v2 oder neuer (https://github.com/inaxeon/hvepromcmd).
// Dieses Programm steht daher ebenfalls unter GPL-2.0-or-later.

using System;
using System.IO.Ports;
using System.Threading;

namespace HvEpromGui
{
    public enum DeviceType : byte
    {
        C1702A = 0x00,
        C2704 = 0x01,
        C2708 = 0x02,
        MCM6876X = 0x03,
        D8748 = 0x04,
        D8749 = 0x05,
        D8741 = 0x06,
        D8742 = 0x07,
        P8048 = 0x08,
        P8049 = 0x09,
        P8050 = 0x0A,
        D8755 = 0x0B,
        P8041 = 0x0C,
        P8042 = 0x0D,
        TMS2716 = 0x0E
    }

    public sealed class DeviceInfo
    {
        public string Name;
        public DeviceType Type;
        public int Size;
        public int DefaultPasses;
        public byte FillByte;
        /// <summary>Default "hit until set" mode (MCS48, 8755, MCM6876x); can be disabled like CLI option -m.</summary>
        public bool HitUntilSetDefault;
        /// <summary>Retry cycles (CLI -r), MCM6876x only.</summary>
        public int DefaultRetries;
        public bool UsesRetries { get { return Type == DeviceType.MCM6876X; } }

        public override string ToString() { return Name; }

        private static DeviceInfo Fixed(string name, DeviceType t, int size, int passes)
        {
            return new DeviceInfo { Name = name, Type = t, Size = size, DefaultPasses = passes, FillByte = 0xFF };
        }

        private static DeviceInfo Hit(string name, DeviceType t, int size, byte fill, int retries)
        {
            return new DeviceInfo
            {
                Name = name, Type = t, Size = size, DefaultPasses = 1, FillByte = fill,
                HitUntilSetDefault = true, DefaultRetries = retries
            };
        }

        // Sizes, fill bytes and default pass counts as in hvepromcmd (main.c / pgm.c)
        public static readonly DeviceInfo[] All =
        {
            new DeviceInfo { Name = "1702A (256 x 8)", Type = DeviceType.C1702A, Size = 0x100, DefaultPasses = 32, FillByte = 0x00 },
            Fixed("2704 (512 x 8)",   DeviceType.C2704,   0x200, 100),
            Fixed("2708 (1K x 8)",    DeviceType.C2708,   0x400, 100),
            Fixed("TMS2716 (2K x 8)", DeviceType.TMS2716, 0x800, 100),
            Hit("MCM6876x (8K x 8)",  DeviceType.MCM6876X, 0x2000, 0xFF, 5),
            Hit("8741 (1K)",          DeviceType.D8741,   0x400, 0x00, 0),
            Hit("8742 (2K)",          DeviceType.D8742,   0x800, 0x00, 0),
            Hit("8748 (1K)",          DeviceType.D8748,   0x400, 0x00, 0),
            Hit("8749 (2K)",          DeviceType.D8749,   0x800, 0x00, 0),
            Hit("8041 (1K)",          DeviceType.P8041,   0x400, 0xFF, 0),
            Hit("8042 (2K)",          DeviceType.P8042,   0x800, 0xFF, 0),
            Hit("8048 (1K)",          DeviceType.P8048,   0x400, 0xFF, 0),
            Hit("8049 (2K)",          DeviceType.P8049,   0x800, 0xFF, 0),
            Hit("8050 (4K)",          DeviceType.P8050,   0x1000, 0xFF, 0),
            Hit("8755 (2K)",          DeviceType.D8755,   0x800, 0xFF, 0),
        };
    }

    public struct WriteResult
    {
        public int MaxWritesPerByte;
        public long TotalWrites;
    }

    public sealed class ProgrammerException : Exception
    {
        public ProgrammerException(string message) : base(message) { }
    }

    public struct BlankCheckResult
    {
        public bool Blank;
        public int Offset;
        public byte Data;
    }

    public sealed class Programmer : IDisposable
    {
        private const byte CmdStartWrite = 0x10;
        private const byte CmdWriteChunk = 0x11;
        private const byte CmdStartRead = 0x12;
        private const byte CmdReadChunk = 0x13;
        private const byte CmdStartBlankCheck = 0x14;
        private const byte CmdBlankCheck = 0x15;
        private const byte CmdDevReset = 0x16;
        private const byte CmdMeasure12V = 0x17;

        private const byte ErrOk = 0x00;
        private const byte ErrProceedDualSocket = 0x01;
        private const byte ErrComplete = 0x02;
        private const byte ErrNotBlank = 0x09;

        // As in the CLI: tiny blocks, because the firmware has no flow control.
        private const int ChunkSize = 8;

        private SerialPort _port;
        private byte _lastErr;

        public bool IsOpen { get { return _port != null && _port.IsOpen; } }

        public void Open(string portName, int baud)
        {
            Close();
            var p = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 3000,
                WriteTimeout = 3000
            };
            p.Open();
            Thread.Sleep(50);
            p.DiscardInBuffer();
            p.DiscardOutBuffer();
            _port = p;
        }

        public void DiscardBuffers()
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }

        public void Close()
        {
            if (_port != null)
            {
                try { if (_port.IsOpen) _port.Close(); } catch { }
                _port.Dispose();
                _port = null;
            }
        }

        public void Dispose() { Close(); }

        // ---------------------------------------------------------------- Low level

        private void Send(byte[] data, int count)
        {
            try { _port.Write(data, 0, count); }
            catch (TimeoutException) { throw new ProgrammerException("Timeout while sending."); }
        }

        private void SendCmd(byte cmd, params byte[] args)
        {
            var b = new byte[2 + args.Length];
            b[0] = cmd;
            b[1] = (byte)(cmd ^ 0xFF);
            Array.Copy(args, 0, b, 2, args.Length);
            Send(b, b.Length);
        }

        private void ReadExact(byte[] buf, int offset, int count)
        {
            int got = 0;
            try
            {
                while (got < count)
                {
                    int n = _port.Read(buf, offset + got, count - got);
                    if (n <= 0) throw new ProgrammerException("Connection lost.");
                    got += n;
                }
            }
            catch (TimeoutException)
            {
                throw new ProgrammerException("Timeout - no response from the programmer.");
            }
        }

        private void CheckReturn(byte cmd)
        {
            var b = new byte[1];
            ReadExact(b, 0, 1);
            if (b[0] != cmd)
                throw new ProgrammerException($"Invalid response (expected 0x{cmd:X2}, received 0x{b[0]:X2}).");
            ReadExact(b, 0, 1);
            _lastErr = b[0];
            if (_lastErr == ErrOk || _lastErr == ErrProceedDualSocket ||
                _lastErr == ErrComplete || _lastErr == ErrNotBlank)
                return;
            throw new ProgrammerException(ErrorText(_lastErr));
        }

        private static string ErrorText(byte code)
        {
            switch (code)
            {
                case 0x03: return "Programmer reports: invalid command.";
                case 0x04: return "Programmer reports: function not supported.";
                case 0x05: return "Programmer reports: no hardware (shield) detected.";
                case 0x06: return "Programmer reports: wrong hardware (shield) for this device.";
                case 0x07: return "Programmer reports: wrong switch position.";
                case 0x0A: return "Programmer reports: maximum number of retries exceeded.";
                default: return $"Programmer reports error code 0x{code:X2}.";
            }
        }

        private static void Report(IProgress<int> p, int pct, ref int last)
        {
            if (p != null && pct != last) { last = pct; p.Report(pct); }
        }

        // ---------------------------------------------------------------- Commands

        public double MeasureSupplyVoltage()
        {
            SendCmd(CmdMeasure12V);
            CheckReturn(CmdMeasure12V);
            var b = new byte[2];
            ReadExact(b, 0, 2);
            return ((b[0] << 8) | b[1]) / 100.0;
        }

        /// <summary>Measures the 12 V supply and checks it like the CLI does.</summary>
        public double CheckSupply(DeviceInfo dev)
        {
            double v = MeasureSupplyVoltage();
            if ((dev.Type == DeviceType.C2704 || dev.Type == DeviceType.C2708) && (v < 11.5 || v >= 13.0))
                throw new ProgrammerException($"Supply voltage outside the allowed range for this device: {v:F2} V (allowed 11.5 ... 13.0 V).");
            if (v < 10.0 || v >= 14.0)
                throw new ProgrammerException($"The programmer must be connected to 12 V: {v:F2} V (allowed 10 ... 14 V).");
            return v;
        }

        public void Reset()
        {
            SendCmd(CmdDevReset);
            CheckReturn(CmdDevReset);
        }

        public byte[] Read(DeviceInfo dev, IProgress<int> progress, CancellationToken ct)
        {
            var buf = new byte[dev.Size];
            int done = 0, last = -1;
            Report(progress, 0, ref last);

            SendCmd(CmdStartRead, (byte)dev.Type);
            CheckReturn(CmdStartRead);

            while (done < buf.Length)
            {
                ct.ThrowIfCancellationRequested();
                SendCmd(CmdReadChunk);
                CheckReturn(CmdReadChunk);
                int n = Math.Min(ChunkSize, buf.Length - done);
                ReadExact(buf, done, n);
                done += n;
                Report(progress, (int)((long)done * 100 / buf.Length), ref last);
            }

            if (_lastErr != ErrComplete)
                throw new ProgrammerException("Unexpected response: read was not completed.");
            return buf;
        }

        public BlankCheckResult BlankCheck(DeviceInfo dev)
        {
            SendCmd(CmdStartBlankCheck, (byte)dev.Type);
            CheckReturn(CmdStartBlankCheck);
            SendCmd(CmdBlankCheck);
            CheckReturn(CmdBlankCheck);

            if (_lastErr == ErrComplete)
                return new BlankCheckResult { Blank = true };

            var r = new byte[3];
            ReadExact(r, 0, 3);
            return new BlankCheckResult { Blank = false, Offset = (r[0] << 8) | r[1], Data = r[2] };
        }

        /// <summary>
        /// Programs the device. Fixed mode: <paramref name="passes"/> passes
        /// (1702A/270x/TMS2716). "Hit until set" (hitTillSet): a single pass in which the programmer
        /// repeats every byte itself, using <paramref name="retries"/> retry cycles (MCM6876x).
        /// </summary>
        public WriteResult Write(DeviceInfo dev, byte[] image, int passes, bool hitTillSet, byte retries,
                                 IProgress<int> progress, CancellationToken ct)
        {
            if (image.Length != dev.Size)
                throw new ProgrammerException("Image size does not match the device size.");
            if (hitTillSet) passes = 1;

            var frame = new byte[2 + ChunkSize];
            var result = new WriteResult();
            int last = -1;
            Report(progress, 0, ref last);

            for (int pass = 0; pass < passes; pass++)
            {
                SendCmd(CmdStartWrite, (byte)dev.Type, (byte)(hitTillSet ? 0x01 : 0x00), retries);
                CheckReturn(CmdStartWrite);

                int written = 0;
                while (written < image.Length)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = Math.Min(ChunkSize, image.Length - written);
                    frame[0] = CmdWriteChunk;
                    frame[1] = (byte)(CmdWriteChunk ^ 0xFF);
                    Array.Copy(image, written, frame, 2, n);
                    Send(frame, 2 + n);
                    CheckReturn(CmdWriteChunk);
                    written += n;

                    int pct = (int)(((long)pass * 10000 / passes +
                                     ((long)written * 10000 / image.Length) / passes) / 100);
                    Report(progress, pct, ref last);
                }

                if (_lastErr != ErrComplete)
                    throw new ProgrammerException("Unexpected response at the end of a write pass.");

                var res = new byte[5];   // only non-zero in "hit until set" mode
                ReadExact(res, 0, 5);
                result.MaxWritesPerByte = res[0];
                result.TotalWrites = ((long)res[1] << 24) | ((long)res[2] << 16) | ((long)res[3] << 8) | res[4];
            }
            return result;
        }
    }
}
