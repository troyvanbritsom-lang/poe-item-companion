using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    // Read-only schematic view of the build's allocated passive tree (positions come from PoB2's tree data).
    // Wheel = zoom, drag = pan, double-click = fit, hover = node details.
    public class PassiveTreeControl : Control
    {
        private TreeData _tree;
        private Dictionary<int, TreeNode> _byId = new Dictionary<int, TreeNode>();
        private RectangleF _bounds;
        private float _scale = 1f;
        private float _fitScale = 1f;
        private PointF _offset;
        private bool _dragging;
        private Point _dragStart;
        private PointF _dragOffsetStart;
        private TreeNode _hover;
        private ToolTip _tip;
        private bool _showUnallocated = true;

        public PassiveTreeControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.Canvas;
            _tip = new ToolTip { InitialDelay = 0, ReshowDelay = 0, AutoPopDelay = 30000, ShowAlways = true };
        }

        public bool ShowUnallocated
        {
            get { return _showUnallocated; }
            set { _showUnallocated = value; Invalidate(); }
        }

        public void SetTree(TreeData tree)
        {
            _tree = tree;
            _byId.Clear();
            _hover = null;
            _tip.SetToolTip(this, null);
            if (tree != null && tree.Nodes != null)
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var n in tree.Nodes)
                {
                    _byId[n.Id] = n;
                    minX = Math.Min(minX, (float)n.X); maxX = Math.Max(maxX, (float)n.X);
                    minY = Math.Min(minY, (float)n.Y); maxY = Math.Max(maxY, (float)n.Y);
                }
                _bounds = tree.Nodes.Count > 0 ? RectangleF.FromLTRB(minX, minY, maxX, maxY) : RectangleF.Empty;
            }
            FitToView();
        }

        public void FitToView()
        {
            if (_tree == null || _bounds.Width <= 0 || _bounds.Height <= 0 || Width < 10 || Height < 10)
            {
                Invalidate();
                return;
            }
            _fitScale = Math.Min((Width - 20) / _bounds.Width, (Height - 20) / _bounds.Height);
            _scale = _fitScale;
            _offset = new PointF(Width / 2f - (_bounds.Left + _bounds.Width / 2f) * _scale, Height / 2f - (_bounds.Top + _bounds.Height / 2f) * _scale);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitToView();
        }

        private PointF ToScreen(TreeNode n)
        {
            return new PointF((float)n.X * _scale + _offset.X, (float)n.Y * _scale + _offset.Y);
        }

        private float NodeRadius(TreeNode n)
        {
            float zoom = (float)Math.Sqrt(_scale / Math.Max(_fitScale, 1e-6f));
            float r;
            switch (n.Type)
            {
                case "Keystone": r = 5.5f; break;
                case "Notable": r = 3.8f; break;
                case "ClassStart":
                case "AscendClassStart": r = 5f; break;
                case "Socket": r = 3.5f; break;
                default: r = 2.2f; break;
            }
            return Math.Min(r * zoom, r * 6);
        }

        private static Color AllocColor(TreeNode n)
        {
            if (n.Ascendancy) return Color.FromArgb(200, 140, 255);
            if (n.WeaponSet == 1) return Color.FromArgb(255, 110, 90);
            if (n.WeaponSet == 2) return Color.FromArgb(110, 230, 120);
            return Color.FromArgb(240, 200, 90);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_tree == null || _tree.Nodes == null || _tree.Nodes.Count == 0)
            {
                using (var b = new SolidBrush(Color.FromArgb(120, 120, 130)))
                    g.DrawString("No passive tree loaded.", Font, b, 12, 12);
                return;
            }

            g.SmoothingMode = SmoothingMode.None;
            if (_showUnallocated && _tree.Edges != null)
            {
                using (var pen = new Pen(Color.FromArgb(48, 48, 56), 1f))
                {
                    foreach (var edge in _tree.Edges)
                    {
                        TreeNode a, b;
                        if (edge.Count < 2 || !_byId.TryGetValue(edge[0], out a) || !_byId.TryGetValue(edge[1], out b)) continue;
                        if (a.Alloc && b.Alloc) continue;
                        g.DrawLine(pen, ToScreen(a), ToScreen(b));
                    }
                }
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_tree.Edges != null)
            {
                float w = Math.Max(1.5f, Math.Min(4f, 2f * (float)Math.Sqrt(_scale / Math.Max(_fitScale, 1e-6f))));
                foreach (var edge in _tree.Edges)
                {
                    TreeNode a, b;
                    if (edge.Count < 2 || !_byId.TryGetValue(edge[0], out a) || !_byId.TryGetValue(edge[1], out b)) continue;
                    if (!(a.Alloc && b.Alloc)) continue;
                    using (var pen = new Pen(Color.FromArgb(200, AllocColor(a.Ascendancy || a.WeaponSet != 0 ? a : b)), w))
                        g.DrawLine(pen, ToScreen(a), ToScreen(b));
                }
            }

            var rect = ClientRectangle;
            using (var unallocBrush = new SolidBrush(Color.FromArgb(70, 70, 80)))
            using (var outline = new Pen(Color.FromArgb(20, 20, 20), 1f))
            {
                foreach (var n in _tree.Nodes)
                {
                    if (!n.Alloc && !_showUnallocated && n.Type != "ClassStart") continue;
                    var p = ToScreen(n);
                    if (p.X < -20 || p.Y < -20 || p.X > rect.Width + 20 || p.Y > rect.Height + 20) continue;
                    float r = NodeRadius(n);
                    if (n.Alloc)
                    {
                        using (var b = new SolidBrush(AllocColor(n)))
                            g.FillEllipse(b, p.X - r, p.Y - r, r * 2, r * 2);
                        g.DrawEllipse(outline, p.X - r, p.Y - r, r * 2, r * 2);
                    }
                    else
                    {
                        g.FillEllipse(unallocBrush, p.X - r, p.Y - r, r * 2, r * 2);
                    }
                    if (n.Type == "Keystone" || n.Type == "Notable")
                    {
                        using (var ring = new Pen(n.Alloc ? Color.White : Color.FromArgb(95, 95, 105), n.Type == "Keystone" ? 1.6f : 1f))
                            g.DrawEllipse(ring, p.X - r - 1.5f, p.Y - r - 1.5f, r * 2 + 3, r * 2 + 3);
                    }
                }
            }

            if (_hover != null)
            {
                var p = ToScreen(_hover);
                float r = NodeRadius(_hover) + 4;
                using (var pen = new Pen(Color.Cyan, 1.5f))
                    g.DrawEllipse(pen, p.X - r, p.Y - r, r * 2, r * 2);
            }

            // Legend
            int ly = Height - 20;
            int lx = 8;
            Action<string, Color> legend = (text, col) =>
            {
                using (var b = new SolidBrush(col)) g.FillEllipse(b, lx, ly + 3, 9, 9);
                using (var b = new SolidBrush(Color.FromArgb(170, 170, 180))) g.DrawString(text, Font, b, lx + 12, ly);
                lx += 16 + (int)g.MeasureString(text, Font).Width;
            };
            legend("Allocated", Color.FromArgb(240, 200, 90));
            legend("Weapon set 1", Color.FromArgb(255, 110, 90));
            legend("Weapon set 2", Color.FromArgb(110, 230, 120));
            legend("Ascendancy", Color.FromArgb(200, 140, 255));
            using (var b = new SolidBrush(Color.FromArgb(120, 120, 130)))
                g.DrawString("Wheel: zoom  Drag: pan  Double-click: fit", Font, b, 8, 6);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_tree == null) return;
            float factor = e.Delta > 0 ? 1.25f : 0.8f;
            float newScale = Math.Max(_fitScale * 0.5f, Math.Min(_fitScale * 40f, _scale * factor));
            float wx = (e.X - _offset.X) / _scale;
            float wy = (e.Y - _offset.Y) / _scale;
            _scale = newScale;
            _offset = new PointF(e.X - wx * _scale, e.Y - wy * _scale);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStart = e.Location;
                _dragOffsetStart = _offset;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            FitToView();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_tree == null) return;
            if (_dragging)
            {
                _offset = new PointF(_dragOffsetStart.X + e.X - _dragStart.X, _dragOffsetStart.Y + e.Y - _dragStart.Y);
                Invalidate();
                return;
            }

            TreeNode best = null;
            float bestD = float.MaxValue;
            foreach (var n in _tree.Nodes)
            {
                if (!n.Alloc && !_showUnallocated) continue;
                var p = ToScreen(n);
                float dx = p.X - e.X, dy = p.Y - e.Y;
                float d = dx * dx + dy * dy;
                float r = NodeRadius(n) + 4;
                if (d <= r * r && d < bestD) { bestD = d; best = n; }
            }
            if (best != _hover)
            {
                _hover = best;
                _tip.SetToolTip(this, best == null ? null : Describe(best));
                Invalidate();
            }
        }

        public static string Describe(TreeNode n)
        {
            var sb = new StringBuilder();
            sb.Append(string.IsNullOrEmpty(n.Name) ? "(unnamed)" : n.Name);
            sb.Append("  [").Append(n.Type).Append(n.Ascendancy ? ", Ascendancy" : "").Append("]");
            sb.AppendLine();
            sb.AppendLine(n.Alloc ? (n.WeaponSet > 0 ? "Allocated (weapon set " + n.WeaponSet + ")" : "Allocated") : "Not allocated");
            if (n.Stats != null)
                foreach (var s in n.Stats) sb.AppendLine(s);
            return sb.ToString().TrimEnd();
        }
    }
}
