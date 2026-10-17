using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace HvEpromGui
{
    /// <summary>
    /// Hex editor with hex and ASCII panes, selection, undo/redo, copy/paste,
    /// a highlighted burn section and differences against a reference image shown in red.
    /// </summary>
    public sealed class HexEditor : Control
    {
        private const int HeaderRows = 1;
        private const int MaxBytesPerRow = 256;
        private static readonly Color MarkColor = Color.FromArgb(255, 245, 190);

        private int _addrDigits = 4;
        private int _bpr = 16;                              // bytes per row (multiple of 16)
        private int AddrChars { get { return _addrDigits + 2; } }
        private int HexChars { get { return HexWidth(_bpr); } }
        private int AsciiCol { get { return AddrChars + HexChars + 1; } }

        // 3 characters per byte plus one extra gap after every 8 bytes
        private static int HexWidth(int n) { return n * 3 + n / 8 - 1; }
        private const int LeftMargin = 6;

        private sealed class Edit
        {
            public int Index;
            public byte[] Old;
            public byte[] New;
        }

        private byte[] _data = new byte[0];
        private byte[] _reference;
        private int _refOffset;
        private int _markStart, _markLen;
        private readonly VScrollBar _vs = new VScrollBar();
        private readonly Stack<Edit> _undo = new Stack<Edit>();
        private readonly Stack<Edit> _redo = new Stack<Edit>();
        private readonly Font _mono;
        private int _cursor, _anchor, _nibble;
        private bool _ascii, _dragging;
        private int _cw = 8, _ch = 16;

        public event EventHandler DataChanged;
        public event EventHandler SelectionChanged;

        public bool ReadOnly { get; set; }

        public HexEditor()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable |
                     ControlStyles.ResizeRedraw, true);
            TabStop = true;
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;
            _mono = new Font("Consolas", 10f);

            _vs.Dock = DockStyle.Right;
            _vs.ValueChanged += (s, e) => Invalidate();
            Controls.Add(_vs);

            var sz = TextRenderer.MeasureText("WWWWWWWWWWWWWWWW", _mono,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            _cw = Math.Max(1, (int)Math.Round(sz.Width / 16.0));
            _ch = sz.Height + 2;
            UpdateLayout();
            UpdateScroll();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _mono.Dispose();
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------ Public API

        public byte[] Data
        {
            get { return _data; }
            set
            {
                _data = value ?? new byte[0];
                _addrDigits = _data.Length > 0x1000000 ? 8 : (_data.Length > 0x10000 ? 6 : 4);
                UpdateLayout();
                _undo.Clear();
                _redo.Clear();
                _cursor = _anchor = _nibble = 0;
                UpdateScroll();
                _vs.Value = 0;
                Invalidate();
                if (DataChanged != null) DataChanged(this, EventArgs.Empty);
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>Reference image (e.g. the last device read); differences are drawn in red.</summary>
        public byte[] Reference
        {
            get { return _reference; }
            set { _reference = value; _refOffset = 0; Invalidate(); }
        }

        /// <summary>Reference that only applies from buffer offset <paramref name="offset"/> (e.g. a section read from a device).</summary>
        public void SetReference(byte[] reference, int offset)
        {
            _reference = reference;
            _refOffset = offset;
            Invalidate();
        }

        /// <summary>Highlights the burn section; length &lt;= 0 removes the highlight.</summary>
        public void SetMark(int start, int length)
        {
            _markStart = start;
            _markLen = length;
            Invalidate();
        }

        public int CursorIndex { get { return _cursor; } }
        public int SelectionStart { get { return Math.Min(_anchor, _cursor); } }
        public int SelectionLength { get { return _data.Length == 0 ? 0 : Math.Abs(_anchor - _cursor) + 1; } }

        public void GoTo(int index)
        {
            if (_data.Length == 0) return;
            _cursor = _anchor = Math.Max(0, Math.Min(_data.Length - 1, index));
            _nibble = 0;
            AfterMove();
        }

        public void SelectRange(int start, int length)
        {
            if (_data.Length == 0 || length <= 0) return;
            _anchor = Math.Max(0, Math.Min(_data.Length - 1, start));
            _cursor = Math.Max(0, Math.Min(_data.Length - 1, start + length - 1));
            _nibble = 0;
            AfterMove();
        }

        public int Find(byte[] pattern, int startIndex)
        {
            int len = _data.Length;
            if (pattern == null || pattern.Length == 0 || len < pattern.Length) return -1;
            for (int k = 0; k < len; k++)
            {
                int i = (startIndex + k) % len;
                if (i + pattern.Length > len) continue;
                bool ok = true;
                for (int j = 0; j < pattern.Length && ok; j++)
                    ok = _data[i + j] == pattern[j];
                if (ok) return i;
            }
            return -1;
        }

        public void FillSelection(byte value)
        {
            int n = SelectionLength;
            if (n > 0) Apply(SelectionStart, Enumerable.Repeat(value, n).ToArray());
        }

        public void FillAll(byte value)
        {
            if (_data.Length > 0) Apply(0, Enumerable.Repeat(value, _data.Length).ToArray());
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            var ed = _undo.Pop();
            Array.Copy(ed.Old, 0, _data, ed.Index, ed.Old.Length);
            _redo.Push(ed);
            _cursor = _anchor = ed.Index;
            _nibble = 0;
            AfterMove();
            if (DataChanged != null) DataChanged(this, EventArgs.Empty);
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var ed = _redo.Pop();
            Array.Copy(ed.New, 0, _data, ed.Index, ed.New.Length);
            _undo.Push(ed);
            _cursor = _anchor = ed.Index;
            _nibble = 0;
            AfterMove();
            if (DataChanged != null) DataChanged(this, EventArgs.Empty);
        }

        public static bool TryParseHex(string text, out byte[] bytes)
        {
            bytes = null;
            var sb = new StringBuilder();
            foreach (var tok in text.Split(new[] { ' ', '\t', '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                sb.Append(tok.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? tok.Substring(2) : tok);

            string s = sb.ToString();
            if (s.Length == 0 || (s.Length % 2) != 0) return false;
            var r = new byte[s.Length / 2];
            for (int i = 0; i < r.Length; i++)
                if (!byte.TryParse(s.Substring(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r[i]))
                    return false;
            bytes = r;
            return true;
        }

        // ------------------------------------------------------------ Editing

        private void Apply(int index, byte[] bytes)
        {
            if (ReadOnly) return;
            int n = Math.Min(bytes.Length, _data.Length - index);
            if (n <= 0) return;

            var old = new byte[n];
            var nw = new byte[n];
            Array.Copy(_data, index, old, 0, n);
            Array.Copy(bytes, 0, nw, 0, n);
            if (old.SequenceEqual(nw)) return;

            Array.Copy(nw, 0, _data, index, n);
            _undo.Push(new Edit { Index = index, Old = old, New = nw });
            _redo.Clear();
            Invalidate();
            if (DataChanged != null) DataChanged(this, EventArgs.Empty);
        }

        private void Copy()
        {
            int s = SelectionStart, n = SelectionLength;
            if (n <= 0) return;
            var slice = _data.Skip(s).Take(n).ToArray();
            string text = _ascii
                ? new string(slice.Select(b => b >= 32 && b < 127 ? (char)b : '.').ToArray())
                : string.Join(" ", slice.Select(b => b.ToString("X2")));
            try { Clipboard.SetText(text); } catch { }
        }

        private void Paste()
        {
            if (ReadOnly || _data.Length == 0 || !Clipboard.ContainsText()) return;
            string t = Clipboard.GetText();
            byte[] bytes;
            if (_ascii) bytes = Encoding.Latin1.GetBytes(t);
            else if (!TryParseHex(t, out bytes)) return;

            int start = SelectionStart;
            Apply(start, bytes);
            _cursor = _anchor = Math.Min(_data.Length - 1, start + Math.Min(bytes.Length, _data.Length - start));
            _nibble = 0;
            AfterMove();
        }

        // ------------------------------------------------------------ Layout/Scroll

        private static int HexCol(int c) { return c * 3 + c / 8; }

        /// <summary>Chooses the number of bytes per row (16, 32, 48, ...) that fits the control width.</summary>
        private void UpdateLayout()
        {
            int avail = (ClientSize.Width - _vs.Width - 2 * LeftMargin) / Math.Max(1, _cw);
            int n = 16;
            while (n + 16 <= MaxBytesPerRow && AddrChars + HexWidth(n + 16) + 1 + (n + 16) <= avail)
                n += 16;
            _bpr = n;
        }

        private int VisibleRows() { return Math.Max(1, ClientSize.Height / _ch - HeaderRows); }

        private int TotalRows() { return Math.Max(1, (_data.Length + _bpr - 1) / _bpr); }

        private void UpdateScroll()
        {
            int total = TotalRows();
            int vis = VisibleRows();
            _vs.Minimum = 0;
            _vs.SmallChange = 1;
            _vs.Maximum = total - 1;
            _vs.LargeChange = Math.Max(1, Math.Min(vis, total));
            _vs.Enabled = total > vis;
            int maxV = Math.Max(0, total - vis);
            if (_vs.Value > maxV) _vs.Value = maxV;
        }

        private void SetScroll(int v)
        {
            int maxV = Math.Max(0, TotalRows() - VisibleRows());
            _vs.Value = Math.Max(0, Math.Min(maxV, v));
        }

        private void EnsureVisible()
        {
            int r = _cursor / _bpr, vis = VisibleRows(), v = _vs.Value;
            if (r < v) SetScroll(r);
            else if (r >= v + vis) SetScroll(r - vis + 1);
        }

        private void AfterMove()
        {
            EnsureVisible();
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            int oldBpr = _bpr;
            int firstByte = _vs.Value * oldBpr;
            UpdateLayout();
            UpdateScroll();
            if (_bpr != oldBpr) SetScroll(firstByte / _bpr);   // keep the same top byte in view
            Invalidate();
        }

        // ------------------------------------------------------------ Painting

        private bool IsSelected(int i)
        {
            return i >= SelectionStart && i < SelectionStart + SelectionLength;
        }

        private void DrawText(Graphics g, string s, int col, int y, Color fore, Color back)
        {
            int x = LeftMargin + col * _cw;
            if (!back.IsEmpty)
                using (var br = new SolidBrush(back))
                    g.FillRectangle(br, x, y, s.Length * _cw, _ch);
            TextRenderer.DrawText(g, s, _mono, new Point(x, y + 1), fore,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);

            for (int c = 0; c < _bpr; c++)
            {
                DrawText(g, c.ToString("X2"), AddrChars + HexCol(c), 0, SystemColors.GrayText, Color.Empty);
                DrawText(g, (c % 16).ToString("X"), AsciiCol + c, 0, SystemColors.GrayText, Color.Empty);
            }

            int first = _vs.Value;
            int rows = VisibleRows();
            int lastRow = Math.Min(TotalRows(), first + rows + 1);

            for (int r = first; r < lastRow; r++)
            {
                int y = (r - first + HeaderRows) * _ch;
                DrawText(g, (r * _bpr).ToString("X" + _addrDigits), 0, y, SystemColors.GrayText, Color.Empty);

                for (int c = 0; c < _bpr; c++)
                {
                    int idx = r * _bpr + c;
                    if (idx >= _data.Length) break;

                    bool sel = IsSelected(idx);
                    int ri = idx - _refOffset;
                    bool diff = _reference != null && ri >= 0 && ri < _reference.Length && _reference[ri] != _data[idx];
                    bool marked = _markLen > 0 && idx >= _markStart && idx < _markStart + _markLen;
                    Color fore = sel ? SystemColors.HighlightText : (diff ? Color.Red : (marked ? Color.Black : ForeColor));
                    Color back = sel ? SystemColors.Highlight : (marked ? MarkColor : Color.Empty);

                    DrawText(g, _data[idx].ToString("X2"), AddrChars + HexCol(c), y, fore, back);
                    char ch = _data[idx] >= 32 && _data[idx] < 127 ? (char)_data[idx] : '.';
                    DrawText(g, ch.ToString(), AsciiCol + c, y, fore, back);
                }
            }

            if (_data.Length > 0)
            {
                int r = _cursor / _bpr, c = _cursor % _bpr;
                if (r >= first && r <= first + rows)
                {
                    int y = (r - first + HeaderRows) * _ch;
                    int hx = LeftMargin + (AddrChars + HexCol(c)) * _cw;
                    int ax = LeftMargin + (AsciiCol + c) * _cw;
                    using (var penHex = new Pen(_ascii ? SystemColors.GrayText : Color.Red))
                    using (var penAsc = new Pen(_ascii ? Color.Red : SystemColors.GrayText))
                    {
                        g.DrawRectangle(penHex, hx - 1, y, 2 * _cw + 1, _ch - 1);
                        g.DrawRectangle(penAsc, ax - 1, y, _cw + 1, _ch - 1);
                    }
                    if (!_ascii && !ReadOnly)
                        g.FillRectangle(Brushes.Red, hx + _nibble * _cw, y + _ch - 3, _cw, 2);
                }
            }
        }

        // ------------------------------------------------------------ Mouse

        private int HitTest(Point p, out bool ascii, out int nibble)
        {
            ascii = false;
            nibble = 0;
            if (_data.Length == 0 || p.Y < HeaderRows * _ch) return -1;

            int row = p.Y / _ch - HeaderRows + _vs.Value;
            int cx = (int)Math.Floor((p.X - LeftMargin) / (double)_cw);
            int col;

            if (cx >= AsciiCol)
            {
                ascii = true;
                col = Math.Max(0, Math.Min(_bpr - 1, cx - AsciiCol));
            }
            else
            {
                int hc = cx - AddrChars;
                col = _bpr - 1;
                for (int b = 0; b < _bpr; b++)
                {
                    int s = HexCol(b);
                    if (hc < s + 3)
                    {
                        col = b;
                        nibble = (hc - s) >= 1 ? 1 : 0;
                        break;
                    }
                }
                if (hc < 0) { col = 0; nibble = 0; }
            }
            return Math.Max(0, Math.Min(_data.Length - 1, row * _bpr + col));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            bool asc;
            int nib;
            int idx = HitTest(e.Location, out asc, out nib);
            if (idx < 0) return;
            _ascii = asc;
            _cursor = idx;
            if ((ModifierKeys & Keys.Shift) == 0) _anchor = idx;
            _nibble = nib;
            _dragging = true;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            bool asc;
            int nib;
            int idx = HitTest(e.Location, out asc, out nib);
            if (idx < 0 || idx == _cursor) return;
            _cursor = idx;
            _nibble = 0;
            AfterMove();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            SetScroll(_vs.Value - Math.Sign(e.Delta) * 3);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // ------------------------------------------------------------ Keyboard

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int len = _data.Length;
            if (len == 0) return;

            if (e.Control)
            {
                switch (e.KeyCode)
                {
                    case Keys.A:
                        _anchor = 0; _cursor = len - 1; _nibble = 0; AfterMove(); e.Handled = true; return;
                    case Keys.C: Copy(); e.Handled = true; return;
                    case Keys.V: Paste(); e.Handled = true; return;
                    case Keys.Z: Undo(); e.Handled = true; return;
                    case Keys.Y: Redo(); e.Handled = true; return;
                }
            }

            if (e.KeyCode == Keys.Delete)
            {
                FillSelection(0xFF);
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Tab || e.KeyCode == Keys.Insert)
            {
                // Insert toggles between the hex and ASCII pane
                if (e.KeyCode == Keys.Insert) { _ascii = !_ascii; _nibble = 0; Invalidate(); e.Handled = true; }
                return;
            }

            int pos = _cursor;
            bool move = true;
            switch (e.KeyCode)
            {
                case Keys.Left: pos = _cursor - 1; break;
                case Keys.Right: pos = _cursor + 1; break;
                case Keys.Up: pos = _cursor - _bpr; break;
                case Keys.Down: pos = _cursor + _bpr; break;
                case Keys.PageUp: pos = _cursor - _bpr * VisibleRows(); break;
                case Keys.PageDown: pos = _cursor + _bpr * VisibleRows(); break;
                case Keys.Home: pos = e.Control ? 0 : (_cursor / _bpr) * _bpr; break;
                case Keys.End: pos = e.Control ? len - 1 : Math.Min(len - 1, (_cursor / _bpr) * _bpr + _bpr - 1); break;
                default: move = false; break;
            }

            if (move)
            {
                _cursor = Math.Max(0, Math.Min(len - 1, pos));
                if (!e.Shift) _anchor = _cursor;
                _nibble = 0;
                AfterMove();
                e.Handled = true;
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (ReadOnly || _data.Length == 0 || char.IsControl(e.KeyChar)) return;

            if (_ascii)
            {
                if (e.KeyChar >= 32 && e.KeyChar < 127)
                {
                    Apply(_cursor, new[] { (byte)e.KeyChar });
                    _cursor = _anchor = Math.Min(_data.Length - 1, _cursor + 1);
                    AfterMove();
                }
            }
            else if (Uri.IsHexDigit(e.KeyChar))
            {
                int nib = Convert.ToInt32(e.KeyChar.ToString(), 16);
                byte old = _data[_cursor];
                byte nv = _nibble == 0 ? (byte)((nib << 4) | (old & 0x0F))
                                       : (byte)((old & 0xF0) | nib);
                Apply(_cursor, new[] { nv });
                if (_nibble == 0) _nibble = 1;
                else
                {
                    _nibble = 0;
                    if (_cursor < _data.Length - 1) _cursor++;
                }
                _anchor = _cursor;
                AfterMove();
            }
            e.Handled = true;
        }
    }
}
