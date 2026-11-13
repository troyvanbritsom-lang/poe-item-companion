using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Poe2ItemCompanion
{
    public class SettingsForm : Form
    {
        private AppConfig _config;
        private Action<AppConfig> _onSaved;

        private TextBox _pobPathBox;
        private TextBox _buildDirectoryBox;
        private TextBox _buildPathBox;
        private ComboBox _buildDropdown;
        private CheckBox _autoDetectBuildBox;
        private CheckBox _autoReloadBox;
        private CheckBox _ctrlBox;
        private CheckBox _altBox;
        private CheckBox _shiftBox;
        private ComboBox _keyDropdown;
        private CheckBox _gemCtrlBox, _gemAltBox, _gemShiftBox;
        private ComboBox _gemKeyDropdown;
        private CheckBox _treeCtrlBox, _treeAltBox, _treeShiftBox;
        private ComboBox _treeKeyDropdown;
        private NumericUpDown _timeoutBox;
        private Button _saveBtn;
        private Button _cancelBtn;
        private Button _testBtn;
        private Button _logBtn;

        public SettingsForm(AppConfig config, Action<AppConfig> onSaved)
        {
            _config = config.Clone();
            _onSaved = onSaved;
            InitializeComponent();
            PopulateFields();
        }

        private void InitializeComponent()
        {
            Text = _config.GameName + " Companion - Settings";
            Size = new Size(760, 720);
            MinimumSize = new Size(660, 560);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UiTheme.Canvas;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);

            int y = 16;
            int labelX = 20;
            int inputX = 140;
            int inputWidth = 280;

            // PoB Path
            var pobLabel = new Label { Text = "PoB Directory:", Location = new Point(labelX, y + 2), AutoSize = true };
            _pobPathBox = new TextBox { Location = new Point(inputX, y), Width = inputWidth, BackColor = Color.FromArgb(40, 40, 44), ForeColor = Color.White };
            var pobBrowse = new Button { Text = "Browse...", Location = new Point(inputX + inputWidth + 10, y - 1), Width = 75, BackColor = Color.FromArgb(50, 50, 56), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            pobBrowse.Click += (s, e) =>
            {
                using (var fbd = new FolderBrowserDialog { SelectedPath = _pobPathBox.Text, Description = "Select Path of Building for " + _config.GameName })
                {
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        _pobPathBox.Text = fbd.SelectedPath;
                        _buildDirectoryBox.Text = GameIdentity.FindBuildDirectory(fbd.SelectedPath, _config.Game);
                        RefreshBuildDropdown();
                    }
                }
            };

            Controls.Add(pobLabel);
            Controls.Add(_pobPathBox);
            Controls.Add(pobBrowse);

            y += 40;
            Controls.Add(new Label { Text = "Build directory:", Location = new Point(labelX, y + 2), AutoSize = true });
            _buildDirectoryBox = new TextBox { Location = new Point(inputX, y), Width = inputWidth, BackColor = Color.FromArgb(40, 40, 44), ForeColor = Color.White };
            var directoryBrowse = new Button { Text = "Browse...", Location = new Point(inputX + inputWidth + 10, y - 1), Width = 75 };
            directoryBrowse.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog { SelectedPath = _buildDirectoryBox.Text })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { _buildDirectoryBox.Text = dialog.SelectedPath; RefreshBuildDropdown(); }
            };
            Controls.Add(_buildDirectoryBox);
            Controls.Add(directoryBrowse);
            y += 40;

            // Build File
            var buildLabel = new Label { Text = "GUIDE file (.xml):", Location = new Point(labelX, y + 2), AutoSize = true };
            _buildPathBox = new TextBox { Location = new Point(inputX, y), Width = inputWidth, BackColor = Color.FromArgb(40, 40, 44), ForeColor = Color.White };
            var buildBrowse = new Button { Text = "Browse...", Location = new Point(inputX + inputWidth + 10, y - 1), Width = 75, BackColor = Color.FromArgb(50, 50, 56), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            buildBrowse.Click += (s, e) =>
            {
                using (var ofd = new OpenFileDialog { Filter = "PoB Build Files (*.xml)|*.xml|All Files (*.*)|*.*", InitialDirectory = Path.GetDirectoryName(_buildPathBox.Text) })
                {
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        _buildPathBox.Text = ofd.FileName;
                    }
                }
            };

            Controls.Add(buildLabel);
            Controls.Add(_buildPathBox);
            Controls.Add(buildBrowse);

            y += 34;

            // Quick select build dropdown
            var selectLabel = new Label { Text = "Quick Select:", Location = new Point(labelX, y + 2), AutoSize = true, ForeColor = Color.FromArgb(170, 170, 170) };
            _buildDropdown = new ComboBox { Location = new Point(inputX, y), Width = inputWidth, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(40, 40, 44), ForeColor = Color.White };
            _buildDropdown.SelectedIndexChanged += (s, e) =>
            {
                var item = _buildDropdown.SelectedItem as BuildItem;
                if (item != null)
                {
                    _buildPathBox.Text = item.FullPath;
                }
            };
            Controls.Add(selectLabel);
            Controls.Add(_buildDropdown);

            y += 34;

            // Auto Detect Latest Build
            _autoDetectBuildBox = new CheckBox
            {
                Text = "Auto-select newest GUIDE file (never changes Current character)",
                Location = new Point(inputX, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 200, 200)
            };
            Controls.Add(_autoDetectBuildBox);

            y += 30;

            // Auto Reload Build
            _autoReloadBox = new CheckBox
            {
                Text = "Re-read Current character file before comparing (when you save in PoB)",
                Location = new Point(inputX, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 200, 200)
            };
            Controls.Add(_autoReloadBox);

            y += 40;

            // Global Hotkey
            var hotkeyLabel = new Label { Text = "Global Hotkey:", Location = new Point(labelX, y + 2), AutoSize = true };
            _ctrlBox = new CheckBox { Text = "Ctrl", Location = new Point(inputX, y + 2), AutoSize = true };
            _altBox = new CheckBox { Text = "Alt", Location = new Point(inputX + 55, y + 2), AutoSize = true };
            _shiftBox = new CheckBox { Text = "Shift", Location = new Point(inputX + 105, y + 2), AutoSize = true };

            _keyDropdown = new ComboBox { Location = new Point(inputX + 170, y), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(40, 40, 44), ForeColor = Color.White };
            PopulateKeys();

            Controls.Add(hotkeyLabel);
            Controls.Add(_ctrlBox);
            Controls.Add(_altBox);
            Controls.Add(_shiftBox);
            Controls.Add(_keyDropdown);

            y += 40;

            var gemLabel = new Label { Text = "Gem overlay key:", Location = new Point(labelX, y + 2), AutoSize = true };
            _gemCtrlBox = new CheckBox { Text = "Ctrl", Location = new Point(inputX, y + 2), AutoSize = true };
            _gemAltBox = new CheckBox { Text = "Alt", Location = new Point(inputX + 55, y + 2), AutoSize = true };
            _gemShiftBox = new CheckBox { Text = "Shift", Location = new Point(inputX + 105, y + 2), AutoSize = true };
            _gemKeyDropdown = new ComboBox { Location = new Point(inputX + 170, y), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(40, 40, 44), ForeColor = Color.White };
            _gemKeyDropdown.Items.Add("(disabled)");
            foreach (var k in _keyDropdown.Items) _gemKeyDropdown.Items.Add(k);
            Controls.Add(gemLabel);
            Controls.Add(_gemCtrlBox);
            Controls.Add(_gemAltBox);
            Controls.Add(_gemShiftBox);
            Controls.Add(_gemKeyDropdown);

            y += 40;

            var treeLabel = new Label { Text = "Passive overlay key:", Location = new Point(labelX, y + 2), AutoSize = true };
            _treeCtrlBox = new CheckBox { Text = "Ctrl", Location = new Point(inputX, y + 2), AutoSize = true };
            _treeAltBox = new CheckBox { Text = "Alt", Location = new Point(inputX + 55, y + 2), AutoSize = true };
            _treeShiftBox = new CheckBox { Text = "Shift", Location = new Point(inputX + 105, y + 2), AutoSize = true };
            _treeKeyDropdown = new ComboBox { Location = new Point(inputX + 170, y), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(40, 40, 44), ForeColor = Color.White };
            _treeKeyDropdown.Items.Add("(disabled)");
            foreach (var k in _keyDropdown.Items) _treeKeyDropdown.Items.Add(k);
            Controls.Add(treeLabel);
            Controls.Add(_treeCtrlBox);
            Controls.Add(_treeAltBox);
            Controls.Add(_treeShiftBox);
            Controls.Add(_treeKeyDropdown);

            y += 40;

            // Popup Timeout
            var timeoutLabel = new Label { Text = "Popup Auto-Close:", Location = new Point(labelX, y + 2), AutoSize = true };
            _timeoutBox = new NumericUpDown
            {
                Location = new Point(inputX, y),
                Width = 70,
                Minimum = 0,
                Maximum = 60,
                BackColor = Color.FromArgb(40, 40, 44),
                ForeColor = Color.White
            };
            var timeoutSecLabel = new Label { Text = "seconds (0 = keep open until Esc or click outside)", Location = new Point(inputX + 80, y + 2), AutoSize = true, ForeColor = Color.FromArgb(170, 170, 170) };

            Controls.Add(timeoutLabel);
            Controls.Add(_timeoutBox);
            Controls.Add(timeoutSecLabel);

            y += 60;

            // Bottom Buttons
            _testBtn = new Button
            {
                Text = "Test Compare...",
                Location = new Point(labelX, y),
                Width = 110,
                Height = 32,
                BackColor = Color.FromArgb(45, 60, 80),
                ForeColor = Color.FromArgb(140, 200, 255),
                FlatStyle = FlatStyle.Flat
            };
            _testBtn.Click += TestCompare_Click;

            _logBtn = new Button
            {
                Text = "View Log...",
                Location = new Point(labelX + 118, y),
                Width = 85,
                Height = 32,
                BackColor = Color.FromArgb(50, 50, 56),
                ForeColor = Color.FromArgb(220, 220, 220),
                FlatStyle = FlatStyle.Flat
            };
            _logBtn.Click += (s, e) => Logger.OpenLogFile();

            _saveBtn = new Button
            {
                Text = "Save & Apply",
                Location = new Point(315, y),
                Width = 100,
                Height = 32,
                BackColor = Color.FromArgb(40, 120, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _saveBtn.Click += Save_Click;

            _cancelBtn = new Button
            {
                Text = "Cancel",
                Location = new Point(425, y),
                Width = 80,
                Height = 32,
                BackColor = Color.FromArgb(60, 60, 64),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _cancelBtn.Click += (s, e) => Close();

            Controls.Add(_testBtn);
            Controls.Add(_logBtn);
            Controls.Add(_saveBtn);
            Controls.Add(_cancelBtn);
            Controls.Clear();
            var content = new Panel { Name = "Settings scroll area", Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16), BackColor = UiTheme.Canvas };
            var sections = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = Padding.Empty, BackColor = UiTheme.Canvas };
            sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var paths = Section("BUILD SOURCES  /  " + _config.GameName);
            SettingRow(paths, "PoB directory", _pobPathBox, pobBrowse);
            SettingRow(paths, "Build directory", _buildDirectoryBox, directoryBrowse);
            SettingRow(paths, "Guide file (.xml)", _buildPathBox, buildBrowse);
            SettingRow(paths, "Quick select guide", _buildDropdown, null);
            _autoDetectBuildBox.AutoSize = _autoReloadBox.AutoSize = false;
            _autoDetectBuildBox.Height = _autoReloadBox.Height = 40;
            SettingRow(paths, "", _autoDetectBuildBox, null);
            SettingRow(paths, "", _autoReloadBox, null);
            AddSection(sections, paths);
            var keys = Section("KEYBOARD SHORTCUTS");
            SettingRow(keys, "Item comparison", UiTheme.Actions(_ctrlBox, _altBox, _shiftBox, _keyDropdown), null);
            SettingRow(keys, "Gem overlay", UiTheme.Actions(_gemCtrlBox, _gemAltBox, _gemShiftBox, _gemKeyDropdown), null);
            SettingRow(keys, "Passive overlay", UiTheme.Actions(_treeCtrlBox, _treeAltBox, _treeShiftBox, _treeKeyDropdown), null);
            foreach (var check in new[] { _ctrlBox, _altBox, _shiftBox, _gemCtrlBox, _gemAltBox, _gemShiftBox, _treeCtrlBox, _treeAltBox, _treeShiftBox })
                check.Margin = new Padding(4, 7, 12, 3);
            AddSection(sections, keys);
            var popup = Section("COMPARISON POPUP");
            timeoutSecLabel.AutoSize = false;
            timeoutSecLabel.Width = 330;
            timeoutSecLabel.Height = 40;
            SettingRow(popup, "Auto-close after", UiTheme.Actions(_timeoutBox, timeoutSecLabel), null);
            AddSection(sections, popup);
            content.Controls.Add(sections);
            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 60, ColumnCount = 2, Padding = new Padding(16, 8, 16, 8), BackColor = UiTheme.Surface };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.Controls.Add(UiTheme.Actions(_testBtn, _logBtn), 0, 0);
            footer.Controls.Add(UiTheme.Actions(_saveBtn, _cancelBtn), 1, 0);
            Controls.Add(content);
            Controls.Add(footer);
            AcceptButton = _saveBtn;
            CancelButton = _cancelBtn;
            _cancelBtn.DialogResult = DialogResult.Cancel;
            UiTheme.Apply(this);
            UiTheme.Emphasize(_saveBtn);
        }

        private static TableLayoutPanel Section(string title)
        {
            var section = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 12), BackColor = UiTheme.Surface };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var heading = new Label { Text = title, ForeColor = UiTheme.Accent, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 14) };
            section.Controls.Add(heading, 0, 0);
            section.SetColumnSpan(heading, 3);
            return section;
        }

        private static void AddSection(TableLayoutPanel sections, TableLayoutPanel section)
        {
            int row = sections.RowCount++;
            sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sections.Controls.Add(section, 0, row);
        }

        private static void SettingRow(TableLayoutPanel section, string caption, Control input, Button action)
        {
            int row = section.RowCount++;
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 8, 8, 8), ForeColor = UiTheme.Muted }, 0, row);
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(0, 4, 8, 8);
            input.AccessibleName = caption;
            section.Controls.Add(input, 1, row);
            if (action == null) section.SetColumnSpan(input, 2);
            else
            {
                action.Height = 28;
                action.Dock = DockStyle.Fill;
                action.Margin = new Padding(0, 2, 0, 6);
                section.Controls.Add(action, 2, row);
            }
        }

        private void PopulateKeys()
        {
            _keyDropdown.Items.Clear();
            var keys = new[]
            {
                Keys.D, Keys.C, Keys.F, Keys.E, Keys.Q, Keys.R, Keys.T, Keys.G,
                Keys.I, Keys.P, Keys.X, Keys.Z, Keys.V, Keys.B,
                Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12,
                Keys.Space
            };
            foreach (var k in keys)
            {
                _keyDropdown.Items.Add(k);
            }
        }

        private void PopulateFields()
        {
            _pobPathBox.Text = _config.PobPath;
            _buildDirectoryBox.Text = _config.BuildDirectory;
            _buildPathBox.Text = _config.BuildFilePath;
            _autoDetectBuildBox.Checked = _config.AutoDetectLatestBuild;
            _autoReloadBox.Checked = _config.AutoReloadBuild;

            _ctrlBox.Checked = (_config.HotkeyModifiers & 2) != 0;
            _altBox.Checked = (_config.HotkeyModifiers & 1) != 0;
            _shiftBox.Checked = (_config.HotkeyModifiers & 4) != 0;

            _keyDropdown.SelectedItem = (Keys)_config.HotkeyKey;
            if (_keyDropdown.SelectedIndex == -1 && _keyDropdown.Items.Count > 0)
            {
                _keyDropdown.SelectedIndex = 0;
            }

            _gemCtrlBox.Checked = (_config.GemOverlayHotkeyModifiers & 2) != 0;
            _gemAltBox.Checked = (_config.GemOverlayHotkeyModifiers & 1) != 0;
            _gemShiftBox.Checked = (_config.GemOverlayHotkeyModifiers & 4) != 0;
            if (_config.GemOverlayHotkeyKey == 0) _gemKeyDropdown.SelectedIndex = 0;
            else _gemKeyDropdown.SelectedItem = (Keys)_config.GemOverlayHotkeyKey;
            if (_gemKeyDropdown.SelectedIndex == -1) _gemKeyDropdown.SelectedIndex = 0;
            _treeCtrlBox.Checked = (_config.PassiveOverlayHotkeyModifiers & 2) != 0;
            _treeAltBox.Checked = (_config.PassiveOverlayHotkeyModifiers & 1) != 0;
            _treeShiftBox.Checked = (_config.PassiveOverlayHotkeyModifiers & 4) != 0;
            if (_config.PassiveOverlayHotkeyKey == 0) _treeKeyDropdown.SelectedIndex = 0;
            else _treeKeyDropdown.SelectedItem = (Keys)_config.PassiveOverlayHotkeyKey;
            if (_treeKeyDropdown.SelectedIndex == -1) _treeKeyDropdown.SelectedIndex = 0;

            _timeoutBox.Value = Math.Max(0, Math.Min(60, _config.PopupTimeoutSeconds));

            RefreshBuildDropdown();
        }

        private void RefreshBuildDropdown()
        {
            _buildDropdown.Items.Clear();
            string pobDir = _pobPathBox.Text;
            if (string.IsNullOrEmpty(pobDir) || !Directory.Exists(pobDir)) return;

            string buildDir = _buildDirectoryBox.Text;
            if (!Directory.Exists(buildDir)) return;

            try
            {
                var files = Directory.GetFiles(buildDir, "*.xml", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    _buildDropdown.Items.Add(new BuildItem(Path.GetFileName(f), f));
                }

                // Select current if matched
                for (int i = 0; i < _buildDropdown.Items.Count; i++)
                {
                    var item = (BuildItem)_buildDropdown.Items[i];
                    if (string.Equals(item.FullPath, _buildPathBox.Text, StringComparison.OrdinalIgnoreCase))
                    {
                        _buildDropdown.SelectedIndex = i;
                        break;
                    }
                }
            }
            catch { }
        }

        private void Save_Click(object sender, EventArgs e)
        {
            try
            {
                if (GameIdentity.DetectInstallation(_pobPathBox.Text.Trim()) != _config.Game)
                    throw new InvalidDataException("Select the PoB installation for " + _config.GameName + ".");
                string folder = _buildDirectoryBox.Text.Trim();
                if (folder.Length > 0 && !Directory.Exists(folder)) throw new DirectoryNotFoundException("Build directory does not exist: " + folder);
                string file = _buildPathBox.Text.Trim();
                bool fileSelected = (!_config.UsesShareCode && !_config.UsesGggBuild)
                    || !string.Equals(file, _config.BuildFilePath ?? "", StringComparison.OrdinalIgnoreCase);
                if (fileSelected && file.Length > 0)
                {
                    string error;
                    if (!GameIdentity.ValidateBuild(File.ReadAllText(file), _config.Game, out error))
                        throw new InvalidDataException(error);
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Invalid game settings", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            int modifiers = 0;
            if (_ctrlBox.Checked) modifiers |= 2;
            if (_altBox.Checked) modifiers |= 1;
            if (_shiftBox.Checked) modifiers |= 4;

            if (modifiers == 0)
            {
                MessageBox.Show("Please select at least one modifier key (e.g. Ctrl or Alt) for safety.", "Invalid Hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_keyDropdown.SelectedItem == null)
            {
                MessageBox.Show("Please select a hotkey key.", "Invalid Hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int gemModifiers = (_gemCtrlBox.Checked ? 2 : 0) | (_gemAltBox.Checked ? 1 : 0) | (_gemShiftBox.Checked ? 4 : 0);
            int gemKey = _gemKeyDropdown.SelectedItem is Keys ? (int)(Keys)_gemKeyDropdown.SelectedItem : 0;
            if (gemKey != 0 && gemModifiers == 0)
            {
                MessageBox.Show("Please select at least one modifier for the gem overlay key, or disable it.", "Invalid Hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (gemKey != 0 && gemKey == (int)(Keys)_keyDropdown.SelectedItem && gemModifiers == modifiers)
            {
                MessageBox.Show("The gem overlay key must differ from the compare hotkey.", "Invalid Hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int treeModifiers = (_treeCtrlBox.Checked ? 2 : 0) | (_treeAltBox.Checked ? 1 : 0) | (_treeShiftBox.Checked ? 4 : 0);
            int treeKey = _treeKeyDropdown.SelectedItem is Keys ? (int)(Keys)_treeKeyDropdown.SelectedItem : 0;
            if (treeKey != 0 && treeModifiers == 0)
            {
                MessageBox.Show("Please select at least one modifier for the passive overlay key, or disable it.", "Invalid Hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (treeKey != 0 && ((treeKey == (int)(Keys)_keyDropdown.SelectedItem && treeModifiers == modifiers) || (treeKey == gemKey && treeModifiers == gemModifiers)))
            {
                MessageBox.Show("The passive overlay key must differ from the compare and gem overlay keys.", "Invalid Hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _config.PobPath = _pobPathBox.Text.Trim();
            _config.BuildDirectory = string.IsNullOrWhiteSpace(_buildDirectoryBox.Text)
                ? GameIdentity.FindBuildDirectory(_config.PobPath, _config.Game) : _buildDirectoryBox.Text.Trim();
            string newBuildPath = _buildPathBox.Text.Trim();
            if (!string.Equals(newBuildPath, _config.BuildFilePath ?? "", StringComparison.OrdinalIgnoreCase) && newBuildPath.Length > 0)
            {
                _config.BuildSource = AppConfig.SourceFile; // user picked a different file explicitly
            }
            _config.BuildFilePath = newBuildPath;
            _config.AutoDetectLatestBuild = _autoDetectBuildBox.Checked;
            _config.AutoReloadBuild = _autoReloadBox.Checked;
            _config.HotkeyModifiers = modifiers;
            _config.HotkeyKey = (int)(Keys)_keyDropdown.SelectedItem;
            _config.PopupTimeoutSeconds = (int)_timeoutBox.Value;
            _config.GemOverlayHotkeyModifiers = gemModifiers;
            _config.GemOverlayHotkeyKey = gemKey;
            _config.PassiveOverlayHotkeyModifiers = treeModifiers;
            _config.PassiveOverlayHotkeyKey = treeKey;

            try { _config.Save(); }
            catch (Exception ex) { MessageBox.Show("Settings were not applied: " + ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (_onSaved != null)
            {
                _onSaved(_config);
            }
            Close();
        }

        private void TestCompare_Click(object sender, EventArgs e)
        {
            string pobPath = _pobPathBox.Text.Trim();
            string buildPath = _buildPathBox.Text.Trim();

            if (!Directory.Exists(pobPath))
            {
                MessageBox.Show("PoB directory does not exist: " + pobPath, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrEmpty(_config.CurrentBuildFilePath) && string.IsNullOrEmpty(_config.CurrentCharacterCode))
            {
                MessageBox.Show("Import your Current character separately in the dashboard first.", "Current character required", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var testEngine = new PobLuaEngine())
            {
                string scriptPath = AppConfig.GetWrapperScriptPath();
                string err;
                if (!testEngine.Initialize(pobPath, scriptPath, out err, _config.Game))
                {
                    MessageBox.Show("Failed to initialize PoB Engine:\n" + err, "PoB Engine Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var testConfig = _config.Clone();
                testConfig.DisableSave = true;
                testConfig.PobPath = pobPath;
                if (!string.Equals(buildPath, _config.BuildFilePath, StringComparison.OrdinalIgnoreCase))
                    testConfig.BuildSource = AppConfig.SourceFile;
                testConfig.BuildFilePath = buildPath;
                var buildInfo = testConfig.LoadConfiguredBuild(testEngine, out err);
                if (buildInfo == null || !buildInfo.Success)
                {
                    MessageBox.Show("Failed to load build file:\n" + err, "Build Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string sampleItem = @"Item Class: Boots
Rarity: Rare
Gale Tread
Dragonscale Boots
--------
Evasion Rating: 420
Armour: 250
--------
Requirements:
Level: 75
Dex: 62
Str: 62
--------
Item Level: 82
--------
+150 to maximum Life
+45% to Fire Resistance
+45% to Lightning Resistance
35% increased Movement Speed
";
                var cmp = GuideComparison.Compare(testConfig, testEngine, sampleItem, out err);
                if (cmp == null || !cmp.Success)
                {
                    MessageBox.Show("Failed to compare sample item:\n" + err + "\n\nSee log file for details.", "Comparison Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var popup = new ComparisonPopup();
                popup.DisplayResult(cmp, _config, Cursor.Position);
            }
        }

        private class BuildItem
        {
            public string FileName { get; private set; }
            public string FullPath { get; private set; }

            public BuildItem(string fileName, string fullPath)
            {
                FileName = fileName;
                FullPath = fullPath;
            }

            public override string ToString()
            {
                return FileName;
            }
        }
    }
}
