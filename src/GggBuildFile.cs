using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Poe2ItemCompanion
{
    // level_interval: [min, max] or a single uint (treated as "from this level on").
    public class GggLevelInterval
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public bool Specified { get; set; }
        public bool SingleValue { get; set; }

        public static readonly GggLevelInterval All = new GggLevelInterval { Min = 0, Max = int.MaxValue };

        public bool Contains(int level) { return level >= Min && level <= Max; }

        public override string ToString()
        {
            if (!Specified) return "all levels";
            if (Max >= 100) return "level " + Math.Max(1, Min) + "+";
            return Min == Max ? "level " + Min : "levels " + Math.Max(1, Min) + "-" + Max;
        }
    }

    // One passive, skill, support or inventory slot entry. Fields holds every key of the object form
    // (known and unknown) exactly as deserialized; nothing is ever written back to the .build file.
    public class GggEntry
    {
        public string Path { get; set; }
        public string Id { get; set; }
        public GggLevelInterval Interval { get; set; }
        public int? WeaponSet { get; set; }
        public string TextRaw { get; set; }
        public string InventoryId { get; set; }
        public int SlotX { get; set; }
        public int SlotY { get; set; }
        public string UniqueName { get; set; }
        public Dictionary<string, object> Fields { get; set; }
        public List<GggEntry> Supports { get; set; }
        public string Text { get { return GggBuildFile.StripMarkup(TextRaw); } }

        public GggEntry() { Interval = GggLevelInterval.All; Supports = new List<GggEntry>(); }
    }

    public class GggStage
    {
        public string Name { get; set; }
        public int From { get; set; }
        public int To { get; set; }
    }

    public class GggBuildFile
    {
        private static readonly string[] BuildKeys = { "name", "author", "link", "description", "ascendancy", "passives", "skills", "inventory_slots" };
        private static readonly string[] PassiveKeys = { "id", "level_interval", "weapon_set", "additional_text" };
        private static readonly string[] SkillKeys = { "id", "level_interval", "additional_text", "support_skills" };
        private static readonly string[] SupportKeys = { "id", "level_interval", "additional_text" };
        private static readonly string[] SlotKeys = { "inventory_id", "slot_x", "slot_y", "level_interval", "unique_name", "additional_text" };

        public string SourcePath { get; set; }
        public string RawJson { get; private set; }
        public Dictionary<string, object> Root { get; private set; }
        public Dictionary<string, object> Build { get; private set; }
        public string Name { get; private set; }
        public string Author { get; private set; }
        public string Link { get; private set; }
        public string Description { get; private set; }
        public string Ascendancy { get; private set; }
        public List<GggEntry> Passives { get; private set; }
        public List<GggEntry> Skills { get; private set; }
        public List<GggEntry> Slots { get; private set; }
        public List<string> UnknownFields { get; private set; }
        public List<string> Warnings { get; private set; }
        public bool UsesSingleLevelValues { get; private set; }

        public static GggBuildFile Load(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { error = "The .build file was not found: " + path; return null; }
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { error = "Could not read the .build file: " + ex.Message; return null; }
            var file = Parse(text, out error);
            if (file != null) file.SourcePath = path;
            return file;
        }

        public static GggBuildFile Parse(string json, out string error)
        {
            error = null;
            object parsed;
            try { parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 }.DeserializeObject(json ?? ""); }
            catch (Exception ex) { error = "Not a valid .build JSON file: " + ex.Message; return null; }
            var root = parsed as Dictionary<string, object>;
            if (root == null) { error = "Not a valid .build file: the JSON must contain a single Build object."; return null; }
            var file = new GggBuildFile
            {
                RawJson = json, Root = root, Build = root,
                Passives = new List<GggEntry>(), Skills = new List<GggEntry>(), Slots = new List<GggEntry>(),
                UnknownFields = new List<string>(), Warnings = new List<string>()
            };
            string prefix = "";
            // Some tools wrap the object: { "build": { ... } }. GGG's own format is the bare Build object.
            if (!root.ContainsKey("name") && !root.ContainsKey("passives") && !root.ContainsKey("skills"))
            {
                foreach (var pair in root)
                {
                    var inner = pair.Value as Dictionary<string, object>;
                    if (inner != null && string.Equals(pair.Key, "build", StringComparison.OrdinalIgnoreCase)) { file.Build = inner; prefix = pair.Key + "."; break; }
                }
            }
            var b = file.Build;
            file.NoteUnknown(root == b ? root : null, BuildKeys, "");
            if (root != b) foreach (var key in root.Keys) if (!string.Equals(key, prefix.TrimEnd('.'), StringComparison.Ordinal)) file.UnknownFields.Add(key + " = " + Short(root[key]));
            if (root != b) file.NoteUnknown(b, BuildKeys, prefix);
            file.Name = Str(b, "name");
            file.Author = Str(b, "author");
            file.Link = Str(b, "link");
            file.Description = Str(b, "description");
            file.Ascendancy = Str(b, "ascendancy");
            if (string.IsNullOrWhiteSpace(file.Name)) file.Warnings.Add("Build has no name.");
            if (!b.ContainsKey("passives") && !b.ContainsKey("skills") && !b.ContainsKey("inventory_slots"))
            { error = "Not a .build file: no passives, skills or inventory_slots found."; return null; }

            int i = 0;
            foreach (var p in Arr(b, "passives", file.Warnings)) file.Passives.Add(file.ReadEntry(p, prefix + "passives[" + i++ + "]", PassiveKeys));
            i = 0;
            foreach (var s in Arr(b, "skills", file.Warnings))
            {
                string path = prefix + "skills[" + i++ + "]";
                var skill = file.ReadEntry(s, path, SkillKeys);
                var dict = s as Dictionary<string, object>;
                int j = 0;
                if (dict != null)
                    foreach (var sup in Arr(dict, "support_skills", file.Warnings)) skill.Supports.Add(file.ReadEntry(sup, path + ".support_skills[" + j++ + "]", SupportKeys));
                file.Skills.Add(skill);
            }
            i = 0;
            foreach (var s in Arr(b, "inventory_slots", file.Warnings))
            {
                var slot = file.ReadEntry(s, prefix + "inventory_slots[" + i++ + "]", SlotKeys);
                if (slot.Fields != null)
                {
                    slot.InventoryId = Str(slot.Fields, "inventory_id");
                    slot.SlotX = Int(slot.Fields, "slot_x") ?? 0;
                    slot.SlotY = Int(slot.Fields, "slot_y") ?? 0;
                    slot.UniqueName = Str(slot.Fields, "unique_name");
                }
                if (string.IsNullOrEmpty(slot.InventoryId)) file.Warnings.Add(slot.Path + " has no inventory_id; ignored.");
                else file.Slots.Add(slot);
            }
            file.Passives.RemoveAll(e => string.IsNullOrEmpty(e.Id));
            file.Skills.RemoveAll(e => string.IsNullOrEmpty(e.Id));
            return file;
        }

        private GggEntry ReadEntry(object value, string path, string[] known)
        {
            var e = new GggEntry { Path = path };
            var s = value as string;
            if (s != null) { e.Id = s; return e; }
            var d = value as Dictionary<string, object>;
            if (d == null) { Warnings.Add(path + " is not a string or object; ignored."); return e; }
            e.Fields = d;
            e.Id = Str(d, "id");
            e.TextRaw = Str(d, "additional_text");
            e.WeaponSet = Int(d, "weapon_set");
            object li;
            if (d.TryGetValue("level_interval", out li) && li != null) e.Interval = ReadInterval(li, path);
            NoteUnknown(d, known, path + ".");
            if (known != SlotKeys && string.IsNullOrEmpty(e.Id)) Warnings.Add(path + " has no id; ignored.");
            return e;
        }

        private GggLevelInterval ReadInterval(object value, string path)
        {
            var arr = value as object[];
            var nums = new List<int>();
            if (arr != null) { foreach (var v in arr) { int? n = ToInt(v); if (n.HasValue) nums.Add(n.Value); } }
            else { int? n = ToInt(value); if (n.HasValue) nums.Add(n.Value); }
            if (nums.Count == 0) { Warnings.Add(path + ".level_interval is not a number or [min, max]; treated as all levels."); return GggLevelInterval.All; }
            if (nums.Count == 1)
            {
                UsesSingleLevelValues = true;
                return new GggLevelInterval { Min = nums[0], Max = int.MaxValue, Specified = true, SingleValue = true };
            }
            return new GggLevelInterval { Min = Math.Min(nums[0], nums[1]), Max = Math.Max(nums[0], nums[1]), Specified = true };
        }

        private void NoteUnknown(Dictionary<string, object> d, string[] known, string prefix)
        {
            if (d == null) return;
            foreach (var pair in d)
                if (!known.Contains(pair.Key)) UnknownFields.Add(prefix + pair.Key + " = " + Short(pair.Value));
        }

        private static string Short(object value)
        {
            string s;
            try { s = new JavaScriptSerializer().Serialize(value); } catch { s = Convert.ToString(value, CultureInfo.InvariantCulture); }
            return s.Length > 120 ? s.Substring(0, 117) + "..." : s;
        }

        private static IEnumerable<object> Arr(Dictionary<string, object> d, string key, List<string> warnings)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return new object[0];
            var arr = v as object[];
            if (arr == null) { warnings.Add(key + " is not an array; ignored."); return new object[0]; }
            return arr;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        private static int? Int(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? ToInt(v) : null;
        }

        private static int? ToInt(object v)
        {
            if (v == null || v is string || v is bool) return null;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return null; }
        }

        // Stages are the distinct level ranges where the guide's level_interval values change. Within one
        // stage every entry is either fully active or inactive. The .build format has no named stages.
        public List<GggStage> GetStages()
        {
            var bounds = new SortedSet<int> { 1 };
            foreach (var e in AllEntries())
            {
                if (!e.Interval.Specified) continue;
                bounds.Add(Math.Max(1, Math.Min(100, e.Interval.Min)));
                if (e.Interval.Max < 100) bounds.Add(Math.Max(1, e.Interval.Max + 1));
            }
            var list = bounds.Where(x => x <= 100).ToList();
            var stages = new List<GggStage>();
            for (int i = 0; i < list.Count; i++)
            {
                int to = i + 1 < list.Count ? list[i + 1] - 1 : 100;
                stages.Add(new GggStage { From = list[i], To = to, Name = list.Count == 1 ? "All levels" : to >= 100 ? "Level " + list[i] + "+" : list[i] == to ? "Level " + to : "Level " + list[i] + "-" + to });
            }
            return stages;
        }

        public IEnumerable<GggEntry> AllEntries()
        {
            foreach (var p in Passives) yield return p;
            foreach (var s in Skills) { yield return s; foreach (var sup in s.Supports) yield return sup; }
            foreach (var s in Slots) yield return s;
        }

        // Removes GGG markup (<red>{text}, <rgb(1,2,3)>{text}, <b>{...}) while keeping the text itself.
        public static string StripMarkup(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string s = text.Replace("\\n", "\n");
            string prev;
            do
            {
                prev = s;
                s = Regex.Replace(s, @"<[A-Za-z]+(?:\([^)]*\))?>\{([^{}]*)\}", "$1");
            } while (s != prev);
            return s.Trim();
        }

        // Numbered/bulleted lines after a "Stat Priority" heading in a slot note.
        public static List<string> StatPriorities(string note)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(note)) return result;
            var lines = note.Replace("\r", "").Split('\n');
            int start = Array.FindIndex(lines, l => Regex.IsMatch(l, @"stat\s*priorit", RegexOptions.IgnoreCase));
            if (start < 0) return result;
            for (int i = start + 1; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (l.Length == 0 || Regex.IsMatch(l, @"^[-=_]{3,}$")) continue;
                var m = Regex.Match(l, @"^(?:\d+[.)]|[-*\u2022])\s*(.+)$");
                if (!m.Success) { if (result.Count > 0) break; continue; }
                result.Add(m.Groups[1].Value.Trim());
            }
            return result;
        }
    }

    // GGG ids -> PoB2 ids. Passives use the bundled data\ggg_passive_ids.tsv (generated from GGG's official
    // poe2-skilltree-export by tools\make-passive-ids.ps1); gems use PoB2's own Data\Gems.lua gameId field.
    public class GggIdMap
    {
        public Dictionary<string, int> Passives = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, string> PassiveNames = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, int> ClassIds = new Dictionary<string, int>(StringComparer.Ordinal);       // ascendancy id -> class integerId
        public Dictionary<string, string> ClassNames = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> AscendNames = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> Gems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> GemKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // PoB2 table key -> gameId
        public string TreeVersion;

        private static GggIdMap _cached;
        private static string _cachedKey;

        public static string MapPath()
        {
            string dir = System.IO.Path.GetDirectoryName(typeof(GggIdMap).Assembly.Location);
            string p = System.IO.Path.Combine(dir, "data", "ggg_passive_ids.tsv");
            if (File.Exists(p)) return p;
            string dev = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, "..", "data", "ggg_passive_ids.tsv"));
            return File.Exists(dev) ? dev : p;
        }

        public static GggIdMap Load(string pobPath, out string error)
        {
            error = null;
            string key = pobPath + "|" + MapPath();
            if (_cached != null && _cachedKey == key) return _cached;
            var map = new GggIdMap();
            string tsv = MapPath();
            if (!File.Exists(tsv)) { error = "Passive id map missing: " + tsv; return null; }
            foreach (var line in File.ReadAllLines(tsv))
            {
                var f = line.Split('\t');
                if (f.Length >= 3 && f[0] == "#class")
                {
                    int cls;
                    if (!int.TryParse(f[1], out cls)) continue;
                    map.ClassNames[f[2]] = f[2];
                    map.ClassIds["class:" + f[2]] = cls;
                    if (f.Length > 3)
                        foreach (var a in f[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            int eq = a.IndexOf('=');
                            string id = eq < 0 ? a : a.Substring(0, eq);
                            map.ClassIds[id] = cls;
                            map.ClassNames[id] = f[2];
                            map.AscendNames[id] = eq < 0 ? "" : a.Substring(eq + 1);
                        }
                    continue;
                }
                if (line.StartsWith("#") || f.Length < 2) continue;
                int hash;
                if (int.TryParse(f[1], out hash)) { map.Passives[f[0]] = hash; map.PassiveNames[f[0]] = f.Length > 2 ? f[2] : f[0]; }
            }
            string gems = System.IO.Path.Combine(pobPath ?? "", "Data", "Gems.lua");
            if (!File.Exists(gems)) { error = "PoB2 gem data not found: " + gems; return null; }
            foreach (Match m in Regex.Matches(File.ReadAllText(gems), "\\[\"([^\"]+)\"\\] = \\{\\s*name = \"([^\"]*)\",[^\\[]*?gameId = \"([^\"]+)\""))
            {
                if (!map.Gems.ContainsKey(m.Groups[3].Value)) map.Gems[m.Groups[3].Value] = m.Groups[2].Value;
                if (!map.GemKeys.ContainsKey(m.Groups[1].Value)) map.GemKeys[m.Groups[1].Value] = m.Groups[3].Value;
            }
            map.TreeVersion = LatestTreeVersion(pobPath);
            _cached = map;
            _cachedKey = key;
            return map;
        }

        private static string LatestTreeVersion(string pobPath)
        {
            string dir = System.IO.Path.Combine(pobPath ?? "", "TreeData");
            string best = "0_5";
            int bestMajor = -1, bestMinor = -1;
            if (!Directory.Exists(dir)) return best;
            foreach (var d in Directory.GetDirectories(dir))
            {
                var m = Regex.Match(System.IO.Path.GetFileName(d), @"^(\d+)_(\d+)$");
                if (!m.Success) continue;
                int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
                if (a > bestMajor || (a == bestMajor && b > bestMinor)) { bestMajor = a; bestMinor = b; best = m.Value; }
            }
            return best;
        }

        // The official example mixes "Metadata/Items/Gems/..." and "Metadata/Items/Gem/...". PoB2 keys its gem
        // table by one form and stores the game's BaseItemTypes id in gameId; accept either, in either form.
        public string GemName(string id)
        {
            string gameId = GemGameId(id);
            return gameId == null ? null : Gems[gameId];
        }

        public string GemGameId(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var candidate in new[] { id, id.Replace("/Items/Gem/", "/Items/Gems/"), id.Replace("/Items/Gems/", "/Items/Gem/") })
            {
                if (Gems.ContainsKey(candidate)) return candidate;
                string gameId;
                if (GemKeys.TryGetValue(candidate, out gameId) && Gems.ContainsKey(gameId)) return gameId;
            }
            return null;
        }
    }

    // What one .build stage resolved to; attached to the guide BuildInfo/BuildDetails.
    public class GggGuideInfo
    {
        public GggBuildFile File;
        public GggStage Stage;
        public List<GggStage> Stages = new List<GggStage>();
        public string ClassName;
        public string AscendName;
        public List<GggEntry> ActivePassives = new List<GggEntry>();
        public List<GggEntry> ActiveSkills = new List<GggEntry>();
        public List<GggEntry> ActiveSlots = new List<GggEntry>();
        public List<string> UnmappedPassives = new List<string>();
        public List<string> UnmappedGems = new List<string>();
        public List<string> DroppedPassives = new List<string>(); // mapped, but PoB2 did not keep them allocated (not connected)
        public int MappedPassives;
        public string Notice;
        public GggIdMap Map;
    }

    public static class GggGuide
    {
        public const string Limitation = ".build files (GGG Build Planner format, also exported by Maxroll's PoE2 planner) only carry unique item names and notes for rare items " +
            "(base type + stat priority). They contain no rare affix rolls, so none are shown or invented. Gem levels/quality are not in the format either.";

        public static string BuildPlannerFolder()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Path of Exile 2", "BuildPlanner");
        }

        // Reads the .build (read-only), converts the chosen stage to an in-memory PoB2 build and loads it in the engine.
        public static BuildInfo Load(AppConfig config, PobLuaEngine engine, out string error)
        {
            var file = GggBuildFile.Load(config.GggBuildFilePath, out error);
            if (file == null) return null;
            var map = GggIdMap.Load(config.PobPath, out error);
            if (map == null) return null;
            var stages = file.GetStages();
            string wanted = config.GetSelectedGuideLoadout();
            var stage = stages.FirstOrDefault(s => s.Name == wanted);
            string notice = null;
            if (stage == null)
            {
                stage = stages[stages.Count - 1];
                if (wanted != null) notice = "Selected stage '" + wanted + "' is not in this .build; showing " + stage.Name + ".";
            }
            GggGuideInfo info;
            string xml = ToPobXml(file, stage, map, out info, out error);
            if (xml == null) return null;
            info.Stages = stages;
            info.Notice = notice;
            var result = engine.LoadBuildXml(xml, string.IsNullOrWhiteSpace(file.Name) ? System.IO.Path.GetFileNameWithoutExtension(config.GggBuildFilePath) : file.Name, out error);
            if (result == null || !result.Success) return result;
            result.Ggg = info;
            result.ActiveLoadout = stage.Name;
            result.LoadoutNotice = notice;
            // No rare rolls: PoB2 totals of a gearless guide are not targets for the Current character.
            result.GuideTargets = null;
            return result;
        }

        public static string ToPobXml(GggBuildFile file, GggStage stage, GggIdMap map, out GggGuideInfo info, out string error)
        {
            error = null;
            info = new GggGuideInfo { File = file, Stage = stage, Map = map };
            int level = stage.From;
            string asc = file.Ascendancy;
            if (string.IsNullOrEmpty(asc) || !map.ClassIds.ContainsKey(asc))
            {
                // Fall back to the ascendancy named by allocated ascendancy passives, e.g. "AscendancyWarrior1Small3".
                foreach (var p in file.Passives)
                {
                    var m = Regex.Match(p.Id ?? "", @"^Ascendancy([A-Za-z]+\d+b?)");
                    if (m.Success && map.ClassIds.ContainsKey(m.Groups[1].Value)) { asc = m.Groups[1].Value; break; }
                }
            }
            if (string.IsNullOrEmpty(asc) || !map.ClassIds.ContainsKey(asc))
            {
                error = "The .build names no known ascendancy" + (string.IsNullOrEmpty(file.Ascendancy) ? "" : " ('" + file.Ascendancy + "')") + ", so PoB2 cannot pick the class.";
                return null;
            }
            info.ClassName = map.ClassNames[asc];
            info.AscendName = map.AscendNames[asc];

            var nodes = new List<int>();
            var sets = new Dictionary<int, List<int>>();
            foreach (var p in file.Passives.Where(p => p.Interval.Contains(level)))
            {
                info.ActivePassives.Add(p);
                int hash;
                if (!map.Passives.TryGetValue(p.Id, out hash)) { info.UnmappedPassives.Add(p.Id); continue; }
                if (nodes.Contains(hash)) continue;
                nodes.Add(hash);
                info.MappedPassives++;
                if (p.WeaponSet.HasValue && p.WeaponSet.Value > 0)
                {
                    if (!sets.ContainsKey(p.WeaponSet.Value)) sets[p.WeaponSet.Value] = new List<int>();
                    sets[p.WeaponSet.Value].Add(hash);
                }
            }

            var x = new StringBuilder();
            x.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            x.AppendLine("<PathOfBuilding2>");
            x.AppendLine("\t<Build level=\"" + level + "\" className=\"" + Esc(info.ClassName) + "\" ascendClassName=\"" + Esc(info.AscendName) +
                "\" targetVersion=\"0_1\" mainSocketGroup=\"1\" viewMode=\"TREE\" characterLevelAutoMode=\"false\"/>");
            x.AppendLine("\t<Tree activeSpec=\"1\">");
            x.Append("\t\t<Spec treeVersion=\"" + Esc(map.TreeVersion) + "\" classInternalId=\"" + map.ClassIds[asc] + "\" ascendancyInternalId=\"" + Esc(asc) +
                "\" secondaryAscendClassId=\"0\" masteryEffects=\"\" nodes=\"" + string.Join(",", nodes.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()) + "\"");
            if (sets.Count == 0) x.AppendLine("/>");
            else
            {
                x.AppendLine(">");
                foreach (var s in sets) x.AppendLine("\t\t\t<WeaponSet" + s.Key + " nodes=\"" + string.Join(",", s.Value.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()) + "\"/>");
                x.AppendLine("\t\t</Spec>");
            }
            x.AppendLine("\t</Tree>");
            // Gem levels are not in the .build: PoB2 picks the highest gem level usable at this character level.
            x.AppendLine("\t<Skills activeSkillSet=\"1\" defaultGemLevel=\"characterLevel\" defaultGemQuality=\"0\" sortGemsByDPS=\"true\">");
            x.AppendLine("\t\t<SkillSet id=\"1\">");
            foreach (var skill in file.Skills.Where(s => s.Interval.Contains(level)))
            {
                info.ActiveSkills.Add(skill);
                var gems = new List<GggEntry> { skill };
                gems.AddRange(skill.Supports.Where(s => s.Interval.Contains(level)));
                var mapped = new List<string>();
                foreach (var g in gems)
                {
                    string id = map.GemGameId(g.Id);
                    if (id == null) { info.UnmappedGems.Add(g.Id); continue; }
                    mapped.Add("\t\t\t\t<Gem gemId=\"" + Esc(id) + "\" nameSpec=\"" + Esc(map.GemName(id)) + "\" level=\"" + GemLevelPlaceholder + "\" quality=\"0\" count=\"1\" enabled=\"true\" enableGlobal1=\"true\" enableGlobal2=\"true\"/>");
                }
                if (mapped.Count == 0) continue;
                x.AppendLine("\t\t\t<Skill enabled=\"true\" label=\"" + Esc(map.GemName(skill.Id) ?? skill.Id) + "\" mainActiveSkill=\"1\" mainActiveSkillCalcs=\"1\" includeInFullDPS=\"" + (info.ActiveSkills.Count == 1 ? "true" : "false") + "\">");
                foreach (var line in mapped) x.AppendLine(line);
                x.AppendLine("\t\t\t</Skill>");
            }
            x.AppendLine("\t\t</SkillSet>");
            x.AppendLine("\t</Skills>");
            x.AppendLine("\t<Items activeItemSet=\"1\">");
            x.AppendLine("\t\t<ItemSet id=\"1\" useSecondWeaponSet=\"false\"/>");
            x.AppendLine("\t</Items>");
            x.AppendLine("\t<Notes>" + Esc(StripForNotes(file)) + "</Notes>");
            x.AppendLine("</PathOfBuilding2>");
            foreach (var slot in file.Slots.Where(s => s.Interval.Contains(level))) info.ActiveSlots.Add(slot);
            return x.ToString();
        }

        // Replaced in Lua by PoB2's own gem level for the character level (see pobCompanion.loadBuild).
        public const string GemLevelPlaceholder = "0";

        private static string StripForNotes(GggBuildFile file)
        {
            return "Imported from GGG .build: " + (file.Name ?? "") + (string.IsNullOrEmpty(file.Author) ? "" : " by " + file.Author) +
                (string.IsNullOrEmpty(file.Link) ? "" : "\n" + file.Link) + (string.IsNullOrEmpty(file.Description) ? "" : "\n\n" + GggBuildFile.StripMarkup(file.Description));
        }

        private static string Esc(string s) { return SecurityElement.Escape(s ?? ""); }

        public static string SlotName(string inventoryId)
        {
            switch (inventoryId ?? "")
            {
                case "Weapon1": return "Weapon 1";
                case "Offhand1": return "Weapon 2";
                case "Weapon2": return "Weapon 1 Swap";
                case "Offhand2": return "Weapon 2 Swap";
                case "BodyArmour1": return "Body Armour";
                case "Helm1": return "Helmet";
                case "Gloves1": return "Gloves";
                case "Boots1": return "Boots";
                case "Belt1": return "Belt";
                case "Amulet1": return "Amulet";
                case "Ring1": return "Ring 1";
                case "Ring2": return "Ring 2";
                case "Ring3": return "Ring 3";
            }
            var m = Regex.Match(inventoryId ?? "", @"^Flask(\d+)$");
            return m.Success ? "Flask " + m.Groups[1].Value : inventoryId;
        }

        // Replaces PoB2's (empty) gear with the .build slot notes and relabels the stages.
        public static void Decorate(BuildInfo build, BuildDetails details)
        {
            if (build == null || build.Ggg == null || details == null || !details.Success) return;
            var g = build.Ggg;
            details.Ggg = g;
            details.Loadouts = g.Stages.Select(s => s.Name).ToList();
            details.ActiveLoadout = g.Stage.Name;
            if (!string.IsNullOrWhiteSpace(g.File.Name)) details.BuildName = g.File.Name;
            var allocated = new HashSet<int>(details.Tree.Nodes.Where(n => n.Alloc).Select(n => n.Id));
            g.DroppedPassives.Clear();
            foreach (var p in g.ActivePassives)
            {
                int hash;
                if (g.Map.Passives.TryGetValue(p.Id, out hash) && !allocated.Contains(hash) && !g.DroppedPassives.Contains(p.Id)) g.DroppedPassives.Add(p.Id);
            }
            details.Gear = new List<GearEntry>();
            foreach (var slot in g.ActiveSlots)
            {
                string note = slot.Text ?? "";
                var priorities = GggBuildFile.StatPriorities(note);
                string firstLine = note.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
                var raw = new StringBuilder();
                raw.AppendLine("GGG .build slot note (" + slot.InventoryId + ", " + slot.Interval + ")");
                if (!string.IsNullOrEmpty(slot.UniqueName)) raw.AppendLine("Unique: " + slot.UniqueName);
                raw.AppendLine();
                raw.AppendLine(string.IsNullOrEmpty(note) ? "(no note text)" : note);
                if (priorities.Count > 0)
                {
                    raw.AppendLine();
                    raw.AppendLine("Parsed stat priority (used for heuristic guide-fit):");
                    for (int i = 0; i < priorities.Count; i++) raw.AppendLine("  " + (i + 1) + ". " + priorities[i]);
                }
                raw.AppendLine();
                raw.AppendLine("No affix rolls: the .build format does not contain them and none are invented.");
                details.Gear.Add(new GearEntry
                {
                    SlotName = SlotName(slot.InventoryId),
                    SlotLabel = SlotName(slot.InventoryId),
                    ItemName = !string.IsNullOrEmpty(slot.UniqueName) ? slot.UniqueName : firstLine != null ? "Note: " + firstLine : "Note",
                    Rarity = !string.IsNullOrEmpty(slot.UniqueName) ? "UNIQUE" : "NOTE",
                    Raw = raw.ToString(),
                    Affixes = new List<ItemAffix>(),
                    Priorities = priorities,
                    FromNotes = true,
                    InactiveWeaponSet = slot.InventoryId == "Weapon2" || slot.InventoryId == "Offhand2"
                });
            }
        }

        private static readonly HashSet<string> Stop = new HashSet<string>(StringComparer.Ordinal)
        { "increased", "to", "of", "the", "a", "an", "and", "with", "for", "on", "in", "maximum", "prioritize", "prioritise", "highest", "high", "more", "any", "your", "is", "are", "recommended", "if", "possible", "or", "you", "gain" };

        private static HashSet<string> Tokens(string text, bool candidate)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            string s = (text ?? "").ToLowerInvariant();
            if (candidate && Regex.IsMatch(s, @"^\s*adds\s")) set.Add("flat");
            if (candidate && s.Contains("all attributes")) { set.Add("strength"); set.Add("dexterity"); set.Add("intelligence"); }
            if (candidate && s.Contains("all elemental resistance")) { set.Add("fire"); set.Add("cold"); set.Add("lightning"); set.Add("elemental"); }
            foreach (Match m in Regex.Matches(s, "[a-z]+"))
            {
                string w = m.Value;
                if (w == "health" || w == "hp") w = "life";
                else if (w == "res" || w == "resist" || w == "resists" || w == "resistances") w = "resistance";
                else if (w == "dps") w = "damage";
                else if (w == "es") { set.Add("energy"); w = "shield"; }
                else if (w == "ms") { set.Add("movement"); w = "speed"; }
                else if (w == "crit") w = "critical";
                else if (w == "attr" || w == "attrs") w = "attribute";
                else if (w.Length > 4 && w.EndsWith("s") && !w.EndsWith("ss")) w = w.Substring(0, w.Length - 1);
                if (w.Length < 2 || Stop.Contains(w)) continue;
                set.Add(w);
            }
            if (candidate && (set.Contains("strength") || set.Contains("dexterity") || set.Contains("intelligence"))) set.Add("attribute");
            return set;
        }

        // Heuristic keyword match of candidate affixes against the slot's .build stat priority note.
        public static List<ItemStatRating> RateAgainstNotes(List<ItemAffix> candidate, GearEntry slot)
        {
            var ratings = new List<ItemStatRating>();
            var priorities = slot == null || slot.Priorities == null ? new List<string>() : slot.Priorities;
            var keys = priorities.Select(p => Tokens(p, false)).ToList();
            foreach (var affix in candidate ?? new List<ItemAffix>())
            {
                var row = new ItemStatRating { Text = affix.Text ?? "", Rating = "Unrated", Explanation = "No stat priority in the .build note for this slot." };
                ratings.Add(row);
                if (priorities.Count == 0) continue;
                var c = Tokens(affix.Text, true);
                int hit = -1;
                for (int i = 0; i < keys.Count && hit < 0; i++)
                    if (keys[i].Count > 0 && keys[i].All(c.Contains)) hit = i;
                if (hit >= 0)
                {
                    row.Rating = "Green";
                    row.Explanation = "Heuristic: matches .build stat priority #" + (hit + 1) + " \"" + priorities[hit] + "\". No roll target in .build, so value is not judged.";
                }
                else
                {
                    row.Rating = "Red";
                    row.Explanation = "Heuristic: not named in this slot's .build stat priority note.";
                }
            }
            return ratings;
        }
    }
}
