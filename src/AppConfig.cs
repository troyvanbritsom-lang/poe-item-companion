using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace Poe2ItemCompanion
{
    public class AppConfig
    {
        public GameId Game { get; set; }
        public string BuildDirectory { get; set; }
        [ScriptIgnore]
        public CompanionProfiles ProfileStore { get; set; }
        [ScriptIgnore]
        public string GameName { get { return GameIdentity.Name(Game); } }
        public string PobPath { get; set; }
        public string GemIconDirectory { get; set; }
        public string BuildFilePath { get; set; } // Legacy config name: GUIDE file only.
        public bool AutoDetectLatestBuild { get; set; }
        public int HotkeyModifiers { get; set; } // 2 = Control, 1 = Alt, 4 = Shift, 8 = Win
        public int HotkeyKey { get; set; }       // Keys enum value, e.g. Keys.D = 68
        public bool AutoReloadBuild { get; set; }
        public int PopupTimeoutSeconds { get; set; }
        public bool PinPopup { get; set; }
        public int PopupFontSize { get; set; }
        public string BuildSource { get; set; }        // GUIDE: "ShareCode", "File" or "GggBuild"
        public string GggBuildFilePath { get; set; }   // GUIDE: GGG/Maxroll .build file (read only)
        public string ShareCode { get; set; }          // GUIDE code or build-site link
        public string ShareCodeResolved { get; set; }  // plain share code (cached so links are not re-downloaded)
        public string CurrentBuildFilePath { get; set; }
        public string CurrentCharacterCode { get; set; }
        public string GuideLoadout { get; set; }        // PoB2 loadout/stage chosen for the guide; empty = guide's saved sets
        public string GuideLoadoutSource { get; set; }  // guide source the loadout was chosen for
        public string GameLogPath { get; set; }         // PoE2 logs\Client.txt; empty = auto-detect
        public bool FollowGameStage { get; set; }       // auto-select the stage suggested from Client.txt
        public int StageMidLevel { get; set; }          // endgame: character level where "Mid" starts
        public int StageLateLevel { get; set; }         // endgame: character level where "Endgame" starts
        public int GemOverlayHotkeyModifiers { get; set; }
        public int GemOverlayHotkeyKey { get; set; }    // 0 = disabled
        public int GemOverlayX { get; set; }
        public int GemOverlayY { get; set; }
        public bool GemOverlayShowAll { get; set; }     // also list gems the Current character already has
        public List<string> ExcludedQuestIds { get; set; }
        public bool QuestOverlayShowExcluded { get; set; }
        public bool QuestOverlayFollowStory { get; set; }
        public int QuestOverlayHotkeyModifiers { get; set; }
        public int QuestOverlayHotkeyKey { get; set; } // 0 = disabled
        public int QuestOverlayX { get; set; }
        public int QuestOverlayY { get; set; }
        public int QuestOverlayWidth { get; set; }
        public int QuestOverlayHeight { get; set; }
        public bool GemAutoShow { get; set; }           // open the overlay when the gem cutting window is detected
        public List<string> GemDetectSpots { get; set; } // "x|y|w|h|file" screen spots of the gem cutting window
        public int GemDetectThreshold { get; set; }     // max mean RGB difference for a spot match
        public int GemDetectHotkeyModifiers { get; set; }
        public int GemDetectHotkeyKey { get; set; }     // marks a detection spot at the mouse; 0 = disabled
        public int PassiveOverlayHotkeyModifiers { get; set; }
        public int PassiveOverlayHotkeyKey { get; set; } // 0 = disabled
        public int PassiveOverlayX { get; set; }
        public int PassiveOverlayY { get; set; }
        public bool PassiveOverlayShowAll { get; set; }
        public bool PassiveAutoShow { get; set; }       // open the passive overlay when the passive tree is detected
        public List<string> TreeDetectSpots { get; set; }
        public int TreeDetectHotkeyModifiers { get; set; }
        public int TreeDetectHotkeyKey { get; set; }    // marks a passive tree detection spot at the mouse; 0 = disabled
        // One global key opens the overlay selector. The legacy per-overlay toggle keys above are kept in
        // config.json (never erased) but are no longer registered; the first load copies one of them here.
        public int OverlaySelectorHotkeyModifiers { get; set; }
        public int OverlaySelectorHotkeyKey { get; set; } // 0 = disabled
        public bool OverlaySelectorMigrated { get; set; }
        public int EquipmentOverlayX { get; set; }
        public int EquipmentOverlayY { get; set; }
        public bool EquipmentOverlayShowSwap { get; set; } // show the guide's weapon swap set instead of the main set
        public string TradeLeague { get; set; }         // league used for official trade searches; empty = ask

        public string GetOverlaySelectorHotkeyDisplayString()
        {
            return HotkeyText(OverlaySelectorHotkeyModifiers, OverlaySelectorHotkeyKey);
        }

        // Picks the selector key from the user's previous overlay toggle keys (gem, then passive, then quest).
        // True only in the session that converted the old per-overlay keys; never saved.
        [ScriptIgnore]
        public bool OverlaySelectorJustMigrated { get; set; }

        public void MigrateOverlaySelector()
        {
            if (OverlaySelectorMigrated) return;
            OverlaySelectorMigrated = true;
            OverlaySelectorJustMigrated = true;
            if (OverlaySelectorHotkeyKey != 0) return;
            var legacy = new[]
            {
                new[] { GemOverlayHotkeyModifiers, GemOverlayHotkeyKey },
                new[] { PassiveOverlayHotkeyModifiers, PassiveOverlayHotkeyKey },
                new[] { QuestOverlayHotkeyModifiers, QuestOverlayHotkeyKey }
            };
            foreach (var pair in legacy)
            {
                if (pair[1] == 0 || pair[0] == 0) continue;
                if (UsedByOtherHotkey(pair[0], pair[1])) continue;
                OverlaySelectorHotkeyModifiers = pair[0];
                OverlaySelectorHotkeyKey = pair[1];
                return;
            }
            bool allDisabled = GemOverlayHotkeyKey == 0 && PassiveOverlayHotkeyKey == 0 && QuestOverlayHotkeyKey == 0;
            if (allDisabled) return; // the user turned every overlay key off; keep the selector off too
            foreach (var key in new[] { Keys.G, Keys.O })
                if (!UsedByOtherHotkey(2, (int)key))
                {
                    OverlaySelectorHotkeyModifiers = 2;
                    OverlaySelectorHotkeyKey = (int)key;
                    return;
                }
        }

        // Compare and detection-spot keys stay separate global hotkeys.
        public bool UsedByOtherHotkey(int modifiers, int key)
        {
            if (key == 0) return false;
            return (key == HotkeyKey && modifiers == HotkeyModifiers) ||
                (key == GemDetectHotkeyKey && modifiers == GemDetectHotkeyModifiers) ||
                (key == TreeDetectHotkeyKey && modifiers == TreeDetectHotkeyModifiers);
        }

        public string GetPassiveOverlayHotkeyDisplayString()
        {
            return HotkeyText(PassiveOverlayHotkeyModifiers, PassiveOverlayHotkeyKey);
        }

        public string GetGemOverlayHotkeyDisplayString()
        {
            return HotkeyText(GemOverlayHotkeyModifiers, GemOverlayHotkeyKey);
        }

        public string GetQuestOverlayHotkeyDisplayString()
        {
            return HotkeyText(QuestOverlayHotkeyModifiers, QuestOverlayHotkeyKey);
        }

        public static string HotkeyText(int modifiers, int key)
        {
            if (key == 0) return "disabled";
            string s = "";
            if ((modifiers & 2) != 0) s += "Ctrl+";
            if ((modifiers & 1) != 0) s += "Alt+";
            if ((modifiers & 4) != 0) s += "Shift+";
            return s + (Keys)key;
        }

        public string GetGuideSourceKey()
        {
            return UsesShareCode ? "code|" + (ShareCode ?? "").Trim() : UsesGggBuild ? "ggg|" + (GggBuildFilePath ?? "") : "file|" + (BuildFilePath ?? "");
        }

        public string GetSelectedGuideLoadout()
        {
            return !string.IsNullOrEmpty(GuideLoadout) && GuideLoadoutSource == GetGuideSourceKey() ? GuideLoadout : null;
        }

        public void SetGuideLoadout(string name)
        {
            GuideLoadout = string.IsNullOrEmpty(name) ? null : name;
            GuideLoadoutSource = GuideLoadout == null ? null : GetGuideSourceKey();
        }

        public AppConfig()
        {
            PobPath = GameIdentity.DefaultPobPath(GameId.Poe2);
            BuildFilePath = null;
            AutoDetectLatestBuild = false;
            HotkeyModifiers = 2; // MOD_CONTROL
            HotkeyKey = (int)Keys.D;
            AutoReloadBuild = true;
            PopupTimeoutSeconds = 0; // 0 = manual close
            PinPopup = false;
            PopupFontSize = 10;
            StageMidLevel = 80;
            StageLateLevel = 90;
            GemOverlayHotkeyModifiers = 2;
            GemOverlayHotkeyKey = (int)Keys.G;
            GemOverlayX = GemOverlayY = -1;
            QuestOverlayX = QuestOverlayY = -1;
            QuestOverlayWidth = 680;
            QuestOverlayHeight = 600;
            QuestOverlayFollowStory = true;
            QuestOverlayHotkeyModifiers = 2;
            QuestOverlayHotkeyKey = (int)Keys.Q;
            ExcludedQuestIds = new List<string>();
            GemAutoShow = true;
            GemDetectSpots = new List<string>();
            GemDetectThreshold = 14;
            GemDetectHotkeyModifiers = 6;
            GemDetectHotkeyKey = (int)Keys.G;
            PassiveOverlayHotkeyModifiers = 2;
            PassiveOverlayHotkeyKey = (int)Keys.P;
            PassiveOverlayX = PassiveOverlayY = -1;
            PassiveAutoShow = true;
            TreeDetectSpots = new List<string>();
            TreeDetectHotkeyModifiers = 6;
            TreeDetectHotkeyKey = (int)Keys.P;
            EquipmentOverlayX = EquipmentOverlayY = -1;
        }

        public static string GetConfigFilePath()
        {
            return Path.Combine(CompanionStorage.DataDirectory, "config.json");
        }

        public static AppConfig Load(string configPath = null, bool validate = true)
        {
            string path = configPath ?? GetConfigFilePath();
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    AppConfig config;
                    if (json.IndexOf("\"SchemaVersion\"", StringComparison.Ordinal) >= 0)
                    {
                        var store = serializer.Deserialize<CompanionProfiles>(json);
                        if (store.SchemaVersion != 1 || !Enum.IsDefined(typeof(GameId), store.ActiveGame)
                            || store.Profiles == null || !store.Profiles.ContainsKey(store.ActiveGame.ToString()))
                            throw new InvalidDataException("Unsupported or incomplete companion profile configuration.");
                        config = store.Profiles[store.ActiveGame.ToString()];
                        if (config.Game != store.ActiveGame) throw new InvalidDataException("Profile game identity mismatch.");
                        foreach (var pair in store.Profiles)
                            if (pair.Value == null || !Enum.IsDefined(typeof(GameId), pair.Value.Game) || pair.Key != pair.Value.Game.ToString())
                                throw new InvalidDataException("Invalid saved game profile.");
                        config.ProfileStore = store;
                    }
                    else
                    {
                        config = serializer.Deserialize<AppConfig>(json);
                        config.Game = GameId.Poe2;
                        config.ProfileStore = new CompanionProfiles { ActiveGame = GameId.Poe2 };
                        File.Copy(path, PobLuaEngine.MakeUniquePath(path + ".legacy-backup"), false);
                    }
                    if (config != null)
                    {
                        if (validate)
                        {
                            config.Validate();
                            if (config.ProfileStore != null && config.ProfileStore.Profiles.Count == 0) config.Save(path);
                        }
                        Logger.Info("Loaded configuration from: " + path);
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Cannot load configuration; original file preserved.", ex);
                    throw;
                }
            }

            var defaultConfig = new AppConfig();
            defaultConfig.ProfileStore = new CompanionProfiles { ActiveGame = GameId.Poe2 };
            if (validate)
            {
                defaultConfig.Validate();
                defaultConfig.Save(path);
                Logger.Info("Created default configuration: " + path);
            }
            return defaultConfig;
        }

        internal static void SaveInstalledPobPath(GameId game, string pobPath)
        {
            if (!CompanionStorage.Installed || !Path.IsPathRooted(pobPath) || GameIdentity.DetectInstallation(pobPath) != game)
                throw new InvalidDataException("Setup did not select a matching Path of Building installation.");
            var active = Load(validate: false);
            AppConfig profile;
            if (active.Game == game) profile = active;
            else if (!active.ProfileStore.Profiles.TryGetValue(game.ToString(), out profile))
            {
                profile = new AppConfig { Game = game };
                active.ProfileStore.Profiles.Add(game.ToString(), profile);
            }
            profile.PobPath = Path.GetFullPath(pobPath);
            active.Save();
        }

        // Set on throwaway (test) copies so they can never overwrite the user's config.json.
        [ScriptIgnore]
        public bool DisableSave { get; set; }

        public void Save(string configPath = null)
        {
            if (DisableSave) return;
            try
            {
                string path = configPath ?? GetConfigFilePath();
                if (configPath == null) Directory.CreateDirectory(CompanionStorage.DataDirectory);
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var store = new CompanionProfiles { ActiveGame = Game };
                if (ProfileStore != null)
                    foreach (var pair in ProfileStore.Profiles) store.Profiles.Add(pair.Key, pair.Value);
                store.Profiles[Game.ToString()] = this;
                string json = serializer.Serialize(store);
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, json);
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                ProfileStore = store;
                Logger.Info("Saved configuration to: " + path);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save config.json: " + ex.Message, ex);
                throw;
            }
        }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(GameId), Game)) throw new InvalidDataException("Unknown selected game.");
            if (GemDetectSpots == null) GemDetectSpots = new List<string>();
            if (TreeDetectSpots == null) TreeDetectSpots = new List<string>();
            if (ExcludedQuestIds == null) ExcludedQuestIds = new List<string>();
            if (GemDetectThreshold <= 0 || GemDetectThreshold > 100) GemDetectThreshold = 14;
            if (StageMidLevel < 1 || StageMidLevel > 100) StageMidLevel = 80;
            if (StageLateLevel <= StageMidLevel || StageLateLevel > 101) StageLateLevel = Math.Min(101, Math.Max(StageMidLevel + 1, 90));
            if (string.IsNullOrEmpty(PobPath) || !Directory.Exists(PobPath))
            {
                if (Directory.Exists(GameIdentity.DefaultPobPath(Game)))
                {
                    PobPath = GameIdentity.DefaultPobPath(Game);
                }
            }
            if (string.IsNullOrEmpty(BuildDirectory)) BuildDirectory = GameIdentity.FindBuildDirectory(PobPath, Game);

            if (AutoDetectLatestBuild || string.IsNullOrEmpty(BuildFilePath) || !File.Exists(BuildFilePath))
            {
                string latest = FindLatestBuild(PobPath, BuildDirectory);
                if (!string.IsNullOrEmpty(latest))
                {
                    string identityError;
                    if (GameIdentity.ValidateBuild(File.ReadAllText(latest), Game, out identityError)) BuildFilePath = latest;
                }
            }

            if (BuildSource != SourceShareCode && BuildSource != SourceFile && BuildSource != SourceGggBuild)
            {
                BuildSource = string.IsNullOrEmpty(ShareCode) ? SourceFile : SourceShareCode;
            }

            if (HotkeyKey == 0)
            {
                HotkeyKey = (int)Keys.D;
                HotkeyModifiers = 2; // Ctrl+D
            }
            MigrateOverlaySelector();
            if (TradeLeague != null) TradeLeague = TradeLeague.Trim();
        }

        public static string FindLatestBuild(string pobDir, string configuredDirectory = null)
        {
            if (string.IsNullOrEmpty(pobDir) || !Directory.Exists(pobDir))
                return null;

            string buildDir = configuredDirectory ?? (File.Exists(Path.Combine(pobDir, "Modules", "Build.lua"))
                ? GameIdentity.FindBuildDirectory(pobDir, GameIdentity.DetectInstallation(pobDir)) : Path.Combine(pobDir, "build"));
            if (!Directory.Exists(buildDir))
                return null;

            try
            {
                var files = Directory.GetFiles(buildDir, "*.xml", SearchOption.AllDirectories);
                if (files.Length == 0) return null;

                string latestFile = null;
                DateTime latestTime = DateTime.MinValue;
                foreach (var f in files)
                {
                    var fi = new FileInfo(f);
                    if (fi.LastWriteTime > latestTime)
                    {
                        latestTime = fi.LastWriteTime;
                        latestFile = f;
                    }
                }
                return latestFile;
            }
            catch
            {
                return null;
            }
        }

        // True if PoB2 recorded a character import in this build XML (<Import lastCharacterHash=...>).
        // PoB2 writes that attribute only after "Import character" from the user's PoE account.
        public static bool XmlHasImportedCharacter(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return false;
            var m = System.Text.RegularExpressions.Regex.Match(xml, "<Import\\b[^>]*>");
            return m.Success && m.Value.IndexOf("lastCharacterHash=", StringComparison.Ordinal) >= 0;
        }

        // Newest PoB2 build file (read-only scan) that was created from a PoE2 character import.
        public static string FindLatestImportedCharacterBuild(string pobDir, string configuredDirectory = null)
        {
            if (string.IsNullOrEmpty(pobDir)) return null;
            string buildDir = configuredDirectory ?? (File.Exists(Path.Combine(pobDir, "Modules", "Build.lua"))
                ? GameIdentity.FindBuildDirectory(pobDir, GameIdentity.DetectInstallation(pobDir)) : Path.Combine(pobDir, "build"));
            if (!Directory.Exists(buildDir)) return null;
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            try
            {
                foreach (var f in Directory.GetFiles(buildDir, "*.xml", SearchOption.AllDirectories))
                {
                    var t = File.GetLastWriteTime(f);
                    if (t <= bestTime) continue;
                    string text;
                    try { text = File.ReadAllText(f); } catch { continue; }
                    if (XmlHasImportedCharacter(text)) { best = f; bestTime = t; }
                }
            }
            catch { }
            return best;
        }

        // Companion-owned folder for exported builds (never inside the PoB2 install).
        public static string GetExportFolder()
        {
            return Path.Combine(CompanionStorage.DataDirectory, "exports");
        }

        public const string SourceShareCode = "ShareCode";
        public const string SourceFile = "File";

        public const string SourceGggBuild = "GggBuild";

        public bool UsesShareCode { get { return BuildSource == SourceShareCode; } }
        public bool UsesGggBuild { get { return BuildSource == SourceGggBuild; } }

        // Guide file whose timestamp matters (XML or .build); null for share codes.
        public string GetGuideFilePath()
        {
            return UsesShareCode ? null : UsesGggBuild ? GggBuildFilePath : BuildFilePath;
        }

        public string GetBuildSourceDisplay()
        {
            string stage = GetSelectedGuideLoadout();
            return GetGuideSourceDisplay() + (stage == null ? "" : " - stage: " + stage);
        }

        private string GetGuideSourceDisplay()
        {
            if (UsesShareCode)
            {
                if (string.IsNullOrEmpty(ShareCode)) return "Share code (none entered)";
                return PobShareCode.IsUrl(ShareCode) ? "Share link: " + ShareCode : "Share code (" + ShareCode.Trim().Length + " chars)";
            }
            if (UsesGggBuild) return string.IsNullOrEmpty(GggBuildFilePath) ? "(No .build file)" : ".build file: " + Path.GetFileName(GggBuildFilePath);
            return string.IsNullOrEmpty(BuildFilePath) ? "(No build file)" : "File: " + Path.GetFileName(BuildFilePath);
        }

        public BuildInfo LoadConfiguredBuild(PobLuaEngine engine, out string error, bool forComparison = false)
        {
            error = null;
            if (engine.Game != Game) { error = "The calculation engine does not match the selected game."; return null; }
            string key = !string.IsNullOrWhiteSpace(CurrentCharacterCode) ? CurrentCharacterCode : CurrentBuildFilePath;
            if (forComparison && !AutoReloadBuild && engine.IsBuildLoaded && !string.IsNullOrEmpty(key)
                && engine.CurrentCharacterSourceKey == key && engine.CurrentCharacterInfo != null)
                return engine.CurrentCharacterInfo;
            BuildInfo info;
            if (!string.IsNullOrWhiteSpace(CurrentCharacterCode))
                info = engine.LoadShareCode(CurrentCharacterCode, out error);
            else if (string.IsNullOrWhiteSpace(CurrentBuildFilePath) || !File.Exists(CurrentBuildFilePath))
            {
                error = "Import a separate CURRENT character file or code. The guide is a target, not Current character gear.";
                return null;
            }
            else info = engine.LoadBuildFile(CurrentBuildFilePath, out error);
            if (info != null && info.Success)
            {
                engine.CurrentCharacterSourceKey = key;
                engine.CurrentCharacterInfo = info;
            }
            return info;
        }

        public string GetCurrentSourceDisplay()
        {
            return !string.IsNullOrWhiteSpace(CurrentCharacterCode) ? "Current character: imported code"
                : "Current character: " + (string.IsNullOrEmpty(CurrentBuildFilePath) ? "REQUIRED" : Path.GetFileName(CurrentBuildFilePath));
        }

        // The legacy source fields now describe the guide only.
        // A link is only downloaded once; the resulting share code is cached in ShareCodeResolved.
        public BuildInfo LoadGuideBuild(PobLuaEngine engine, out string error)
        {
            error = null;
            if (engine.Game != Game) { error = "The guide engine does not match the selected game."; return null; }
            if (UsesGggBuild && Game != GameId.Poe2) { error = ".build guides are supported for Path of Exile 2 only."; return null; }
            if (UsesGggBuild) return GggGuide.Load(this, engine, out error);
            if (UsesShareCode)
            {
                string input = !string.IsNullOrEmpty(ShareCodeResolved) ? ShareCodeResolved : ShareCode;
                if (string.IsNullOrWhiteSpace(input))
                {
                    error = "No GUIDE code configured. Paste one in the GUIDE code / link box.";
                    return null;
                }
                string xml, resolved, desc;
                if (!PobShareCode.TryResolve(input, Game, out xml, out resolved, out desc, out error))
                {
                    return null;
                }
                if (resolved != ShareCodeResolved)
                {
                    ShareCodeResolved = resolved;
                    Save();
                }
                return ApplyGuideLoadout(engine, engine.LoadShareCode(resolved, out error), ref error);
            }
            return ApplyGuideLoadout(engine, engine.LoadBuildFile(BuildFilePath, out error), ref error);
        }

        // The guide opens on its saved sets, like PoB2. A stage chosen in the Build tab is then activated
        // with PoB2's own loadout logic. A stage missing from the guide falls back to the saved sets.
        private BuildInfo ApplyGuideLoadout(PobLuaEngine engine, BuildInfo info, ref string error)
        {
            string loadout = GetSelectedGuideLoadout();
            if (info == null || !info.Success || loadout == null) return info;
            string loadoutError;
            var switched = engine.SetLoadout(loadout, out loadoutError);
            if (switched != null && switched.Success) return switched;
            Logger.Warn("Guide stage '" + loadout + "' unavailable: " + loadoutError);
            string reloadError;
            var fallback = UsesShareCode ? engine.LoadShareCode(ShareCodeResolved, out reloadError) : engine.LoadBuildFile(BuildFilePath, out reloadError);
            if (fallback == null || !fallback.Success) { error = reloadError; return fallback; }
            fallback.LoadoutNotice = "Selected stage '" + loadout + "' unavailable (" + loadoutError + "); using the guide's saved sets.";
            return fallback;
        }

        public AppConfig Clone()
        {
            var copy = (AppConfig)MemberwiseClone();
            copy.GemDetectSpots = new List<string>(GemDetectSpots ?? new List<string>());
            copy.TreeDetectSpots = new List<string>(TreeDetectSpots ?? new List<string>());
            return copy;
        }

        public AppConfig SelectGame(GameId game, string configPath = null)
        {
            if (!Enum.IsDefined(typeof(GameId), game)) throw new InvalidDataException("Unknown selected game.");
            Save(configPath);
            AppConfig incoming;
            if (!ProfileStore.Profiles.TryGetValue(game.ToString(), out incoming))
            {
                incoming = new AppConfig { Game = game, PobPath = GameIdentity.DefaultPobPath(game),
                    BuildFilePath = null, GemAutoShow = game == GameId.Poe2 };
            }
            else incoming = incoming.Clone();
            incoming.ProfileStore = ProfileStore;
            incoming.Validate();
            incoming.Save(configPath);
            return incoming;
        }

        public string GetHotkeyDisplayString()
        {
            string s = "";
            if ((HotkeyModifiers & 2) != 0) s += "Ctrl+";
            if ((HotkeyModifiers & 1) != 0) s += "Alt+";
            if ((HotkeyModifiers & 4) != 0) s += "Shift+";
            if ((HotkeyModifiers & 8) != 0) s += "Win+";
            s += ((Keys)HotkeyKey).ToString();
            return s;
        }

        public static string GetWrapperScriptPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string p1 = Path.Combine(baseDir, "scripts", "pob_wrapper.lua");
            if (File.Exists(p1)) return p1;
            string p2 = Path.Combine(baseDir, "..", "scripts", "pob_wrapper.lua");
            if (File.Exists(p2)) return Path.GetFullPath(p2);
            string p3 = Path.Combine(baseDir, "pob_wrapper.lua");
            if (File.Exists(p3)) return p3;
            return p1;
        }
    }
}
