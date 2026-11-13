using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    internal static class UiTheme
    {
        public static readonly Color Canvas = Color.FromArgb(15, 19, 23);
        public static readonly Color Surface = Color.FromArgb(23, 29, 34);
        public static readonly Color Input = Color.FromArgb(30, 38, 44);
        public static readonly Color Border = Color.FromArgb(61, 70, 74);
        public static readonly Color Text = Color.FromArgb(237, 234, 225);
        public static readonly Color Muted = Color.FromArgb(170, 181, 184);
        public static readonly Color Accent = Color.FromArgb(223, 190, 126);
        public static readonly Color Primary = Color.FromArgb(85, 67, 37);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private static void ApplyWindowFrame(object sender, EventArgs e)
        {
            var form = (Form)sender;
            if (!form.IsHandleCreated || form.FormBorderStyle == FormBorderStyle.None) return;
            int dark = 1;
            DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
            int caption = ColorTranslator.ToWin32(Canvas);
            int text = ColorTranslator.ToWin32(Text);
            int border = ColorTranslator.ToWin32(Border);
            // Windows 11 caption, text and border colors. Older systems ignore unsupported attributes.
            int captionResult = DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            NativeControlSkin.SetFrameFallback(form, captionResult != 0);
            DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
        }

        public static void EnableDpiAwareness()
        {
            if (!SetProcessDPIAware()) Logger.Info("Windows retained the process's existing DPI awareness context.");
        }

        public static void Apply(Control root)
        {
            root.SuspendLayout();
            if (root is ScrollableControl || root is TextBoxBase || root is ListBox || root is ListView || root is ComboBox)
                NativeControlSkin.Attach(root);
            if (root.ContextMenuStrip != null) ApplyMenu(root.ContextMenuStrip);
            var form = root as Form;
            if (form != null)
            {
                form.AutoScaleDimensions = new SizeF(96, 96);
                form.AutoScaleMode = AutoScaleMode.Dpi;
                form.Font = new Font("Segoe UI", 9f);
                form.HandleCreated -= ApplyWindowFrame;
                form.HandleCreated += ApplyWindowFrame;
                ApplyWindowFrame(form, EventArgs.Empty);
            }
            if (root.BackColor != Canvas && root.BackColor != Surface && root.BackColor != Input && Neutral(root.BackColor))
                root.BackColor = root.BackColor.R < 28 ? Canvas : Surface;
            if (Neutral(root.ForeColor)) root.ForeColor = root.ForeColor.R < 195 ? Muted : Text;

            var button = root as Button;
            if (button != null)
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = Input;
                button.ForeColor = Text;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(44, 55, 61);
                button.FlatAppearance.MouseDownBackColor = Primary;
                button.Cursor = Cursors.Hand;
                button.Paint -= DrawDisabledButton;
                button.Paint += DrawDisabledButton;
                if (object.Equals(button.Tag, "primary")) Emphasize(button);
            }
            var text = root as TextBoxBase;
            if (text != null)
            {
                text.BackColor = text.ReadOnly ? Canvas : Input;
                text.ForeColor = Text;
                text.BorderStyle = text is RichTextBox ? BorderStyle.None : BorderStyle.FixedSingle;
                if (text.ContextMenuStrip == null) InstallTextMenu(text);
            }
            var combo = root as ComboBox;
            if (combo != null)
            {
                combo.BackColor = Input;
                combo.ForeColor = Text;
                combo.FlatStyle = FlatStyle.Flat;
                if (combo.DropDownStyle == ComboBoxStyle.DropDownList)
                {
                    combo.DrawMode = DrawMode.OwnerDrawFixed;
                    combo.ItemHeight = Math.Max(20, combo.Font.Height + 4);
                    combo.DrawItem -= DrawCombo;
                    combo.DrawItem += DrawCombo;
                }
            }
            var number = root as NumericUpDown;
            if (number != null) { number.BackColor = Input; number.ForeColor = Text; }
            var list = root as ListBox;
            if (list != null) { list.BackColor = Canvas; list.ForeColor = Text; list.IntegralHeight = false; }
            var details = root as ListView;
            if (details != null)
            {
                details.BackColor = Canvas;
                details.ForeColor = Text;
                details.GridLines = false;
                details.HideSelection = false;
                details.BorderStyle = BorderStyle.None;
                details.OwnerDraw = true;
                details.DrawColumnHeader -= DrawColumnHeader;
                details.DrawColumnHeader += DrawColumnHeader;
                details.DrawItem -= DrawListItem;
                details.DrawItem += DrawListItem;
                details.DrawSubItem -= DrawListSubItem;
                details.DrawSubItem += DrawListSubItem;
            }
            var tabs = root as TabControl;
            if (tabs != null)
            {
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.Padding = new Point(20, 9);
                tabs.DrawItem -= DrawTab;
                tabs.DrawItem += DrawTab;
            }
            foreach (Control child in root.Controls) Apply(child);
            root.ResumeLayout(true);
        }

        public static void ApplyMenu(ToolStrip menu)
        {
            menu.Renderer = new CompanionMenuRenderer();
            menu.BackColor = Surface;
            menu.ForeColor = Text;
            foreach (ToolStripItem item in menu.Items)
            {
                item.ForeColor = Text;
                var nested = item as ToolStripDropDownItem;
                if (nested != null && nested.HasDropDownItems) ApplyMenu(nested.DropDown);
            }
        }

        private static void InstallTextMenu(TextBoxBase box)
        {
            var menu = new ContextMenuStrip();
            var undo = menu.Items.Add("Undo", null, (s, e) => box.Undo());
            menu.Items.Add(new ToolStripSeparator());
            var cut = menu.Items.Add("Cut", null, (s, e) => box.Cut());
            var copy = menu.Items.Add("Copy", null, (s, e) => box.Copy());
            var paste = menu.Items.Add("Paste", null, (s, e) => box.Paste());
            menu.Items.Add(new ToolStripSeparator());
            var select = menu.Items.Add("Select all", null, (s, e) => box.SelectAll());
            menu.Opening += (s, e) =>
            {
                undo.Enabled = !box.ReadOnly && box.CanUndo;
                cut.Enabled = !box.ReadOnly && box.SelectionLength > 0;
                copy.Enabled = box.SelectionLength > 0;
                paste.Enabled = !box.ReadOnly;
                select.Enabled = box.TextLength > 0;
            };
            ApplyMenu(menu);
            box.ContextMenuStrip = menu;
            box.Disposed += (s, e) => menu.Dispose();
        }

        private static bool Neutral(Color color)
        {
            return color.A == 255 && Math.Abs(color.R - color.G) <= 12 && Math.Abs(color.G - color.B) <= 12;
        }

        internal static void DrawTab(object sender, DrawItemEventArgs e)
        {
            var tabs = (TabControl)sender;
            bool selected = tabs.SelectedIndex == e.Index;
            using (var brush = new SolidBrush(selected ? Surface : Canvas))
                e.Graphics.FillRectangle(brush, e.Bounds);
            if (selected)
                using (var pen = new Pen(Accent, 2))
                    e.Graphics.DrawLine(pen, e.Bounds.Left + 4, e.Bounds.Bottom - 2, e.Bounds.Right - 4, e.Bounds.Bottom - 2);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds,
                selected ? Text : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -4, -4));
        }

        private static void DrawCombo(object sender, DrawItemEventArgs e)
        {
            var combo = (ComboBox)sender;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var brush = new SolidBrush(selected ? Primary : combo.Enabled ? Input : Surface))
                e.Graphics.FillRectangle(brush, e.Bounds);
            string text = e.Index >= 0 ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
            TextRenderer.DrawText(e.Graphics, text, combo.Font, Rectangle.Inflate(e.Bounds, -3, 0),
                combo.Enabled ? Text : Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        private static void DrawDisabledButton(object sender, PaintEventArgs e)
        {
            var button = (Button)sender;
            if (button.Enabled) return;
            using (var brush = new SolidBrush(Surface)) e.Graphics.FillRectangle(brush, button.ClientRectangle);
            using (var pen = new Pen(Border)) e.Graphics.DrawRectangle(pen, 0, 0, button.Width - 1, button.Height - 1);
            TextRenderer.DrawText(e.Graphics, button.Text, button.Font, button.ClientRectangle, Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.HidePrefix);
        }

        private static void DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var brush = new SolidBrush(Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, ((ListView)sender).Font, Rectangle.Inflate(e.Bounds, -6, 0), Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static void DrawListItem(object sender, DrawListViewItemEventArgs e) { e.DrawDefault = true; }
        private static void DrawListSubItem(object sender, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; }

        public static void Emphasize(Button button)
        {
            button.BackColor = Primary;
            button.FlatAppearance.BorderColor = Accent;
            button.ForeColor = Accent;
        }

        public static FlowLayoutPanel Actions(params Control[] controls)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Dock = DockStyle.Fill };
            foreach (Control control in controls)
            {
                control.Margin = new Padding(4, 3, 0, 3);
                panel.Controls.Add(control);
            }
            return panel;
        }
    }

    internal sealed class WorkbenchHeader : Panel
    {
        public WorkbenchHeader()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var gradient = new LinearGradientBrush(ClientRectangle, Color.FromArgb(36, 44, 48), UiTheme.Canvas, 0f))
                g.FillRectangle(gradient, ClientRectangle);
            float scale = DeviceDpiScale();
            g.ScaleTransform(scale, scale);
            using (var line = new Pen(Color.FromArgb(94, 80, 55), 1))
            using (var gold = new Pen(UiTheme.Accent, 1.5f))
            {
                g.DrawEllipse(line, 21, 19, 60, 60);
                g.DrawEllipse(line, 28, 26, 46, 46);
                g.DrawPolygon(gold, new[] { new Point(51, 16), new Point(72, 49), new Point(51, 82), new Point(30, 49) });
                g.DrawLine(gold, 40, 49, 62, 49);
                g.DrawLine(gold, 51, 33, 51, 65);
            }
            using (var title = new Font("Georgia", 21f))
            using (var small = new Font("Segoe UI", 8f, FontStyle.Bold))
            {
                using (var ink = new SolidBrush(UiTheme.Text)) g.DrawString("EXILE'S COMPANION", title, ink, 96, 22);
                using (var ink = new SolidBrush(UiTheme.Accent)) g.DrawString("THEORYCRAFT  /  COMPARE  /  CONQUER", small, ink, 100, 61);
            }
            g.ResetTransform();
            using (var edge = new Pen(UiTheme.Accent)) g.DrawLine(edge, 0, Height - 1, Width, Height - 1);
        }

        private float DeviceDpiScale() { using (var g = CreateGraphics()) return g.DpiX / 96f; }
    }

    internal sealed class DarkTabControl : TabControl
    {
        public DarkTabControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(UiTheme.Canvas);
            for (int i = 0; i < TabCount; i++)
                UiTheme.DrawTab(this, new DrawItemEventArgs(e.Graphics, Font, GetTabRect(i), i,
                    Focused && SelectedIndex == i ? DrawItemState.Focus : DrawItemState.None));
            using (var pen = new Pen(UiTheme.Border))
                e.Graphics.DrawLine(pen, DisplayRectangle.Left, DisplayRectangle.Top - 1, DisplayRectangle.Right, DisplayRectangle.Top - 1);
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }
    }
}
