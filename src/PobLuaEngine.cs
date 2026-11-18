using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Security.Cryptography;

namespace Poe2ItemCompanion
{
    public class LabelValue
    {
        public string Section { get; set; }
        public string Label { get; set; }
        public string Value { get; set; }
    }

    public class StatValue
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public double Value { get; set; }
    }

    public class GearEntry
    {
        public string SlotName { get; set; }
        public string SlotLabel { get; set; }
        public string ItemName { get; set; }
        public string Rarity { get; set; }
        public string BaseName { get; set; }
        public string Raw { get; set; }
        public List<ItemAffix> Affixes { get; set; }
        public bool InactiveWeaponSet { get; set; }
        public bool IsJewel { get; set; }
        public List<string> Priorities { get; set; }   // .build slot note stat priority lines
        public bool FromNotes { get; set; }             // .build note, not a real item
        public int? ItemLevel { get; set; }
        public int? LevelRequirement { get; set; }
        public int? Quality { get; set; }
        public int SocketCount { get; set; }
        public int MaxLinks { get; set; }
        public bool Corrupted { get; set; }
        public bool Mirrored { get; set; }
        public List<string> Influences { get; set; }
        public string ItemType { get; set; }
    }

    public class SkillGem
    {
        public int? RequiredLevel { get; set; }
        public int? TargetRequiredLevel { get; set; }
        public string Id { get; set; }
        public string Variant { get; set; }
        public string Name { get; set; }
        public int? Level { get; set; }
        public int? Quality { get; set; }
        public bool Enabled { get; set; }
        public int? Tier { get; set; }
        public string Kind { get; set; }               // Skill, Support or Spirit (PoB2 gem data)
        public int Color { get; set; }                // 1 strength, 2 dexterity, 3 intelligence, 0 unknown
        public string GemType { get; set; }
        public string WeaponRequirements { get; set; }
        public string Tags { get; set; }
    }

    public class SkillGroup
    {
        public string Label { get; set; }
        public string Slot { get; set; }
        public bool Enabled { get; set; }
        public bool Main { get; set; }
        public List<SkillGem> Gems { get; set; }
    }

    public class TreeNode
    {
        public int? MasteryEffect { get; set; }
        public string GraphContext { get; set; }
        public int Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public bool Alloc { get; set; }
        public bool Ascendancy { get; set; }
        public int WeaponSet { get; set; }
        public List<string> Stats { get; set; }
    }

    public class TreeData
    {
        public List<TreeNode> Nodes { get; set; }
        public List<List<int>> Edges { get; set; }

        public TreeData()
        {
            Nodes = new List<TreeNode>();
            Edges = new List<List<int>>();
        }
    }

    public class BuildDetails
    {
        public string Game { get; set; }
        public string TreeVersion { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public string BuildName { get; set; }
        public int Level { get; set; }
        public string ClassName { get; set; }
        public string AscendClassName { get; set; }
        public string MainSkill { get; set; }
        public List<LabelValue> Assumptions { get; set; }
        public List<LabelValue> Config { get; set; }
        public List<StatValue> Stats { get; set; }
        public List<GearEntry> Gear { get; set; }
        public TreeData Tree { get; set; }
        public List<SkillGroup> Skills { get; set; }
        public List<string> Loadouts { get; set; }
        public string ActiveLoadout { get; set; }
        [System.Web.Script.Serialization.ScriptIgnore]
        public GggGuideInfo Ggg { get; set; }          // set when the guide came from a GGG .build file

        public BuildDetails()
        {
            Loadouts = new List<string>();
            Assumptions = new List<LabelValue>();
            Config = new List<LabelValue>();
            Stats = new List<StatValue>();
            Gear = new List<GearEntry>();
            Tree = new TreeData();
            Skills = new List<SkillGroup>();
        }
    }

    public class BuildInfo
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public bool Cached { get; set; }
        public string CharacterName { get; set; }
        public int Level { get; set; }
        public string ClassName { get; set; }
        public string AscendClassName { get; set; }
        public string MainSkill { get; set; }
        public Dictionary<string, double> Baseline { get; set; }
        public Dictionary<string, double> GuideTargets { get; set; }
        public string ActiveLoadout { get; set; }
        public string LoadoutNotice { get; set; }
        [System.Web.Script.Serialization.ScriptIgnore]
        public GggGuideInfo Ggg { get; set; }
    }

    public class SlotComparison
    {
        public string CompatibilityNotice { get; set; }
        public string SlotName { get; set; }
        public string SlotLabel { get; set; }
        public string ReplacedItemName { get; set; }
        public string ReplacedItemRarity { get; set; }
        public List<string> Lines { get; set; }
        public Dictionary<string, double> Before { get; set; }
        public Dictionary<string, double> After { get; set; }
        public string GuideFit { get; set; }
        public string GuideItemName { get; set; }
        public string GuideStage { get; set; }
        public string ItemRatingNotice { get; set; }
        public List<ItemStatRating> ItemStats { get; set; }

        public SlotComparison()
        {
            Lines = new List<string>();
        }
    }

    public class ExportResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string Xml { get; set; }
        public string ItemName { get; set; }
        public string SlotName { get; set; }
        public string SlotLabel { get; set; }
        public string ReplacedItemName { get; set; }
        public bool Validated { get; set; }
        public int ValidatedLevel { get; set; }
        public double ValidatedLife { get; set; }
        public double ValidatedTotalDPS { get; set; }
    }

    public class ImportInfo
    {
        public bool Success { get; set; }
        public bool ImportedCharacter { get; set; }
        public string Realm { get; set; }
        public string League { get; set; }
        public string ImportLink { get; set; }
    }

    public class ComparisonResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string ItemName { get; set; }
        public string ItemRarity { get; set; }
        public string ItemBase { get; set; }
        public string ItemType { get; set; }
        public List<ItemAffix> Affixes { get; set; }
        public List<SlotComparison> Comparisons { get; set; }

        public ComparisonResult()
        {
            Comparisons = new List<SlotComparison>();
        }
    }

    public class PobLuaEngine : IDisposable
    {
        private static readonly object RuntimeLock = new object();
        private static string _runtimeHash;
        public GameId Game { get; private set; }
        internal string CurrentCharacterSourceKey { get; set; }
        internal BuildInfo CurrentCharacterInfo { get; set; }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr luaL_newstate();

        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void luaL_openlibs(IntPtr L);

        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lua_close(IntPtr L);

        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int luaL_loadfile(IntPtr L, string filename);

        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int luaL_loadstring(IntPtr L, string s);

        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lua_pcall(IntPtr L, int nargs, int nresults, int errfunc);

        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lua_tolstring(IntPtr L, int idx, out UIntPtr len);

        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lua_gettop(IntPtr L);

        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lua_settop(IntPtr L, int idx);

        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lua_pushlstring(IntPtr L, byte[] s, UIntPtr len);

        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lua_pushnil(IntPtr L);

        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lua_setfield(IntPtr L, int idx, string k);

        private const int LUA_GLOBALSINDEX = -10002;

        private IntPtr _L = IntPtr.Zero;
        private readonly object _syncLock = new object();
        private bool _initialized = false;
        private bool _currentBuildLoaded = false;
        private string _pobPath;
        private string _currentBuildPath;
        private string _currentSourceDescription;
        private DateTime _currentBuildLastWriteTime = DateTime.MinValue;
        private JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public bool IsInitialized { get { return _initialized; } }
        public bool IsBuildLoaded { get { return _currentBuildLoaded; } }
        public string CurrentBuildPath { get { return _currentBuildPath; } }
        public string CurrentSourceDescription { get { return _currentSourceDescription; } }

        public bool Initialize(string pobPath, string wrapperScriptPath, out string error, GameId? expectedGame = null)
        {
            error = null;
            lock (RuntimeLock)
            lock (_syncLock)
            {
                if (_initialized)
                {
                    if ((expectedGame.HasValue && expectedGame.Value != Game) || !string.Equals(Path.GetFullPath(pobPath), Path.GetFullPath(_pobPath), StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Create a new engine when changing the selected game or installation.";
                        Logger.Warn(error);
                        return false;
                    }
                    return true;
                }

                Logger.Info("Initializing PoB Lua Engine with PoB path: " + pobPath);
                Logger.Info("Wrapper script path: " + wrapperScriptPath);

                _pobPath = pobPath;
                if (!Directory.Exists(_pobPath))
                {
                    error = "Path of Building directory not found: " + _pobPath;
                    Logger.Error(error);
                    return false;
                }

                if (!File.Exists(wrapperScriptPath))
                {
                    error = "Wrapper script not found: " + wrapperScriptPath;
                    Logger.Error(error);
                    return false;
                }

                string luaDll = Path.Combine(_pobPath, "lua51.dll");
                if (!File.Exists(luaDll))
                {
                    error = "lua51.dll not found in PoB directory: " + luaDll;
                    Logger.Error(error);
                    return false;
                }

                string origCwd = Directory.GetCurrentDirectory();
                try
                {
                    Game = GameIdentity.DetectInstallation(pobPath);
                    if (expectedGame.HasValue && Game != expectedGame.Value)
                        throw new InvalidDataException("The selected installation belongs to " + GameIdentity.Name(Game) + ", not " + GameIdentity.Name(expectedGame.Value) + ".");
                    var fingerprints = new List<string>();
                    foreach (string native in new[] { "lua51.dll", "lua-utf8.dll", "lcurl.dll", "lzip.dll", "socket.dll", "zlib1.dll" })
                        using (var hash = SHA256.Create())
                        using (var stream = File.OpenRead(Path.Combine(_pobPath, native)))
                            fingerprints.Add(BitConverter.ToString(hash.ComputeHash(stream)));
                    string runtimeHash = string.Join("|", fingerprints);
                    if (_runtimeHash != null && _runtimeHash != runtimeHash)
                        throw new InvalidDataException("This PoB installation uses an incompatible Lua runtime. Restart the companion with this game selected.");
                    Directory.SetCurrentDirectory(_pobPath);
                    SetDllDirectory(_pobPath);

                    _L = luaL_newstate();
                    _runtimeHash = runtimeHash;
                    if (_L == IntPtr.Zero)
                    {
                        error = "Failed to create LuaJIT state via lua51.dll";
                        Logger.Error(error);
                        return false;
                    }

                    luaL_openlibs(_L);

                    // Load wrapper script
                    int res = luaL_loadfile(_L, wrapperScriptPath);
                    if (res != 0)
                    {
                        error = "Failed to load wrapper script: " + GetLuaError();
                        Logger.Error(error);
                        Cleanup();
                        return false;
                    }

                    res = lua_pcall(_L, 0, 1, 0);
                    if (res != 0)
                    {
                        error = "Failed to run wrapper script: " + GetLuaError();
                        Logger.Error(error);
                        Cleanup();
                        return false;
                    }

                    // Call pobCompanion.init(pobPath)
                    string sanitizedPobPath = _pobPath.Replace("\\", "/");
                    string initCode = string.Format("return pobCompanion.init([[{0}]], '{1}')", sanitizedPobPath, Game == GameId.Poe1 ? "poe1" : "poe2");
                    res = luaL_loadstring(_L, initCode);
                    if (res != 0)
                    {
                        error = "Failed to load init code: " + GetLuaError();
                        Logger.Error(error);
                        Cleanup();
                        return false;
                    }

                    res = lua_pcall(_L, 0, 1, 0);
                    if (res != 0)
                    {
                        error = "Failed to initialize PoB modules: " + GetLuaError();
                        Logger.Error(error);
                        Cleanup();
                        return false;
                    }

                    _initialized = true;
                    Logger.Info("PoB Lua Engine successfully initialized and ready.");
                    return true;
                }
                catch (Exception ex)
                {
                    error = "Exception during Lua initialization: " + ex.Message;
                    Logger.Error(error, ex);
                    Cleanup();
                    return false;
                }
                finally
                {
                    Directory.SetCurrentDirectory(origCwd);
                }
            }
        }

        // Read-only: the build file is only ever read, never written.
        public BuildInfo LoadBuildFile(string buildPath, out string error)
        {
            error = null;
            if (!File.Exists(buildPath))
            {
                error = "Build file does not exist: " + buildPath;
                Logger.Error(error);
                return null;
            }

            Logger.Info("Loading " + GameIdentity.Name(Game) + " PoB build file (read-only): " + buildPath);

            string xmlText;
            try
            {
                xmlText = File.ReadAllText(buildPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = "Failed to read build file: " + ex.Message;
                Logger.Error(error, ex);
                return null;
            }

            string buildName = Path.GetFileNameWithoutExtension(buildPath);
            var info = LoadBuildXml(xmlText, buildName, out error);
            if (info != null && info.Success)
            {
                _currentBuildPath = buildPath;
                _currentBuildLastWriteTime = File.GetLastWriteTime(buildPath);
                _currentSourceDescription = "File: " + buildPath;
            }
            return info;
        }

        public BuildInfo LoadShareCode(string codeOrUrl, out string error)
        {
            error = null;
            string xml;
            string source;
            string resolved;
            if (!PobShareCode.TryResolve(codeOrUrl, Game, out xml, out resolved, out source, out error))
            {
                Logger.Error("Share code could not be resolved: " + error);
                return null;
            }
            var info = LoadBuildXml(xml, "Share code build", out error);
            if (info != null && info.Success)
            {
                _currentBuildPath = null;
                _currentBuildLastWriteTime = DateTime.MinValue;
                _currentSourceDescription = source;
            }
            return info;
        }

        public bool CheckAndReloadIfModified(out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(_currentBuildPath) || !File.Exists(_currentBuildPath))
            {
                return false;
            }

            DateTime lastWrite = File.GetLastWriteTime(_currentBuildPath);
            if (lastWrite > _currentBuildLastWriteTime)
            {
                Logger.Info("Detected change in build file on disk. Reloading: " + _currentBuildPath);
                var info = LoadBuildFile(_currentBuildPath, out error);
                return info != null && info.Success;
            }
            return false;
        }

        public BuildInfo LoadBuildXml(string xmlText, string buildName, out string error)
        {
            if (!GameIdentity.ValidateBuild(xmlText, Game, out error)) return null;
            CurrentCharacterSourceKey = null;
            CurrentCharacterInfo = null;
            var globals = new Dictionary<string, string>();
            globals["buildXml"] = xmlText;
            globals["buildName"] = buildName ?? "ActiveBuild";
            string jsonStr = CallWrapper("return pobCompanion.loadBuild(buildXml, buildName)", globals, out error);
            if (jsonStr == null) return null;
            try
            {
                var buildInfo = _json.Deserialize<BuildInfo>(jsonStr);
                if (!buildInfo.Success)
                {
                    _currentBuildLoaded = false;
                    error = buildInfo.Error ?? "Unknown error loading build";
                    Logger.Error(error);
                }
                else
                {
                    _currentBuildLoaded = true;
                    Logger.Info(string.Format("Build '{0}' loaded. Level: {1}, Class: {2}/{3}, Skill: {4}",
                        buildInfo.CharacterName, buildInfo.Level, buildInfo.ClassName, buildInfo.AscendClassName, buildInfo.MainSkill));
                }
                return buildInfo;
            }
            catch (Exception ex)
            {
                error = "Exception reading build result: " + ex.Message;
                Logger.Error(error, ex);
                return null;
            }
        }

        public ComparisonResult CompareItem(string itemRawText, out string error)
        {
            error = null;
            lock (RuntimeLock)
            lock (_syncLock)
            {
                string reloadErr;
                CheckAndReloadIfModified(out reloadErr);

                var globals = new Dictionary<string, string>();
                globals["candidateItemText"] = (itemRawText ?? "").Replace("\r\n", "\n");
                string jsonStr = CallWrapper("return pobCompanion.compareItem(candidateItemText)", globals, out error);
                if (jsonStr == null) return null;
                try
                {
                    var result = _json.Deserialize<ComparisonResult>(jsonStr);
                    if (!result.Success)
                    {
                        error = result.Error ?? "Unknown comparison error";
                        Logger.Warn("Comparison failed: " + error + Environment.NewLine + "Candidate Item Raw Text: " + Environment.NewLine + itemRawText);
                    }
                    else
                    {
                        int slotsCount = result.Comparisons != null ? result.Comparisons.Count : 0;
                        Logger.Info(string.Format("Compared item '{0}' across {1} slot(s).", result.ItemName, slotsCount));
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    error = "Exception reading comparison result: " + ex.Message;
                    Logger.Error(error, ex);
                    return null;
                }
            }
        }

        // Builds NEW XML with the candidate equipped (in memory only). The loaded source build is restored.
        public ExportResult ExportWithCandidate(string itemRawText, string slotName, out string error)
        {
            lock (RuntimeLock)
            lock (_syncLock)
            {
                var globals = new Dictionary<string, string>();
                globals["candidateItemText"] = (itemRawText ?? "").Replace("\r\n", "\n");
                globals["candidateSlot"] = slotName ?? "";
                string jsonStr = CallWrapper("return pobCompanion.exportWithCandidate(candidateItemText, candidateSlot)", globals, out error);
                if (jsonStr == null) return null;
                try
                {
                    var r = _json.Deserialize<ExportResult>(jsonStr);
                    if (!r.Success) { error = r.Error ?? "Export failed"; Logger.Error(error); }
                    else Logger.Info("Generated " + GameIdentity.Name(Game) + " PoB build XML with '" + r.ItemName + "' in " + r.SlotName + " (validated by reload).");
                    return r;
                }
                catch (Exception ex)
                {
                    error = "Exception reading export result: " + ex.Message;
                    Logger.Error(error, ex);
                    return null;
                }
            }
        }

        public ImportInfo GetImportInfo()
        {
            string error;
            string jsonStr = CallWrapper("return pobCompanion.getImportInfo()", null, out error);
            if (jsonStr == null) return null;
            try { return _json.Deserialize<ImportInfo>(jsonStr); } catch { return null; }
        }

        // Writes exported XML to a NEW file. Refuses to overwrite anything, and refuses the
        // currently loaded source build path.
        public static string WriteNewBuildFile(string xml, string requestedPath, string sourceBuildPath, out string error)
        {
            error = null;
            try
            {
                string full = Path.GetFullPath(requestedPath);
                if (!string.IsNullOrEmpty(sourceBuildPath) && string.Equals(full, Path.GetFullPath(sourceBuildPath), StringComparison.OrdinalIgnoreCase))
                {
                    error = "Refusing to overwrite the source build file.";
                    return null;
                }
                full = MakeUniquePath(full);
                using (var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                {
                    sw.Write(xml);
                }
                Logger.Info("Wrote new PoB build file: " + full);
                return full;
            }
            catch (Exception ex)
            {
                error = "Could not write build file: " + ex.Message;
                Logger.Error(error, ex);
                return null;
            }
        }

        public static string MakeUniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; i < 1000; i++)
            {
                string p = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(p)) return p;
            }
            return Path.Combine(dir, name + " " + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);
        }

        public static string SuggestExportFileName(string buildName, string itemName, string slotLabel)
        {
            string raw = (buildName ?? "Build") + " - " + (slotLabel ?? "") + " - " + (itemName ?? "candidate") + " " + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            foreach (char c in Path.GetInvalidFileNameChars()) raw = raw.Replace(c, '_');
            if (raw.Length > 120) raw = raw.Substring(0, 120);
            return raw + ".xml";
        }

        // Activates a PoB2 loadout (stage) in memory, exactly like PoB2's Loadouts dropdown. Nothing is saved.
        public BuildInfo SetLoadout(string loadoutName, out string error)
        {
            CurrentCharacterSourceKey = null;
            CurrentCharacterInfo = null;
            var globals = new Dictionary<string, string>();
            globals["loadoutName"] = loadoutName ?? "";
            string jsonStr = CallWrapper("return pobCompanion.setLoadout(loadoutName)", globals, out error);
            if (jsonStr == null) return null;
            try
            {
                var info = _json.Deserialize<BuildInfo>(jsonStr);
                if (!info.Success) { error = info.Error ?? "Unknown loadout error"; Logger.Warn(error); }
                else Logger.Info("Activated " + GameIdentity.Name(Game) + " PoB loadout '" + info.ActiveLoadout + "'.");
                return info;
            }
            catch (Exception ex)
            {
                error = "Exception reading loadout result: " + ex.Message;
                Logger.Error(error, ex);
                return null;
            }
        }

        public BuildDetails GetBuildDetails(out string error)
        {
            string jsonStr = CallWrapper("return pobCompanion.exportBuildDetails()", null, out error);
            if (jsonStr == null) return null;
            try
            {
                var details = _json.Deserialize<BuildDetails>(jsonStr);
                if (!details.Success)
                {
                    error = details.Error ?? "Unknown error exporting build details";
                    Logger.Error(error);
                }
                return details;
            }
            catch (Exception ex)
            {
                error = "Exception reading build details: " + ex.Message;
                Logger.Error(error, ex);
                return null;
            }
        }

        private string CallWrapper(string code, Dictionary<string, string> globals, out string error)
        {
            error = null;
            lock (RuntimeLock)
            lock (_syncLock)
            {
                if (!_initialized)
                {
                    error = "Engine not initialized";
                    Logger.Error(error);
                    return null;
                }

                string origCwd = Directory.GetCurrentDirectory();
                try
                {
                    Directory.SetCurrentDirectory(_pobPath);
                    if (globals != null)
                    {
                        foreach (var kv in globals) SetGlobalString(kv.Key, kv.Value);
                    }

                    int res = luaL_loadstring(_L, code);
                    if (res != 0)
                    {
                        error = "Failed to prepare PoB call: " + GetLuaError();
                        Logger.Error(error);
                        return null;
                    }
                    res = lua_pcall(_L, 0, 1, 0);
                    if (res != 0)
                    {
                        error = "PoB call failed: " + GetLuaError();
                        Logger.Error(error);
                        return null;
                    }
                    string jsonStr = GetLuaStringUtf8(-1);
                    lua_settop(_L, 0);
                    return jsonStr;
                }
                catch (Exception ex)
                {
                    error = "Exception calling PoB: " + ex.Message;
                    Logger.Error(error, ex);
                    return null;
                }
                finally
                {
                    if (globals != null)
                    {
                        foreach (var kv in globals) SetGlobalString(kv.Key, null);
                    }
                    Directory.SetCurrentDirectory(origCwd);
                }
            }
        }

        private string GetLuaStringUtf8(int idx)
        {
            UIntPtr len;
            IntPtr ptr = lua_tolstring(_L, idx, out len);
            if (ptr == IntPtr.Zero) return "";
            byte[] buf = new byte[(int)len];
            Marshal.Copy(ptr, buf, 0, buf.Length);
            return Encoding.UTF8.GetString(buf);
        }

        // Pushes the exact UTF-8 bytes onto the Lua stack (no escaping/ANSI conversion) and stores them
        // as a global. A null value clears the global.
        private void SetGlobalString(string name, string value)
        {
            if (value == null)
            {
                lua_pushnil(_L);
            }
            else
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                lua_pushlstring(_L, bytes, new UIntPtr((uint)bytes.Length));
            }
            lua_setfield(_L, LUA_GLOBALSINDEX, name);
        }

        private string GetLuaString(int idx)
        {
            return GetLuaStringUtf8(idx);
        }

        private string GetLuaError()
        {
            string err = GetLuaString(-1);
            lua_settop(_L, 0);
            return err;
        }

        private void Cleanup()
        {
            if (_L != IntPtr.Zero)
            {
                try
                {
                    lua_close(_L);
                }
                catch { }
                _L = IntPtr.Zero;
            }
            _initialized = false;
        }

        public void Dispose()
        {
            lock (RuntimeLock)
            lock (_syncLock)
            {
                Cleanup();
            }
        }
    }
}
