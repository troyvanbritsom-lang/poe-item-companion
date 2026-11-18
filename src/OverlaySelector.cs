using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    // Small picker opened by the single overlay hotkey: toggle an overlay or run a command by number/click.
    // It takes focus only while open (so number keys work) and hands focus back to the game when it closes.
    internal sealed class OverlaySelector : Form
    {
        internal sealed class Entry
        {
            public string Name;
            public string Hint;
            public Func<bool> IsOpen;
            public Action Toggle;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly List<Entry> _entries;
        private readonly IntPtr _previous;
        private readonly GuideBuildView _guide;
        private readonly ComboBox _stage;
        private readonly Label _stageHint;
        private bool _syncStage;
        private bool _closing;

        public string Chosen { get; private set; }

        public OverlaySelector(IList<Entry> entries, string hotkeyText, GuideBuildView guide = null)
        {
            _entries = entries.ToList();
            _previous = GetForegroundWindow();
            _guide = guide;
            if (_guide != null) _guide.EnsureLoaded();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = UiTheme.Border;
            Padding = new Padding(1);
            Text = "Overlays";

            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, ColumnCount = 1, Padding = new Padding(6), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            frame.Controls.Add(new Label { Text = "Overlays", AutoSize = true, ForeColor = UiTheme.Accent, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(4, 2, 4, 6) });
            frame.Controls.Add(new Label { Text = "Guide stage", AutoSize = true, ForeColor = UiTheme.Text, Margin = new Padding(4, 2, 4, 2) });
            _stage = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, DropDownWidth = 420, MaxDropDownItems = 12,
                IntegralHeight = false, DropDownHeight = 240, Margin = new Padding(2), AccessibleName = "Shared Guide stage"
            };
            _stageHint = new Label { Width = 260, Height = 34, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8f), Margin = new Padding(4, 2, 4, 6) };
            _stage.SelectedIndexChanged += (s, e) =>
            {
                if (!_syncStage && _guide != null) _guide.SelectStage(_stage.SelectedItem as string);
            };
            frame.Controls.Add(_stage);
            frame.Controls.Add(_stageHint);
            if (_guide != null) _guide.StageChoicesChanged += SyncStagePicker;
            SyncStagePicker(this, EventArgs.Empty);
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                int index = i;
                bool open = entry.IsOpen != null && entry.IsOpen();
                var button = new Button
                {
                    Text = (i + 1) + "   " + entry.Name + (open ? "   (open - hide)" : ""),
                    TextAlign = ContentAlignment.MiddleLeft, AutoSize = false, Width = 260, Height = 32, Margin = new Padding(2),
                    AccessibleName = entry.Name + (entry.IsOpen == null ? ", press to run" : open ? ", open; press to hide" : ", press to show")
                };
                if (!string.IsNullOrEmpty(entry.Hint)) new ToolTip().SetToolTip(button, entry.Hint);
                button.Click += (s, e) => Choose(index);
                frame.Controls.Add(button);
            }
            frame.Controls.Add(new Label
            {
                Text = "1-" + _entries.Count + " or click; Esc" + (string.IsNullOrEmpty(hotkeyText) ? "" : " or " + hotkeyText) + " closes",
                AutoSize = true, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8f), Margin = new Padding(4, 6, 4, 2)
            });
            Controls.Add(frame);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            UiTheme.Apply(this);
            Deactivate += (s, e) => { if (!_closing) Close(); };
        }

        private void SyncStagePicker(object sender, EventArgs e)
        {
            _syncStage = true;
            _stage.BeginUpdate();
            try
            {
                _stage.Items.Clear();
                if (_guide != null)
                {
                    _stage.Items.AddRange(_guide.StageChoices);
                    _stage.SelectedItem = _guide.SelectedStage;
                }
                _stage.Enabled = _guide != null && _guide.HasGuideStages;
                if (!_stage.Enabled)
                    _stageHint.Text = _stage.Items.Count > 0 ? "This Guide has no selectable stages." : "No Guide stages available. Load a Guide in Build.";
                else
                    _stageHint.Text = _guide.FollowsGameStage ? "Shared with Build and overlays. Follow game is on."
                        : "Manual stage; resume Follow game in Build.";
            }
            finally { _stage.EndUpdate(); _syncStage = false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _guide != null) _guide.StageChoicesChanged -= SyncStagePicker;
            base.Dispose(disposing);
        }

        // Runs entry 'index' and closes. Returns false for an out-of-range index.
        internal bool Choose(int index)
        {
            if (index < 0 || index >= _entries.Count) return false;
            Chosen = _entries[index].Name;
            CloseAndRestore();
            _entries[index].Toggle();
            return true;
        }

        internal static int IndexForKey(Keys key)
        {
            if (key >= Keys.D1 && key <= Keys.D9) return key - Keys.D1;
            if (key >= Keys.NumPad1 && key <= Keys.NumPad9) return key - Keys.NumPad1;
            return -1;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            // Let the open stage list handle typing and Esc before overlay shortcuts resume.
            if (_stage.DroppedDown) return;
            if (e.KeyCode == Keys.Escape) { CloseAndRestore(); e.Handled = true; return; }
            int index = IndexForKey(e.KeyCode);
            if (index >= 0 && Choose(index)) e.Handled = true;
        }

        public void CloseAndRestore()
        {
            if (_closing) return;
            _closing = true;
            IntPtr own = IsHandleCreated ? Handle : IntPtr.Zero;
            Close();
            if (_previous != IntPtr.Zero && _previous != own) SetForegroundWindow(_previous);
        }

        public void ShowNear(Point cursor)
        {
            var area = Screen.FromPoint(cursor).WorkingArea;
            Show();
            var size = Size;
            int x = Math.Max(area.Left, Math.Min(cursor.X - size.Width / 2, area.Right - size.Width));
            int y = Math.Max(area.Top, Math.Min(cursor.Y - 20, area.Bottom - size.Height));
            Location = new Point(x, y);
            Activate();
            if (_entries.Count > 0) Controls[0].Controls.OfType<Button>().First().Focus();
        }
    }
}
