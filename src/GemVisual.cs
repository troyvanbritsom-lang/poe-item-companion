using System;
using System.Drawing;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    internal static class GemVisual
    {
        public static Color Border(int color)
        {
            return color == 1 ? Color.FromArgb(224, 90, 85) : color == 2 ? Color.FromArgb(91, 190, 113)
                : color == 3 ? Color.FromArgb(100, 160, 230) : UiTheme.Muted;
        }

        public static void Draw(Graphics graphics, Rectangle bounds, int color, string kind, Image image = null)
        {
            if (image == null) return;
            Color tint = Border(color);
            using (var background = new SolidBrush(Color.FromArgb(30, tint.R / 3, tint.G / 3)))
                graphics.FillRectangle(background, bounds);
            using (var pen = new Pen(tint))
                graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            float scale = Math.Min((bounds.Width - 6f) / image.Width, (bounds.Height - 6f) / image.Height);
            int width = Math.Max(1, (int)(image.Width * scale)), height = Math.Max(1, (int)(image.Height * scale));
            graphics.DrawImage(image, bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
        }
    }

    internal sealed class SkillRows : ListBox
    {
        private GemIconCache _icons;

        public void SetIcons(AppConfig config)
        {
            if (_icons != null) _icons.Dispose();
            _icons = new GemIconCache(config);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _icons != null) _icons.Dispose();
            base.Dispose(disposing);
        }
        internal sealed class Row
        {
            public string Text;
            public SkillGem Gem;
            public override string ToString() { return Text; }
        }

        public SkillRows()
        {
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Canvas;
            ForeColor = UiTheme.Text;
            BorderStyle = BorderStyle.None;
            DrawMode = DrawMode.OwnerDrawVariable;
            HorizontalScrollbar = true;
            AccessibleName = "Guide skills";
        }

        public void Add(string text, SkillGem gem = null)
        {
            Items.Add(new Row { Text = text, Gem = gem });
            HorizontalExtent = Math.Max(HorizontalExtent, TextRenderer.MeasureText(text, Font).Width + (gem == null ? 16 : 60));
        }

        public void ResetRows()
        {
            Items.Clear();
            HorizontalExtent = 0;
        }

        protected override void OnMeasureItem(MeasureItemEventArgs e)
        {
            base.OnMeasureItem(e);
            if (e.Index >= 0 && e.Index < Items.Count)
                e.ItemHeight = ((Row)Items[e.Index]).Gem == null ? Math.Max(26, Font.Height + 8) : Math.Max(48, Font.Height + 12);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            e.DrawBackground();
            var row = (Row)Items[e.Index];
            int x = e.Bounds.Left + 6;
            if (row.Gem != null)
            {
                GemVisual.Draw(e.Graphics, new Rectangle(x, e.Bounds.Top + (e.Bounds.Height - 38) / 2, 38, 38), row.Gem.Color, row.Gem.Kind,
                    _icons == null ? null : _icons.Get(row.Gem.Name, row.Gem.Kind));
                x += 50;
            }
            TextRenderer.DrawText(e.Graphics, row.Text, Font, new Rectangle(x, e.Bounds.Top, Math.Max(1, e.Bounds.Right - x), e.Bounds.Height),
                (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : row.Gem == null ? UiTheme.Muted : UiTheme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
            base.OnDrawItem(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C && SelectedItem != null)
            {
                try { Clipboard.SetText(SelectedItem.ToString()); }
                catch (System.Runtime.InteropServices.ExternalException ex) { Logger.Warn("Could not copy skill row: " + ex.Message); }
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
    }
}
