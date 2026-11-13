using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    public class GuideBuildView : UserControl
    {
        private readonly Label _source;
        private readonly RichTextBox _summary;
        private readonly RichTextBox _skills;
        private readonly RichTextBox _passives;
        private readonly ListBox _gear;
        private readonly ListBox _jewels;
        private readonly TextBox _item;
        private readonly PassiveTreeControl _tree;
        private readonly ComboBox _stage;
        private readonly ListView _gemList;
        private readonly Label _gemNote;
        private readonly Label _gameLog;
        private readonly Button _useSuggestion;
        private readonly CheckBox _followGame;
        private readonly Timer _logTimer;
        private GameLogWatcher _watcher;
        private string _suggestedStage;
        private BuildDetails _details;
        private BuildDetails _currentDetails;
        private string _currentError;
        private const string SavedSetsOption = "(Guide's saved sets)";
        private bool _suppressStage;
        private string _loadoutNotice;
        private AppConfig _config;
        private readonly Button _loadBuildFile;
        private readonly TabPage _gemPage;

        // Raised after the user picks another guide stage; comparisons should re-run against it.
        public event EventHandler GuideStageChanged;
        // Raised after the guide source itself changes (e.g. a .build file was loaded here).
        public event EventHandler GuideSourceChanged;
        private readonly Label _stageLabel;
        private string _sourceKey;
        private bool _stale = true;

        public GuideBuildView()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(24, 24, 27);
            var header = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(12, 6, 12, 6), BackColor = UiTheme.Surface };
            var reload = new Button { Text = "Reload guide", Width = 110, Height = 28 };
            var loadBuildFile = _loadBuildFile = new Button { Text = "Load .build guide...", Width = 130, Height = 28 };
            loadBuildFile.Click += (s, e) => PickBuildFile();
            reload.Click += (s, e) => { _stale = true; EnsureLoaded(); };
            _source = new Label { Dock = DockStyle.Fill, ForeColor = Color.LightSkyBlue, Padding = new Padding(8), AutoEllipsis = true };
            var stagePanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, WrapContents = false };
            var stageLabel = _stageLabel = new Label { Text = "Stage (PoB loadout):", ForeColor = UiTheme.Muted, AutoSize = true, Margin = new Padding(0, 9, 8, 0) };
            _stage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 278, Enabled = false, Margin = new Padding(0, 5, 12, 0), AccessibleName = "Guide stage" };
            _stage.SelectedIndexChanged += (s, e) => OnStageSelected();
            stagePanel.Controls.Add(stageLabel);
            stagePanel.Controls.Add(_stage);
            stagePanel.Controls.Add(reload);
            stagePanel.Controls.Add(loadBuildFile);
            header.Controls.Add(_source);
            header.Controls.Add(stagePanel);

            var logPanel = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = UiTheme.Surface };
            _gameLog = new Label { Dock = DockStyle.Fill, ForeColor = Color.Gainsboro, Padding = new Padding(8, 8, 4, 0), AutoEllipsis = true, Text = "Game log: not started." };
            _useSuggestion = new Button { Text = "Use suggested stage", Dock = DockStyle.Right, Width = 140, Enabled = false };
            _useSuggestion.Click += (s, e) => ApplySuggestion();
            _followGame = new CheckBox { Text = "Follow game", Dock = DockStyle.Right, Width = 100, ForeColor = Color.White };
            _followGame.CheckedChanged += (s, e) =>
            {
                if (_config == null || _config.FollowGameStage == _followGame.Checked) return;
                _config.FollowGameStage = _followGame.Checked;
                _config.Save();
                if (_followGame.Checked) ApplySuggestion();
            };
            logPanel.Controls.Add(_gameLog);
            logPanel.Controls.Add(_followGame);
            logPanel.Controls.Add(_useSuggestion);
            _logTimer = new Timer { Interval = 3000 };
            _logTimer.Tick += (s, e) => PollGameLog();

            var tabs = new DarkTabControl { Dock = DockStyle.Fill };
            _summary = TextPage(tabs, "Guide summary");
            var gearPage = new TabPage("Gear & jewels");
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            var lists = new DarkTabControl { Dock = DockStyle.Fill };
            _gear = ItemList(lists, "Equipment");
            _jewels = ItemList(lists, "Jewels");
            _item = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Color.FromArgb(22, 22, 25), ForeColor = Color.White, Font = new Font("Consolas", 9) };
            _gear.SelectedIndexChanged += (s, e) => ShowItem(_gear);
            _jewels.SelectedIndexChanged += (s, e) => ShowItem(_jewels);
            lists.SelectedIndexChanged += (s, e) => ShowItem(lists.SelectedIndex == 0 ? _gear : _jewels);
            split.Panel1.Controls.Add(lists);
            split.Panel2.Controls.Add(_item);
            gearPage.Controls.Add(split);
            tabs.TabPages.Add(gearPage);
            _skills = TextPage(tabs, "Skills");
            _passives = TextPage(tabs, "Allocated passives");
            var treePage = new TabPage("Tree overview");
            _tree = new PassiveTreeControl { Dock = DockStyle.Fill };
            var treeNote = new Label { Text = "Guide tree of the selected stage. No leveling or allocation order is provided.", Dock = DockStyle.Top, Height = 28, ForeColor = Color.White, BackColor = BackColor };
            treePage.Controls.Add(_tree);
            treePage.Controls.Add(treeNote);
            tabs.TabPages.Add(treePage);

            var gemPage = _gemPage = new TabPage("Gem cutting");
            _gemList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, BackColor = Color.FromArgb(22, 22, 25), ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9.5f) };
            _gemList.Columns.Add("Gem", 170);
            _gemList.Columns.Add("Current character", 190);
            _gemList.Columns.Add("Tier", 45);
            _gemList.Columns.Add("Gem cutter tab", 190);
            _gemList.Columns.Add("Uncut gem to use", 380);
            _gemList.Columns.Add("Tags", 260);
            _gemList.Columns.Add("Character level requirements", 310);
            _gemList.Columns.Add("Buy / acquire (quest unlocks)", 700);
            _gemList.ShowItemToolTips = true;
            _gemList.DoubleClick += (s, e) =>
            {
                if (_gemList.SelectedItems.Count == 0) return;
                var acquisition = _gemList.SelectedItems[0].Tag as GemAcquisitionInfo;
                if (acquisition == null) return;
                try { System.Diagnostics.Process.Start(acquisition.SourceUrl); }
                catch (Exception ex) { Logger.Error("Cannot open gem wiki.", ex); MessageBox.Show(this, ex.Message, "Wiki link failed"); }
            };
            _gemNote = new Label { Dock = DockStyle.Top, Height = 44, ForeColor = Color.White, BackColor = BackColor, Padding = new Padding(4) };
            gemPage.Controls.Add(_gemList);
            gemPage.Controls.Add(_gemNote);
            tabs.TabPages.Add(gemPage);
            Controls.Add(tabs);
            Controls.Add(logPanel);
            Controls.Add(header);
            UiTheme.Apply(this);
        }

        private static RichTextBox TextPage(TabControl tabs, string title)
        {
            var page = new TabPage(title);
            var box = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, BackColor = Color.FromArgb(22, 22, 25), ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 10), BorderStyle = BorderStyle.None };
            page.Controls.Add(box);
            tabs.TabPages.Add(page);
            return box;
        }

        private static ListBox ItemList(TabControl tabs, string title)
        {
            var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "SlotLabel", BackColor = Color.FromArgb(28, 28, 32), ForeColor = Color.White };
            list.Format += (s, e) =>
            {
                var g = (GearEntry)e.ListItem;
                e.Value = (g.SlotLabel ?? g.SlotName) + ": " + (g.ItemName ?? "(Empty)") + (g.InactiveWeaponSet ? " (other weapon set)" : "");
            };
            list.FormattingEnabled = true;
            var page = new TabPage(title);
            page.Controls.Add(list);
            tabs.TabPages.Add(page);
            return list;
        }

        private void ShowItem(ListBox list)
        {
            var gear = list.SelectedItem as GearEntry;
            _item.Text = gear == null ? "No item available in this list." :
                string.IsNullOrEmpty(gear.Raw) ? "No item text available for this slot." : gear.Raw.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        public void SetSource(AppConfig config)
        {
            _config = config;
            _loadBuildFile.Enabled = config.Game == GameId.Poe2;
            _gemPage.Text = config.Game == GameId.Poe1 ? "Gem checklist" : "Gem cutting";
            _gemList.Columns[2].Text = config.Game == GameId.Poe1 ? "Level" : "Tier";
            _gemList.Columns[3].Text = config.Game == GameId.Poe1 ? "Equipment slot" : "Gem cutter tab";
            _gemList.Columns[4].Text = config.Game == GameId.Poe1 ? "Guide requirements" : "Uncut gem to use";
            _gemList.Columns[6].Width = config.Game == GameId.Poe1 ? 310 : 0;
            _gemList.Columns[7].Width = config.Game == GameId.Poe1 ? 700 : 0;
            _followGame.Checked = config.FollowGameStage;
            StartGameLog();
            string key = config.Game + "|" + config.PobPath + "|" + config.BuildSource + "|" + config.BuildFilePath + "|" + config.ShareCode + "|" + config.ShareCodeResolved + "|" + config.GetSelectedGuideLoadout()
                + "|" + config.CurrentBuildFilePath + "|" + config.CurrentCharacterCode + "|" + config.GggBuildFilePath;
            if (_sourceKey == key) return;
            _sourceKey = key;
            _stale = true;
            Clear("Guide source changed. Open this tab or reload to read its details.");
        }

        private void OnStageSelected()
        {
            if (_suppressStage || _config == null || _stage.SelectedItem == null) return;
            string name = (string)_stage.SelectedItem;
            _config.SetGuideLoadout(name == SavedSetsOption ? null : name);
            _config.Save();
            _sourceKey = null;
            SetSource(_config);
            EnsureLoaded();
            var handler = GuideStageChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // Loads a GGG Build Planner .build (e.g. exported by Maxroll) as the GUIDE. The file is only read.
        private void PickBuildFile()
        {
            if (_config == null || _config.Game != GameId.Poe2) return;
            using (var dialog = new OpenFileDialog { Title = "Load GGG / Maxroll .build guide", Filter = "PoE2 build planner (*.build)|*.build|All files (*.*)|*.*" })
            {
                string folder = GggGuide.BuildPlannerFolder();
                if (!string.IsNullOrEmpty(_config.GggBuildFilePath) && File.Exists(_config.GggBuildFilePath)) dialog.InitialDirectory = Path.GetDirectoryName(_config.GggBuildFilePath);
                else if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                string error;
                if (GggBuildFile.Load(dialog.FileName, out error) == null)
                {
                    MessageBox.Show(FindForm(), "Not a readable .build file: " + error, "Load .build guide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _config.BuildSource = AppConfig.SourceGggBuild;
                _config.GggBuildFilePath = dialog.FileName;
                _config.SetGuideLoadout(null);
                _config.Save();
            }
            _sourceKey = null;
            SetSource(_config);
            EnsureLoaded();
            var handler = GuideSourceChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void FillStages(BuildDetails d)
        {
            if (_stageLabel != null) _stageLabel.Text = d != null && d.Ggg != null ? "Stage (.build level range):" : "Stage (PoB loadout):";
            _suppressStage = true;
            try
            {
                _stage.Items.Clear();
                if (d == null) { _stage.Enabled = false; return; }
                if (d.ActiveLoadout == null) _stage.Items.Add(SavedSetsOption);
                foreach (var name in d.Loadouts) _stage.Items.Add(name);
                _stage.SelectedItem = d.ActiveLoadout ?? SavedSetsOption;
                _stage.Enabled = d.Loadouts.Count > 0;
            }
            finally { _suppressStage = false; }
        }

        private void Clear(string message)
        {
            _source.Text = "GUIDE / TARGET - " + (_config == null ? "not configured" : _config.GetBuildSourceDisplay());
            _summary.Text = message;
            _skills.Text = "Skills unavailable until a guide is loaded.";
            _passives.Text = "Passive tree unavailable until a guide is loaded.";
            _gear.Items.Clear();
            _jewels.Items.Clear();
            _item.Text = "";
            _tree.SetTree(null);
            _details = null;
            _gemList.Items.Clear();
            _gemNote.Text = "Gem cutting list unavailable until a guide is loaded.";
        }

        public void RenderGems(BuildDetails guide, BuildDetails current, string currentError)
        {
            _gemList.BeginUpdate();
            try
            {
                _gemList.Items.Clear();
                var plan = GemCutting.Plan(guide, current);
                foreach (var e in plan)
                {
                    var item = new ListViewItem(new[] { e.Name + (e.GuideEnabled ? "" : " [disabled in guide]"), e.Status, e.Poe1 ? e.GuideLevel.ToString() : GemCutting.Roman(e.Tier), e.CutterTab, e.UncutGem, e.Tags ?? "",
                        e.Acquisition == null ? "" : e.Acquisition.Requirements, e.Acquisition == null ? "" : e.Acquisition.Where });
                    item.Tag = e.Acquisition;
                    if (e.Acquisition != null) item.ToolTipText = e.Acquisition.Requirements + "\n" + e.Acquisition.Where + "\n" + e.Acquisition.SourceUrl;
                    item.ForeColor = e.NeedsCut ? Color.Gold : e.IsUnderLevel || e.IsUnderQuality ? Color.Orange : e.CurrentLevel.HasValue ? Color.LightGreen : Color.Gainsboro;
                    _gemList.Items.Add(item);
                }
                int missing = plan.Count(p => p.NeedsCut);
                _gemNote.Text = "Gems used by the selected guide stage (" + (_details == null ? "?" : _details.ActiveLoadout ?? "saved sets") + "). Tier/weapon tab come from PoB2 gem data; an uncut gem of level N lists every gem up to tier N. " +
                    (current == null ? "Current character not loaded" + (string.IsNullOrEmpty(currentError) ? "" : ": " + currentError) + "."
                        : missing + " gem(s) missing on your Current character (gold).");
                if (_config != null && _config.Game == GameId.Poe1)
                    _gemNote.Text = "PoE1 skill/support checklist, one row per required gem instance. Levels, quality, variants and equipment groups come from PoB1. Verify socket colours and links; no gem cutting in PoE1. "
                        + (current == null ? "Current character not loaded." : missing + " missing instance(s).") + " " + GemAcquisition.DataNotice;
            }
            finally { _gemList.EndUpdate(); }
        }

        private void StartGameLog()
        {
            string path = GameLog.FindLogPath(_config.GameLogPath, _config.Game);
            if (path == null)
            {
                _watcher = null;
                _logTimer.Stop();
                _gameLog.Text = "Game log: " + _config.GameName + " logs\\Client.txt not found" + (string.IsNullOrWhiteSpace(_config.GameLogPath) ? "" : " at " + _config.GameLogPath) + ". Configure this game's log path.";
                return;
            }
            if (_watcher != null && string.Equals(_watcher.Path, path, StringComparison.OrdinalIgnoreCase)) return;
            _watcher = new GameLogWatcher(path, _config.Game);
            PollGameLog();
            _logTimer.Start();
        }

        private void PollGameLog()
        {
            if (_watcher == null) return;
            string error;
            bool changed = _watcher.Poll(out error);
            if (error != null) { _gameLog.Text = "Game log: cannot read " + _watcher.Path + " (" + error + ")"; return; }
            // Follow mode needs the guide's loadout list even when the Build tab was never opened.
            if (_followGame.Checked && _stale && _details == null && _watcher.State.Phase != GamePhase.Unknown) { EnsureLoaded(); return; }
            if (changed || _gameLog.Text.StartsWith("Game log: not started", StringComparison.Ordinal)) UpdateSuggestion();
        }

        private void UpdateSuggestion()
        {
            if (_watcher == null || _config == null) return;
            var state = _watcher.State;
            string reason = null;
            _suggestedStage = _details == null ? null
                : GameLog.SuggestStage(_details.Loadouts, state, _config.StageMidLevel, _config.StageLateLevel, out reason);
            string current = _details == null ? null : _details.ActiveLoadout;
            _gameLog.Text = "Game log: " + state.Describe() + "  ->  " +
                (_details == null ? "open/reload the guide to suggest a stage"
                    : _suggestedStage == null ? "no suggestion (" + reason + ")"
                    : "suggested stage: " + _suggestedStage + (_suggestedStage == current ? " (selected)" : "") + " [" + reason + "]");
            _useSuggestion.Enabled = _suggestedStage != null && _suggestedStage != current;
            if (_followGame.Checked && _useSuggestion.Enabled) ApplySuggestion();
        }

        private void ApplySuggestion()
        {
            if (_suggestedStage == null || _details == null || _suggestedStage == _details.ActiveLoadout || !_stage.Items.Contains(_suggestedStage)) return;
            _stage.SelectedItem = _suggestedStage;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _logTimer.Dispose();
            base.Dispose(disposing);
        }

        public static BuildDetails ReadGuide(AppConfig config, out string error)
        {
            string notice;
            return ReadGuide(config, out error, out notice);
        }

        public static BuildDetails ReadGuide(AppConfig config, out string error, out string loadoutNotice)
        {
            BuildDetails current;
            string currentError;
            return ReadGuide(config, false, out error, out loadoutNotice, out current, out currentError);
        }

        // Optionally also reads the Current character (in the same engine, after the guide) for the gem cutting list.
        public static BuildDetails ReadGuide(AppConfig config, bool withCurrent, out string error, out string loadoutNotice, out BuildDetails current, out string currentError)
        {
            loadoutNotice = null;
            current = null;
            currentError = null;
            using (var engine = new PobLuaEngine())
            {
                if (!engine.Initialize(config.PobPath, AppConfig.GetWrapperScriptPath(), out error, config.Game)) return null;
                var build = config.LoadGuideBuild(engine, out error);
                if (build == null || !build.Success) return null;
                loadoutNotice = build.LoadoutNotice;
                var guide = engine.GetBuildDetails(out error);
                GggGuide.Decorate(build, guide);
                if (withCurrent && guide != null && guide.Success)
                {
                    var info = config.LoadConfiguredBuild(engine, out currentError);
                    if (info != null && info.Success)
                    {
                        current = engine.GetBuildDetails(out currentError);
                        if (current != null && !current.Success) { currentError = current.Error; current = null; }
                    }
                    else if (currentError == null && info != null) currentError = info.Error;
                }
                return guide;
            }
        }

        public void EnsureLoaded()
        {
            if (!_stale || _config == null) return;
            _stale = false;
            string error;
            BuildDetails details;
            UseWaitCursor = true;
            try { details = ReadGuide(_config, true, out error, out _loadoutNotice, out _currentDetails, out _currentError); }
            finally { UseWaitCursor = false; }
            if (details == null || !details.Success)
            {
                _details = null;
                FillStages(null);
                Clear("Guide details unavailable: " + error + "\nLoad a matching PoB guide code or XML file. Interactive website planner data is not inferred.");
                UpdateSuggestion();
                return;
            }
            Render(details);
        }

        public void Render(BuildDetails d)
        {
            Clear("");
            _details = d;
            FillStages(d);
            RenderGems(d, _currentDetails, _currentError);
            UpdateSuggestion();
            if (d.Ggg != null) { RenderGgg(d); return; }
            var text = new StringBuilder();
            text.AppendLine("GUIDE BUILD (target, not the comparison baseline)");
            text.AppendLine(d.BuildName + " - Level " + d.Level + " " + d.ClassName + " / " + d.AscendClassName);
            text.AppendLine("Main skill: " + (d.MainSkill ?? "Unavailable"));
            text.AppendLine("Stage: " + (d.ActiveLoadout ?? "guide's saved sets (no matching PoB2 loadout)") +
                (d.Loadouts.Count > 0 ? "  (" + d.Loadouts.Count + " PoB2 loadouts: " + string.Join(", ", d.Loadouts) + ")" : "  (no PoB2 loadouts defined)"));
            if (!string.IsNullOrEmpty(_loadoutNotice)) text.AppendLine("NOTE: " + _loadoutNotice);
            text.AppendLine("\nShows the item/skill/tree/config sets of the selected stage, exactly as PoB2's Loadouts dropdown activates them; other weapon-set gear is labeled.");
            text.AppendLine("Guide fit ratings on the Compare tab use this stage's gear. No leveling order or missing website planner data is invented.");
            text.AppendLine("\nCALCULATION ASSUMPTIONS");
            foreach (var a in d.Assumptions) text.AppendLine(a.Label + ": " + a.Value);
            text.AppendLine("\nNON-DEFAULT CONFIGURATION");
            if (d.Config.Count == 0) text.AppendLine("No non-default options reported by PoB2.");
            foreach (var c in d.Config) text.AppendLine(c.Section + " / " + c.Label + ": " + c.Value);
            text.AppendLine("\nGUIDE CALCULATED STATS (not Current character stats)");
            foreach (var stat in d.Stats) text.AppendLine(stat.Label + ": " + stat.Value.ToString("N2"));
            foreach (var gear in d.Gear) (gear.IsJewel ? _jewels : _gear).Items.Add(gear);
            text.AppendLine("\nEquipment slots: " + _gear.Items.Count + "; equipped jewels: " + _jewels.Items.Count);
            if (_jewels.Items.Count == 0) text.AppendLine("No equipped jewels reported in this saved guide.");
            _summary.Text = _config.Game == GameId.Poe1 ? text.ToString().Replace("PoB2", "PoB1") : text.ToString();
            if (_gear.Items.Count > 0) _gear.SelectedIndex = 0;
            if (_jewels.Items.Count > 0) _jewels.SelectedIndex = 0;
            ShowItem(_gear);

            var skills = new StringBuilder("Skill set of the selected stage; names, levels and quality as reported by PoB2.\n\n");
            foreach (var group in d.Skills)
            {
                skills.AppendLine((group.Main ? "[Main] " : "") + (string.IsNullOrEmpty(group.Label) ? "Unlabeled group" : group.Label) + (group.Enabled ? "" : " [Disabled]") + (string.IsNullOrEmpty(group.Slot) ? "" : " / " + group.Slot));
                foreach (var gem in group.Gems)
                    skills.AppendLine("  " + gem.Name + " - Level " + (gem.Level.HasValue ? gem.Level.Value.ToString() : "unavailable") +
                        ", Quality " + (gem.Quality.HasValue ? gem.Quality.Value + "%" : "unavailable") + (gem.Enabled ? "" : " [Disabled]"));
                if (group.Gems.Count == 0) skills.AppendLine("  No gem details available.");
                skills.AppendLine();
            }
            if (d.Skills.Count == 0) skills.AppendLine("No skill groups reported in the saved guide.");
            _skills.Text = _config.Game == GameId.Poe1 ? skills.ToString().Replace("PoB2", "PoB1") : skills.ToString();
            var nodes = d.Tree.Nodes.Where(n => n.Alloc).ToList();
            var passives = new StringBuilder(nodes.Count + " allocated nodes as saved. Allocation/leveling order is unavailable.\n\n");
            foreach (var node in nodes.OrderBy(n => n.Name))
            {
                passives.AppendLine((node.Name ?? "Unnamed node") + " [" + node.Type + "]" + (node.Ascendancy ? " (Ascendancy)" : ""));
                foreach (var stat in node.Stats ?? new System.Collections.Generic.List<string>()) passives.AppendLine("  " + stat);
            }
            if (nodes.Count == 0) passives.AppendLine("No allocated nodes reported.");
            _passives.Text = passives.ToString();
            _tree.SetTree(d.Tree);
        }

        // Guide read from a GGG .build: passives/skills calculated by PoB2, gear only as the file's notes.
        private void RenderGgg(BuildDetails d)
        {
            var g = d.Ggg;
            var f = g.File;
            var text = new StringBuilder();
            text.AppendLine("GUIDE BUILD from a .build file (target, not the comparison baseline)");
            text.AppendLine((string.IsNullOrWhiteSpace(f.Name) ? "Unnamed build" : f.Name) + " - " + g.ClassName + " / " + g.AscendName);
            text.AppendLine("File (read only): " + f.SourcePath);
            if (!string.IsNullOrWhiteSpace(f.Author)) text.AppendLine("Author: " + f.Author);
            if (!string.IsNullOrWhiteSpace(f.Link)) text.AppendLine("Link: " + f.Link);
            text.AppendLine("Stage: " + g.Stage.Name + "  (" + g.Stages.Count + " level ranges in this file: " + string.Join(", ", g.Stages.Select(s => s.Name)) + ")");
            if (!string.IsNullOrEmpty(g.Notice)) text.AppendLine("NOTE: " + g.Notice);
            if (f.UsesSingleLevelValues) text.AppendLine("NOTE: entries with a single level value are treated as active from that level onward.");
            text.AppendLine("\nLIMITATION: " + GggGuide.Limitation);
            text.AppendLine("Gem levels are PoB2's default for the stage's character level. Compare-tab guide fit for this source is a HEURISTIC keyword match against slot notes.");
            if (!string.IsNullOrWhiteSpace(f.Description)) text.AppendLine("\nDESCRIPTION\n" + GggBuildFile.StripMarkup(f.Description));
            text.AppendLine("\nMAPPING");
            text.AppendLine("Passives: " + g.MappedPassives + " of " + g.ActivePassives.Count + " mapped to PoB2 tree nodes; skills/supports: " + g.ActiveSkills.Count + " active skills.");
            if (g.UnmappedPassives.Count > 0) text.AppendLine("Unmapped passive ids (not in this PoB2 tree): " + string.Join(", ", g.UnmappedPassives));
            if (g.UnmappedGems.Count > 0) text.AppendLine("Unmapped gem ids (not in this PoB2 version): " + string.Join(", ", g.UnmappedGems));
            if (g.DroppedPassives.Count > 0)
                text.AppendLine("Not allocated by PoB2 (not connected to the class start at this stage, so PoB2 dropped them): " +
                    string.Join(", ", g.DroppedPassives.Select(id => { string n; return g.Map.PassiveNames.TryGetValue(id, out n) ? n + " (" + id + ")" : id; }).ToArray()));
            if (f.Warnings.Count > 0) { text.AppendLine("\nWARNINGS"); foreach (var w in f.Warnings) text.AppendLine(w); }
            if (f.UnknownFields.Count > 0) { text.AppendLine("\nUNKNOWN FIELDS (preserved, not interpreted)"); foreach (var u in f.UnknownFields) text.AppendLine(u); }
            text.AppendLine("\nPOB2 STATS OF TREE + GEMS ONLY (no gear; not targets for the Current character)");
            foreach (var stat in d.Stats) text.AppendLine(stat.Label + ": " + stat.Value.ToString("N2"));
            foreach (var gear in d.Gear) (gear.IsJewel ? _jewels : _gear).Items.Add(gear);
            text.AppendLine("\nSlot notes at this stage: " + _gear.Items.Count + "; jewel notes: " + _jewels.Items.Count);
            _summary.Text = text.ToString();
            if (_gear.Items.Count > 0) _gear.SelectedIndex = 0;
            if (_jewels.Items.Count > 0) _jewels.SelectedIndex = 0;
            ShowItem(_gear);

            var skills = new StringBuilder("Skills of this stage as loaded into PoB2 (gem levels: PoB2 default for level " + d.Level + "; quality not in .build).\n\n");
            foreach (var group in d.Skills)
            {
                skills.AppendLine(string.IsNullOrEmpty(group.Label) ? "Skill group" : group.Label);
                foreach (var gem in group.Gems)
                    skills.AppendLine("  " + gem.Name + " - Level " + (gem.Level.HasValue ? gem.Level.Value.ToString() : "unavailable"));
                skills.AppendLine();
            }
            if (d.Skills.Count == 0) skills.AppendLine("No mapped skills at this stage.");
            var noted = g.ActiveSkills.Where(s => !string.IsNullOrWhiteSpace(s.Text) || s.Supports.Any(x => !string.IsNullOrWhiteSpace(x.Text))).ToList();
            if (noted.Count > 0)
            {
                skills.AppendLine("GUIDE NOTES");
                foreach (var s in noted)
                {
                    string name = g.Map.GemName(s.Id) ?? s.Id;
                    if (!string.IsNullOrWhiteSpace(s.Text)) skills.AppendLine(name + ": " + s.Text);
                    foreach (var sup in s.Supports.Where(x => !string.IsNullOrWhiteSpace(x.Text)))
                        skills.AppendLine("  " + (g.Map.GemName(sup.Id) ?? sup.Id) + ": " + sup.Text);
                }
            }
            _skills.Text = skills.ToString();

            var nodes = d.Tree.Nodes.Where(n => n.Alloc).ToList();
            var passives = new StringBuilder(nodes.Count + " nodes allocated by PoB2 for this stage. The .build gives no allocation order.\n\n");
            var passiveNotes = g.ActivePassives.Where(p => !string.IsNullOrWhiteSpace(p.Text)).ToList();
            if (passiveNotes.Count > 0)
            {
                passives.AppendLine("GUIDE NOTES");
                foreach (var p in passiveNotes)
                {
                    string name;
                    passives.AppendLine((g.Map.PassiveNames.TryGetValue(p.Id, out name) ? name : p.Id) + ": " + p.Text);
                }
                passives.AppendLine();
            }
            foreach (var node in nodes.OrderBy(n => n.Name))
            {
                passives.AppendLine((node.Name ?? "Unnamed node") + " [" + node.Type + "]" + (node.Ascendancy ? " (Ascendancy)" : ""));
                foreach (var stat in node.Stats ?? new System.Collections.Generic.List<string>()) passives.AppendLine("  " + stat);
            }
            _passives.Text = passives.ToString();
            _tree.SetTree(d.Tree);
        }
    }
}
