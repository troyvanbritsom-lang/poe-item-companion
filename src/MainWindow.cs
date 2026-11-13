using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Poe2ItemCompanion
{
    public class MainWindow : Form
    {
        private AppConfig _config;
        private PobLuaEngine _engine;
        private HotkeyManager _hotkeyManager;
        private NotifyIcon _notifyIcon;
        private Action _onBuildChanged;
        private Action _openSettings;
        private Action<GameId> _switchGame;
        public bool IsBusy { get; private set; }

        // Build source controls
        private TextBox _shareCodeBox;
        private Button _loadShareBtn;
        private Button _pasteShareBtn;
        private ComboBox _buildDropdown;
        private Button _refreshBuildsBtn;
        private Button _browseBuildBtn;
        private Button _reloadBuildBtn;
        private Label _activeSourceLabel;
        private Label _guideSourceLabel;
        private Label _statusLabel;
        private Label _hotkeyLabel;
        private CheckBox _autoReloadCheck;
        private CheckBox _autoDetectCheck;
        private bool _suppressDropdownEvents;

        // Compare tab: build overview
        private Label _charNameVal;
        private Label _classVal;
        private Label _levelVal;
        private Label _mainSkillVal;
        private Label _totalDpsVal;
        private Label _combinedDpsVal;
        private Label _lifeVal;
        private Label _esVal;
        private Label _ehpVal;
        private Label _resistsVal;
        private Label _assumptionVal;
        private Label _baselineKindLabel;
        private Button _useCharacterBtn;
        private string _lastItemText;
        private string _lastBuildName;

        // Compare tab: preview
        private TextBox _sampleItemBox;
        private Panel _comparisonContainer;

        // Build tab
        private TabControl _tabs;
        private GuideBuildView _guideBuildView;
        private RichTextBox _summaryBox;
        private ListBox _gearList;
        private TextBox _gearItemBox;
        private RichTextBox _nodesBox;
        private PassiveTreeControl _treeControl;
        private BuildDetails _details;
        private bool _detailsStale = true;

        private Button _settingsBtn;
        private Button _viewLogBtn;
        private readonly ToolTip _sourceTips = new ToolTip();

        public MainWindow(AppConfig config, PobLuaEngine engine, HotkeyManager hotkeyManager, NotifyIcon notifyIcon, Action onBuildChanged, Action openSettings, Action<GameId> switchGame = null)
        {
            _config = config;
            _engine = engine;
            _hotkeyManager = hotkeyManager;
            _notifyIcon = notifyIcon;
            _onBuildChanged = onBuildChanged;
            _openSettings = openSettings;
            _switchGame = switchGame;

            InitializeComponent();
            Disposed += (s, e) => _sourceTips.Dispose();
            RefreshBuildDropdown();
            ReloadActiveBuild(false);
            UpdateStatus();
        }

        public void SetConfig(AppConfig config)
        {
            _config = config;
            _hotkeyLabel.Text = "Hotkey: " + _config.GetHotkeyDisplayString();
            _autoReloadCheck.Checked = _config.AutoReloadBuild;
            _autoDetectCheck.Checked = _config.AutoDetectLatestBuild;
            _shareCodeBox.Text = _config.ShareCode ?? "";
            RefreshBuildDropdown();
            UpdateSourceLabel();
        }

        private static Button MakeButton(string text, int x, int y, int w, Color back)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Width = w,
                Height = 27,
                BackColor = back,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
        }

        private void InitializeComponent()
        {
            Text = _config.GameName + " Companion";
            Size = new Size(1080, 760);
            MinimumSize = new Size(960, 680);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 24, 27);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            Icon = SystemIcons.Shield;

            // Top bar: build source
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 192, BackColor = UiTheme.Surface, Padding = new Padding(12, 8, 12, 8) };
            var gold = Color.FromArgb(230, 200, 110);

            var guideCaption = new Label { Text = "GUIDE code / link", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = gold, AutoSize = true };
            _shareCodeBox = new TextBox
            {
                Location = new Point(185, 10),
                Width = 520,
                BackColor = Color.FromArgb(45, 45, 52),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9f),
                MaxLength = 0, // PoB2 share codes are often > 32,767 chars (TextBox default limit truncates them)
                Text = _config.ShareCode ?? ""
            };
            _pasteShareBtn = MakeButton("Paste", 712, 8, 60, Color.FromArgb(50, 50, 60));
            _pasteShareBtn.Click += (s, e) =>
            {
                string t = InputSimulator.GetClipboardTextSafe();
                if (!string.IsNullOrEmpty(t)) _shareCodeBox.Text = t.Trim();
            };
            _loadShareBtn = MakeButton("Load share code", 778, 8, 125, Color.FromArgb(38, 90, 140));
            _loadShareBtn.Click += (s, e) => LoadShareCodeFromBox();

            var currentCaption = new Label { Text = "CURRENT file", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = UiTheme.Text, AutoSize = true };
            _buildDropdown = new ComboBox
            {
                Location = new Point(185, 43),
                Width = 360,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 45, 52),
                ForeColor = Color.White
            };
            _buildDropdown.SelectedIndexChanged += BuildDropdown_SelectedIndexChanged;
            _refreshBuildsBtn = MakeButton("Refresh list", 552, 41, 85, Color.FromArgb(50, 50, 60));
            _refreshBuildsBtn.Click += (s, e) => RefreshBuildDropdown();
            _browseBuildBtn = MakeButton("Browse...", 643, 41, 75, Color.FromArgb(50, 50, 60));
            _browseBuildBtn.Click += BrowseBuildBtn_Click;
            _reloadBuildBtn = MakeButton("Reload build", 724, 41, 95, Color.FromArgb(50, 50, 60));
            _reloadBuildBtn.Click += (s, e) => ReloadActiveBuild(true);
            _useCharacterBtn = MakeButton("Use my character", 825, 41, 165, Color.FromArgb(110, 80, 30));
            _useCharacterBtn.Click += (s, e) => UseImportedCharacter();

            _activeSourceLabel = new Label { Location = new Point(12, 80), AutoSize = true, ForeColor = Color.FromArgb(130, 200, 255), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };

            _autoReloadCheck = new CheckBox { Text = "Re-read file when PoB saves it", Location = new Point(560, 79), AutoSize = true, Checked = _config.AutoReloadBuild, ForeColor = Color.FromArgb(200, 200, 200) };
            _autoReloadCheck.CheckedChanged += (s, e) => { _config.AutoReloadBuild = _autoReloadCheck.Checked; _config.Save(); };
            _autoDetectCheck = new CheckBox { Text = "Auto-pick guide file", Location = new Point(790, 79), AutoSize = true, Checked = _config.AutoDetectLatestBuild, ForeColor = Color.FromArgb(200, 200, 200) };
            _autoDetectCheck.CheckedChanged += (s, e) =>
            {
                _config.AutoDetectLatestBuild = _autoDetectCheck.Checked;
                _config.Save();
                if (_autoDetectCheck.Checked)
                {
                    string latest = AppConfig.FindLatestBuild(_config.PobPath, _config.BuildDirectory);
                    if (!string.IsNullOrEmpty(latest)) { _config.BuildFilePath = latest; _config.Save(); UpdateSourceLabel(); }
                }
            };

            _hotkeyLabel = new Label { Text = "Hotkey: " + _config.GetHotkeyDisplayString(), Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(100, 220, 150), Location = new Point(915, 13), AutoSize = true };

            topPanel.Controls.AddRange(new Control[] { _shareCodeBox, _pasteShareBtn, _loadShareBtn, _buildDropdown, _refreshBuildsBtn, _browseBuildBtn, _reloadBuildBtn, _useCharacterBtn, _activeSourceLabel, _autoReloadCheck, _autoDetectCheck, _hotkeyLabel });
            var currentCode = MakeButton("Import Current character code...", 12, 112, 225, Color.FromArgb(45, 90, 65));
            currentCode.Click += (s, e) => ImportCurrentCode();
            var guideFile = MakeButton("Guide file...", 245, 112, 110, Color.FromArgb(38, 90, 140));
            guideFile.Click += (s, e) =>
            {
                using (var dialog = new OpenFileDialog { Filter = _config.Game == GameId.Poe1 ? "PoB1 guide (*.xml)|*.xml" : "Guide build (*.xml;*.build)|*.xml;*.build|PoB2 build (*.xml)|*.xml|GGG / Maxroll planner (*.build)|*.build" })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        if (string.Equals(Path.GetExtension(dialog.FileName), ".build", StringComparison.OrdinalIgnoreCase))
                        {
                            string error;
                            if (GggBuildFile.Load(dialog.FileName, out error) == null)
                            {
                                MessageBox.Show(this, "Not a readable .build file: " + error, "Guide file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                            _config.GggBuildFilePath = dialog.FileName;
                            _config.BuildSource = AppConfig.SourceGggBuild;
                        }
                        else
                        {
                            string error;
                            if (!GameIdentity.ValidateBuild(File.ReadAllText(dialog.FileName), _config.Game, out error))
                            { MessageBox.Show(this, error, "Guide rejected"); return; }
                            _config.BuildFilePath = dialog.FileName;
                            _config.BuildSource = AppConfig.SourceFile;
                        }
                        _config.Save();
                        UpdateSourceLabel();
                        ResetComparisonPreview();
                    }
            };
            topPanel.Controls.Add(currentCode);
            topPanel.Controls.Add(guideFile);
            _guideSourceLabel = new Label { Location = new Point(365, 117), Size = new Size(650, 25), ForeColor = Color.LightSkyBlue };
            topPanel.Controls.Add(_guideSourceLabel);
            topPanel.Controls.Clear();
            var sources = new TableLayoutPanel { Name = "Source layout", Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5 };
            sources.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
            sources.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sources.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sources.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            sources.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            sources.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            sources.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            sources.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            guideCaption.Margin = currentCaption.Margin = new Padding(0, 9, 6, 0);
            sources.Controls.Add(guideCaption, 0, 0);
            sources.Controls.Add(currentCaption, 0, 1);
            _shareCodeBox.Dock = _buildDropdown.Dock = DockStyle.Fill;
            _shareCodeBox.Margin = _buildDropdown.Margin = new Padding(0, 5, 6, 5);
            _shareCodeBox.AccessibleName = "Guide code or link";
            _buildDropdown.AccessibleName = "Current character file";
            sources.Controls.Add(_shareCodeBox, 1, 0);
            sources.Controls.Add(_buildDropdown, 1, 1);
            sources.Controls.Add(UiTheme.Actions(_pasteShareBtn, _loadShareBtn, guideFile), 2, 0);
            sources.Controls.Add(UiTheme.Actions(_refreshBuildsBtn, _browseBuildBtn, _reloadBuildBtn, _useCharacterBtn), 2, 1);
            var options = UiTheme.Actions(currentCode, _autoReloadCheck, _autoDetectCheck);
            _autoReloadCheck.Margin = _autoDetectCheck.Margin = new Padding(16, 8, 0, 0);
            sources.Controls.Add(options, 0, 2);
            sources.SetColumnSpan(options, 3);
            foreach (var label in new[] { _activeSourceLabel, _guideSourceLabel })
            {
                label.AutoSize = false;
                label.AutoEllipsis = true;
                label.Dock = DockStyle.Fill;
                label.Margin = new Padding(0, 2, 0, 0);
            }
            sources.Controls.Add(_activeSourceLabel, 0, 3);
            sources.SetColumnSpan(_activeSourceLabel, 3);
            sources.Controls.Add(_guideSourceLabel, 0, 4);
            sources.SetColumnSpan(_guideSourceLabel, 3);
            topPanel.Controls.Add(sources);

            // Bottom status bar
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = UiTheme.Canvas, Padding = new Padding(12, 6, 12, 6) };
            _statusLabel = new Label { Text = "Ready", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted };
            _settingsBtn = new Button { Text = "Settings", Dock = DockStyle.Right, Width = 85, BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _settingsBtn.Click += SettingsBtn_Click;
            _viewLogBtn = new Button { Text = "View log", Dock = DockStyle.Right, Width = 85, BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _viewLogBtn.Click += (s, e) => Logger.OpenLogFile();
            bottomPanel.Controls.Add(_statusLabel);
            bottomPanel.Controls.Add(_viewLogBtn);
            bottomPanel.Controls.Add(_settingsBtn);

            // Tabs
            _tabs = new DarkTabControl { Dock = DockStyle.Fill };
            var compareTab = new TabPage("Compare") { BackColor = Color.FromArgb(24, 24, 27) };
            var buildTab = new TabPage("Current character") { BackColor = Color.FromArgb(24, 24, 27) };
            var guideTab = new TabPage("Build") { BackColor = Color.FromArgb(24, 24, 27) };
            _guideBuildView = new GuideBuildView();
            _guideBuildView.GuideStageChanged += (s, e) =>
            {
                UpdateSourceLabel();
                if (_comparisonContainer != null) ResetComparisonPreview();
                string stage = _config.GetSelectedGuideLoadout();
                _statusLabel.Text = "Guide stage: " + (stage ?? "saved sets") + ". The next comparison rates items against this stage; Current character baseline unchanged.";
            };
            guideTab.Controls.Add(_guideBuildView);
            _guideBuildView.GuideSourceChanged += (s, e) =>
            {
                UpdateSourceLabel();
                if (_comparisonContainer != null) ResetComparisonPreview();
                _statusLabel.Text = "Guide source: " + _config.GetBuildSourceDisplay() + ". Current character baseline unchanged.";
            };

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = Color.FromArgb(38, 38, 42) };
            split.Panel1.BackColor = Color.FromArgb(24, 24, 27);
            split.Panel2.BackColor = Color.FromArgb(24, 24, 27);
            split.Panel1.Controls.Add(CreateBuildOverviewPanel());
            split.Panel2.Controls.Add(CreateComparisonPreviewPanel());
            compareTab.Controls.Add(split);
            split.HandleCreated += (s, e) =>
            {
                split.Panel1MinSize = 360;
                split.Panel2MinSize = 440;
                split.SplitterDistance = Math.Min(400, split.Width - split.Panel2MinSize - split.SplitterWidth);
            };

            buildTab.Controls.Add(CreateBuildTab());

            _tabs.TabPages.Add(compareTab);
            _tabs.TabPages.Add(buildTab);
            _tabs.TabPages.Add(guideTab);
            _tabs.SelectedIndexChanged += (s, e) =>
            {
                if (_tabs.SelectedIndex == 1) EnsureBuildDetails();
                if (_tabs.SelectedIndex == 2) _guideBuildView.EnsureLoaded();
            };

            Controls.Add(_tabs);
            Controls.Add(topPanel);
            Controls.Add(bottomPanel);
            var gamePanel = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = UiTheme.Surface, Padding = new Padding(12, 8, 12, 8) };
            gamePanel.Controls.Add(new Label { Text = "Companion game:", AutoSize = true, Location = new Point(12, 12), ForeColor = Color.Gold });
            var selector = new ComboBox { Name = "Companion game selector", DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(145, 8), Width = 220 };
            selector.Items.Add("Path of Exile 1");
            selector.Items.Add("Path of Exile 2");
            selector.SelectedIndex = _config.Game == GameId.Poe1 ? 0 : 1;
            selector.SelectedIndexChanged += (s, e) =>
            {
                GameId selected = selector.SelectedIndex == 0 ? GameId.Poe1 : GameId.Poe2;
                if (_switchGame != null && selected != _config.Game) _switchGame(selected);
                if (!IsDisposed) selector.SelectedIndex = _config.Game == GameId.Poe1 ? 0 : 1;
            };
            gamePanel.Controls.Add(selector);
            gamePanel.Controls.Add(new Label { Text = "Independent Current / Guide snapshots per game.", AutoSize = true,
                Location = new Point(385, 12), ForeColor = Color.Silver });
            _hotkeyLabel.Dock = DockStyle.Right;
            _hotkeyLabel.AutoSize = false;
            _hotkeyLabel.Width = 150;
            _hotkeyLabel.TextAlign = ContentAlignment.MiddleRight;
            gamePanel.Controls.Add(_hotkeyLabel);
            Controls.Add(gamePanel);
            UiTheme.Apply(this);
            UiTheme.Emphasize(_loadShareBtn);
        }

        private Panel CreateBuildOverviewPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16) };
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var heading = new Label { Text = "CURRENT CHARACTER", Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = UiTheme.Text, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            grid.Controls.Add(heading, 0, 0);
            grid.SetColumnSpan(heading, 2);
            _baselineKindLabel = new Label { Text = "-", Height = 40, Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Margin = Padding.Empty };
            grid.Controls.Add(_baselineKindLabel, 0, 1);
            grid.SetColumnSpan(_baselineKindLabel, 2);
            Func<string, Color, Label> row = (label, valColor) =>
            {
                int index = grid.RowCount++;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                grid.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, ForeColor = UiTheme.Muted }, 0, index);
                var val = new Label { Text = "-", Height = 26, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = valColor, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
                grid.Controls.Add(val, 1, index);
                return val;
            };

            _charNameVal = row("Build:", Color.FromArgb(255, 215, 100));
            _classVal = row("Class / Ascend:", Color.White);
            _levelVal = row("Level:", Color.White);
            _mainSkillVal = row("Main skill:", Color.FromArgb(130, 200, 255));
            _totalDpsVal = row("Total DPS:", Color.FromArgb(255, 100, 100));
            _combinedDpsVal = row("Combined DPS:", Color.FromArgb(255, 120, 120));
            _lifeVal = row("Life:", Color.FromArgb(240, 80, 80));
            _esVal = row("Energy Shield:", Color.FromArgb(100, 180, 255));
            _ehpVal = row("Effective HP:", Color.FromArgb(255, 200, 80));
            _resistsVal = row("Res (F/C/L):", Color.FromArgb(180, 180, 180));
            int assumptionsRow = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var assumptionsHeading = new Label { Text = "CALCULATION ASSUMPTIONS (PoB config)", Dock = DockStyle.Fill, Height = 38, ForeColor = UiTheme.Muted, Padding = new Padding(0, 12, 0, 0), Margin = Padding.Empty };
            grid.Controls.Add(assumptionsHeading, 0, assumptionsRow);
            grid.SetColumnSpan(assumptionsHeading, 2);
            int textRow = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _assumptionVal = new Label { Text = "-", Height = 220, Dock = DockStyle.Fill, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 9f), Margin = Padding.Empty };
            grid.Controls.Add(_assumptionVal, 0, textRow);
            grid.SetColumnSpan(_assumptionVal, 2);
            panel.Controls.Add(grid);
            return panel;
        }

        private Panel CreateComparisonPreviewPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(new Label { Text = "ITEM COMPARISON", Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = UiTheme.Text, Location = new Point(14, 14), AutoSize = true });
            var hint = new Label { Text = "Against Current gear; guide fit is separate. Paste item text (Ctrl+C in game), or use the hotkey in game.", ForeColor = UiTheme.Muted, Location = new Point(14, 40), Size = new Size(420, 40), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            panel.Controls.Add(hint);

            _sampleItemBox = new TextBox
            {
                Location = new Point(14, 84),
                Size = new Size(600, 120),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(32, 32, 36),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Consolas", 8.5f),
                Text = "Item Class: Boots\r\nRarity: Rare\r\nStorm Tread\r\nDragonscale Boots\r\n--------\r\nEvasion Rating: 420\r\nArmour: 250\r\n--------\r\nItem Level: 82\r\n--------\r\n+150 to maximum Life\r\n+45% to Fire Resistance\r\n+45% to Lightning Resistance\r\n35% increased Movement Speed"
            };
            panel.Controls.Add(_sampleItemBox);

            var compareBtn = MakeButton("Compare item text", 14, 212, 140, Color.FromArgb(40, 110, 60));
            compareBtn.Click += (s, e) => CompareText(_sampleItemBox.Text);
            var clipBtn = MakeButton("Compare from clipboard", 162, 212, 170, Color.FromArgb(50, 60, 80));
            clipBtn.Click += (s, e) =>
            {
                string text = InputSimulator.GetClipboardTextSafe();
                if (string.IsNullOrEmpty(text))
                {
                    MessageBox.Show(this, "Clipboard does not contain Path of Exile item text.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _sampleItemBox.Text = text;
                CompareText(text);
            };
            panel.Controls.Add(compareBtn);
            panel.Controls.Add(clipBtn);

            _comparisonContainer = new Panel
            {
                Location = new Point(14, 250),
                Size = new Size(600, 330),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(20, 20, 22),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            ResetComparisonPreview();
            panel.Controls.Add(_comparisonContainer);
            panel.Resize += (s, e) =>
            {
                int margin = _sampleItemBox.Left;
                _sampleItemBox.Width = hint.Width = Math.Max(200, panel.ClientSize.Width - margin * 2);
                _comparisonContainer.Size = new Size(Math.Max(200, panel.ClientSize.Width - margin * 2), Math.Max(40, panel.ClientSize.Height - _comparisonContainer.Top - margin));
            };
            return panel;
        }

        private void ResetComparisonPreview()
        {
            _comparisonContainer.Controls.Clear();
            _comparisonContainer.Controls.Add(new Label { Text = "Paste an item above to compare it with Current gear.\r\nComparing does not change your build.", ForeColor = UiTheme.Muted, Dock = DockStyle.Top, Height = 64, Padding = new Padding(12) });
        }

        private Control CreateBuildTab()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = Color.FromArgb(38, 38, 42) };
            split.Panel1.BackColor = Color.FromArgb(24, 24, 27);
            split.Panel2.BackColor = Color.FromArgb(24, 24, 27);
            split.HandleCreated += (s, e) => { try { split.SplitterDistance = 430; } catch { } };

            var inner = new DarkTabControl { Dock = DockStyle.Fill };

            var summaryPage = new TabPage("Summary & calc config");
            _summaryBox = MakeRichBox();
            summaryPage.Controls.Add(_summaryBox);

            var gearPage = new TabPage("Gear");
            var gearSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            gearSplit.HandleCreated += (s, e) => { try { gearSplit.SplitterDistance = 230; } catch { } };
            _gearList = new ListBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(28, 28, 32), ForeColor = Color.White, BorderStyle = BorderStyle.None, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 20 };
            _gearList.DrawItem += GearList_DrawItem;
            _gearList.SelectedIndexChanged += (s, e) =>
            {
                var g = _gearList.SelectedItem as GearEntry;
                _gearItemBox.Text = g == null ? "" : (g.Raw ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            };
            _gearItemBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Color.FromArgb(20, 20, 22), ForeColor = Color.FromArgb(220, 220, 220), Font = new Font("Consolas", 8.5f) };
            gearSplit.Panel1.Controls.Add(_gearList);
            gearSplit.Panel2.Controls.Add(_gearItemBox);
            gearPage.Controls.Add(gearSplit);

            var nodesPage = new TabPage("Allocated passives");
            _nodesBox = MakeRichBox();
            nodesPage.Controls.Add(_nodesBox);

            inner.TabPages.Add(summaryPage);
            inner.TabPages.Add(gearPage);
            inner.TabPages.Add(nodesPage);
            split.Panel1.Controls.Add(inner);

            _treeControl = new PassiveTreeControl { Dock = DockStyle.Fill };
            var treeTop = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = UiTheme.Surface, Padding = new Padding(8, 4, 8, 4) };
            treeTop.Controls.Add(new Label { Text = "Saved passive tree; no leveling order is implied.", Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = UiTheme.Muted });
            var showAll = new CheckBox { Text = "Show unallocated", Checked = true, Dock = DockStyle.Bottom, Height = 24, ForeColor = UiTheme.Text };
            showAll.CheckedChanged += (s, e) => _treeControl.ShowUnallocated = showAll.Checked;
            treeTop.Controls.Add(showAll);
            split.Panel2.Controls.Add(_treeControl);
            split.Panel2.Controls.Add(treeTop);
            return split;
        }

        private static RichTextBox MakeRichBox()
        {
            return new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(22, 22, 25), ForeColor = Color.FromArgb(220, 220, 220), BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9f), DetectUrls = false };
        }

        private static void Append(RichTextBox box, string text, Color color, bool bold = false)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = color;
            box.SelectionFont = new Font(box.Font, bold ? FontStyle.Bold : FontStyle.Regular);
            box.AppendText(text);
        }

        private void GearList_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            var g = _gearList.Items[e.Index] as GearEntry;
            if (g == null) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(sel ? UiTheme.Primary : UiTheme.Canvas))
                e.Graphics.FillRectangle(back, e.Bounds);
            string slot = (g.SlotLabel ?? g.SlotName) + (g.InactiveWeaponSet ? " (other weapon set)" : "");
            using (var b = new SolidBrush(UiTheme.Muted))
                e.Graphics.DrawString(slot, _gearList.Font, b, e.Bounds.X + 4, e.Bounds.Y + 3);
            using (var b = new SolidBrush(PobStatParser.GetRarityColor(g.Rarity)))
                e.Graphics.DrawString(g.ItemName ?? "", _gearList.Font, b, e.Bounds.X + 150, e.Bounds.Y + 3);
        }

        // ---------------- Build source handling ----------------

        private void LoadShareCodeFromBox()
        {
            string code = (_shareCodeBox.Text ?? "").Trim();
            if (code.Length == 0)
            {
                MessageBox.Show(this, "Paste a Path of Building code or supported build link for " + _config.GameName + ".", "Share code", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string oldSource = _config.BuildSource, oldCode = _config.ShareCode, oldResolved = _config.ShareCodeResolved;
            _config.BuildSource = AppConfig.SourceShareCode;
            _config.ShareCode = code;
            _config.ShareCodeResolved = null;

            SetBusy(true, PobShareCode.IsUrl(code) ? "Downloading build from link..." : "Decoding share code...");
            string err;
            BuildInfo info;
            try
            {
                using (var guideEngine = new PobLuaEngine())
                {
                    info = guideEngine.Initialize(_config.PobPath, AppConfig.GetWrapperScriptPath(), out err, _config.Game)
                        ? _config.LoadGuideBuild(guideEngine, out err) : null;
                }
            }
            finally { SetBusy(false, null); }

            if (info != null && info.Success)
            {
                _config.Save();
                UpdateSourceLabel();
                ResetComparisonPreview();
                _statusLabel.Text = "Guide loaded. Current character baseline unchanged.";
            }
            else
            {
                _config.BuildSource = oldSource;
                _config.ShareCode = oldCode;
                _config.ShareCodeResolved = oldResolved;
                _config.Save();
                _statusLabel.Text = "Share code failed: " + err;
                MessageBox.Show(this, "Could not load the share code:\n" + err + "\n\nThe previously active build is unchanged.", "Share code", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetBusy(bool busy, string status)
        {
            IsBusy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _loadShareBtn.Enabled = !busy;
            _reloadBuildBtn.Enabled = !busy;
            if (status != null) _statusLabel.Text = status;
            Update();
        }

        public void RefreshBuildDropdown()
        {
            _suppressDropdownEvents = true;
            try
            {
                _buildDropdown.Items.Clear();
                string pobDir = _config.PobPath;
                if (string.IsNullOrEmpty(pobDir) || !Directory.Exists(pobDir)) return;
                string buildDir = _config.BuildDirectory;
                if (!Directory.Exists(buildDir)) return;

                BuildItem selected = null;
                foreach (var f in Directory.GetFiles(buildDir, "*.xml", SearchOption.AllDirectories))
                {
                    var item = new BuildItem(f.Substring(buildDir.Length).TrimStart('\\', '/'), f);
                    _buildDropdown.Items.Add(item);
                    if (string.Equals(f, _config.CurrentBuildFilePath, StringComparison.OrdinalIgnoreCase)) selected = item;
                }
                if (selected != null && string.IsNullOrEmpty(_config.CurrentCharacterCode)) _buildDropdown.SelectedItem = selected;
                else _buildDropdown.SelectedIndex = -1;
            }
            finally
            {
                _suppressDropdownEvents = false;
            }
        }

        private void BuildDropdown_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressDropdownEvents) return;
            var item = _buildDropdown.SelectedItem as BuildItem;
            if (item != null) SwitchToBuild(item.FullPath);
        }

        private void BrowseBuildBtn_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "Path of Building files (*.xml)|*.xml" })
            {
                try { ofd.InitialDirectory = Path.GetDirectoryName(_config.CurrentBuildFilePath); } catch { }
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    SwitchToBuild(ofd.FileName);
                    RefreshBuildDropdown();
                }
            }
        }

        // Selects a PoB2 XML file as the build source. The file is only read, never written.
        public void SwitchToBuild(string fullPath)
        {
            if (!File.Exists(fullPath)) return;
            try
            {
                string error;
                if (!GameIdentity.ValidateBuild(File.ReadAllText(fullPath), _config.Game, out error))
                {
                    MessageBox.Show(this, error + "\nThe Current snapshot is unchanged.", "Build rejected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    RefreshBuildDropdown();
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Cannot read the selected build.", ex);
                MessageBox.Show(this, ex.Message, "Build read failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string oldPath = _config.CurrentBuildFilePath, oldCode = _config.CurrentCharacterCode;
            _config.CurrentBuildFilePath = fullPath;
            _config.CurrentCharacterCode = null;

            SetBusy(true, "Loading build: " + Path.GetFileName(fullPath) + "...");
            string err;
            BuildInfo info;
            try { info = _config.LoadConfiguredBuild(_engine, out err); }
            finally { SetBusy(false, null); }

            if (info != null && info.Success)
            {
                _config.Save();
                ResetComparisonPreview();
                OnBuildLoaded(info, "Loaded build file " + Path.GetFileName(fullPath));
            }
            else
            {
                _config.CurrentBuildFilePath = oldPath;
                _config.CurrentCharacterCode = oldCode;
                _statusLabel.Text = "Failed to load build: " + err;
                OnBuildLoaded(null, null);
                MessageBox.Show(this, "Failed to load build file:\n" + err, "Build error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ReloadActiveBuild(bool showErrors)
        {
            ResetComparisonPreview();
            SetBusy(true, "Loading build...");
            string err;
            BuildInfo info;
            try { info = _config.LoadConfiguredBuild(_engine, out err); }
            finally { SetBusy(false, null); }

            if (info != null && info.Success)
            {
                OnBuildLoaded(info, "Build loaded at " + DateTime.Now.ToString("HH:mm:ss"));
            }
            else
            {
                OnBuildLoaded(null, null);
                _statusLabel.Text = "No build loaded: " + err;
                if (showErrors) MessageBox.Show(this, "Failed to load build:\n" + err, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnBuildLoaded(BuildInfo info, string status)
        {
            UpdateBuildOverview(info);
            UpdateSourceLabel();
            UpdateBaselineKind(info);
            _details = null;
            _detailsStale = true;
            if (info != null && info.Success)
            {
                LoadDetails();
            }
            else
            {
                RenderBuildTab();
            }
            if (status != null) _statusLabel.Text = status;
            if (_onBuildChanged != null) _onBuildChanged();
        }

        private void UpdateSourceLabel()
        {
            string src = _config.GetBuildSourceDisplay();
            _activeSourceLabel.Text = _config.GetCurrentSourceDisplay();
            _guideSourceLabel.Text = "Guide target: " + src;
            _sourceTips.SetToolTip(_activeSourceLabel, _activeSourceLabel.Text);
            _sourceTips.SetToolTip(_guideSourceLabel, _guideSourceLabel.Text);
            _guideBuildView.SetSource(_config);
            if (_tabs.SelectedIndex == 2) _guideBuildView.EnsureLoaded();
            _loadShareBtn.Text = "Load GUIDE code";
            _shareCodeBox.AccessibleDescription = "Desired guide build, never used as Current character gear";
            _statusLabel.Text = "Guide: " + src;
        }

        // Import history does not prove that a saved snapshot still matches live gear.
        private void UpdateBaselineKind(BuildInfo info)
        {
            _lastBuildName = info != null && info.Success ? info.CharacterName : null;
            if (info == null || !info.Success)
            {
                _baselineKindLabel.Text = "-";
                _baselineKindLabel.ForeColor = Color.FromArgb(160, 160, 170);
                return;
            }
            var ii = _engine.GetImportInfo();
            if (ii != null && ii.Success && ii.ImportedCharacter)
            {
                string where = string.Join(" / ", new[] { ii.League, ii.Realm }.Where(x => !string.IsNullOrEmpty(x)).ToArray());
                _baselineKindLabel.Text = "Imported " + _config.GameName + " character (via PoB character import" + (where.Length > 0 ? ", " + where : "") + ").\nGear is as of the last import in PoB.";
                _baselineKindLabel.ForeColor = Color.FromArgb(110, 220, 140);
            }
            else
            {
                _baselineKindLabel.Text = "Manually supplied Current snapshot (not account-verified).\nEnsure it represents your equipped gear, not a guide.";
                _baselineKindLabel.ForeColor = Color.FromArgb(240, 170, 80);
            }
        }

        private const string CharacterImportHelp =
            "To compare against the gear you are wearing right now:\n\n" +
            "1. Open the matching Path of Building > Import/Export Build > Character import.\n" +
            "2. Use PoB's character import for this game and import items + passive tree.\n" +
            "3. Save the build in PoB (Ctrl+S), then press \"Use my character\" here again.\n" +
            "   (Or Generate a code and use 'Import Current character code'. The top code box is GUIDE only.)\n\n" +
            "The companion never asks for your password and stores no login tokens. Direct account import inside the " +
            "companion would need its own OAuth client registered with Grinding Gear Games; PoB's login cannot legitimately be reused by another app.";

        // Switches the baseline to the newest PoB2 build that was imported from a PoE2 character (read-only).
        private void UseImportedCharacter()
        {
            string path = AppConfig.FindLatestImportedCharacterBuild(_config.PobPath, _config.BuildDirectory);
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show(this, "No imported character build was found in this game's PoB build folder.\n\n" + CharacterImportHelp, "Use my character", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var res = MessageBox.Show(this,
                "Use this character import as the comparison baseline?\n\n" + Path.GetFileName(path) +
                "\nLast saved in PoB: " + File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm") +
                "\n\nIf you changed gear since then, re-import the character in PoB and save first.\nThe file is only read, never modified.",
                "Use my character", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (res != DialogResult.Yes) return;
            SwitchToBuild(path);
            RefreshBuildDropdown();
        }

        private void ImportCurrentCode()
        {
            using (var form = new Form { Text = "Import Current character code (not guide)", Size = new Size(640, 280), MinimumSize = new Size(480, 240), Padding = new Padding(12), StartPosition = FormStartPosition.CenterParent })
            {
                var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, MaxLength = 0, ScrollBars = ScrollBars.Vertical };
                var button = new Button { Text = "Use as Current character baseline", Dock = DockStyle.Bottom, Height = 35 };
                form.Controls.Add(box);
                form.Controls.Add(button);
                form.Controls.Add(new Label { Text = "CURRENT snapshot only. Your Guide source and goals remain unchanged.", Dock = DockStyle.Top, Height = 36, ForeColor = UiTheme.Muted });
                box.AccessibleName = "Current character share code";
                UiTheme.Apply(form);
                UiTheme.Emphasize(button);
                form.AcceptButton = button;
                button.Click += (s, e) =>
                {
                    string xml, resolved, description, err;
                    if (!PobShareCode.TryResolve(box.Text.Trim(), _config.Game, out xml, out resolved, out description, out err))
                    { MessageBox.Show(form, err, "Character import failed"); return; }
                    var info = _engine.LoadShareCode(resolved, out err);
                    if (info == null || !info.Success) { MessageBox.Show(form, err, "Character import failed"); return; }
                    _config.CurrentCharacterCode = resolved;
                    _config.CurrentBuildFilePath = null;
                    _config.Save();
                    ResetComparisonPreview();
                    OnBuildLoaded(info, "Current character imported; guide retained.");
                    RefreshBuildDropdown();
                    form.Close();
                };
                form.ShowDialog(this);
            }
        }

        private void LoadDetails()
        {
            string err;
            _details = _engine.GetBuildDetails(out err);
            _detailsStale = false;
            if (_details == null || !_details.Success)
            {
                Logger.Error("GetBuildDetails failed: " + err);
                _details = new BuildDetails { Success = false, Error = err };
            }
            UpdateAssumptionLabel();
            RenderBuildTab();
        }

        private void EnsureBuildDetails()
        {
            if (_detailsStale && _engine.IsBuildLoaded) LoadDetails();
        }

        private void UpdateAssumptionLabel()
        {
            if (_details == null || !_details.Success || _details.Assumptions == null)
            {
                _assumptionVal.Text = "-";
                return;
            }
            var lines = new List<string>();
            foreach (var a in _details.Assumptions) lines.Add(a.Label + ": " + a.Value);
            int cfg = _details.Config != null ? _details.Config.Count : 0;
            lines.Add(cfg + " non-default config option(s) active - see Current character tab.");
            _assumptionVal.Text = string.Join("\n", lines.ToArray());
        }

        private void UpdateBuildOverview(BuildInfo info)
        {
            if (info != null && info.Success)
            {
                _charNameVal.Text = string.IsNullOrEmpty(info.CharacterName) ? "(Unnamed)" : info.CharacterName;
                _classVal.Text = (info.ClassName ?? "Unknown") + (!string.IsNullOrEmpty(info.AscendClassName) ? " (" + info.AscendClassName + ")" : "");
                _levelVal.Text = info.Level > 0 ? info.Level.ToString() : "1";
                _mainSkillVal.Text = string.IsNullOrEmpty(info.MainSkill) ? "None" : info.MainSkill;

                var b = info.Baseline ?? new Dictionary<string, double>();
                Func<string, string, string> v = (k, f) => b.ContainsKey(k) ? b[k].ToString(f) : "unavailable";
                _totalDpsVal.Text = v("TotalDPS", "N1");
                _combinedDpsVal.Text = v("CombinedDPS", "N1");
                _lifeVal.Text = v("Life", "N0");
                _esVal.Text = v("EnergyShield", "N0");
                _ehpVal.Text = v("TotalEHP", "N0");
                _resistsVal.Text = v("FireResist", "N0") + "% / " + v("ColdResist", "N0") + "% / " + v("LightningResist", "N0") + "%";
                var stats = new[] { _totalDpsVal, _combinedDpsVal, _lifeVal, _esVal, _ehpVal };
                var statKeys = new[] { "TotalDPS", "CombinedDPS", "Life", "EnergyShield", "TotalEHP" };
                var colors = new[] { Color.FromArgb(255, 100, 100), Color.FromArgb(255, 120, 120), Color.FromArgb(240, 80, 80), Color.FromArgb(100, 180, 255), Color.FromArgb(255, 200, 80) };
                for (int i = 0; i < stats.Length; i++) stats[i].ForeColor = b.ContainsKey(statKeys[i]) ? colors[i] : UiTheme.Muted;
                foreach (var label in new[] { _charNameVal, _classVal, _mainSkillVal, _resistsVal })
                    _sourceTips.SetToolTip(label, label.Text);
            }
            else
            {
                foreach (var l in new[] { _classVal, _levelVal, _mainSkillVal, _totalDpsVal, _combinedDpsVal, _lifeVal, _esVal, _ehpVal, _resistsVal, _assumptionVal }) l.Text = "-";
                _charNameVal.Text = "(Not loaded)";
            }
        }

        private void RenderBuildTab()
        {
            _summaryBox.Clear();
            _nodesBox.Clear();
            _gearList.Items.Clear();
            _gearItemBox.Text = "";
            var d = _details;
            if (d == null || !d.Success)
            {
                Append(_summaryBox, d == null ? "No Current character loaded. Import its separate file or code." : "Could not read Current character details: " + d.Error, Color.FromArgb(255, 120, 120));
                _treeControl.SetTree(null);
                return;
            }

            var gold = Color.FromArgb(230, 200, 110);
            var dim = Color.FromArgb(150, 150, 160);
            var white = Color.FromArgb(225, 225, 230);

            Append(_summaryBox, (d.BuildName ?? "Build") + "\n", gold, true);
            Append(_summaryBox, string.Format("Level {0} {1}{2}\n", d.Level, d.ClassName, string.IsNullOrEmpty(d.AscendClassName) ? "" : " / " + d.AscendClassName), white);
            Append(_summaryBox, "Main skill: " + d.MainSkill + "\n\n", Color.FromArgb(130, 200, 255));

            Append(_summaryBox, "CALCULATION ASSUMPTIONS\n", gold, true);
            Append(_summaryBox, "Comparisons use the selected PoB's own calculator with exactly these settings from the build.\n", dim);
            foreach (var a in d.Assumptions) { Append(_summaryBox, a.Label + ": ", dim); Append(_summaryBox, a.Value + "\n", white); }

            Append(_summaryBox, "\nCONFIGURATION (non-default options)\n", gold, true);
            if (d.Config.Count == 0) Append(_summaryBox, "All configuration options at the selected PoB's defaults.\n", dim);
            string lastSection = null;
            foreach (var c in d.Config)
            {
                if (c.Section != lastSection) { Append(_summaryBox, (c.Section ?? "General") + "\n", Color.FromArgb(190, 170, 120)); lastSection = c.Section; }
                Append(_summaryBox, "   " + c.Label + ": ", dim);
                Append(_summaryBox, c.Value + "\n", white);
            }

            Append(_summaryBox, "\nBASELINE STATS\n", gold, true);
            foreach (var s in d.Stats)
            {
                Append(_summaryBox, s.Label + ": ", dim);
                Append(_summaryBox, FormatStat(s.Value) + "\n", white);
            }

            foreach (var g in d.Gear) _gearList.Items.Add(g);
            if (_gearList.Items.Count > 0) _gearList.SelectedIndex = 0;

            // Allocated passive list grouped by kind
            int total = 0;
            var groups = new Dictionary<string, List<TreeNode>>();
            string[] order = { "Ascendancy", "Keystone", "Notable", "Socket", "Small passives" };
            foreach (var k in order) groups[k] = new List<TreeNode>();
            foreach (var n in d.Tree.Nodes)
            {
                if (!n.Alloc || n.Type == "ClassStart" || n.Type == "AscendClassStart") continue;
                total++;
                string key = n.Ascendancy ? "Ascendancy" : (n.Type == "Keystone" || n.Type == "Notable" || n.Type == "Socket" ? n.Type : "Small passives");
                groups[key].Add(n);
            }
            Append(_nodesBox, total + " allocated passive points (as saved in the build; order of allocation is not stored).\n\n", dim);
            foreach (var k in order)
            {
                var list = groups[k];
                if (list.Count == 0) continue;
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                Append(_nodesBox, k.ToUpperInvariant() + " (" + list.Count + ")\n", gold, true);
                if (k == "Small passives")
                {
                    var counts = new SortedDictionary<string, int>();
                    foreach (var n in list) { string nm = n.Name ?? "?"; int c; counts.TryGetValue(nm, out c); counts[nm] = c + 1; }
                    foreach (var kv in counts) Append(_nodesBox, "   " + kv.Key + (kv.Value > 1 ? " x" + kv.Value : "") + "\n", white);
                }
                else
                {
                    foreach (var n in list)
                    {
                        Append(_nodesBox, "   " + n.Name + (n.WeaponSet > 0 ? "  [weapon set " + n.WeaponSet + "]" : "") + "\n", white, true);
                        if (n.Stats != null) foreach (var s in n.Stats) Append(_nodesBox, "      " + s + "\n", Color.FromArgb(140, 160, 220));
                    }
                }
                Append(_nodesBox, "\n", white);
            }

            _treeControl.SetTree(d.Tree);
            _summaryBox.SelectionStart = 0;
            _nodesBox.SelectionStart = 0;
        }

        private static string FormatStat(double v)
        {
            if (Math.Abs(v - Math.Round(v)) < 1e-9 || Math.Abs(v) >= 1000) return v.ToString("N0");
            return v.ToString("N2");
        }

        // ---------------- Comparison ----------------

        public void CompareText(string itemText)
        {
            if (string.IsNullOrEmpty(itemText)) return;
            string err;
            var result = GuideComparison.Compare(_config, _engine, itemText, out err);
            RenderComparisonResult(result, err, itemText);
        }

        public void RenderComparisonResult(ComparisonResult result, string error, string itemText = null)
        {
            _comparisonContainer.Controls.Clear();
            int width = Math.Max(300, _comparisonContainer.ClientSize.Width - 30);

            if (result == null || !result.Success)
            {
                _comparisonContainer.Controls.Add(new Label
                {
                    Text = "Comparison error:\n" + (error ?? (result != null ? result.Error : "Unknown error")),
                    ForeColor = Color.FromArgb(255, 100, 100),
                    Location = new Point(12, 12),
                    Size = new Size(width, 100),
                    Font = new Font("Segoe UI", 9.5f)
                });
                return;
            }

            int curY = 8;
            _lastItemText = itemText;
            var itemBanner = new Label { Text = result.ItemName ?? "Unknown Item", ForeColor = PobStatParser.GetRarityColor(result.ItemRarity), Font = new Font("Segoe UI", 10f, FontStyle.Bold), Location = new Point(8, curY), AutoSize = true };
            _comparisonContainer.Controls.Add(itemBanner);
            curY += itemBanner.Height + 2;

            string sub = result.ItemBase ?? result.ItemType ?? "Item";
            if (!string.IsNullOrEmpty(result.ItemRarity)) sub += " (" + result.ItemRarity + ")";
            var subLabel = new Label { Text = sub, ForeColor = Color.FromArgb(160, 160, 160), Font = new Font("Segoe UI", 8.5f), Location = new Point(8, curY), AutoSize = true };
            _comparisonContainer.Controls.Add(subLabel);
            curY += subLabel.Height + 8;

            foreach (var cmp in result.Comparisons)
            {
                var slotHeader = new Panel { Location = new Point(4, curY), Size = new Size(width + 10, 50), BackColor = Color.FromArgb(32, 32, 36) };
                var slotBadge = new Label { Text = "SLOT: " + (cmp.SlotLabel ?? "").ToUpperInvariant(), Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(220, 180, 100), AutoSize = true, Location = new Point(6, 5) };
                slotHeader.Controls.Add(slotBadge);
                slotHeader.Controls.Add(new Label { Text = "Current: " + cmp.ReplacedItemName, Font = new Font("Segoe UI", 8.5f), ForeColor = PobStatParser.GetRarityColor(cmp.ReplacedItemRarity), AutoEllipsis = true, Size = new Size(Math.Max(120, width - 190), 20), Location = new Point(6, 28) });
                if (!string.IsNullOrEmpty(itemText))
                {
                    string slotName = cmp.SlotName;
                    var exportBtn = new Button { Text = "Export as new PoB build...", Size = new Size(175, 22), Location = new Point(slotHeader.Width - 180, 2), Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Color.FromArgb(60, 70, 100), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8f), Cursor = Cursors.Hand };
                    exportBtn.Click += (s, e) => BuildExporter.ExportInteractive(this, _engine, _config, _lastItemText, slotName, _lastBuildName);
                    slotHeader.Controls.Add(exportBtn);
                    exportBtn.BringToFront();
                    string equipLabel = cmp.SlotLabel;
                    var equipBtn = new Button { Text = "Equip into Current build...", Size = new Size(175, 22), Location = new Point(slotHeader.Width - 180, 26), Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Color.FromArgb(110, 70, 40), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8f), Cursor = Cursors.Hand };
                    equipBtn.Click += (s, e) =>
                    {
                        if (SourceEquipper.EquipInteractive(this, _engine, _config, _lastItemText, slotName, equipLabel))
                        {
                            ReloadActiveBuild(false);
                            _statusLabel.Text = "Equipped into Current character build. Reopen it in the matching PoB to see it there.";
                        }
                    };
                    slotHeader.Controls.Add(equipBtn);
                    equipBtn.BringToFront();
                }
                UiTheme.Apply(slotHeader);
                _comparisonContainer.Controls.Add(slotHeader);
                curY += slotHeader.Height + 4;
                var fit = ItemStatRatings.CreateView(cmp, width, 9);
                fit.Location = new Point(8, curY);
                _comparisonContainer.Controls.Add(fit);
                curY += fit.PreferredSize.Height + 6;

                int rendered = 0;
                foreach (string rawLine in cmp.Lines)
                {
                    if (rawLine.StartsWith("Equipping this in") || rawLine.StartsWith("(replacing")) continue;
                    var lineControl = new FormattedLineControl(rawLine, 9) { Location = new Point(8, curY), Width = width };
                    _comparisonContainer.Controls.Add(lineControl);
                    curY += lineControl.Height + 1;
                    rendered++;
                }
                if (rendered == 0)
                {
                    string msg = string.Equals(cmp.ReplacedItemName, result.ItemName, StringComparison.OrdinalIgnoreCase)
                        ? "^7No stat differences (identical to equipped item)"
                        : "^7No stat differences for current build/skills";
                    var c = new FormattedLineControl(msg, 9) { Location = new Point(8, curY), Width = width };
                    _comparisonContainer.Controls.Add(c);
                    curY += c.Height + 1;
                }
                curY += 8;
            }
        }

        private void SettingsBtn_Click(object sender, EventArgs e)
        {
            if (_openSettings != null) _openSettings();
        }

        private void UpdateStatus()
        {
            if (!_engine.IsInitialized) _statusLabel.Text = _config.GameName + " PoB engine not initialized - check Settings.";
            else if (_engine.IsBuildLoaded) _statusLabel.Text = "Ready. Hover an item in game and press " + _config.GetHotkeyDisplayString() + ".";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (_notifyIcon != null)
                    _notifyIcon.ShowBalloonTip(2000, _config.GameName + " Companion", "Still running in the tray. Press " + _config.GetHotkeyDisplayString() + " in game or double-click the tray icon.", ToolTipIcon.Info);
            }
            base.OnFormClosing(e);
        }

        private class BuildItem
        {
            public string FileName { get; private set; }
            public string FullPath { get; private set; }
            public BuildItem(string fileName, string fullPath) { FileName = fileName; FullPath = fullPath; }
            public override string ToString() { return FileName; }
        }
    }
}
