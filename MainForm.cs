using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HvEpromGui
{
    public sealed class MainForm : Form
    {
        private readonly HexEditor _hex = new HexEditor { Dock = DockStyle.Fill };

        private ComboBox _cbPort, _cbBaud, _cbDev, _cbCrcRange;
        private Button _btnRefresh, _btnConnect;
        private ListView _lvCrc;
        private Label _lblRange, _lblSec;
        private TextBox _txtSrc, _txtLen, _txtDst;
        private Button _btnFromSel, _btnPrevBank, _btnNextBank;
        private CheckBox _chkReadInto, _chkFixed, _chkBlank, _chkVerify;
        private NumericUpDown _numPasses, _numRetries;
        private Button _btnVolt, _btnRead, _btnBlank, _btnWrite, _btnVerify, _btnCancel;
        private ProgressBar _bar;
        private Label _lblTime;
        private System.Windows.Forms.Timer _timer;
        private readonly Stopwatch _opWatch = new Stopwatch();
        private readonly Stopwatch _phaseWatch = new Stopwatch();
        private int _lastPct;
        private string _phaseName = "";
        private TextBox _log;
        private ToolStripStatusLabel _status;

        private Programmer _prog;
        private CancellationTokenSource _cts;
        private bool _busy, _init, _dirty, _settingSection;
        private string _filePath;
        private byte[] _lastPattern;

        private DeviceInfo Dev { get { return (DeviceInfo)_cbDev.SelectedItem; } }

        public MainForm()
        {
            Text = AppInfo.Name + " v" + AppInfo.Version;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1200, 820);
            MinimumSize = new Size(1000, 680);
            StartPosition = FormStartPosition.CenterScreen;

            BuildUi();

            _init = true;
            _cbDev.Items.AddRange(DeviceInfo.All);
            _cbDev.SelectedIndex = 2;                        // 2708
            _cbBaud.SelectedIndex = 1;                       // 38400 as in the CLI
            _cbCrcRange.SelectedIndex = 0;
            ApplyDeviceDefaults();
            _hex.Data = NewBuffer(Dev);
            ApplyPorts(PortInfo.Enumerate(false));           // plain names right away
            _init = false;
            Shown += (s, e) => { var _ = RefreshPortsAsync(); };   // Device Manager names follow in the background

            _dirty = false;
            SetSectionDefaults();
            UpdateCrc();
            UpdateStatus();
            UpdateTitle();
            UpdateUi();
            Log("Ready. Select a port and click \"Connect\".");
        }

        // ================================================================ UI construction

        private static Label Lbl(string t)
        {
            return new Label { Text = t, AutoSize = true, Margin = new Padding(8, 7, 2, 0) };
        }

        private static ToolStripMenuItem Item(string text, Keys shortcut, Action act)
        {
            var i = new ToolStripMenuItem(text);
            if (shortcut != Keys.None) i.ShortcutKeys = shortcut;
            i.Click += (s, e) => act();
            return i;
        }

        private void BuildUi()
        {
            // ---- Menu
            var menu = new MenuStrip();

            var mFile = new ToolStripMenuItem("&File");
            mFile.DropDownItems.Add(Item("&Open (BIN / HEX)...", Keys.Control | Keys.O, OpenFile));
            mFile.DropDownItems.Add(Item("&New buffer (device size)", Keys.Control | Keys.N, NewImage));
            mFile.DropDownItems.Add(Item("Resi&ze buffer...", Keys.None, ResizeBuffer));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(Item("&Save as BIN...", Keys.Control | Keys.S, SaveBin));
            mFile.DropDownItems.Add(Item("Save as &Intel HEX...", Keys.None, SaveHex));
            mFile.DropDownItems.Add(Item("Save s&ection as BIN...", Keys.None, SaveSectionBin));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(Item("E&xit", Keys.None, Close));

            var mEdit = new ToolStripMenuItem("&Edit");
            var undo = Item("&Undo", Keys.None, () => _hex.Undo());
            undo.ShortcutKeyDisplayString = "Ctrl+Z";
            var redo = Item("&Redo", Keys.None, () => _hex.Redo());
            redo.ShortcutKeyDisplayString = "Ctrl+Y";
            mEdit.DropDownItems.Add(undo);
            mEdit.DropDownItems.Add(redo);
            mEdit.DropDownItems.Add(new ToolStripSeparator());
            mEdit.DropDownItems.Add(Item("Find (&hex)...", Keys.Control | Keys.F, FindHex));
            mEdit.DropDownItems.Add(Item("Find (&text)...", Keys.Control | Keys.Shift | Keys.F, FindText));
            mEdit.DropDownItems.Add(Item("Find &next", Keys.F3, FindNext));
            mEdit.DropDownItems.Add(Item("&Go to address...", Keys.Control | Keys.G, GoToAddress));
            mEdit.DropDownItems.Add(new ToolStripSeparator());
            mEdit.DropDownItems.Add(Item("Use selection as burn &section", Keys.Control | Keys.B, SectionFromSelection));
            var fillSel = Item("Fill selection with &FF", Keys.None, () => _hex.FillSelection(0xFF));
            fillSel.ShortcutKeyDisplayString = "Del";
            mEdit.DropDownItems.Add(fillSel);
            mEdit.DropDownItems.Add(Item("Fill all with FF", Keys.None, () => _hex.FillAll(0xFF)));
            mEdit.DropDownItems.Add(Item("Fill all with 00", Keys.None, () => _hex.FillAll(0x00)));

            var mView = new ToolStripMenuItem("&View");
            mView.DropDownItems.Add(Item("Clear device &reference (red differences)", Keys.None, () => _hex.Reference = null));

            var mHelp = new ToolStripMenuItem("&Help");
            mHelp.DropDownItems.Add(Item("&Documentation (English)", Keys.F1, () => OpenDoc("MANUAL.en.html")));
            mHelp.DropDownItems.Add(Item("&Dokumentation (Deutsch)", Keys.None, () => OpenDoc("HANDBUCH.de.html")));
            mHelp.DropDownItems.Add(new ToolStripSeparator());
            mHelp.DropDownItems.Add(Item("&About...", Keys.None, ShowAbout));

            menu.Items.AddRange(new ToolStripItem[] { mFile, mEdit, mView, mHelp });

            // ---- Top bar
            _cbPort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            _btnRefresh = new Button { Text = "\u21BB", Width = 30, Height = 25 };
            _cbBaud = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
            _cbBaud.Items.AddRange(new object[] { "9600", "38400", "115200" });
            _cbDev = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            _btnConnect = new Button { Text = "Connect", Width = 100, Height = 25 };

            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                WrapContents = false,
                Padding = new Padding(4, 4, 4, 0)
            };
            top.Controls.AddRange(new Control[]
            {
                Lbl("Port:"), _cbPort, _btnRefresh, Lbl("Baud:"), _cbBaud, Lbl("Device:"), _cbDev, _btnConnect
            });

            // ---- Right: section + programmer
            var right = new Panel { Dock = DockStyle.Right, Width = 372, AutoScroll = true };

            var gbSec = new GroupBox { Text = "Burn section (buffer \u2192 device)", Left = 6, Top = 4, Width = 354, Height = 208 };
            var l1 = new Label { Left = 8, Top = 27, Width = 140, Text = "Source offset (hex):" };
            var l2 = new Label { Left = 8, Top = 55, Width = 140, Text = "Length (hex):" };
            var l3 = new Label { Left = 8, Top = 83, Width = 140, Text = "Device offset (hex):" };
            _txtSrc = new TextBox { Left = 150, Top = 23, Width = 90 };
            _txtLen = new TextBox { Left = 150, Top = 51, Width = 90 };
            _txtDst = new TextBox { Left = 150, Top = 79, Width = 90 };
            _btnFromSel = new Button { Left = 8, Top = 110, Width = 104, Height = 28, Text = "From selection" };
            _btnPrevBank = new Button { Left = 118, Top = 110, Width = 110, Height = 28, Text = "\u25C0 Bank" };
            _btnNextBank = new Button { Left = 234, Top = 110, Width = 110, Height = 28, Text = "Bank \u25B6" };
            _lblSec = new Label { Left = 8, Top = 144, Width = 338, Height = 34 };
            _chkReadInto = new CheckBox { Left = 8, Top = 180, Width = 340, Height = 22, Text = "Read: insert into buffer at source offset" };
            gbSec.Controls.AddRange(new Control[]
            {
                l1, l2, l3, _txtSrc, _txtLen, _txtDst, _btnFromSel, _btnPrevBank, _btnNextBank, _lblSec, _chkReadInto
            });

            var gbPgm = new GroupBox { Text = "Programmer", Left = 6, Top = 218, Width = 354, Height = 286 };
            var lblPasses = new Label { Left = 8, Top = 28, Width = 200, Text = "Passes (fixed modes):" };
            _numPasses = new NumericUpDown { Left = 215, Top = 24, Width = 70, Minimum = 1, Maximum = 1000, Value = 100 };
            _chkFixed = new CheckBox { Left = 8, Top = 54, Width = 338, Height = 22, Text = "Fixed passes instead of \"hit until set\" (-m)" };
            var lblRetries = new Label { Left = 8, Top = 86, Width = 200, Text = "Retry cycles (MCM6876x):" };
            _numRetries = new NumericUpDown { Left = 215, Top = 82, Width = 70, Minimum = 0, Maximum = 255, Value = 0 };
            _chkBlank = new CheckBox { Left = 8, Top = 112, Width = 338, Height = 22, Text = "Blank check before writing", Checked = true };
            _chkVerify = new CheckBox { Left = 8, Top = 136, Width = 338, Height = 22, Text = "Verify section after writing", Checked = true };

            _btnVolt = new Button { Left = 8, Top = 168, Width = 164, Height = 32, Text = "Measure 12 V" };
            _btnBlank = new Button { Left = 178, Top = 168, Width = 164, Height = 32, Text = "Blank check" };
            _btnRead = new Button { Left = 8, Top = 206, Width = 164, Height = 32, Text = "Read" };
            _btnVerify = new Button { Left = 178, Top = 206, Width = 164, Height = 32, Text = "Verify section" };
            _btnWrite = new Button { Left = 8, Top = 244, Width = 164, Height = 32, Text = "Write section" };
            _btnCancel = new Button { Left = 178, Top = 244, Width = 164, Height = 32, Text = "Cancel" };
            gbPgm.Controls.AddRange(new Control[]
            {
                lblPasses, _numPasses, _chkFixed, lblRetries, _numRetries, _chkBlank, _chkVerify,
                _btnVolt, _btnBlank, _btnRead, _btnVerify, _btnWrite, _btnCancel
            });
            right.Controls.Add(gbSec);
            right.Controls.Add(gbPgm);

            // ---- Bottom: log + progress, checksums on the right
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 260 };

            var logPanel = new Panel { Dock = DockStyle.Fill };
            _log = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f)
            };
            _bar = new ProgressBar { Dock = DockStyle.Top, Height = 18 };
            _lblTime = new Label { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
            logPanel.Controls.Add(_log);
            logPanel.Controls.Add(_lblTime);
            logPanel.Controls.Add(_bar);

            _timer = new System.Windows.Forms.Timer { Interval = 500 };
            _timer.Tick += (s, e) => UpdateTimeLabel();

            var gbCrc = new GroupBox { Text = "Checksums", Dock = DockStyle.Right, Width = 360 };
            _cbCrcRange = new ComboBox { Left = 8, Top = 20, Width = 340, DropDownStyle = ComboBoxStyle.DropDownList };
            _cbCrcRange.Items.AddRange(new object[] { "Whole buffer", "Selection", "Burn section" });
            _lblRange = new Label { Left = 8, Top = 48, Width = 340, Height = 18 };
            _lvCrc = new ListView
            {
                Left = 8, Top = 68, Width = 342, Height = 182,
                View = View.Details, FullRowSelect = true, GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _lvCrc.Columns.Add("Algorithm (double-click = copy)", 205);
            _lvCrc.Columns.Add("Value", 110);
            gbCrc.Controls.AddRange(new Control[] { _cbCrcRange, _lblRange, _lvCrc });

            bottom.Controls.Add(logPanel);
            bottom.Controls.Add(gbCrc);

            var statusStrip = new StatusStrip();
            _status = new ToolStripStatusLabel("Ready");
            statusStrip.Items.Add(_status);

            // Order matters: Fill control first, outermost docked controls last
            Controls.Add(_hex);
            Controls.Add(right);
            Controls.Add(bottom);
            Controls.Add(top);
            Controls.Add(statusStrip);
            Controls.Add(menu);
            MainMenuStrip = menu;

            var tip = new ToolTip();
            tip.SetToolTip(_chkFixed, "MCS48/8755/MCM6876x: fixed number of passes instead of writing until the byte is set (CLI option -m).");
            tip.SetToolTip(_chkBlank, "Only useful if the device is supposed to be completely empty. Turn it off when burning several sections one after another.");
            tip.SetToolTip(_chkReadInto, "The device contents (device range) are copied into the buffer at the source offset; the buffer grows if needed.");
            tip.SetToolTip(_btnFromSel, "Use the selected range as source (Ctrl+B).");

            // ---- Events
            _btnRefresh.Click += async (s, e) => await RefreshPortsAsync();
            _btnConnect.Click += async (s, e) => await ToggleConnectAsync();
            _cbDev.SelectedIndexChanged += (s, e) => OnDeviceChanged();
            _chkFixed.CheckedChanged += (s, e) => UpdateUi();
            _btnVolt.Click += async (s, e) => await MeasureAsync();
            _btnBlank.Click += async (s, e) => await BlankCheckAsync();
            _btnRead.Click += async (s, e) => await ReadAsync();
            _btnVerify.Click += async (s, e) => await VerifyAsync();
            _btnWrite.Click += async (s, e) => await WriteAsync();
            _btnCancel.Click += (s, e) => { if (_cts != null) _cts.Cancel(); };

            _txtSrc.TextChanged += (s, e) => UpdateSection();
            _txtLen.TextChanged += (s, e) => UpdateSection();
            _txtDst.TextChanged += (s, e) => UpdateSection();
            _btnFromSel.Click += (s, e) => SectionFromSelection();
            _btnPrevBank.Click += (s, e) => StepBank(-1);
            _btnNextBank.Click += (s, e) => StepBank(+1);

            _hex.DataChanged += (s, e) =>
            {
                if (_init) return;
                _dirty = true;
                UpdateCrc();
                UpdateTitle();
                UpdateSection();
            };
            _hex.SelectionChanged += (s, e) =>
            {
                if (_init) return;
                UpdateStatus();
                if (_cbCrcRange.SelectedIndex == 1) UpdateCrc();
            };
            _cbCrcRange.SelectedIndexChanged += (s, e) => UpdateCrc();
            _lvCrc.DoubleClick += (s, e) =>
            {
                if (_lvCrc.SelectedItems.Count == 0) return;
                try { Clipboard.SetText(_lvCrc.SelectedItems[0].SubItems[1].Text); } catch { }
                _status.Text = "Checksum copied to the clipboard.";
            };

            FormClosing += OnFormClosing;
            FormClosed += (s, e) => { if (_prog != null) _prog.Dispose(); };
        }

        // ================================================================ Helpers

        private static byte[] NewBuffer(DeviceInfo dev)
        {
            var b = new byte[dev.Size];
            Array.Fill(b, dev.FillByte);
            return b;
        }

        private static bool TryHex(string s, out int v)
        {
            v = 0;
            s = (s ?? "").Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return s.Length > 0 && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) && v >= 0;
        }

        private void Log(string msg)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), msg); return; }
            _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
        }

        private void Ui(Action a)
        {
            if (InvokeRequired) Invoke(a); else a();
        }

        /// <summary>Fills the port list now, then adds the Device Manager descriptions (WMI) in the background.</summary>
        private async Task RefreshPortsAsync()
        {
            ApplyPorts(PortInfo.Enumerate(false));
            System.Collections.Generic.List<PortInfo> full = null;
            try { full = await Task.Run(() => PortInfo.Enumerate(true)); } catch { }
            if (full != null && !IsDisposed) ApplyPorts(full);
        }

        private void ApplyPorts(System.Collections.Generic.List<PortInfo> ports)
        {
            var cur = _cbPort.SelectedItem as PortInfo;
            _cbPort.BeginUpdate();
            _cbPort.Items.Clear();
            int width = 120;
            foreach (var p in ports)
            {
                _cbPort.Items.Add(p);
                width = Math.Max(width, TextRenderer.MeasureText(p.ToString(), _cbPort.Font).Width + 28);
            }
            _cbPort.DropDownWidth = width;

            int idx = cur == null ? -1 : ports.FindIndex(p => p.PortName == cur.PortName);
            if (idx >= 0) _cbPort.SelectedIndex = idx;
            else if (ports.Count > 0) _cbPort.SelectedIndex = 0;
            _cbPort.EndUpdate();
        }

        private void ApplyDeviceDefaults()
        {
            var dev = Dev;
            _numPasses.Value = dev.DefaultPasses;
            _numRetries.Value = dev.DefaultRetries;
            _chkFixed.Checked = false;
        }

        private bool HitMode(DeviceInfo dev) { return dev.HitUntilSetDefault && !_chkFixed.Checked; }

        private void UpdateUi()
        {
            bool connected = _prog != null;
            bool ops = connected && !_busy;
            var dev = _cbDev.SelectedItem as DeviceInfo;

            _cbPort.Enabled = _cbBaud.Enabled = _btnRefresh.Enabled = !connected && !_busy;
            _cbDev.Enabled = !_busy;
            _btnConnect.Enabled = !_busy;
            _btnConnect.Text = connected ? "Disconnect" : "Connect";
            _btnVolt.Enabled = _btnBlank.Enabled = _btnRead.Enabled =
                _btnVerify.Enabled = _btnWrite.Enabled = ops;
            _btnCancel.Enabled = _busy;
            _chkBlank.Enabled = _chkVerify.Enabled = !_busy;

            _chkFixed.Enabled = !_busy && dev != null && dev.HitUntilSetDefault;
            _numPasses.Enabled = !_busy && dev != null && !HitMode(dev);
            _numRetries.Enabled = !_busy && dev != null && dev.UsesRetries;
        }

        private void UpdateTitle()
        {
            string name = _filePath != null ? Path.GetFileName(_filePath) : "Untitled";
            Text = AppInfo.Name + " v" + AppInfo.Version + " - " + name + (_dirty ? " *" : "");
        }

        private void UpdateStatus()
        {
            var d = _hex.Data;
            if (d.Length == 0) { _status.Text = ""; return; }
            int c = _hex.CursorIndex;
            _status.Text = string.Format("Buffer {0} bytes   |   Offset 0x{1:X4}   Value 0x{2:X2} ({2})   |   Selection: {3} byte(s) from 0x{4:X4}",
                d.Length, c, d[c], _hex.SelectionLength, _hex.SelectionStart);
        }

        private void UpdateCrc()
        {
            if (_cbCrcRange == null || _cbCrcRange.SelectedIndex < 0) return;
            var d = _hex.Data;
            int off = 0, n = d.Length;

            switch (_cbCrcRange.SelectedIndex)
            {
                case 1:
                    if (_hex.SelectionLength > 0) { off = _hex.SelectionStart; n = _hex.SelectionLength; }
                    break;
                case 2:
                    int s, l, t;
                    string err;
                    if (TryGetSection(false, out s, out l, out t, out err)) { off = s; n = l; }
                    else { off = 0; n = 0; }
                    break;
            }

            _lblRange.Text = n > 0
                ? string.Format("Range 0x{0:X4} - 0x{1:X4}  ({2} bytes)", off, off + n - 1, n)
                : "No valid range";

            _lvCrc.BeginUpdate();
            _lvCrc.Items.Clear();
            foreach (var r in Checksums.Compute(d, off, n))
                _lvCrc.Items.Add(new ListViewItem(new[] { r.Name, r.Value }));
            _lvCrc.EndUpdate();
        }

        private void OnDeviceChanged()
        {
            if (_init || _cbDev.SelectedItem == null) return;
            ApplyDeviceDefaults();
            _hex.Reference = null;
            SetSectionDefaults();
            UpdateUi();
            Log("Device: " + Dev.Name + " (" + Dev.Size + " bytes).");
        }

        private static string Ask(IWin32Window owner, string title, string label, string def)
        {
            using (var f = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(380, 104)
            })
            {
                var l = new Label { Left = 10, Top = 10, Width = 360, Text = label };
                var t = new TextBox { Left = 10, Top = 34, Width = 360, Text = def };
                var ok = new Button { Text = "OK", Left = 210, Top = 68, Width = 75, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 295, Top = 68, Width = 75, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
            }
        }

        // ================================================================ Section

        /// <summary>
        /// Reads the section fields. forRead=true: the source may lie beyond the end of the buffer
        /// (the buffer is extended when reading).
        /// </summary>
        private bool TryGetSection(bool forRead, out int src, out int len, out int dst, out string err)
        {
            src = len = dst = 0;
            err = null;
            var dev = _cbDev.SelectedItem as DeviceInfo;
            if (dev == null) { err = "No device selected."; return false; }

            if (!TryHex(_txtSrc.Text, out src) || !TryHex(_txtLen.Text, out len) || !TryHex(_txtDst.Text, out dst))
            {
                err = "Source offset, length and device offset must be hex numbers.";
                return false;
            }
            if (len <= 0) { err = "The length must be greater than 0."; return false; }
            if ((long)dst + len > dev.Size)
            {
                err = string.Format("Does not fit into the device: offset 0x{0:X} + length 0x{1:X} > 0x{2:X}.", dst, len, dev.Size);
                return false;
            }
            if (!forRead && (long)src + len > _hex.Data.Length)
            {
                err = string.Format("Source 0x{0:X} + length 0x{1:X} is beyond the end of the buffer (0x{2:X}).", src, len, _hex.Data.Length);
                return false;
            }
            if (forRead && (long)src + len > IntelHex.MaxImage)
            {
                err = "The buffer would exceed the maximum size of 16 MB.";
                return false;
            }
            return true;
        }

        private void SetSection(int src, int len, int dst)
        {
            _settingSection = true;
            _txtSrc.Text = src.ToString("X");
            _txtLen.Text = len.ToString("X");
            _txtDst.Text = dst.ToString("X");
            _settingSection = false;
            UpdateSection();
        }

        private void SetSectionDefaults()
        {
            var dev = Dev;
            SetSection(0, Math.Max(1, Math.Min(dev.Size, _hex.Data.Length)), 0);
        }

        private void UpdateSection()
        {
            if (_init || _settingSection || _lblSec == null) return;
            int src, len, dst;
            string err;
            var dev = Dev;
            if (TryGetSection(false, out src, out len, out dst, out err))
            {
                int banks = (_hex.Data.Length + dev.Size - 1) / dev.Size;
                _lblSec.Text = string.Format("Buffer 0x{0:X}-0x{1:X}  \u2192  device 0x{2:X}-0x{3:X}   ({4} bytes, bank {5}/{6})",
                    src, src + len - 1, dst, dst + len - 1, len, src / dev.Size + 1, banks);
                _hex.SetMark(src, len);
            }
            else
            {
                _lblSec.Text = err;
                _hex.SetMark(0, 0);
            }
            if (_cbCrcRange.SelectedIndex == 2) UpdateCrc();
        }

        private void SectionFromSelection()
        {
            var dev = Dev;
            int dst;
            if (!TryHex(_txtDst.Text, out dst) || dst >= dev.Size) dst = 0;
            int len = Math.Min(_hex.SelectionLength, dev.Size - dst);
            if (len < _hex.SelectionLength)
                Log("Note: the selection is larger than the device - length trimmed to " + len + " bytes.");
            SetSection(_hex.SelectionStart, Math.Max(1, len), dst);
        }

        private void StepBank(int delta)
        {
            var dev = Dev;
            int src;
            if (!TryHex(_txtSrc.Text, out src)) src = 0;
            int bank = src / dev.Size + delta;
            if (bank < 0 || (long)bank * dev.Size >= _hex.Data.Length) return;
            int s = bank * dev.Size;
            SetSection(s, Math.Min(dev.Size, _hex.Data.Length - s), 0);
            _hex.GoTo(s);
        }

        // ================================================================ Files

        private void OpenFile()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "EPROM images (*.bin;*.rom;*.hex;*.ihx)|*.bin;*.rom;*.hex;*.ihx|All files (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var dev = Dev;
                    byte[] raw = File.ReadAllBytes(dlg.FileName);
                    string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                    byte[] img;

                    if (ext == ".hex" || ext == ".ihx")
                    {
                        img = IntelHex.ParseAuto(Encoding.ASCII.GetString(raw));
                    }
                    else
                    {
                        if (raw.Length == 0) throw new InvalidDataException("The file is empty.");
                        if (raw.Length > IntelHex.MaxImage)
                            throw new InvalidDataException("File too large (maximum 16 MB).");
                        img = raw;
                    }

                    _hex.Data = img;
                    _hex.Reference = null;
                    _filePath = dlg.FileName;
                    _dirty = false;
                    UpdateTitle();
                    SetSectionDefaults();
                    Log(string.Format("Loaded: {0} ({1} bytes)", dlg.FileName, img.Length));
                    if (img.Length > dev.Size)
                        Log(string.Format("The file is larger than the device ({0} bytes) - choose a section/bank on the right.", dev.Size));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Open failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void NewImage()
        {
            _hex.Data = NewBuffer(Dev);
            _hex.Reference = null;
            _filePath = null;
            _dirty = false;
            UpdateTitle();
            SetSectionDefaults();
        }

        private void ResizeBuffer()
        {
            string s = Ask(this, "Resize buffer", "New size in bytes (hex, e.g. 2000 for 8 KB):",
                _hex.Data.Length.ToString("X"));
            if (s == null) return;
            int n;
            if (!TryHex(s, out n) || n < 1 || n > IntelHex.MaxImage)
            {
                MessageBox.Show(this, "Invalid size (1 ... 0x1000000).", "Resize buffer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var old = _hex.Data;
            if (n < old.Length &&
                MessageBox.Show(this, "The buffer will be shrunk and data at the end will be lost. Continue?",
                    "Resize buffer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            var nb = new byte[n];
            Array.Fill(nb, (byte)0xFF);
            Array.Copy(old, nb, Math.Min(old.Length, n));
            _hex.Data = nb;
            _hex.Reference = null;
            SetSectionDefaults();
        }

        private void SaveBin()
        {
            using (var dlg = new SaveFileDialog { Filter = "Binary file (*.bin)|*.bin|All files (*.*)|*.*", DefaultExt = "bin" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllBytes(dlg.FileName, _hex.Data);
                    _filePath = dlg.FileName;
                    _dirty = false;
                    UpdateTitle();
                    Log("Saved: " + dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SaveSectionBin()
        {
            int src, len, dst;
            string err;
            if (!TryGetSection(false, out src, out len, out dst, out err))
            {
                MessageBox.Show(this, err, "Section", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (var dlg = new SaveFileDialog { Filter = "Binary file (*.bin)|*.bin|All files (*.*)|*.*", DefaultExt = "bin" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var part = new byte[len];
                    Array.Copy(_hex.Data, src, part, 0, len);
                    File.WriteAllBytes(dlg.FileName, part);
                    Log(string.Format("Section 0x{0:X}-0x{1:X} saved: {2}", src, src + len - 1, dlg.FileName));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SaveHex()
        {
            using (var dlg = new SaveFileDialog { Filter = "Intel HEX (*.hex)|*.hex|All files (*.*)|*.*", DefaultExt = "hex" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, IntelHex.Write(_hex.Data), Encoding.ASCII);
                    Log("Saved (Intel HEX): " + dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_busy)
            {
                MessageBox.Show(this, "An operation is still running. Please cancel it or wait until it finishes.",
                    "Programmer busy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }
            if (_dirty &&
                MessageBox.Show(this, "The buffer contains unsaved changes. Exit anyway?",
                    "Exit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                e.Cancel = true;
        }

        // ================================================================ Find / Go to

        private void FindHex()
        {
            string s = Ask(this, "Find (hex)", "Bytes in hex, e.g. DE AD BE EF:", "");
            if (s == null) return;
            byte[] pat;
            if (!HexEditor.TryParseHex(s, out pat))
            {
                MessageBox.Show(this, "Invalid hex sequence.", "Find", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _lastPattern = pat;
            FindNext();
        }

        private void FindText()
        {
            string s = Ask(this, "Find (text)", "ASCII text:", "");
            if (string.IsNullOrEmpty(s)) return;
            _lastPattern = Encoding.Latin1.GetBytes(s);
            FindNext();
        }

        private void FindNext()
        {
            if (_lastPattern == null) return;
            int i = _hex.Find(_lastPattern, _hex.SelectionStart + 1);
            if (i < 0) _status.Text = "Not found.";
            else _hex.SelectRange(i, _lastPattern.Length);
        }

        private void GoToAddress()
        {
            string s = Ask(this, "Go to address", "Address (hex), e.g. 3F0:", "");
            if (s == null) return;
            int a;
            if (TryHex(s, out a) && a < _hex.Data.Length) _hex.GoTo(a);
            else MessageBox.Show(this, "Address invalid or outside the buffer.", "Go to address", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OpenDoc(string file)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "docs", file);
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "Documentation file not found:\n" + path, "Documentation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception)
            {
                MessageBox.Show(this, "The documentation could not be opened in the default browser.\nYou can open the file manually:\n" + path,
                    "Documentation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                AppInfo.Name + " GUI  -  version " + AppInfo.Version + "\n\n" +
                "Windows front end for the HVEPROM programmer by Matthew Millman.\n" +
                "The serial protocol is taken from the hvepromcmd project (GPL v2 or later), " +
                "so this program is also licensed under GPL-2.0-or-later.\n\n" +
                "Hex editor keys: arrows/PgUp/PgDn/Home/End, Shift = select, Ctrl+A/C/V/Z/Y, " +
                "Del = fill selection with FF, Ins = switch between hex and ASCII pane, " +
                "Ctrl+B = use selection as burn section. The number of bytes per row adapts to the window width.",
                "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ================================================================ Connection & operations

        private async Task ToggleConnectAsync()
        {
            if (_busy) return;

            if (_prog != null)
            {
                _prog.Dispose();
                _prog = null;
                Log("Disconnected.");
                UpdateUi();
                return;
            }

            if (_cbPort.SelectedItem == null || _cbBaud.SelectedItem == null)
            {
                Log("No port selected.");
                return;
            }

            var portInfo = (PortInfo)_cbPort.SelectedItem;
            string port = portInfo.PortName;
            int baud = int.Parse((string)_cbBaud.SelectedItem, CultureInfo.InvariantCulture);
            var p = new Programmer();
            _busy = true;
            UpdateUi();
            try
            {
                double v = await Task.Run(() =>
                {
                    p.Open(port, baud);
                    double volt;
                    try
                    {
                        volt = p.MeasureSupplyVoltage();
                    }
                    catch (ProgrammerException)
                    {
                        // Boards that reset when the port is opened (e.g. Arduino) need a moment to boot
                        Log("No answer yet - waiting for the board to start and retrying ...");
                        Thread.Sleep(2000);
                        p.DiscardBuffers();
                        volt = p.MeasureSupplyVoltage();
                    }
                    p.Reset();
                    return volt;
                });
                _prog = p;
                Log(string.Format("Connected to {0} ({1} baud). Supply voltage: {2:F2} V", portInfo, baud, v));
            }
            catch (Exception ex)
            {
                p.Dispose();
                Log("Connection failed: " + ex.Message);
            }
            finally
            {
                _busy = false;
                UpdateUi();
            }
        }

        private static string FormatTime(TimeSpan t)
        {
            return t.TotalHours >= 1
                ? string.Format("{0}:{1:D2}:{2:D2}", (int)t.TotalHours, t.Minutes, t.Seconds)
                : string.Format("{0}:{1:D2}", (int)t.TotalMinutes, t.Seconds);
        }

        /// <summary>Shows the current phase, percentage, elapsed time and the estimated remaining time.</summary>
        private void UpdateTimeLabel()
        {
            if (!_busy) return;
            TimeSpan pe = _phaseWatch.Elapsed;
            string text = string.Format("{0}: {1} %   elapsed {2}", _phaseName, _lastPct, FormatTime(pe));
            if (_lastPct >= 1 && pe.TotalSeconds >= 2)
            {
                var rem = TimeSpan.FromSeconds(pe.TotalSeconds * (100 - _lastPct) / _lastPct);
                text += "   remaining ~" + FormatTime(rem);
            }
            if ((_opWatch.Elapsed - pe).TotalSeconds >= 1)
                text += "   (total " + FormatTime(_opWatch.Elapsed) + ")";
            _lblTime.Text = text;
        }

        /// <summary>Starts a new phase (e.g. "Writing", "Verifying") with its own progress and time estimate.</summary>
        private void SetPhase(string phase)
        {
            Ui(() =>
            {
                _phaseName = phase;
                _lastPct = 0;
                _bar.Value = 0;
                _phaseWatch.Restart();
                UpdateTimeLabel();
            });
        }

        private async Task RunOp(string name, Action<IProgress<int>, CancellationToken> work)
        {
            if (_busy || _prog == null) return;
            _busy = true;
            _cts = new CancellationTokenSource();
            UpdateUi();
            Log("-- " + name + " --");

            _phaseName = name;
            _lastPct = 0;
            _opWatch.Restart();
            _phaseWatch.Restart();
            _timer.Start();
            UpdateTimeLabel();

            var progress = new Progress<int>(p =>
            {
                if (!_busy) return;
                _lastPct = Math.Max(0, Math.Min(100, p));
                _bar.Value = _lastPct;
                UpdateTimeLabel();
            });
            var ct = _cts.Token;
            string outcome = "finished";
            try
            {
                await Task.Run(() => work(progress, ct));
            }
            catch (OperationCanceledException)
            {
                outcome = "cancelled";
                Log("Cancelled. (A partially programmed device is possible - verify if in doubt.)");
            }
            catch (Exception ex)
            {
                outcome = "failed";
                Log("ERROR: " + ex.Message);
            }
            finally
            {
                try { await Task.Run(() => _prog.Reset()); } catch { }
                _timer.Stop();
                _opWatch.Stop();
                string summary = name + " " + outcome + " after " + FormatTime(_opWatch.Elapsed);
                if (_opWatch.Elapsed.TotalSeconds >= 2) Log(summary + ".");
                _cts.Dispose();
                _cts = null;
                _busy = false;
                _bar.Value = 0;
                _lblTime.Text = summary;
                UpdateUi();
            }
        }

        private Task MeasureAsync()
        {
            var dev = Dev;
            return RunOp("Measure 12 V", (p, ct) =>
            {
                double v = _prog.CheckSupply(dev);
                Log(string.Format("Supply voltage: {0:F2} V (OK)", v));
            });
        }

        private Task BlankCheckAsync()
        {
            var dev = Dev;
            return RunOp("Blank check", (p, ct) =>
            {
                _prog.CheckSupply(dev);
                var r = _prog.BlankCheck(dev);
                if (r.Blank) Log("Device is blank.");
                else Log(string.Format("Device is NOT blank. Offset 0x{0:X4}, value 0x{1:X2}", r.Offset, r.Data));
            });
        }

        private Task ReadAsync()
        {
            var dev = Dev;
            bool into = _chkReadInto.Checked;
            int src = 0, len = dev.Size, dst = 0;
            if (into)
            {
                string err;
                if (!TryGetSection(true, out src, out len, out dst, out err))
                {
                    Log("Section: " + err);
                    return Task.CompletedTask;
                }
            }
            int fSrc = src, fLen = len, fDst = dst;

            return RunOp("Read", (p, ct) =>
            {
                double v = _prog.CheckSupply(dev);
                Log(string.Format("Supply voltage: {0:F2} V (OK)", v));
                var data = _prog.Read(dev, p, ct);

                if (!into)
                {
                    Ui(() =>
                    {
                        _hex.Data = data;
                        _hex.Reference = (byte[])data.Clone();
                        _filePath = null;
                        _dirty = false;
                        UpdateTitle();
                        SetSectionDefaults();
                    });
                    Log(string.Format("Read successful ({0} bytes). CRC-32: 0x{1:X8}", data.Length, Checksums.Crc32(data, 0, data.Length)));
                }
                else
                {
                    var slice = new byte[fLen];
                    Array.Copy(data, fDst, slice, 0, fLen);
                    Ui(() =>
                    {
                        var old = _hex.Data;
                        var nb = new byte[Math.Max(old.Length, fSrc + fLen)];
                        Array.Fill(nb, (byte)0xFF);
                        Array.Copy(old, nb, old.Length);
                        Array.Copy(slice, 0, nb, fSrc, fLen);
                        _hex.Data = nb;
                        _hex.SetReference(slice, fSrc);
                        _hex.GoTo(fSrc);
                        UpdateSection();
                    });
                    Log(string.Format("Device 0x{0:X}-0x{1:X} inserted into the buffer at 0x{2:X}. CRC-32 of the section: 0x{3:X8}",
                        fDst, fDst + fLen - 1, fSrc, Checksums.Crc32(slice, 0, slice.Length)));
                }
            });
        }

        /// <summary>Compares only the burn section (device at dst against the expected data from srcBase in the buffer).</summary>
        private bool CompareSection(byte[] expected, byte[] devData, int dst, int srcBase)
        {
            int diffs = 0, first = -1;
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != devData[dst + i])
                {
                    diffs++;
                    if (first < 0) first = i;
                }
            }

            if (diffs == 0)
            {
                Log(string.Format("Verify successful - {0} bytes match.", expected.Length));
                return true;
            }

            int pos = srcBase + first;
            Log(string.Format("Verify FAILED - first difference at buffer 0x{0:X} / device 0x{1:X}: expected 0x{2:X2}, device 0x{3:X2}; {4} byte(s) differ in total.",
                pos, dst + first, expected[first], devData[dst + first], diffs));
            Ui(() => _hex.GoTo(pos));
            return false;
        }

        private Task VerifyAsync()
        {
            var dev = Dev;
            int src, len, dst;
            string err;
            if (!TryGetSection(false, out src, out len, out dst, out err))
            {
                Log("Section: " + err);
                return Task.CompletedTask;
            }
            var expected = new byte[len];
            Array.Copy(_hex.Data, src, expected, 0, len);

            return RunOp("Verify section", (p, ct) =>
            {
                _prog.CheckSupply(dev);
                var devData = _prog.Read(dev, p, ct);
                var slice = new byte[len];
                Array.Copy(devData, dst, slice, 0, len);
                Ui(() => _hex.SetReference(slice, src));
                CompareSection(expected, devData, dst, src);
            });
        }

        private async Task WriteAsync()
        {
            var dev = Dev;
            int src, len, dst;
            string err;
            if (!TryGetSection(false, out src, out len, out dst, out err))
            {
                Log("Section: " + err);
                return;
            }

            // Device image: unprogrammed state outside the section so nothing else is changed
            var image = NewBuffer(dev);
            Array.Copy(_hex.Data, src, image, dst, len);
            var expected = new byte[len];
            Array.Copy(_hex.Data, src, expected, 0, len);

            bool hit = HitMode(dev);
            int passes = hit ? 1 : (int)_numPasses.Value;
            byte retries = dev.UsesRetries ? (byte)_numRetries.Value : (byte)0;
            bool doBlank = _chkBlank.Checked, doVerify = _chkVerify.Checked;

            var answer = MessageBox.Show(this,
                string.Format("{0}: buffer 0x{1:X}-0x{2:X} ({3} bytes) will be programmed starting at device address 0x{4:X}.\n" +
                              "Mode: {5}\n\n" +
                              "Is the correct device inserted and the programming voltage ready?\n\nStart writing?",
                              dev.Name, src, src + len - 1, len, dst,
                              hit ? "hit until set (the programmer repeats each byte itself)" : passes + " passes"),
                "Write", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;

            await RunOp("Write section", (p, ct) =>
            {
                double v = _prog.CheckSupply(dev);
                Log(string.Format("Supply voltage: {0:F2} V (OK)", v));

                if (doBlank)
                {
                    SetPhase("Blank check");
                    var r = _prog.BlankCheck(dev);
                    if (!r.Blank)
                    {
                        throw new ProgrammerException(string.Format(
                            "Device is not blank (offset 0x{0:X4}, value 0x{1:X2}) - write aborted. " +
                            "Turn the blank check off if you deliberately want to program on top of existing data.", r.Offset, r.Data));
                    }
                    Log("Device is blank.");
                    _prog.Reset();
                }

                SetPhase("Writing");
                Log("Writing ...");
                var sw = Stopwatch.StartNew();
                var wr = _prog.Write(dev, image, passes, hit, retries, p, ct);
                Log(string.Format("Write finished ({0:F1} s).", sw.Elapsed.TotalSeconds));
                if (hit)
                    Log(string.Format("Maximum writes of a single byte: {0}, total writes: {1}",
                        wr.MaxWritesPerByte, wr.TotalWrites));

                if (doVerify)
                {
                    _prog.Reset();
                    SetPhase("Verifying");
                    Log("Verifying ...");
                    var devData = _prog.Read(dev, p, ct);
                    var slice = new byte[len];
                    Array.Copy(devData, dst, slice, 0, len);
                    Ui(() => _hex.SetReference(slice, src));
                    CompareSection(expected, devData, dst, src);
                }
            });
        }
    }
}
