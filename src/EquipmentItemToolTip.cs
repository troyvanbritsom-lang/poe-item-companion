using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    internal sealed class EquipmentItemToolTip : ToolTip
    {
        private EquipmentItemCard _card;

        public EquipmentItemToolTip()
        {
            OwnerDraw = true;
            ShowAlways = true;
            InitialDelay = 300;
            ReshowDelay = 100;
            AutoPopDelay = 30000;
            UseAnimation = false;
            UseFading = false;
            Popup += (s, e) =>
            {
                if (_card == null) { e.Cancel = true; return; }
                using (var graphics = e.AssociatedControl.CreateGraphics())
                    e.ToolTipSize = _card.Measure(graphics, Screen.FromPoint(Cursor.Position).WorkingArea.Size);
            };
            Draw += (s, e) => { if (_card != null) _card.Draw(e.Graphics, e.Bounds); };
        }

        public void SetItem(Control owner, string slot, GearEntry gear, ItemArtCache art)
        {
            Clear(owner);
            _card = new EquipmentItemCard(slot, gear, art);
            SetToolTip(owner, gear == null ? slot + ": empty in this guide stage" : EquipmentOverlay.ToolTipFor(gear, art));
        }

        public void Clear(Control owner)
        {
            Hide(owner);
            SetToolTip(owner, null);
            if (_card != null) { _card.Dispose(); _card = null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _card != null) { _card.Dispose(); _card = null; }
            base.Dispose(disposing);
        }
    }

    // Original vector decoration; no game UI textures or fonts are copied or bundled.
    internal sealed class EquipmentItemCard : IDisposable
    {
        internal sealed class Line
        {
            public string Text;
            public Color Color;
            public bool Header, Small, Divider;
            public Rectangle Bounds;
        }

        internal static readonly Color Background = Color.FromArgb(8, 9, 12);
        internal static readonly Color Modifier = Color.FromArgb(153, 153, 255);
        internal static readonly Color Crafted = Color.FromArgb(184, 218, 255);
        private static readonly Color Fractured = Color.FromArgb(208, 180, 122);
        private static readonly Color Muted = Color.FromArgb(168, 165, 155);
        private const TextFormatFlags TextFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;
        private Font _headerFont, _bodyFont, _smallFont;
        private readonly List<Line> _lines = new List<Line>();
        private float _scale;
        private int _headerBottom;

        internal IList<Line> Lines { get { return _lines.AsReadOnly(); } }
        internal Color Rarity { get; private set; }

        public EquipmentItemCard(string slot, GearEntry gear, ItemArtCache art)
        {
            Rarity = PobStatParser.GetRarityColor(gear == null ? null : gear.Rarity);
            if (gear == null)
            {
                Add(slot, Rarity, true);
                Add("Empty in this guide stage", Muted);
                return;
            }

            string title = TradeSearch.TradeRarity(gear.Rarity) == "unique" ? TradeSearch.UniqueName(gear) : gear.ItemName;
            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(gear.BaseName) &&
                title.EndsWith(", " + gear.BaseName, StringComparison.OrdinalIgnoreCase))
                title = title.Substring(0, title.Length - gear.BaseName.Length - 2);
            Add(string.IsNullOrEmpty(title) ? gear.BaseName ?? "(unnamed)" : title, Rarity, true);
            if (!string.IsNullOrEmpty(gear.BaseName) && !string.Equals(title, gear.BaseName, StringComparison.OrdinalIgnoreCase))
                Add(gear.BaseName, Rarity, true);

            // Reuse the text tooltip's content so the styled card retains every detail and warning.
            string[] details = EquipmentOverlay.ToolTipFor(gear, art).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            int headerLines = 1 + (!string.IsNullOrEmpty(gear.BaseName) && gear.BaseName != gear.ItemName ? 1 : 0);
            bool mods = false;
            foreach (string text in details.Skip(headerLines))
            {
                if (text.StartsWith("-- ", StringComparison.Ordinal) && text.EndsWith(" --", StringComparison.Ordinal))
                {
                    _lines.Add(new Line { Text = text.Substring(3, text.Length - 6), Color = Muted, Small = true, Divider = true });
                    mods = true;
                }
                else if (text.StartsWith("Right-click:", StringComparison.Ordinal) ||
                    text.StartsWith("Trade search unavailable:", StringComparison.Ordinal) || text.StartsWith("Art:", StringComparison.Ordinal))
                    Add(text, Muted, false, true);
                else if (text == "Corrupted") Add(text, Color.FromArgb(235, 98, 98));
                else if (text == "Mirrored") Add(text, Crafted);
                else if (mods)
                    Add(text, text.Contains("(crafted") ? Crafted : text.Contains("(fractured") ? Fractured : Modifier);
                else
                    foreach (string part in text.Split(new[] { " | " }, StringSplitOptions.None))
                        Add(part, Muted);
            }
        }

        private void Add(string text, Color color, bool header = false, bool small = false)
        {
            _lines.Add(new Line { Text = text, Color = color, Header = header, Small = small });
        }

        private Font FontFor(Line line) { return line.Header ? _headerFont : line.Small ? _smallFont : _bodyFont; }

        internal Size Measure(Graphics graphics, Size available)
        {
            float scale = graphics.DpiX / 96f;
            if (_headerFont == null || Math.Abs(_scale - scale) > 0.001f)
            {
                Dispose();
                _headerFont = new Font("Georgia", 16f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                _bodyFont = new Font("Georgia", 13.333f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                _smallFont = new Font("Segoe UI", 10.667f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            _scale = scale;
            int padding = (int)Math.Ceiling(18 * _scale);
            int maxWidth = Math.Max(1, Math.Min((int)(580 * _scale), available.Width - (int)(24 * _scale)));
            int contentWidth = Math.Max(1, maxWidth - padding * 2);
            int naturalWidth = _lines.Max(l => TextRenderer.MeasureText(graphics, l.Text, FontFor(l), new Size(int.MaxValue, int.MaxValue),
                TextFlags & ~TextFormatFlags.WordBreak).Width);
            int width = Math.Min(maxWidth, Math.Max((int)(330 * _scale), naturalWidth + padding * 2));
            contentWidth = Math.Max(1, width - padding * 2);
            int y = (int)(10 * _scale);
            _headerBottom = 0;
            foreach (var line in _lines)
            {
                if (!line.Header && _headerBottom == 0)
                {
                    _headerBottom = y + (int)(7 * _scale);
                    y = _headerBottom + (int)(10 * _scale);
                }
                int height = TextRenderer.MeasureText(graphics, line.Text, FontFor(line), new Size(contentWidth, int.MaxValue), TextFlags).Height;
                if (line.Divider) y += (int)(9 * _scale);
                line.Bounds = new Rectangle(padding, y, contentWidth, height);
                y += height + (int)(3 * _scale);
            }
            return new Size(width, y + (int)(10 * _scale));
        }

        internal void Draw(Graphics graphics, Rectangle bounds)
        {
            graphics.Clear(Background);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var header = new Rectangle(1, 1, bounds.Width - 2, Math.Max(1, _headerBottom - 1));
            using (var fill = new LinearGradientBrush(header, Color.FromArgb(48, Rarity), Color.FromArgb(12, Rarity), 90f))
                graphics.FillRectangle(fill, header);
            using (var border = new Pen(Rarity))
            using (var faint = new Pen(Color.FromArgb(100, Rarity)))
            {
                graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
                graphics.DrawRectangle(faint, 3, 3, bounds.Width - 7, bounds.Height - 7);
                graphics.DrawLine(border, 1, _headerBottom, bounds.Width - 2, _headerBottom);
                int inset = (int)(9 * _scale), diamond = Math.Max(2, (int)(3 * _scale));
                foreach (int x in new[] { inset, bounds.Width - inset - 1 })
                {
                    int y = Math.Max(inset, _headerBottom / 2);
                    graphics.DrawPolygon(border, new[] { new Point(x, y - diamond), new Point(x + diamond, y),
                        new Point(x, y + diamond), new Point(x - diamond, y) });
                }
                foreach (var line in _lines)
                {
                    if (line.Divider)
                    {
                        int y = line.Bounds.Top - (int)(5 * _scale);
                        graphics.DrawLine(faint, bounds.Width / 4, y, bounds.Width * 3 / 4, y);
                    }
                    TextRenderer.DrawText(graphics, line.Text, FontFor(line), line.Bounds, line.Color, TextFlags);
                }
            }
        }

        public void Dispose()
        {
            if (_headerFont != null) { _headerFont.Dispose(); _headerFont = null; }
            if (_bodyFont != null) { _bodyFont.Dispose(); _bodyFont = null; }
            if (_smallFont != null) { _smallFont.Dispose(); _smallFont = null; }
        }
    }
}
