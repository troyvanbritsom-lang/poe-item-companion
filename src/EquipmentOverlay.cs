using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    // In-game panel: the selected GUIDE stage's gear in an inventory-style layout. Never shows the Current character.
    public class EquipmentOverlay : OverlayForm
    {
        public const float GridUnits = 8.8f;

        public sealed class Cell
        {
            public string Slot;
            public string Label;
            public RectangleF Units;   // position/size in inventory cells
            public GearEntry Gear;
        }

        private static readonly Dictionary<string, RectangleF> MainLayout = new Dictionary<string, RectangleF>
        {
            { "Weapon 1", new RectangleF(0f, 0f, 2f, 4f) },
            { "Helmet", new RectangleF(3.4f, 0f, 2f, 2f) },
            { "Ring 1", new RectangleF(2.2f, 2.2f, 1f, 1f) },
            { "Body Armour", new RectangleF(3.4f, 2.2f, 2f, 3f) },
            { "Amulet", new RectangleF(5.6f, 1f, 1f, 1f) },
            { "Ring 2", new RectangleF(5.6f, 2.2f, 1f, 1f) },
            { "Ring 3", new RectangleF(5.6f, 3.4f, 1f, 1f) },
            { "Weapon 2", new RectangleF(6.8f, 0f, 2f, 4f) },
            { "Gloves", new RectangleF(0f, 4.4f, 2f, 2f) },
            { "Belt", new RectangleF(3.4f, 5.4f, 2f, 1f) },
            { "Boots", new RectangleF(6.8f, 4.4f, 2f, 2f) },
        };

        private readonly EquipmentPanel _panel;
        private BuildDetails _guide;
        private string _error;
        private List<Cell> _cells = new List<Cell>();
        private int _hiddenJewels, _artCount, _itemCount;

        public EquipmentOverlay(AppConfig config) : base(config, 430)
        {
            _panel = new EquipmentPanel(this) { BackColor = UiTheme.Surface };
            UseCustomBody(_panel, () => ContentHeight(_panel.ClientSize.Width > 0 ? _panel.ClientSize.Width : Width - 20));
        }

        internal List<Cell> Cells { get { return _cells; } }
        internal ItemArtCache Art { get { return _panel.Art; } }
        internal BuildDetails GuideDetails { get { return _guide; } }

        protected override string LoadingTitle { get { return "Equipment - loading..."; } }

        protected override bool ShowAll
        {
            get { return _config.EquipmentOverlayShowSwap; }
            set { _config.EquipmentOverlayShowSwap = value; }
        }

        protected override Point SavedPosition
        {
            get { return new Point(_config.EquipmentOverlayX, _config.EquipmentOverlayY); }
            set { _config.EquipmentOverlayX = value.X; _config.EquipmentOverlayY = value.Y; _config.Save(); }
        }

        protected override Point DefaultPosition(Rectangle area)
        {
            return new Point(area.Right - Width - 40, area.Top + 120);
        }

        // 'current' is deliberately ignored: recommended equipment always comes from the selected guide stage.
        protected override void LoadData(BuildDetails guide, BuildDetails current, string guideError, string currentError)
        {
            _guide = guide;
            _error = guide == null ? "Guide unavailable: " + (guideError ?? "no guide loaded") : null;
        }

        internal void LoadForTest(BuildDetails guide, BuildDetails current)
        {
            LoadData(guide, current, null, null);
            RefreshCells();
            if (Visible) RenderLoadedForTest();
        }

        protected override void RenderContent()
        {
            RefreshCells();
            SetToggleText(ShowAll ? "swap" : "main", ShowAll ? "Showing weapon swap set; click for main set" : "Showing main weapon set; click for weapon swap set");
            if (_guide == null)
            {
                SetTitle("Equipment");
                SetFooter((_error ?? "Load a guide in the companion.") + Environment.NewLine + "Open overlays with " + SelectorKeyText(_config) + ".");
                return;
            }
            SetTitle("Equipment - guide: " + (_guide.ActiveLoadout ?? _guide.BuildName ?? "build"));
            var footer = new StringBuilder("Selected guide stage, not your Current character. Right-click an item: Search on Trade.");
            footer.Append(Environment.NewLine);
            if (_panel.Art.Unavailable != null) footer.Append(_panel.Art.Unavailable);
            else footer.Append("Local art: " + _artCount + "/" + _itemCount + " items; others are text only.");
            if (_hiddenJewels > 0) footer.Append(" " + _hiddenJewels + " tree jewel(s) not shown.");
            if (_cells.Any(c => c.Gear != null && c.Gear.FromNotes)) footer.Append(" Dashed = .build guide note, not a rolled item.");
            SetFooter(footer.ToString());
        }

        internal static string SelectorKeyText(AppConfig config)
        {
            return config.OverlaySelectorHotkeyKey == 0 ? "the tray menu (selector key disabled)" : config.GetOverlaySelectorHotkeyDisplayString();
        }

        private void RefreshCells()
        {
            _cells = BuildCells(_guide, ShowAll, out _hiddenJewels);
            _itemCount = _cells.Count(c => c.Gear != null && !c.Gear.FromNotes);
            string reason;
            _artCount = _cells.Count(c => c.Gear != null && _panel.Art.ArtName(c.Gear, out reason) != null);
            _panel.ClearHover();
        }

        internal static List<Cell> BuildCells(BuildDetails guide, bool swap, out int hiddenJewels)
        {
            hiddenJewels = 0;
            var cells = new List<Cell>();
            var gear = guide == null || guide.Gear == null ? new List<GearEntry>() : guide.Gear.Where(g => g != null && !string.IsNullOrEmpty(g.SlotName)
                && (!string.IsNullOrEmpty(g.ItemName) || !string.IsNullOrEmpty(g.BaseName))).ToList();
            var bySlot = new Dictionary<string, GearEntry>(StringComparer.Ordinal);
            foreach (var g in gear)
            {
                if (g.IsJewel) { hiddenJewels++; continue; }
                if (!bySlot.ContainsKey(g.SlotName)) bySlot[g.SlotName] = g;
            }
            foreach (var pair in MainLayout)
            {
                string slot = pair.Key;
                if (swap && (slot == "Weapon 1" || slot == "Weapon 2")) slot += " Swap";
                GearEntry item;
                bySlot.TryGetValue(slot, out item);
                bool optionalRing = pair.Key == "Ring 3" && item == null;
                if (optionalRing) continue;
                cells.Add(new Cell { Slot = slot, Label = slot, Units = pair.Value, Gear = item });
            }
            var used = new HashSet<string>(MainLayout.Keys, StringComparer.Ordinal) { "Weapon 1 Swap", "Weapon 2 Swap" };
            var flasks = bySlot.Keys.Where(s => s.StartsWith("Flask ", StringComparison.Ordinal)).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var charms = bySlot.Keys.Where(s => s.StartsWith("Charm ", StringComparison.Ordinal)).OrderBy(s => s, StringComparer.Ordinal).ToList();
            float row = 6.8f;
            float width = flasks.Count * 1.1f + charms.Count * 1.1f - 0.1f;
            float x = Math.Max(0f, (GridUnits - width) / 2f);
            foreach (var slot in flasks) { cells.Add(new Cell { Slot = slot, Label = slot, Units = new RectangleF(x, row, 1f, 2f), Gear = bySlot[slot] }); x += 1.1f; used.Add(slot); }
            foreach (var slot in charms) { cells.Add(new Cell { Slot = slot, Label = slot, Units = new RectangleF(x, row + 0.5f, 1f, 1f), Gear = bySlot[slot] }); x += 1.1f; used.Add(slot); }
            float extraRow = flasks.Count + charms.Count > 0 ? 9.0f : 6.8f;
            x = 0f;
            foreach (var slot in bySlot.Keys.Where(s => !used.Contains(s)).OrderBy(s => s, StringComparer.Ordinal))
            {
                if (x + 1f > GridUnits + 0.01f) { x = 0f; extraRow += 1.1f; }
                cells.Add(new Cell { Slot = slot, Label = slot, Units = new RectangleF(x, extraRow, 1f, 1f), Gear = bySlot[slot] });
                x += 1.1f;
            }
            return cells;
        }

        private int ContentHeight(int width)
        {
            float unit = UnitSize(width);
            float bottom = _cells.Count == 0 ? 6.4f : _cells.Max(c => c.Units.Bottom);
            return (int)Math.Ceiling(bottom * unit) + 8;
        }

        private static float UnitSize(int width)
        {
            return Math.Max(24f, (width - 4) / GridUnits);
        }

        internal static string ToolTipFor(GearEntry g, ItemArtCache art)
        {
            var sb = new StringBuilder();
            sb.AppendLine(g.ItemName ?? "(unnamed)");
            if (!string.IsNullOrEmpty(g.BaseName) && g.BaseName != g.ItemName) sb.AppendLine(g.BaseName);
            sb.AppendLine((g.SlotLabel ?? g.SlotName) + " - " + (g.FromNotes ? (g.Rarity == "UNIQUE" ? "unique named in .build guide note" : ".build guide note") : RarityName(g.Rarity)));
            if (g.FromNotes)
            {
                sb.AppendLine("Not a concrete rolled item: the .build format has no base type or affix rolls.");
                if (g.Priorities != null && g.Priorities.Count > 0)
                {
                    sb.AppendLine("Stat priority from the note:");
                    foreach (var p in g.Priorities.Take(8)) sb.AppendLine("  " + p);
                }
                return sb.ToString().TrimEnd();
            }
            var meta = new List<string>();
            if (g.ItemLevel.HasValue) meta.Add("Item level " + g.ItemLevel);
            if (g.LevelRequirement.HasValue && g.LevelRequirement > 0) meta.Add("Requires level " + g.LevelRequirement);
            if (g.Quality.HasValue && g.Quality > 0) meta.Add("Quality +" + g.Quality + "%");
            if (g.SocketCount > 0) meta.Add(g.SocketCount + " socket" + (g.SocketCount == 1 ? "" : "s") + (g.MaxLinks > 1 ? ", " + g.MaxLinks + " linked" : ""));
            if (meta.Count > 0) sb.AppendLine(string.Join(" | ", meta.ToArray()));
            if (g.Influences != null && g.Influences.Count > 0) sb.AppendLine("Influence: " + string.Join(", ", g.Influences.ToArray()));
            if (g.Corrupted) sb.AppendLine("Corrupted");
            if (g.Mirrored) sb.AppendLine("Mirrored");
            foreach (var group in (g.Affixes ?? new List<ItemAffix>()).GroupBy(a => KindLabel(a)))
            {
                sb.AppendLine("-- " + group.Key + " --");
                foreach (var a in group)
                {
                    string text = TradeSearch.CleanModText(a.Text);
                    if (text != null) sb.AppendLine(text + Flags(a));
                }
            }
            if (art != null)
            {
                string reason;
                if (art.ArtName(g, out reason) == null) sb.AppendLine("Art: text only (" + reason + ")");
            }
            string why = TradeSearch.UnsearchableReason(g);
            sb.Append(why == null ? "Right-click: Search on Trade" : "Trade search unavailable: " + why);
            return sb.ToString();
        }

        private static string RarityName(string rarity)
        {
            string r = (rarity ?? "").ToLowerInvariant();
            return r.Length == 0 ? "item" : char.ToUpperInvariant(r[0]) + r.Substring(1);
        }

        private static string KindLabel(ItemAffix a)
        {
            switch (TradeSearch.AffixKind(a))
            {
                case "rune": return "Rune / augment";
                case "enchant": return "Enchant";
                case "implicit": return "Implicit";
                default: return "Explicit";
            }
        }

        private static string Flags(ItemAffix a)
        {
            var f = new List<string>();
            if (a.Crafted) f.Add("crafted");
            if (a.Fractured) f.Add("fractured");
            if (a.Exarch) f.Add("Exarch");
            if (a.Eater) f.Add("Eater");
            if (a.Synthesis) f.Add("synthesised");
            return f.Count == 0 ? "" : "  (" + string.Join(", ", f.ToArray()) + ")";
        }

        internal void OpenTradeSearch(GearEntry gear)
        {
            string why = TradeSearch.UnsearchableReason(gear);
            if (why != null)
            {
                MessageBox.Show(why, "Search on Trade", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dialog = new TradeSearchDialog(_config, gear)) dialog.ShowDialog();
        }

        private sealed class EquipmentPanel : Panel
        {
            private readonly EquipmentOverlay _owner;
            private readonly EquipmentItemToolTip _toolTip = new EquipmentItemToolTip();
            private readonly Font _nameFont = new Font("Segoe UI", 8f, FontStyle.Bold);
            private readonly Font _smallFont = new Font("Segoe UI", 7f);
            private Cell _hovered;
            public readonly ItemArtCache Art;

            public EquipmentPanel(EquipmentOverlay owner)
            {
                _owner = owner;
                Art = new ItemArtCache(owner._config.Game);
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            private RectangleF CellBounds(Cell c)
            {
                float u = UnitSize(ClientSize.Width);
                float offset = Math.Max(0f, (ClientSize.Width - GridUnits * u) / 2f);
                return new RectangleF(offset + c.Units.X * u, 2 + c.Units.Y * u, c.Units.Width * u, c.Units.Height * u);
            }

            private Cell CellAt(Point p)
            {
                return _owner._cells.FirstOrDefault(c => CellBounds(c).Contains(p));
            }

            public void ClearHover()
            {
                _toolTip.Clear(this);
                _hovered = null;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (e.Button != MouseButtons.None) { ClearHover(); return; }
                var cell = CellAt(e.Location);
                if (cell == _hovered) return;
                ClearHover();
                _hovered = cell;
                Cursor = cell != null && cell.Gear != null ? Cursors.Hand : Cursors.Default;
                if (cell != null)
                    _toolTip.SetItem(this, cell.Label, cell.Gear, Art);
            }

            protected override void OnMouseLeave(EventArgs e) { ClearHover(); base.OnMouseLeave(e); }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Right) return;
                var cell = CellAt(e.Location);
                if (cell == null || cell.Gear == null) return;
                ClearHover();
                var gear = cell.Gear;
                var menu = new ContextMenuStrip();
                string why = TradeSearch.UnsearchableReason(gear);
                var search = new ToolStripMenuItem("Search on Trade...") { Enabled = why == null };
                search.Click += (s, a) => _owner.OpenTradeSearch(gear);
                menu.Items.Add(search);
                if (why != null) menu.Items.Add(new ToolStripMenuItem(why.Length > 70 ? why.Substring(0, 67) + "..." : why) { Enabled = false, ToolTipText = why });
                menu.Closed += (s, a) => BeginInvoke((Action)menu.Dispose);
                menu.Show(this, e.Location);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                foreach (var cell in _owner._cells)
                {
                    var r = CellBounds(cell);
                    var rect = Rectangle.Round(r);
                    using (var back = new SolidBrush(Color.FromArgb(14, 14, 18))) g.FillRectangle(back, rect);
                    var gear = cell.Gear;
                    if (gear == null)
                    {
                        using (var pen = new Pen(UiTheme.Border)) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                        TextRenderer.DrawText(g, cell.Label, _smallFont, rect, UiTheme.Muted,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        continue;
                    }
                    Color color = gear.FromNotes && gear.Rarity != "UNIQUE" ? UiTheme.Muted : PobStatParser.GetRarityColor(gear.Rarity);
                    string reason;
                    string artName = Art.ArtName(gear, out reason);
                    var image = artName == null ? null : Art.Get(artName);
                    if (image != null)
                    {
                        var inner = Rectangle.Inflate(rect, -4, -4);
                        // PoE1 flask art is a 3-part strip (glass, cut-away frame, liquid): draw the liquid behind the glass.
                        bool flaskStrip = _owner._config.Game == GameId.Poe1 &&
                            cell.Slot.StartsWith("Flask", StringComparison.Ordinal) && image.Width > image.Height;
                        var source = flaskStrip ? new RectangleF(0, 0, image.Width / 3f, image.Height) : new RectangleF(0, 0, image.Width, image.Height);
                        float scale = Math.Min(inner.Width / source.Width, inner.Height / source.Height);
                        var size = new SizeF(source.Width * scale, source.Height * scale);
                        var target = new RectangleF(inner.X + (inner.Width - size.Width) / 2f, inner.Y + (inner.Height - size.Height) / 2f, size.Width, size.Height);
                        if (flaskStrip)
                            g.DrawImage(image, target, new RectangleF(source.Width * 2f, 0, source.Width, source.Height), GraphicsUnit.Pixel);
                        g.DrawImage(image, target, source, GraphicsUnit.Pixel);
                    }
                    else
                    {
                        var textRect = Rectangle.Inflate(rect, -3, -3);
                        int tagHeight = TextRenderer.MeasureText("Ag", _smallFont).Height;
                        if (textRect.Height > tagHeight * 2) textRect.Height -= tagHeight;
                        TextRenderer.DrawText(g, gear.ItemName ?? gear.BaseName ?? cell.Label, _nameFont, textRect, color,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                        if (rect.Height > tagHeight * 2)
                            TextRenderer.DrawText(g, gear.FromNotes ? "note" : "text only", _smallFont,
                                new Rectangle(rect.X, rect.Bottom - tagHeight - 2, rect.Width, tagHeight), UiTheme.Muted,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                    }
                    using (var pen = new Pen(color, 2f))
                    {
                        if (gear.FromNotes) pen.DashStyle = DashStyle.Dash;
                        g.DrawRectangle(pen, rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
                    }
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { _toolTip.Dispose(); _nameFont.Dispose(); _smallFont.Dispose(); Art.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }
}
