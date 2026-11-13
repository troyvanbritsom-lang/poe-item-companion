using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Poe2ItemCompanion
{
    public class ComparisonPopup : Form
    {
        private ComparisonResult _result;
        private bool _isPinned;
        private int _fontSize;
        private Timer _autoCloseTimer;
        private Panel _contentPanel;
        private Button _pinBtn;
        private Button _closeBtn;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Point _dragStart;
        private bool _isDragging = false;

        // Optional: invoked with the PoB slot name when the user explicitly asks to export.
        public Action<string> ExportRequested;
        // Optional: invoked with the PoB slot name when the user explicitly asks to equip into the source build.
        public Action<string> EquipRequested;

        public ComparisonPopup()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(24, 24, 24);
            ForeColor = Color.White;
            DoubleBuffered = true;
            KeyPreview = true;
            Padding = new Padding(1);

            // Top Header Panel
            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(32, 32, 32),
                Padding = new Padding(8, 4, 8, 4)
            };

            _closeBtn = new Button
            {
                Text = "x",
                Dock = DockStyle.Right,
                Width = 28,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(180, 180, 180),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            _closeBtn.FlatAppearance.BorderSize = 0;
            _closeBtn.Click += (s, e) => Close();

            _pinBtn = new Button
            {
                Text = "Pin",
                Dock = DockStyle.Right,
                Width = 42,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(160, 160, 160),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            _pinBtn.FlatAppearance.BorderSize = 0;
            _pinBtn.Click += TogglePin;

            var titlePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            _titleLabel = new Label
            {
                Text = "Candidate Item",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 255, 120),
                Dock = DockStyle.Top,
                Height = 22,
                AutoEllipsis = true
            };

            _subtitleLabel = new Label
            {
                Text = "Path of Building 2 Preview",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(150, 150, 150),
                Dock = DockStyle.Bottom,
                Height = 18,
                AutoEllipsis = true
            };

            titlePanel.Controls.Add(_titleLabel);
            titlePanel.Controls.Add(_subtitleLabel);

            headerPanel.Controls.Add(titlePanel);
            headerPanel.Controls.Add(_pinBtn);
            headerPanel.Controls.Add(_closeBtn);

            // Dragging support on header
            headerPanel.MouseDown += Header_MouseDown;
            headerPanel.MouseMove += Header_MouseMove;
            headerPanel.MouseUp += Header_MouseUp;
            titlePanel.MouseDown += Header_MouseDown;
            titlePanel.MouseMove += Header_MouseMove;
            titlePanel.MouseUp += Header_MouseUp;
            _titleLabel.MouseDown += Header_MouseDown;
            _titleLabel.MouseMove += Header_MouseMove;
            _titleLabel.MouseUp += Header_MouseUp;

            // Content scroll panel
            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(20, 20, 20),
                Padding = new Padding(10, 8, 10, 8)
            };

            Controls.Add(_contentPanel);
            Controls.Add(headerPanel);

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };

            Deactivate += (s, e) =>
            {
                if (!_isPinned)
                {
                    Close();
                }
            };
            UiTheme.Apply(this);
            _pinBtn.AccessibleName = "Pin comparison popup";
            _closeBtn.AccessibleName = "Close comparison popup";
        }

        private void Header_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _dragStart = e.Location;
            }
        }

        private void Header_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point screenPos = PointToScreen(e.Location);
                Location = new Point(screenPos.X - _dragStart.X, screenPos.Y - _dragStart.Y);
            }
        }

        private void Header_MouseUp(object sender, MouseEventArgs e)
        {
            _isDragging = false;
        }

        private void TogglePin(object sender, EventArgs e)
        {
            _isPinned = !_isPinned;
            _pinBtn.ForeColor = _isPinned ? UiTheme.Accent : UiTheme.Muted;
            if (_autoCloseTimer != null && _isPinned)
            {
                _autoCloseTimer.Stop();
            }
        }

        public void DisplayResult(ComparisonResult result, AppConfig config, Point mousePos)
        {
            _result = result;
            _fontSize = Math.Max(8, Math.Min(16, config.PopupFontSize));
            _isPinned = config.PinPopup;
            _pinBtn.ForeColor = _isPinned ? UiTheme.Accent : UiTheme.Muted;

            _contentPanel.Controls.Clear();

            // Set Header item name & colors
            Color rarityColor = PobStatParser.GetRarityColor(result.ItemRarity);
            _titleLabel.Text = result.ItemName ?? "Unknown Item";
            _titleLabel.ForeColor = rarityColor;

            string sub = result.ItemBase ?? result.ItemType ?? "Item";
            if (!string.IsNullOrEmpty(result.ItemRarity))
            {
                sub += " (" + result.ItemRarity + ")";
            }
            _subtitleLabel.Text = sub + " - deltas vs Current gear";

            int currentY = 4;
            int maxContentWidth = 320;

            foreach (var cmp in result.Comparisons)
            {
                // Slot Header Card
                var slotHeader = new Panel
                {
                    Location = new Point(0, currentY),
                    Width = 330,
                    Height = 54,
                    BackColor = UiTheme.Surface
                };

                var slotBadge = new Label
                {
                    Text = "SLOT: " + cmp.SlotLabel.ToUpperInvariant(),
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    ForeColor = UiTheme.Accent,
                    AutoSize = true,
                    Location = new Point(6, 6)
                };

                Color replColor = PobStatParser.GetRarityColor(cmp.ReplacedItemRarity);
                var replacingLabel = new Label
                {
                    Text = "Current: " + cmp.ReplacedItemName,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                    ForeColor = replColor,
                    AutoEllipsis = true,
                    Size = new Size(318, 20),
                    Location = new Point(6, 29)
                };

                slotHeader.Controls.Add(slotBadge);
                slotHeader.Controls.Add(replacingLabel);
                if (ExportRequested != null)
                {
                    string slotName = cmp.SlotName;
                    var exportBtn = new Button
                    {
                        Text = "Export",
                        Size = new Size(54, 22),
                        Location = new Point(slotHeader.Width - 58, 4),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.FromArgb(60, 70, 100),
                        ForeColor = Color.White,
                        Font = new Font("Segoe UI", 7.5f),
                        Cursor = Cursors.Hand
                    };
                    exportBtn.FlatAppearance.BorderSize = 0;
                    new ToolTip().SetToolTip(exportBtn, "Write a NEW PoB2 build file with this item in " + cmp.SlotLabel + " (source build is not changed)");
                    exportBtn.Click += (s, e) =>
                    {
                        var cb = ExportRequested;
                        _isPinned = true; // keep Deactivate from closing mid-click
                        Hide();
                        try { cb(slotName); }
                        finally { Close(); }
                    };
                    slotHeader.Controls.Add(exportBtn);
                    exportBtn.BringToFront();
                }
                if (EquipRequested != null)
                {
                    string equipSlot = cmp.SlotName;
                    var equipBtn = new Button
                    {
                        Text = "Equip",
                        Size = new Size(54, 22),
                        Location = new Point(slotHeader.Width - 116, 4),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.FromArgb(110, 70, 40),
                        ForeColor = Color.White,
                        Font = new Font("Segoe UI", 7.5f),
                        Cursor = Cursors.Hand
                    };
                    equipBtn.FlatAppearance.BorderSize = 0;
                    new ToolTip().SetToolTip(equipBtn, "Save this item into your Current character build file in " + cmp.SlotLabel + " (asks first, backs up the original)");
                    equipBtn.Click += (s, e) =>
                    {
                        var cb = EquipRequested;
                        _isPinned = true;
                        Hide();
                        try { cb(equipSlot); }
                        finally { Close(); }
                    };
                    slotHeader.Controls.Add(equipBtn);
                    equipBtn.BringToFront();
                }
                UiTheme.Apply(slotHeader);
                _contentPanel.Controls.Add(slotHeader);
                currentY += slotHeader.Height + 4;
                var fit = ItemStatRatings.CreateView(cmp, 324, _fontSize);
                fit.Location = new Point(6, currentY);
                _contentPanel.Controls.Add(fit);
                currentY += fit.PreferredSize.Height + 6;

                // Stat lines
                // Filter out lines that are headers since we already show the slot header nicely
                int renderedStatLines = 0;
                foreach (string rawLine in cmp.Lines)
                {
                    if (rawLine.StartsWith("Equipping this in") || rawLine.StartsWith("(replacing"))
                    {
                        continue;
                    }

                    var lineControl = new FormattedLineControl(rawLine, _fontSize)
                    {
                        Location = new Point(6, currentY),
                        Width = 324
                    };

                    _contentPanel.Controls.Add(lineControl);
                    currentY += lineControl.Height + 1;
                    renderedStatLines++;
                    if (lineControl.PreferredWidth > maxContentWidth)
                    {
                        maxContentWidth = lineControl.PreferredWidth;
                    }
                }

                if (renderedStatLines == 0)
                {
                    string noChangeMsg = string.Equals(cmp.ReplacedItemName, result.ItemName, StringComparison.OrdinalIgnoreCase)
                        ? "^7No stat differences (identical to equipped item)"
                        : "^7No stat differences for current build/skills";

                    var noChangeControl = new FormattedLineControl(noChangeMsg, _fontSize)
                    {
                        Location = new Point(6, currentY),
                        Width = 324
                    };
                    _contentPanel.Controls.Add(noChangeControl);
                    currentY += noChangeControl.Height + 1;
                    if (noChangeControl.PreferredWidth > maxContentWidth)
                    {
                        maxContentWidth = noChangeControl.PreferredWidth;
                    }
                }

                currentY += 8; // spacing between slots
            }

            // Auto-size window based on content
            int totalWidth = Math.Max(350, Math.Min(600, maxContentWidth + 36));
            int totalHeight = Math.Min(650, currentY + 54);

            Size = new Size(totalWidth, totalHeight);

            // Position near cursor
            PositionNearCursor(mousePos);

            // Timeout timer
            if (config.PopupTimeoutSeconds > 0 && !_isPinned)
            {
                _autoCloseTimer = new Timer { Interval = config.PopupTimeoutSeconds * 1000 };
                _autoCloseTimer.Tick += (s, e) =>
                {
                    _autoCloseTimer.Stop();
                    Close();
                };
                _autoCloseTimer.Start();
            }

            Show();
            BringToFront();
        }

        private void PositionNearCursor(Point cursor)
        {
            Screen screen = Screen.FromPoint(cursor);
            Rectangle workArea = screen.WorkingArea;

            int targetX = cursor.X + 24;
            int targetY = cursor.Y - 20;

            // Flip horizontally if overflow
            if (targetX + Width > workArea.Right)
            {
                targetX = cursor.X - Width - 16;
            }

            // Keep within bounds
            if (targetX < workArea.Left)
            {
                targetX = workArea.Left + 10;
            }

            if (targetY + Height > workArea.Bottom)
            {
                targetY = workArea.Bottom - Height - 10;
            }

            if (targetY < workArea.Top)
            {
                targetY = workArea.Top + 10;
            }

            Location = new Point(targetX, targetY);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Outer subtle border
            using (var pen = new Pen(UiTheme.Border, 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    public class FormattedLineControl : Control
    {
        private List<FormattedRun> _runs;
        private Font _font;

        public int PreferredWidth { get; private set; }

        public FormattedLineControl(string rawLine, int fontSize)
        {
            DoubleBuffered = true;
            _font = new Font("Segoe UI", fontSize, FontStyle.Regular);
            _runs = PobStatParser.ParseFormattedLine(rawLine, PobStatParser.NeutralColor);

            Height = _font.Height + 2;

            // Measure width
            int w = 0;
            using (var g = CreateGraphics())
            {
                foreach (var run in _runs)
                {
                    var sz = TextRenderer.MeasureText(g, run.Text, _font, Size.Empty, TextFormatFlags.NoPadding);
                    w += sz.Width;
                }
            }
            PreferredWidth = w + 16;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int currentX = 0;
            int y = 0;

            foreach (var run in _runs)
            {
                TextRenderer.DrawText(e.Graphics, run.Text, _font, new Point(currentX, y), run.Color, TextFormatFlags.NoPadding);
                var sz = TextRenderer.MeasureText(e.Graphics, run.Text, _font, Size.Empty, TextFormatFlags.NoPadding);
                currentX += sz.Width;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_font != null) _font.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
