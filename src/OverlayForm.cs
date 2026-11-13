using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    // Shared non-activating, always-on-top, draggable text panel used by the gem and passive overlays.
    public abstract class OverlayForm : Form
    {
        private const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        protected static readonly Color HeaderColor = UiTheme.Text;

        protected class Line
        {
            public string Text;
            public Color Color;
            public bool Header;
            public bool Wrap;
            public string ToolTipText;
        }

        private class LinesPanel : Panel
        {
            public readonly List<Line> Lines = new List<Line>();
            private readonly Font _font = new Font("Consolas", 9.5f);
            private readonly Font _headerFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            private readonly ToolTip _toolTip = new ToolTip { ShowAlways = true, InitialDelay = 400, ReshowDelay = 100, AutoPopDelay = 30000 };
            private Line _hoveredLine;

            public LinesPanel() { DoubleBuffered = true; }

            public int RowHeight(Line l)
            {
                return l.Wrap ? TextRenderer.MeasureText(l.Text, l.Header ? _headerFont : _font, new Size(Math.Max(40, ClientSize.Width - 26), int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 6 : TextRenderer.MeasureText("Ag", l.Header ? _headerFont : _font).Height + (l.Header ? 12 : 4);
            }

            public int ContentHeight { get { return Lines.Sum(l => RowHeight(l)) + 4; } }

            private Line LineAt(Point point)
            {
                if (!ClientRectangle.Contains(point) || point.X >= ClientSize.Width - 20) return null;
                int y = 2 + AutoScrollPosition.Y;
                foreach (var line in Lines)
                {
                    int height = RowHeight(line);
                    if (point.Y >= y && point.Y < y + height) return line;
                    y += height;
                }
                return null;
            }

            public void ClearHover()
            {
                _toolTip.Hide(this);
                _toolTip.SetToolTip(this, null);
                _hoveredLine = null;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (e.Button != MouseButtons.None) { ClearHover(); return; }
                var line = LineAt(e.Location);
                if (line == _hoveredLine) return;
                ClearHover();
                _hoveredLine = line;
                if (line != null && !string.IsNullOrEmpty(line.ToolTipText))
                    _toolTip.SetToolTip(this, line.ToolTipText);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                ClearHover();
                base.OnMouseLeave(e);
            }

            protected override void OnScroll(ScrollEventArgs e)
            {
                ClearHover();
                base.OnScroll(e);
            }

            protected override void OnVisibleChanged(EventArgs e)
            {
                ClearHover();
                base.OnVisibleChanged(e);
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                ClearHover();
                base.OnSizeChanged(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                int y = 2 + AutoScrollPosition.Y;
                foreach (var l in Lines)
                {
                    if (l.Header && y > 2)
                        using (var pen = new Pen(UiTheme.Border))
                            e.Graphics.DrawLine(pen, 6, y + 2, ClientSize.Width - 20, y + 2);
                    TextRenderer.DrawText(e.Graphics, l.Text, l.Header ? _headerFont : _font, new Rectangle(6, y + (l.Header ? 7 : 2), Math.Max(40, ClientSize.Width - 26), RowHeight(l)), l.Color,
                        TextFormatFlags.Left | TextFormatFlags.NoPrefix | (l.Wrap ? TextFormatFlags.WordBreak : TextFormatFlags.EndEllipsis));
                    y += RowHeight(l);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { _toolTip.Dispose(); _font.Dispose(); _headerFont.Dispose(); }
                base.Dispose(disposing);
            }
        }

        protected readonly AppConfig _config;
        private readonly Label _title;
        private readonly LinesPanel _body;
        private readonly Label _footer;
        private string _cacheKey;
        private bool _loaded;
        private Point _dragStart;

        // True when screen detection opened the overlay (so it may also close it).
        public bool AutoShown { get; set; }

        protected OverlayForm(AppConfig config, int width)
        {
            _config = config;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.Border;
            Opacity = 0.96;
            Padding = new Padding(1);
            Width = width;
            Font = new Font("Segoe UI", 9.5f);

            var frame = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(8, 6, 8, 8) };
            var header = new Panel { Dock = DockStyle.Top, Height = 30 };
            _title = new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.Accent, Font = new Font("Segoe UI", 10f, FontStyle.Bold), AutoEllipsis = true };
            _body = new LinesPanel { Dock = DockStyle.Fill, BackColor = frame.BackColor, AutoScroll = true };
            _footer = new Label { Dock = DockStyle.Bottom, Height = 36, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8.5f) };
            var close = new Label { Text = "x", Dock = DockStyle.Right, Width = 24, ForeColor = UiTheme.Text, Cursor = Cursors.Hand, TextAlign = ContentAlignment.TopCenter, AccessibleName = "Hide overlay" };
            close.Click += (s, e) => { AutoShown = false; Hide(); };
            var toggle = new Label { Text = "all", Dock = DockStyle.Right, Width = 32, ForeColor = UiTheme.Muted, Cursor = Cursors.Hand, TextAlign = ContentAlignment.TopCenter, AccessibleName = "Toggle all entries" };
            toggle.Click += (s, e) => { ShowAll = !ShowAll; _config.Save(); Render(); };
            header.Controls.Add(_title);
            header.Controls.Add(toggle);
            header.Controls.Add(close);
            frame.Controls.Add(_body);
            frame.Controls.Add(header);
            frame.Controls.Add(_footer);
            Controls.Add(frame);

            var timer = new Timer { Interval = 3000 };
            timer.Tick += (s, e) => { if (Visible && BuildKey() != _cacheKey) Reload(false); };
            timer.Start();
            Disposed += (s, e) => timer.Dispose();
            foreach (Control c in new Control[] { frame, _title, _body, _footer })
            {
                c.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) _dragStart = e.Location; };
                c.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left) Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y); };
                c.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) SavedPosition = Location; };
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected abstract string LoadingTitle { get; }
        protected abstract bool ShowAll { get; set; }
        protected abstract Point SavedPosition { get; set; }
        protected abstract Point DefaultPosition(Rectangle workingArea);
        // Reads guide + Current character and computes the overlay's data.
        protected abstract void LoadData(BuildDetails guide, BuildDetails current, string guideError, string currentError);
        // Fills the panel via SetTitle/AddLine/SetFooter.
        protected abstract void RenderContent();

        public string BodyText { get { return string.Join(Environment.NewLine, _body.Lines.Select(l => l.Text).ToArray()); } }
        public string TitleText { get { return _title.Text; } }

        protected void SetTitle(string text) { _title.Text = text; }
        protected void SetFooter(string text) { _footer.Text = text; }
        protected void AddLine(string text, Color color, bool header, string toolTipText = null)
        {
            _body.Lines.Add(new Line { Text = text, Color = color, Header = header, ToolTipText = toolTipText });
        }
        protected void AddWrappedLine(string text, Color color) { _body.Lines.Add(new Line { Text = text, Color = color, Wrap = true }); }
        protected int LineCount { get { return _body.Lines.Count; } }

        public void Toggle()
        {
            AutoShown = false;
            if (Visible) { Hide(); return; }
            ShowOverlay();
        }

        public void ShowOverlay()
        {
            PlaceOnScreen();
            if (!_loaded) _title.Text = LoadingTitle;
            if (!Visible) Show();
            PlaceOnScreen();
            Refresh();
            Reload(false);
        }

        // Recomputes when the guide, its stage or the Current character changed.
        public void Reload(bool force)
        {
            string key = BuildKey();
            if (force || key != _cacheKey || !_loaded)
            {
                Cursor = Cursors.WaitCursor;
                try
                {
                    string error, notice, currentError;
                    BuildDetails current;
                    var guide = GuideBuildView.ReadGuide(_config, true, out error, out notice, out current, out currentError);
                    LoadData(guide != null && guide.Success ? guide : null, current, error, currentError);
                    _cacheKey = key;
                    _loaded = true;
                }
                finally { Cursor = Cursors.Default; }
            }
            Render();
        }

        public void RefreshContent() { if (_loaded) Render(); }

        private void Render()
        {
            _body.ClearHover();
            _body.Lines.Clear();
            RenderContent();
            _footer.Height = Math.Max(30, TextRenderer.MeasureText(_footer.Text, _footer.Font,
                new Size(Math.Max(40, Width - 20), int.MaxValue), TextFormatFlags.WordBreak).Height + 4);
            int h = _title.Parent.Height + _body.ContentHeight + _footer.Height + _body.Parent.Padding.Vertical + Padding.Vertical;
            var area = Screen.FromPoint(Location).WorkingArea;
            Height = Math.Min(h, area.Height - 20);
            _body.AutoScrollMinSize = new Size(0, _body.ContentHeight);
            _body.Invalidate();
        }

        private string BuildKey()
        {
            string stamp = "";
            try
            {
                if (string.IsNullOrWhiteSpace(_config.CurrentCharacterCode) && File.Exists(_config.CurrentBuildFilePath ?? ""))
                    stamp = File.GetLastWriteTimeUtc(_config.CurrentBuildFilePath).Ticks.ToString();
                string guideFile = _config.GetGuideFilePath();
                if (File.Exists(guideFile ?? ""))
                    stamp += "|" + File.GetLastWriteTimeUtc(guideFile).Ticks;
            }
            catch (Exception) { }
            return _config.GetGuideSourceKey() + "|" + _config.GetSelectedGuideLoadout() + "|" + _config.CurrentCharacterCode + "|" + _config.CurrentBuildFilePath + "|" + stamp;
        }

        private void PlaceOnScreen()
        {
            var p = SavedPosition;
            if (!Screen.AllScreens.Any(s => s.WorkingArea.Contains(p))) p = DefaultPosition(Screen.PrimaryScreen.WorkingArea);
            Location = p;
        }
    }
}
