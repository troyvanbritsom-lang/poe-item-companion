using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    internal static class UiTheme
    {
        public static readonly Color Canvas = Color.FromArgb(23, 26, 31);
        public static readonly Color Surface = Color.FromArgb(31, 35, 42);
        public static readonly Color Input = Color.FromArgb(40, 46, 55);
        public static readonly Color Border = Color.FromArgb(76, 85, 99);
        public static readonly Color Text = Color.FromArgb(232, 235, 240);
        public static readonly Color Muted = Color.FromArgb(172, 181, 195);
        public static readonly Color Accent = Color.FromArgb(226, 196, 133);
        public static readonly Color Primary = Color.FromArgb(48, 83, 111);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        public static void EnableDpiAwareness()
        {
            if (!SetProcessDPIAware()) Logger.Info("Windows retained the process's existing DPI awareness context.");
        }

        public static void Apply(Control root)
        {
            root.SuspendLayout();
            var form = root as Form;
            if (form != null)
            {
                form.AutoScaleDimensions = new SizeF(96, 96);
                form.AutoScaleMode = AutoScaleMode.Dpi;
                form.Font = new Font("Segoe UI", 9f);
            }
            if (Neutral(root.BackColor)) root.BackColor = root.BackColor.R < 28 ? Canvas : Surface;
            if (Neutral(root.ForeColor)) root.ForeColor = root.ForeColor.R < 195 ? Muted : Text;

            var button = root as Button;
            if (button != null)
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = Input;
                button.ForeColor = Text;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 65, 79);
                button.FlatAppearance.MouseDownBackColor = Primary;
                button.Cursor = Cursors.Hand;
                button.Paint -= DrawDisabledButton;
                button.Paint += DrawDisabledButton;
            }
            var text = root as TextBoxBase;
            if (text != null)
            {
                text.BackColor = text.ReadOnly ? Canvas : Input;
                text.ForeColor = Text;
                text.BorderStyle = text is RichTextBox ? BorderStyle.None : BorderStyle.FixedSingle;
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
                tabs.Padding = new Point(14, 6);
                tabs.DrawItem -= DrawTab;
                tabs.DrawItem += DrawTab;
            }
            foreach (Control child in root.Controls) Apply(child);
            root.ResumeLayout(true);
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
            button.FlatAppearance.BorderColor = Color.FromArgb(104, 146, 176);
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
                e.Graphics.DrawRectangle(pen, Rectangle.Inflate(DisplayRectangle, 1, 1));
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }
    }
}
