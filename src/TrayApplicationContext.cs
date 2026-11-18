using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;

namespace Poe2ItemCompanion
{
    public class TrayApplicationContext : ApplicationContext
    {
        private AppConfig _config;
        private NotifyIcon _notifyIcon;
        private HotkeyManager _hotkeyManager;
        private HotkeyManager _selectorHotkey;
        private OverlaySelector _selector;
        private EquipmentOverlay _equipmentOverlay;
        private PobLuaEngine _engine;
        private ComparisonPopup _activePopup;
        private MainWindow _mainWindow;
        private bool _isComparing = false;
        private OverlayFeature _gemFeature;
        private OverlayFeature _passiveFeature;
        private Timer _detectTimer;
        private bool _checkingUpdates;
        private bool _downloadingUpdate;
        private bool _exiting;

        public TrayApplicationContext(AppConfig config)
        {
            _config = config;
            InitializeApp();
        }

        private void InitializeApp()
        {
            // Initialize Engine
            _engine = new PobLuaEngine();
            string scriptPath = AppConfig.GetWrapperScriptPath();
            string initErr;
            bool engineReady = _engine.Initialize(_config.PobPath, scriptPath, out initErr, _config.Game);

            if (engineReady)
            {
                string buildErr;
                _config.LoadConfiguredBuild(_engine, out buildErr);
            }

            // Create System Tray Icon
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Shield, // standard system shield/gear icon
                Text = _config.GameName + " - PoB bridge",
                Visible = true
            };
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

            UpdateContextMenu();

            // Setup Hotkey
            _hotkeyManager = new HotkeyManager();
            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
            _gemFeature = new OverlayFeature
            {
                Title = "Gem overlay", WindowName = "gem cutting window", SpotPrefix = "spot-", GemCutter = true,
                Create = c => new GemOverlay(c), Spots = c => c.GemDetectSpots,
                GetAutoShow = c => c.GemAutoShow, SetAutoShow = (c, v) => c.GemAutoShow = v,
                MarkModifiers = c => c.GemDetectHotkeyModifiers, MarkKey = c => c.GemDetectHotkeyKey
            };
            _gemFeature.Start(_config, 9003, _notifyIcon, UpdateContextMenu);
            _passiveFeature = new OverlayFeature
            {
                Title = "Passive overlay", WindowName = "passive tree", SpotPrefix = "tree-",
                Create = c => new PassiveOverlay(c), Spots = c => c.TreeDetectSpots,
                GetAutoShow = c => c.PassiveAutoShow, SetAutoShow = (c, v) => c.PassiveAutoShow = v,
                MarkModifiers = c => c.TreeDetectHotkeyModifiers, MarkKey = c => c.TreeDetectHotkeyKey
            };
            _passiveFeature.Start(_config, 9005, _notifyIcon, UpdateContextMenu);
            _selectorHotkey = new HotkeyManager(9007);
            _selectorHotkey.HotkeyPressed += ShowOverlaySelector;
            _detectTimer = new Timer { Interval = 500 };
            _detectTimer.Tick += (s, e) => PollOverlays();
            _detectTimer.Start();
            RegisterConfiguredHotkey();

            // Create and show Main Window on launch
            _mainWindow = new MainWindow(_config, _engine, _hotkeyManager, _notifyIcon, UpdateContextMenu, OpenSettings, SwitchGame);
            _mainWindow.Show();
            if (ReleaseUpdates.IsInstalledBuild)
                _mainWindow.BeginInvoke(new Action(() => CheckForUpdates(false)));
        }

        public void ShowMainWindow()
        {
            EnsureMainWindow();
            _mainWindow.Show();
            _mainWindow.WindowState = FormWindowState.Normal;
            _mainWindow.BringToFront();
            _mainWindow.Activate();
        }

        private void EnsureMainWindow()
        {
            if (_mainWindow == null || _mainWindow.IsDisposed)
            {
                _mainWindow = new MainWindow(_config, _engine, _hotkeyManager, _notifyIcon, UpdateContextMenu, OpenSettings, SwitchGame);
            }
        }

        private void RegisterConfiguredHotkey()
        {
            string err;
            if (!_hotkeyManager.Register(_config.HotkeyModifiers, _config.HotkeyKey, out err))
            {
                MessageBox.Show(
                    "Could not register global hotkey " + _config.GetHotkeyDisplayString() + ":\n" + err + "\n\nPlease select another hotkey in Settings.",
                    "Hotkey Registration Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
            var taken = new System.Collections.Generic.HashSet<int> { (_config.HotkeyModifiers << 16) | _config.HotkeyKey };
            if (_gemFeature != null) _gemFeature.RegisterHotkeys(taken);
            if (_passiveFeature != null) _passiveFeature.RegisterHotkeys(taken);
            if (_selectorHotkey != null)
            {
                _selectorHotkey.Unregister();
                if (_config.OverlaySelectorHotkeyKey != 0)
                {
                    string selectorError = null;
                    if (!taken.Add((_config.OverlaySelectorHotkeyModifiers << 16) | _config.OverlaySelectorHotkeyKey))
                        selectorError = "This shortcut is already used by another companion action.";
                    else
                        _selectorHotkey.Register(_config.OverlaySelectorHotkeyModifiers, _config.OverlaySelectorHotkeyKey, out selectorError);
                    if (selectorError != null)
                    {
                        Logger.Warn("Overlay selector hotkey not registered: " + selectorError);
                        _notifyIcon.ShowBalloonTip(4000, "Overlay selector hotkey",
                            "Could not register " + _config.GetOverlaySelectorHotkeyDisplayString() + ". " + selectorError + " Pick another in Settings.", ToolTipIcon.Warning);
                    }
                }
            }
            UpdateContextMenu();
            NotifySelectorMigration();
        }

        private void NotifySelectorMigration()
        {
            if (!_config.OverlaySelectorJustMigrated) return;
            _config.OverlaySelectorJustMigrated = false;
            string key = _config.OverlaySelectorHotkeyKey == 0 ? "no key (set one in Settings)" : _config.GetOverlaySelectorHotkeyDisplayString();
            Logger.Info("Overlay shortcuts combined into the overlay selector: " + key + ". Previous per-overlay keys are kept in config.json.");
            try { _config.Save(); }
            catch (Exception ex) { Logger.Warn("Could not save the overlay selector migration: " + ex.Message); }
            _notifyIcon.ShowBalloonTip(6000, "Overlay selector",
                "Press " + key + " and pick Gems, Passives, Quests, Recommended equipment or Update character. Compare and detection-spot keys are unchanged.", ToolTipIcon.Info);
        }

        internal List<OverlaySelector.Entry> SelectorEntries()
        {
            return new List<OverlaySelector.Entry>
            {
                new OverlaySelector.Entry { Name = "Gems", Hint = "Guide gems with level and vendor details", IsOpen = () => _gemFeature.IsOpen, Toggle = ToggleGemOverlay },
                new OverlaySelector.Entry { Name = "Passives", Hint = "Next guide passives from your Current tree", IsOpen = () => _passiveFeature.IsOpen, Toggle = TogglePassiveOverlay },
                new OverlaySelector.Entry { Name = "Quests", Hint = "Campaign route and rewards", IsOpen = () => Application.OpenForms.OfType<QuestOverlay>().Any(f => f.Visible), Toggle = ToggleQuestOverlay },
                new OverlaySelector.Entry { Name = "Recommended equipment", Hint = "Selected guide stage gear; right-click an item to search on trade", IsOpen = () => _equipmentOverlay != null && !_equipmentOverlay.IsDisposed && _equipmentOverlay.Visible, Toggle = ToggleEquipmentOverlay },
                new OverlaySelector.Entry
                {
                    Name = "Update character", Hint = "Import the selected PoB character into Current; Guide stays unchanged",
                    Toggle = async () => { ShowMainWindow(); await _mainWindow.ImportViaPob(); }
                },
            };
        }

        public void ShowOverlaySelector()
        {
            if (_selector != null && !_selector.IsDisposed && _selector.Visible) { _selector.CloseAndRestore(); return; }
            EnsureMainWindow();
            _selector = new OverlaySelector(SelectorEntries(), _config.OverlaySelectorHotkeyKey == 0 ? null : _config.GetOverlaySelectorHotkeyDisplayString(), _mainWindow.GuideView);
            _selector.ShowNear(Cursor.Position);
        }

        public void ToggleEquipmentOverlay()
        {
            if (_equipmentOverlay == null || _equipmentOverlay.IsDisposed) _equipmentOverlay = new EquipmentOverlay(_config);
            _equipmentOverlay.Toggle();
        }

        private void UpdateContextMenu()
        {
            var menu = new ContextMenuStrip();
            var games = new ToolStripMenuItem(_config.GameName);
            foreach (GameId game in new[] { GameId.Poe1, GameId.Poe2 })
            {
                GameId choice = game;
                var item = new ToolStripMenuItem(GameIdentity.Name(game)) { Checked = game == _config.Game };
                item.Click += (s, e) => SwitchGame(choice);
                games.DropDownItems.Add(item);
            }
            menu.Items.Add(games);

            var showWindowItem = new ToolStripMenuItem("Open Companion Dashboard");
            showWindowItem.Font = new Font(menu.Font, FontStyle.Bold);
            showWindowItem.Click += (s, e) => ShowMainWindow();
            menu.Items.Add(showWindowItem);

            string buildLabel = _config.GetBuildSourceDisplay();
            if (buildLabel.Length > 60) buildLabel = buildLabel.Substring(0, 57) + "...";
            var buildItem = new ToolStripMenuItem("Guide: " + buildLabel);
            buildItem.Enabled = false;
            menu.Items.Add(buildItem);
            menu.Items.Add(new ToolStripMenuItem(_config.GetCurrentSourceDisplay()) { Enabled = false });

            menu.Items.Add(new ToolStripSeparator());

            var compareItem = new ToolStripMenuItem("Compare Hovered Item (" + _config.GetHotkeyDisplayString() + ")");
            compareItem.Click += (s, e) => TriggerComparison();
            menu.Items.Add(compareItem);

            var testItem = new ToolStripMenuItem("Test Compare (Sample Boots)");
            testItem.Click += (s, e) => TriggerSampleComparison();
            menu.Items.Add(testItem);

            string selectorKey = _config.OverlaySelectorHotkeyKey == 0 ? "no key" : _config.GetOverlaySelectorHotkeyDisplayString();
            var overlays = new ToolStripMenuItem("Overlays (selector: " + selectorKey + ")") { Name = "Overlays" };
            var selectorItem = new ToolStripMenuItem("Open overlay selector...");
            selectorItem.Click += (s, e) => ShowOverlaySelector();
            overlays.DropDownItems.Add(selectorItem);
            overlays.DropDownItems.Add(new ToolStripSeparator());
            if (_gemFeature != null)
                foreach (var entry in SelectorEntries())
                {
                    var choice = entry;
                    string label = choice.Name + (choice.IsOpen == null ? "" : " overlay");
                    var overlayItem = new ToolStripMenuItem(label) { Name = label };
                    overlayItem.Click += (s, e) => choice.Toggle();
                    overlays.DropDownItems.Add(overlayItem);
                }
            menu.Items.Add(overlays);
            AddOverlayMenu(menu, _gemFeature, AppConfig.HotkeyText(_config.GemDetectHotkeyModifiers, _config.GemDetectHotkeyKey), "gem cutter");
            AddOverlayMenu(menu, _passiveFeature, AppConfig.HotkeyText(_config.TreeDetectHotkeyModifiers, _config.TreeDetectHotkeyKey), "passive tree");

            var reloadItem = new ToolStripMenuItem("Reload Current Character");
            reloadItem.Click += (s, e) => ReloadBuild();
            menu.Items.Add(reloadItem);

            menu.Items.Add(new ToolStripSeparator());

            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (s, e) => OpenSettings();
            menu.Items.Add(settingsItem);

            var updatesItem = new ToolStripMenuItem("Check for updates...");
            updatesItem.Click += (s, e) => CheckForUpdates(true);
            menu.Items.Add(updatesItem);

            var logItem = new ToolStripMenuItem("View Log File...");
            logItem.Click += (s, e) => Logger.OpenLogFile();
            menu.Items.Add(logItem);

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) =>
            {
                if (_downloadingUpdate)
                    _notifyIcon.ShowBalloonTip(3000, "Companion update", "Wait for the installer download and verification to finish.", ToolTipIcon.Info);
                else ExitApp();
            };
            menu.Items.Add(exitItem);

            UiTheme.ApplyMenu(menu);
            var previous = _notifyIcon.ContextMenuStrip;
            _notifyIcon.ContextMenuStrip = menu;
            if (previous != null) previous.Dispose();
        }

        private bool UpdateCanCloseApp()
        {
            return !_isComparing && (_mainWindow == null || !_mainWindow.IsBusy)
                && !Application.OpenForms.Cast<Form>().Any(form => form.Modal);
        }

        private async void CheckForUpdates(bool manual)
        {
            const string title = "Companion updates";
            if (_exiting) return;
            if (_checkingUpdates)
            {
                if (manual) _notifyIcon.ShowBalloonTip(3000, title, "An update check or download is already running.", ToolTipIcon.Info);
                return;
            }
            _checkingUpdates = true;
            ReleaseUpdates.Release release = null;
            string stage = null;
            try
            {
                release = await ReleaseUpdates.CheckAsync();
                if (_exiting) return;
                if (release == null)
                {
                    if (manual) MessageBox.Show("No newer stable GitHub release was found.", title);
                    return;
                }
                if (!manual && !UpdateCanCloseApp())
                {
                    _notifyIcon.ShowBalloonTip(5000, title, release.Tag + " is available. Use Check for updates when you're ready.", ToolTipIcon.Info);
                    return;
                }
                if (!ReleaseUpdates.IsInstalledBuild || !release.CanInstall)
                {
                    string reason = !ReleaseUpdates.IsInstalledBuild ? "This checkout won't update itself."
                        : "GitHub did not provide a verifiable installer for this release.";
                    if (MessageBox.Show(release.Tag + " is available. " + reason + "\n\nOpen its official GitHub release page?",
                        title, MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                        using (Process.Start(new ProcessStartInfo(release.Page) { UseShellExecute = true })) { }
                    return;
                }
                if (!UpdateCanCloseApp())
                {
                    MessageBox.Show("Finish the current operation or close Settings before updating.", title);
                    return;
                }
                if (MessageBox.Show("Install " + release.Tag + "?\n\nThe Companion will download and verify Setup, then exit and apply the update. "
                    + "Your settings and Path of Building installations will not be changed by Setup.",
                    title, MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                _notifyIcon.ShowBalloonTip(4000, title, "Downloading " + release.Tag + " in the background.", ToolTipIcon.Info);
                _downloadingUpdate = true;
                stage = await ReleaseUpdates.StageAsync(release);
                if (_exiting) return;
                if (!UpdateCanCloseApp())
                {
                    MessageBox.Show("The installer was verified, but a Companion operation is still open. Finish it and try again.", title);
                    return;
                }
                ReleaseUpdates.LaunchHelper(stage, release);
                stage = null;
                ExitApp();
            }
            catch (Exception ex)
            {
                Logger.Error("Companion update check or download failed.", ex);
                if (!_exiting && (manual || release != null))
                    MessageBox.Show("The update could not be prepared:\n" + ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (stage != null) ReleaseUpdates.Discard(stage, release);
                _downloadingUpdate = false;
                _checkingUpdates = false;
            }
        }

        private void OnHotkeyPressed()
        {
            TriggerComparison();
        }

        public void TriggerComparison()
        {
            if (_isComparing || (_mainWindow != null && _mainWindow.IsBusy)) return;
            if (!GameIdentity.IsForeground(_config))
            {
                _notifyIcon.ShowBalloonTip(3000, "Selected game", "Open " + _config.GameName + " before capturing a hovered item. Use dashboard paste/compare for offline items.", ToolTipIcon.Warning);
                return;
            }
            _isComparing = true;

            try
            {
                // If a popup is currently open, close it (toggle behavior)
                if (_activePopup != null && !_activePopup.IsDisposed && _activePopup.Visible)
                {
                    _activePopup.Close();
                    _activePopup = null;
                }

                // Send Ctrl+C and read clipboard
                string itemText = InputSimulator.CopyHoveredItem(_config);
                if (string.IsNullOrEmpty(itemText))
                {
                    _notifyIcon.ShowBalloonTip(2500, "Item Copy Notice", "No item detected on clipboard after " + _config.GetHotkeyDisplayString() + ".\nIf in-game, ensure the mouse is hovering the item.", ToolTipIcon.Warning);
                    return;
                }

                CompareAndShow(itemText, Cursor.Position);
            }
            finally
            {
                _isComparing = false;
            }
        }

        public void TriggerSampleComparison()
        {
            string sampleItem = @"Item Class: Boots
Rarity: Rare
Storm Tread
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
            CompareAndShow(sampleItem, Cursor.Position);
        }

        private void CompareAndShow(string itemText, Point position)
        {
            if (!_engine.IsInitialized)
            {
                string scriptPath = AppConfig.GetWrapperScriptPath();
                string err;
                if (!_engine.Initialize(_config.PobPath, scriptPath, out err, _config.Game))
                {
                    Logger.Error("PoB Engine failed to initialize in CompareAndShow: " + err);
                    MessageBox.Show("PoB Engine failed to initialize:\n" + err + "\n\nSee log file for details.", "Engine Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            string cmpErr;
            var result = GuideComparison.Compare(_config, _engine, itemText, out cmpErr);

            // Also reflect in dashboard if main window is open
            if (_mainWindow != null && !_mainWindow.IsDisposed && _mainWindow.Visible)
            {
                _mainWindow.RenderComparisonResult(result, cmpErr, itemText);
            }

            if (result == null || !result.Success)
            {
                string warnMsg = cmpErr ?? "Failed to compare item.";
                Logger.Warn("Item comparison could not be shown: " + warnMsg);
                _notifyIcon.ShowBalloonTip(3500, "Comparison Notice", warnMsg + "\nCheck log file for full details.", ToolTipIcon.Warning);
                return;
            }

            if (_activePopup != null && !_activePopup.IsDisposed)
            {
                _activePopup.Close();
            }

            _activePopup = new ComparisonPopup();
            _activePopup.ExportRequested = slot => BuildExporter.ExportInteractive(null, _engine, _config, itemText, slot, null);
            _activePopup.EquipRequested = slot =>
            {
                string label = null;
                foreach (var c in result.Comparisons) if (c.SlotName == slot) label = c.SlotLabel;
                if (SourceEquipper.EquipInteractive(null, _engine, _config, itemText, slot, label)
                    && _mainWindow != null && !_mainWindow.IsDisposed)
                    _mainWindow.ReloadActiveBuild(false);
            };
            _activePopup.DisplayResult(result, _config, position);
        }

        private void ReloadBuild()
        {
            if (_mainWindow != null && _mainWindow.IsBusy) return;
            Logger.Info("Reloading Current character from tray request: " + _config.GetCurrentSourceDisplay());
            string err;
            var info = _config.LoadConfiguredBuild(_engine, out err);
            if (info != null && info.Success)
            {
                _notifyIcon.ShowBalloonTip(2000, "Build Reloaded", string.Format("Level {0} {1}\nMain skill: {2}", info.Level, info.AscendClassName ?? info.ClassName, info.MainSkill), ToolTipIcon.Info);
                if (_mainWindow != null && !_mainWindow.IsDisposed) _mainWindow.ReloadActiveBuild(false);
            }
            else
            {
                Logger.Error("Build reload failed: " + err);
                _notifyIcon.ShowBalloonTip(3000, "Build Reload Failed", (err ?? "Unknown error") + "\nSee log file for details.", ToolTipIcon.Error);
            }
        }

        private void AddOverlayMenu(ContextMenuStrip menu, OverlayFeature feature, string markKey, string window)
        {
            if (feature == null) return;
            if (feature.GemCutter && _config.Game == GameId.Poe1) return;
            string plain = feature.PlainSpotCount > 0 ? ", " + feature.PlainSpotCount + " too plain/ignored - clear and re-mark" : "";
            var auto = new ToolStripMenuItem("Auto-open " + feature.Title.ToLowerInvariant() + " with " + window + " (" + feature.SpotCount + " spot(s)" + plain + "; mark with " + markKey + ")");
            auto.Checked = feature.AutoShow;
            auto.Click += (s, e) => { feature.SetAutoShow(_config, !feature.AutoShow); _config.Save(); UpdateContextMenu(); };
            menu.Items.Add(auto);
            var clear = new ToolStripMenuItem("Clear " + window + " detection spots");
            clear.Enabled = feature.SpotCount + feature.PlainSpotCount > 0;
            clear.Click += (s, e) => feature.ClearSpots();
            menu.Items.Add(clear);
        }

        public void ToggleGemOverlay()
        {
            _gemFeature.Toggle();
        }

        public void TogglePassiveOverlay()
        {
            _passiveFeature.Toggle();
        }

        public void ToggleQuestOverlay()
        {
            EnsureMainWindow();
            _mainWindow.ToggleQuestOverlay();
        }

        private void PollOverlays()
        {
            bool spots = (_gemFeature.SpotCount > 0 && _gemFeature.AutoShow) || (_passiveFeature.SpotCount > 0 && _passiveFeature.AutoShow);
            if (!spots) return;
            bool foreground = _gemFeature.IsPoeForeground();
            if (foreground && spots)
            {
                // The passive tree and the gem cutter are never open together: while the tree is detected
                // (or its overlay was auto-opened) the gem overlay must not auto-open.
                bool? tree = _passiveFeature.Poll(false);
                _gemFeature.Poll(tree == true || _passiveFeature.AutoVisible);
            }
            else { _gemFeature.HideAuto(); _passiveFeature.HideAuto(); }
        }

        private void OpenSettings()
        {
            if (_isComparing || (_mainWindow != null && _mainWindow.IsBusy))
            {
                MessageBox.Show("Finish the current operation before applying settings.", "Settings");
                return;
            }
            var form = new SettingsForm(_config, (newConfig) =>
            {
                ActivateProfile(newConfig);
            });
            form.ShowDialog();
        }

        public void SwitchGame(GameId game)
        {
            if (game == _config.Game) return;
            if (_isComparing || (_mainWindow != null && _mainWindow.IsBusy))
            {
                MessageBox.Show("Finish the current operation before switching games.", "Game switch");
                return;
            }
            AppConfig incoming;
            try { incoming = _config.SelectGame(game); }
            catch (Exception ex)
            {
                Logger.Error("Game switch could not save/load the profile.", ex);
                MessageBox.Show(ex.Message + "\nThe active companion is unchanged.", "Game switch failed");
                return;
            }
            ActivateProfile(incoming);
        }

        private void ActivateProfile(AppConfig incoming)
        {
            _detectTimer.Stop();
            _hotkeyManager.Unregister();
            _selectorHotkey.Unregister();
            if (_selector != null && !_selector.IsDisposed) _selector.Dispose();
            if (_equipmentOverlay != null) { _equipmentOverlay.Dispose(); _equipmentOverlay = null; }
            if (_activePopup != null) { _activePopup.Dispose(); _activePopup = null; }
            if (_mainWindow != null) { _mainWindow.Dispose(); _mainWindow = null; }
            _gemFeature.SetConfig(incoming);
            _passiveFeature.SetConfig(incoming);
            _engine.Dispose();
            _config = incoming;
            _engine = new PobLuaEngine();
            string error;
            if (!_engine.Initialize(_config.PobPath, AppConfig.GetWrapperScriptPath(), out error, _config.Game))
                _notifyIcon.ShowBalloonTip(4000, "Game setup required", error, ToolTipIcon.Warning);
            _notifyIcon.Text = _config.GameName + " - PoB bridge";
            RegisterConfiguredHotkey();
            _detectTimer.Start();
            ShowMainWindow();
        }

        private void ExitApp()
        {
            _exiting = true;
            _notifyIcon.Visible = false;
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_mainWindow != null) _mainWindow.Dispose();
                if (_notifyIcon != null) _notifyIcon.Dispose();
                if (_hotkeyManager != null) _hotkeyManager.Dispose();
                if (_selectorHotkey != null) _selectorHotkey.Dispose();
                if (_equipmentOverlay != null) _equipmentOverlay.Dispose();
                if (_detectTimer != null) _detectTimer.Dispose();
                if (_gemFeature != null) _gemFeature.Dispose();
                if (_passiveFeature != null) _passiveFeature.Dispose();
                if (_engine != null) _engine.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
