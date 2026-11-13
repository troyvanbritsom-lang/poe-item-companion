using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    // Keep Windows' scrolling, keyboard and accessibility behavior; skin its existing geometry.
    internal sealed class NativeControlSkin : NativeWindow
    {
        private static readonly ConditionalWeakTable<Control, NativeControlSkin> Skins = new ConditionalWeakTable<Control, NativeControlSkin>();
        private readonly Control _control;
        private NativeControlSkin _dropList;
        private bool _painting;
        private bool _frameFallback;
        private readonly Timer _tracking = new Timer { Interval = 30 };
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct ScrollInfo
        {
            public int Size; public Rect Rect; public int Button, ThumbTop, ThumbBottom, Reserved;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public int[] State;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ComboInfo
        {
            public int Size; public Rect Item, Button; public int State; public IntPtr Combo, Edit, List;
        }
        [DllImport("user32.dll")] private static extern bool GetScrollBarInfo(IntPtr hwnd, int id, ref ScrollInfo info);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr hwnd, ref ComboInfo info);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [StructLayout(LayoutKind.Sequential)] private struct TitleInfo
        {
            public int Size; public Rect Title;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public int[] State;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public Rect[] Parts;
        }
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, ref TitleInfo info);

        internal static void SetFrameFallback(Form form, bool enabled)
        {
            var skin = Skins.GetValue(form, c => new NativeControlSkin(c));
            skin._frameFallback = enabled;
        }

        internal static void Attach(Control control)
        {
            Skins.GetValue(control, c => new NativeControlSkin(c));
        }
        private NativeControlSkin(Control control)
        {
            _control = control;
            _tracking.Tick += (s, e) => { PaintChrome(); if (GetAsyncKeyState(1) >= 0) _tracking.Stop(); };
            if (control == null) return;
            control.HandleCreated += (s, e) => Connect();
            control.HandleDestroyed += (s, e) => { _tracking.Stop(); ReleaseHandle(); ReleaseList(); };
            control.Disposed += (s, e) => { _tracking.Dispose(); ReleaseList(); ReleaseHandle(); };
            var combo = control as ComboBox;
            if (combo != null) combo.DropDown += (s, e) => ConnectList();
            if (control.IsHandleCreated) Connect();
        }
        private void Connect() { AssignHandle(_control.Handle); ConnectList(); }
        private void ReleaseList()
        {
            if (_dropList == null) return;
            _dropList._tracking.Dispose(); _dropList.ReleaseHandle(); _dropList = null;
        }
        private void ConnectList()
        {
            if (!(_control is ComboBox) || Handle == IntPtr.Zero) return;
            var info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) };
            if (!GetComboBoxInfo(Handle, ref info) || info.List == IntPtr.Zero) return;
            if (_dropList != null && _dropList.Handle == info.List) return;
            ReleaseList();
            _dropList = new NativeControlSkin(null); _dropList.AssignHandle(info.List);
        }
        protected override void WndProc(ref Message m)
        {
            int message = m.Msg;
            if (message == 0xA1 || message == 0x201) _tracking.Start(); // native thumb tracking runs a nested loop
            base.WndProc(ref m);
            if (message == 0x82) { _tracking.Stop(); return; }
            if (message == 0xF || message == 0x85 || message == 0x114 || message == 0x115 ||
                message == 0x20A || message == 0x20E || message == 5 || message == 0xA0 ||
                message == 0xA1 || message == 0xA2 || message == 0x200 || message == 0x202 ||
                message == 0x100 || message == 0x7 || message == 0x8 || message == 0xA3 || message == 0x86 || message == 0xC)
                PaintChrome();
            // DrawToBitmap / print clients supply their own window DC.
            if (message == 0x317 && m.WParam != IntPtr.Zero)
                using (var g = Graphics.FromHdc(m.WParam)) DrawChrome(g);
        }
        private void PaintChrome()
        {
            if (_painting || Handle == IntPtr.Zero) return;
            _painting = true;
            IntPtr dc = GetWindowDC(Handle);
            try { if (dc != IntPtr.Zero) using (var g = Graphics.FromHdc(dc)) DrawChrome(g); }
            finally { if (dc != IntPtr.Zero) ReleaseDC(Handle, dc); _painting = false; }
        }
        private void DrawChrome(Graphics g)
        {
            Rect window;
            if (!GetWindowRect(Handle, out window)) return;
            if (_frameFallback) DrawFrame(g, window);
            Rectangle vertical = DrawScroll(g, window, -5, true);
            Rectangle horizontal = DrawScroll(g, window, -6, false);
            if (!vertical.IsEmpty && !horizontal.IsEmpty)
                using (var b = new SolidBrush(UiTheme.Surface)) g.FillRectangle(b, vertical.Left, horizontal.Top, vertical.Width, horizontal.Height);
            var combo = _control as ComboBox;
            if (combo != null)
            {
                var info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) };
                if (GetComboBoxInfo(Handle, ref info))
                {
                    // Cover the native light inner frame as well as the outer border.
                    using (var border = new Pen(combo.Focused ? UiTheme.Accent : UiTheme.Border))
                    {
                        g.DrawRectangle(border, 0, 0, combo.Width - 1, combo.Height - 1);
                        g.DrawRectangle(border, 1, 1, Math.Max(1, info.Button.Left - 1), combo.Height - 3);
                    }
                    var button = Rectangle.FromLTRB(info.Button.Left, info.Button.Top, info.Button.Right, info.Button.Bottom);
                    using (var b = new SolidBrush(combo.Enabled ? UiTheme.Input : UiTheme.Surface)) g.FillRectangle(b, button);
                    Arrow(g, button, false, true, combo.Enabled ? UiTheme.Accent : UiTheme.Muted);
                }
            }
            if (combo != null || _control == null)
                using (var p = new Pen(_control != null && _control.Focused ? UiTheme.Accent : UiTheme.Border))
                    g.DrawRectangle(p, 0, 0, window.Right - window.Left - 1, window.Bottom - window.Top - 1);
        }
        private void DrawFrame(Graphics g, Rect window)
        {
            var form = _control as Form;
            if (form == null || form.FormBorderStyle == FormBorderStyle.None) return;
            var info = new TitleInfo { Size = Marshal.SizeOf(typeof(TitleInfo)), State = new int[6], Parts = new Rect[6] };
            SendMessage(Handle, 0x33F, IntPtr.Zero, ref info);
            Point client = form.PointToScreen(Point.Empty);
            int width = window.Right - window.Left, height = window.Bottom - window.Top;
            int left = client.X - window.Left, top = client.Y - window.Top;
            var saved = g.Save();
            g.SetClip(new Rectangle(left, top, form.ClientSize.Width, form.ClientSize.Height), System.Drawing.Drawing2D.CombineMode.Exclude);
            using (var background = new SolidBrush(UiTheme.Canvas)) g.FillRectangle(background, 0, 0, width, height);
            using (var edge = new Pen(UiTheme.Border)) g.DrawRectangle(edge, 0, 0, width - 1, height - 1);
            int textRight = width - left;
            for (int i = 2; i <= 5; i++)
            {
                if (i == 4 || (info.State[i] & 0x8000) != 0) continue;
                Rect part = info.Parts[i];
                var r = Rectangle.FromLTRB(part.Left - window.Left, part.Top - window.Top, part.Right - window.Left, part.Bottom - window.Top);
                if (r.Width <= 0 || r.Height <= 0) continue;
                textRight = Math.Min(textRight, r.Left - 8);
                Point mouse = Control.MousePosition;
                bool hover = r.Contains(mouse.X - window.Left, mouse.Y - window.Top);
                using (var b = new SolidBrush(hover ? (i == 5 ? Color.FromArgb(150, 48, 48) : UiTheme.Input) : UiTheme.Canvas)) g.FillRectangle(b, r);
                int x = r.Left + r.Width / 2, y = r.Top + r.Height / 2;
                using (var pen = new Pen((info.State[i] & 1) != 0 ? UiTheme.Muted : UiTheme.Text))
                {
                    if (i == 2) g.DrawLine(pen, x - 4, y + 3, x + 4, y + 3);
                    if (i == 3) { g.DrawRectangle(pen, x - 4, y - 4, 8, 8); if (form.WindowState == FormWindowState.Maximized) g.DrawLines(pen, new[] { new Point(x - 2, y - 6), new Point(x + 6, y - 6), new Point(x + 6, y + 2) }); }
                    if (i == 5) { g.DrawLine(pen, x - 4, y - 4, x + 4, y + 4); g.DrawLine(pen, x - 4, y + 4, x + 4, y - 4); }
                }
            }
            int textLeft = left + 8;
            if (form.ShowIcon && form.Icon != null)
            {
                g.DrawIcon(form.Icon, new Rectangle(textLeft, Math.Max(2, (top - 16) / 2), 16, 16));
                textLeft += 24;
            }
            TextRenderer.DrawText(g, form.Text, form.Font, new Rectangle(textLeft, 2, Math.Max(0, textRight - textLeft), Math.Max(0, top - 2)), UiTheme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
            g.Restore(saved);
        }

        private Rectangle DrawScroll(Graphics g, Rect window, int id, bool vertical)
        {
            var info = new ScrollInfo { Size = Marshal.SizeOf(typeof(ScrollInfo)), State = new int[6] };
            if (!GetScrollBarInfo(Handle, id, ref info) || (info.State[0] & 0x8000) != 0) return Rectangle.Empty;
            var r = Rectangle.FromLTRB(info.Rect.Left - window.Left, info.Rect.Top - window.Top, info.Rect.Right - window.Left, info.Rect.Bottom - window.Top);
            if (r.Width <= 0 || r.Height <= 0) return Rectangle.Empty;
            using (var b = new SolidBrush(UiTheme.Surface)) g.FillRectangle(b, r);
            var first = vertical ? new Rectangle(r.Left, r.Top, r.Width, info.Button) : new Rectangle(r.Left, r.Top, info.Button, r.Height);
            var last = vertical ? new Rectangle(r.Left, r.Bottom - info.Button, r.Width, info.Button) : new Rectangle(r.Right - info.Button, r.Top, info.Button, r.Height);
            Arrow(g, first, !vertical, false, UiTheme.Muted); Arrow(g, last, !vertical, true, UiTheme.Muted);
            if ((info.State[3] & 0x8001) == 0 && info.ThumbBottom > info.ThumbTop)
            {
                var thumb = vertical ? new Rectangle(r.Left + 4, r.Top + info.ThumbTop, Math.Max(2, r.Width - 8), info.ThumbBottom - info.ThumbTop)
                    : new Rectangle(r.Left + info.ThumbTop, r.Top + 4, info.ThumbBottom - info.ThumbTop, Math.Max(2, r.Height - 8));
                using (var b = new SolidBrush((info.State[3] & 0x8) != 0 ? UiTheme.Accent : UiTheme.Border)) g.FillRectangle(b, thumb);
            }
            return r;
        }
        private static void Arrow(Graphics g, Rectangle r, bool horizontal, bool forward, Color color)
        {
            int x = r.Left + r.Width / 2, y = r.Top + r.Height / 2, d = forward ? 1 : -1;
            using (var p = new Pen(color, 1.5f))
                if (horizontal) g.DrawLines(p, new[] { new Point(x - d * 2, y - 3), new Point(x + d, y), new Point(x - d * 2, y + 3) });
                else g.DrawLines(p, new[] { new Point(x - 3, y - d * 2), new Point(x, y + d), new Point(x + 3, y - d * 2) });
        }
    }

    internal sealed class CompanionMenuColors : ProfessionalColorTable
    {
        public CompanionMenuColors() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return UiTheme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return UiTheme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return UiTheme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return UiTheme.Surface; } }
        public override Color MenuItemSelected { get { return UiTheme.Primary; } }
        public override Color MenuItemBorder { get { return UiTheme.Accent; } }
        public override Color MenuBorder { get { return UiTheme.Border; } }
        public override Color MenuItemSelectedGradientBegin { get { return UiTheme.Primary; } }
        public override Color MenuItemSelectedGradientEnd { get { return UiTheme.Primary; } }
        public override Color CheckBackground { get { return UiTheme.Primary; } }
        public override Color CheckSelectedBackground { get { return UiTheme.Primary; } }
        public override Color CheckPressedBackground { get { return UiTheme.Primary; } }
        public override Color SeparatorDark { get { return UiTheme.Border; } }
        public override Color SeparatorLight { get { return UiTheme.Surface; } }
    }
    internal sealed class CompanionMenuRenderer : ToolStripProfessionalRenderer
    {
        public CompanionMenuRenderer() : base(new CompanionMenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        { e.TextColor = e.Item.Enabled ? UiTheme.Text : UiTheme.Muted; base.OnRenderItemText(e); }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        { e.ArrowColor = e.Item.Enabled ? UiTheme.Accent : UiTheme.Muted; base.OnRenderArrow(e); }
    }
}
