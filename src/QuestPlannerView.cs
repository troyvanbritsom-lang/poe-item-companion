using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    public sealed class QuestPlannerView : UserControl
    {
        private AppConfig _config;
        private QuestPlan _plan;
        private readonly DataGridView _grid;
        private readonly ComboBox _chapter;
        private readonly CheckBox _showExcluded;
        private readonly CheckBox _followStory;
        private readonly Label _note;
        private readonly Button _openOverlay;
        private QuestOverlay _overlay;
        private bool _updating;
        private readonly bool _compact;

        public QuestPlannerView(bool compact = false)
        {
            _compact = compact;
            Dock = DockStyle.Fill;
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            _chapter = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Quest act" };
            _showExcluded = new CheckBox { Text = "Show deselected quests", AutoSize = true, AccessibleName = "Show deselected quests" };
            _followStory = new CheckBox { Text = "Follow story act", AutoSize = true, Visible = compact, AccessibleName = "Follow story act" };
            _openOverlay = new Button { Text = "Open quest overlay", AutoSize = true, Visible = !compact };
            toolbar.Controls.AddRange(new Control[] { _chapter, _showExcluded, _followStory, _openOverlay });
            _note = new Label { Dock = DockStyle.Bottom, Height = compact ? 96 : 92, Padding = new Padding(6), ForeColor = UiTheme.Muted };
            _grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, BackgroundColor = UiTheme.Canvas,
                BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false, AccessibleName = "Campaign quest route" };
            _grid.DefaultCellStyle.BackColor = UiTheme.Surface;
            _grid.DefaultCellStyle.ForeColor = UiTheme.Text;
            _grid.DefaultCellStyle.SelectionBackColor = UiTheme.Input;
            _grid.DefaultCellStyle.SelectionForeColor = UiTheme.Text;
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.Input;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.Text;
            _grid.GridColor = UiTheme.Border;
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Included", HeaderText = "Do", Width = 36 });
            _grid.Columns.Add("Chapter", "Act");
            _grid.Columns[1].Width = compact ? 65 : 85;
            _grid.Columns[1].Visible = !compact;
            _grid.Columns.Add("Quest", "Quest / requirement");
            _grid.Columns[2].Width = compact ? 190 : 300;
            _grid.Columns.Add("Reward", "Completion rewards / choices");
            _grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns[3].MinimumWidth = 180;
            foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.CellContentClick += (s, e) => { if (e.ColumnIndex == 0) ToggleRow(e.RowIndex); };
            _grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Space || _grid.CurrentRow == null) return;
                ToggleRow(_grid.CurrentRow.Index);
                e.Handled = e.SuppressKeyPress = true;
            };
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex == 0) return;
                var quest = (CampaignQuest)_grid.Rows[e.RowIndex].Tag;
                try { Process.Start(quest.SourceUrl); }
                catch (Exception ex) { Logger.Error("Cannot open quest source.", ex); MessageBox.Show(this, ex.Message, "Quest source failed"); }
            };
            _chapter.SelectedIndexChanged += (s, e) =>
            {
                if (_updating) return;
                if (_compact && _followStory.Checked) _followStory.Checked = false;
                RefreshRows();
            };
            _followStory.CheckedChanged += (s, e) =>
            {
                if (_updating || !_compact || _config == null) return;
                bool before = _config.QuestOverlayFollowStory;
                _config.QuestOverlayFollowStory = _followStory.Checked;
                try { _config.Save(); }
                catch
                {
                    _config.QuestOverlayFollowStory = before;
                    _updating = true;
                    _followStory.Checked = before;
                    _updating = false;
                    throw;
                }
                OnRouteChanged(this, EventArgs.Empty);
            };
            _showExcluded.CheckedChanged += (s, e) =>
            {
                if (_updating || _config == null) return;
                if (_compact)
                {
                    bool before = _config.QuestOverlayShowExcluded;
                    _config.QuestOverlayShowExcluded = _showExcluded.Checked;
                    try { _config.Save(); }
                    catch
                    {
                        _config.QuestOverlayShowExcluded = before;
                        _updating = true;
                        _showExcluded.Checked = before;
                        _updating = false;
                        throw;
                    }
                }
                RefreshRows();
            };
            _openOverlay.Click += (s, e) => ShowOverlay();
            Controls.Add(_grid);
            Controls.Add(toolbar);
            Controls.Add(_note);
            UiTheme.Apply(this);
        }

        public void SetSource(AppConfig config) { SetSource(config, null); }

        public void UpdateGameProgress(GameLogState state, string error)
        {
            if (_plan != null) _plan.UpdateProgress(state, error);
        }

        private void ShowOverlay()
        {
            if (_plan == null)
            {
                MessageBox.Show(this, _note.Text, "Quest overlay unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (_overlay == null || _overlay.IsDisposed) _overlay = new QuestOverlay(_config, _plan);
            _overlay.Show();
        }

        public void ToggleOverlay()
        {
            if (_overlay != null && !_overlay.IsDisposed && _overlay.Visible) _overlay.Hide();
            else ShowOverlay();
        }

        internal void SetSource(AppConfig config, QuestPlan shared)
        {
            if (ReferenceEquals(_config, config) && _plan != null && (shared == null || ReferenceEquals(_plan, shared))) return;
            if (_overlay != null) { _overlay.Dispose(); _overlay = null; }
            if (_plan != null) _plan.Changed -= OnRouteChanged;
            _config = config;
            _updating = true;
            try
            {
                _plan = shared ?? new QuestPlan(config);
                _plan.Changed += OnRouteChanged;
                _chapter.Items.Clear();
                _chapter.Items.Add("All acts");
                foreach (string chapter in _plan.Quests.Select(q => q.Chapter).Distinct()) _chapter.Items.Add(chapter);
                if (_compact) _chapter.Items.Add(GameLog.PhaseText(GamePhase.Endgame));
                _chapter.SelectedIndex = 0;
                _showExcluded.Checked = _compact ? config.QuestOverlayShowExcluded : true;
                _followStory.Checked = _compact && config.QuestOverlayFollowStory;
                _openOverlay.Enabled = true;
            }
            catch (Exception ex)
            {
                _plan = null;
                _grid.Rows.Clear();
                _note.Text = "Quest overview unavailable: " + ex.Message;
                _openOverlay.Enabled = false;
            }
            finally { _updating = false; }
            OnRouteChanged(this, EventArgs.Empty);
        }

        private void OnRouteChanged(object sender, EventArgs e)
        {
            if (_plan != null && _compact && _followStory.Checked && _plan.StoryChapter != null && _chapter.Items.Contains(_plan.StoryChapter))
            {
                _updating = true;
                _chapter.SelectedItem = _plan.StoryChapter;
                _updating = false;
            }
            RefreshRows();
        }

        private void ToggleRow(int index)
        {
            if (_plan == null || index < 0) return;
            var quest = (CampaignQuest)_grid.Rows[index].Tag;
            if (!quest.Optional) return;
            _plan.SetIncluded(quest, !_plan.IsIncluded(quest));
        }

        private void RefreshRows()
        {
            if (_updating || _plan == null) return;
            string selected = _grid.CurrentRow == null ? null : ((CampaignQuest)_grid.CurrentRow.Tag).Id;
            int scroll = _grid.FirstDisplayedScrollingRowIndex;
            _grid.Rows.Clear();
            string chapter = _chapter.SelectedIndex <= 0 ? null : (string)_chapter.SelectedItem;
            foreach (var quest in _plan.Visible(_showExcluded.Checked, chapter))
            {
                bool included = _plan.IsIncluded(quest);
                int index = _grid.Rows.Add(included, quest.Chapter,
                    (_compact && chapter == null ? quest.Chapter + " - " : "") + quest.Name
                    + (quest.Optional ? " [optional]" : " [locked]") + Environment.NewLine + quest.Requirement, quest.Reward);
                var row = _grid.Rows[index];
                row.Tag = quest;
                row.DefaultCellStyle.ForeColor = included ? UiTheme.Text : UiTheme.Muted;
                row.Cells[0].ToolTipText = quest.Optional ? "Click or press Space to include/exclude. This does not mark completion."
                    : "Main-story / potentially required: always included.";
                if (!quest.Optional) row.Cells[0].Style.BackColor = UiTheme.Canvas;
                row.Cells[2].ToolTipText = quest.SourceUrl + Environment.NewLine + "Double-click for source facts.";
                row.Cells[3].ToolTipText = quest.Reward;
                if (quest.Id == selected) _grid.CurrentCell = row.Cells[0];
            }
            if (scroll >= 0 && _grid.Rows.Count > 0) _grid.FirstDisplayedScrollingRowIndex = Math.Min(scroll, _grid.Rows.Count - 1);
            _note.Text = (_compact ? (_followStory.Checked ? "" : "Manual act selection. ") + _plan.StoryStatus
                + Environment.NewLine + "Main story locked. Uncheck optional quests to skip; show deselected to restore. Drag title to move; edges/corners to resize. No completion detection."
                : QuestCatalog.Coverage + Environment.NewLine + QuestCatalog.Notice + " Double-click a row for its source. Uncheck optional quests to skip; locked quests stay included.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_plan != null) _plan.Changed -= OnRouteChanged;
                if (_overlay != null) _overlay.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public sealed class QuestOverlay : Form
    {
        private readonly AppConfig _config;
        private Point _dragStart;

        internal QuestOverlay(AppConfig config, QuestPlan plan)
        {
            _config = config;
            var point = new Point(config.QuestOverlayX, config.QuestOverlayY);
            var area = Screen.AllScreens.FirstOrDefault(screen => screen.WorkingArea.Contains(point));
            Rectangle bounds = (area ?? Screen.PrimaryScreen).WorkingArea;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(Math.Min(460, bounds.Width), Math.Min(300, bounds.Height));
            Size = new Size(Math.Max(MinimumSize.Width, Math.Min(config.QuestOverlayWidth, bounds.Width)),
                Math.Max(MinimumSize.Height, Math.Min(config.QuestOverlayHeight, bounds.Height)));
            BackColor = UiTheme.Surface;
            Padding = new Padding(8, 8, 8, 18);
            SetStyle(ControlStyles.ResizeRedraw, true);
            var title = new Label { Text = config.GameName + " quests - drag to move", Dock = DockStyle.Top, Height = 30,
                ForeColor = UiTheme.Accent, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            var close = new Button { Text = "x", Dock = DockStyle.Right, Width = 32, AccessibleName = "Hide quest overlay" };
            close.Click += (s, e) => Hide();
            title.Controls.Add(close);
            title.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) _dragStart = e.Location; };
            title.MouseMove += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
            };
            title.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) SaveBounds(); };
            var view = new QuestPlannerView(true);
            view.SetSource(config, plan);
            Controls.Add(view);
            Controls.Add(title);
            var grip = new Panel { Name = "Quest overlay resize grip", AccessibleName = "Drag to resize quest overlay",
                Size = new Size(16, 16), Location = new Point(ClientSize.Width - 18, ClientSize.Height - 18),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Cursor = Cursors.SizeNWSE };
            grip.Paint += (s, e) => ControlPaint.DrawSizeGrip(e.Graphics, UiTheme.Muted, grip.ClientRectangle);
            bool resizing = false;
            Point resizeStart = Point.Empty;
            Size resizeSize = Size.Empty;
            grip.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                resizing = true;
                resizeStart = grip.PointToScreen(e.Location);
                resizeSize = Size;
                grip.Capture = true;
            };
            grip.MouseMove += (s, e) =>
            {
                if (!resizing || e.Button != MouseButtons.Left) return;
                Point cursor = grip.PointToScreen(e.Location);
                Size = new Size(Math.Max(MinimumSize.Width, resizeSize.Width + cursor.X - resizeStart.X),
                    Math.Max(MinimumSize.Height, resizeSize.Height + cursor.Y - resizeStart.Y));
            };
            grip.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || !resizing) return;
                resizing = false;
                grip.Capture = false;
                SaveBounds();
            };
            grip.MouseCaptureChanged += (s, e) =>
            {
                if (!resizing || grip.Capture) return;
                resizing = false;
                SaveBounds();
            };
            Controls.Add(grip);
            grip.BringToFront();
            UiTheme.Apply(this);
            Size = new Size(Math.Min(Width, bounds.Width), Math.Min(Height, bounds.Height));
            Location = new Point(Math.Max(bounds.Left, Math.Min(point.X < bounds.Left ? bounds.Left + 20 : point.X, bounds.Right - Width)),
                Math.Max(bounds.Top, Math.Min(point.Y < bounds.Top ? bounds.Top + 60 : point.Y, bounds.Bottom - Height)));
        }

        private void SaveBounds()
        {
            _config.QuestOverlayX = Left;
            _config.QuestOverlayY = Top;
            _config.QuestOverlayWidth = Width;
            _config.QuestOverlayHeight = Height;
            _config.Save();
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            SaveBounds();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x83) // WM_NCCALCSIZE: use the whole window for the custom, captionless frame.
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
            if (m.Msg != 0x84) return; // WM_NCHITTEST: keep the panel non-activating while exposing native resize edges.
            long coordinates = m.LParam.ToInt64();
            var point = PointToClient(new Point(unchecked((short)coordinates), unchecked((short)(coordinates >> 16))));
            bool left = point.X < Padding.Left, right = point.X >= ClientSize.Width - Padding.Right;
            bool top = point.Y < Padding.Top, bottom = point.Y >= ClientSize.Height - 8;
            if (left) m.Result = new IntPtr(top ? 13 : bottom ? 16 : 10);
            else if (right) m.Result = new IntPtr(top ? 14 : bottom ? 17 : 11);
            else if (top) m.Result = new IntPtr(12);
            else if (bottom) m.Result = new IntPtr(15);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00040000; // WS_THICKFRAME enables native sizing without a standard title bar.
                cp.ExStyle |= 0x8 | 0x80 | 0x08000000;
                return cp;
            }
        }
    }
}
