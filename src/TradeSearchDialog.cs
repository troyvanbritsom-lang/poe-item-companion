using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    // Lets the user choose which constraints go into the official trade search before the browser opens.
    // Nothing is sent anywhere until "Open in browser" (or the optional "Load leagues" button) is pressed.
    internal sealed class TradeSearchDialog : Form
    {
        private readonly AppConfig _config;
        private readonly TradeSearchPlan _plan;
        private readonly ComboBox _league;
        private readonly CheckBox _name, _rarity, _ilvl, _sockets, _links;
        private readonly NumericUpDown _threshold;
        private readonly DataGridView _grid;
        private readonly string _originalName, _originalRarity;

        public TradeSearchPlan Plan { get { return _plan; } }
        public string LastUrl { get; private set; }

        public TradeSearchDialog(AppConfig config, GearEntry gear)
        {
            _config = config;
            string indexError;
            var index = TradeStatIndex.Load(config, out indexError);
            _plan = TradeSearch.Plan(gear, config.Game, index, indexError);
            _originalName = _plan.Name;
            _originalRarity = _plan.Rarity;

            Text = "Search on Trade - " + _plan.DisplayName;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            Size = new Size(860, 640);
            MinimumSize = new Size(760, 600);
            BackColor = UiTheme.Surface;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
            root.Layout += (s, e) => { foreach (Control c in root.Controls) if (c.MaximumSize.Width > 0) c.MaximumSize = new Size(Math.Max(200, root.ClientSize.Width - root.Padding.Horizontal - 6), 0); };
            Controls.Add(root);

            root.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = PobStatParser.GetRarityColor(gear.Rarity), Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Text = _plan.DisplayName + (_plan.BaseType != _plan.DisplayName ? "  (" + _plan.BaseType + ")" : "")
            });
            root.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = UiTheme.Muted,
                Text = config.GameName + " official trade site, guide slot " + _plan.Slot + ". Choose the constraints; nothing is bought or messaged. " +
                    "Minimums start at the threshold below of the guide's rolls - not an exact match."
            });

            var leagueRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 4) };
            leagueRow.Controls.Add(new Label { Text = "League:", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            _league = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDown };
            foreach (var l in new[] { config.TradeLeague, "Standard", "Hardcore" })
                if (!string.IsNullOrWhiteSpace(l) && !_league.Items.Contains(l)) _league.Items.Add(l);
            _league.Text = config.TradeLeague ?? "";
            leagueRow.Controls.Add(_league);
            var load = new Button { Text = "Load leagues", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
            new ToolTip().SetToolTip(load, "Downloads the public league list from " + TradeSearch.LeaguesEndpoint(config.Game) + " (no account data is sent).");
            load.Click += (s, e) => LoadLeagues();
            leagueRow.Controls.Add(load);
            root.Controls.Add(leagueRow);

            var options = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(820, 0), Margin = new Padding(0, 0, 0, 4) };
            _name = Option(options, "Unique name: " + _plan.Name, !string.IsNullOrEmpty(_plan.Name), !string.IsNullOrEmpty(_plan.Name));
            _rarity = Option(options, "Rarity: " + (_plan.Rarity ?? "any"), _plan.Rarity != null, _plan.Rarity != null && string.IsNullOrEmpty(_plan.Name));
            if (!string.IsNullOrEmpty(_plan.Name)) _rarity.Checked = true;
            _ilvl = Option(options, _plan.ItemLevel.HasValue ? "Item level >= " + _plan.ItemLevel : "Item level (unknown)", _plan.ItemLevel.HasValue, false);
            new ToolTip().SetToolTip(_ilvl, "Item level (affix tier pool), not the level requirement to wear it.");
            bool poe1 = config.Game == GameId.Poe1;
            _sockets = Option(options, (poe1 ? "Sockets >= " : "Rune sockets >= ") + _plan.Sockets, _plan.Sockets > 0, false);
            _links = Option(options, "Links >= " + _plan.Links, poe1 && _plan.Links > 1, false);
            if (!poe1) _links.Visible = false;
            options.Controls.Add(new Label { Text = "Base type: " + _plan.BaseType + " (always)", AutoSize = true, ForeColor = UiTheme.Muted, Margin = new Padding(8, 4, 0, 0) });
            root.Controls.Add(options);

            var thresholdRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            thresholdRow.Controls.Add(new Label { Text = "Minimum = ", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            _threshold = new NumericUpDown { Minimum = 1, Maximum = 100, Value = _plan.ThresholdPercent, Width = 60 };
            thresholdRow.Controls.Add(_threshold);
            thresholdRow.Controls.Add(new Label { Text = "% of the guide value", AutoSize = true, Margin = new Padding(4, 6, 8, 0) });
            var apply = new Button { Text = "Recalculate minimums", AutoSize = true };
            apply.Click += (s, e) => { ReadGrid(); TradeSearch.ApplyThreshold(_plan, (int)_threshold.Value); FillGrid(); };
            thresholdRow.Controls.Add(apply);
            root.Controls.Add(thresholdRow);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect,
                BackgroundColor = UiTheme.Canvas, BorderStyle = BorderStyle.None, GridColor = UiTheme.Border, EnableHeadersVisualStyles = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
            };
            _grid.DefaultCellStyle.BackColor = UiTheme.Canvas;
            _grid.DefaultCellStyle.ForeColor = UiTheme.Text;
            _grid.DefaultCellStyle.SelectionBackColor = UiTheme.Input;
            _grid.DefaultCellStyle.SelectionForeColor = UiTheme.Text;
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.Surface;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.Text;
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "use", HeaderText = "Use", FillWeight = 8 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "Kind", ReadOnly = true, FillWeight = 12 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "mod", HeaderText = "Guide mod", ReadOnly = true, FillWeight = 34 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "stat", HeaderText = "Official trade stat", ReadOnly = true, FillWeight = 30 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "min", HeaderText = "Min", FillWeight = 9 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "max", HeaderText = "Max", FillWeight = 9 });
            root.Controls.Add(_grid);
            root.RowStyles.Clear();
            for (int i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));

            var issues = new List<string>();
            issues.Add(_plan.StatSource);
            foreach (var u in _plan.Unmatched) issues.Add("NOT SEARCHED: " + u.Text + " - " + u.Reason);
            issues.AddRange(_plan.Notes);
            var issueBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = string.Join(Environment.NewLine, issues.ToArray()) };
            root.Controls.Add(new Label { Text = _plan.Unmatched.Count == 0 ? "Notes" : _plan.Unmatched.Count + " guide mod(s) could not be mapped to an official trade stat:", AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(issueBox);
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            var open = new Button { Text = "Open in browser", AutoSize = true, Tag = "primary" };
            open.Click += (s, e) => OpenSearch();
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(open);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            AcceptButton = open;
            CancelButton = cancel;

            FillGrid();
            UiTheme.Apply(this);
        }

        private static CheckBox Option(FlowLayoutPanel panel, string text, bool enabled, bool chosen)
        {
            var box = new CheckBox { Text = text, AutoSize = true, Enabled = enabled, Checked = enabled && chosen, Margin = new Padding(0, 2, 14, 2) };
            panel.Controls.Add(box);
            return box;
        }

        private void FillGrid()
        {
            _grid.Rows.Clear();
            foreach (var f in _plan.Filters)
            {
                int row = _grid.Rows.Add(f.Enabled, f.Kind, f.Source + (f.Note != null ? Environment.NewLine + f.Note : ""), f.Stat.Text + Environment.NewLine + f.Stat.Id,
                    f.Min.HasValue ? f.Min.Value.ToString(CultureInfo.InvariantCulture) : "", f.Max.HasValue ? f.Max.Value.ToString(CultureInfo.InvariantCulture) : "");
                _grid.Rows[row].Tag = f;
            }
        }

        private bool ReadGrid()
        {
            _grid.EndEdit();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var f = (TradeFilter)row.Tag;
                f.Enabled = Convert.ToBoolean(row.Cells["use"].Value);
                double? min, max;
                if (!ParseBound(row.Cells["min"].Value, out min) || !ParseBound(row.Cells["max"].Value, out max))
                {
                    MessageBox.Show(this, "Min/Max for '" + f.Source + "' must be a number or empty.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                f.Min = min;
                f.Max = max;
            }
            return true;
        }

        private static bool ParseBound(object value, out double? result)
        {
            result = null;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text)) return true;
            double d;
            if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return false;
            result = d;
            return true;
        }

        private void LoadLeagues()
        {
            Cursor = Cursors.WaitCursor;
            string error;
            var leagues = TradeSearch.FetchLeagues(_config.Game, out error);
            Cursor = Cursors.Default;
            if (leagues == null) { MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string typed = _league.Text;
            _league.Items.Clear();
            foreach (var l in leagues) _league.Items.Add(l);
            _league.Text = typed;
            _league.DroppedDown = true;
        }

        private void OpenSearch()
        {
            if (!ReadGrid()) return;
            string league = (_league.Text ?? "").Trim();
            if (league.Length == 0)
            {
                MessageBox.Show(this, "Choose or type the trade league first (for example the current challenge league, or Standard).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _league.Focus();
                return;
            }
            _plan.Name = _name.Checked ? _originalName : null;
            _plan.Rarity = _rarity.Checked ? _originalRarity : null;
            _plan.UseItemLevel = _ilvl.Checked;
            _plan.UseSockets = _sockets.Checked;
            _plan.UseLinks = _links.Checked && _links.Visible;
            if (!_plan.HasConstraintsBeyondBase)
            {
                string msg = "No stat filters are selected" + (_plan.Unmatched.Count > 0 ? " (and " + _plan.Unmatched.Count + " guide mod(s) could not be mapped)" : "") +
                    ".\n\nSearch for any '" + _plan.BaseType + "'" + (_plan.Rarity != null ? " (" + _plan.Rarity + ")" : "") + " instead?";
                if (MessageBox.Show(this, msg, "Base-type only search", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            string url = TradeSearch.Url(_plan, league);
            LastUrl = url;
            if (!string.Equals(_config.TradeLeague, league, StringComparison.Ordinal)) { _config.TradeLeague = league; _config.Save(); }
            try
            {
                Process.Start(url);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                if (!(ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)) throw;
                Clipboard.SetText(url);
                MessageBox.Show(this, "Could not open the browser (" + ex.Message + "). The search link was copied to the clipboard.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
