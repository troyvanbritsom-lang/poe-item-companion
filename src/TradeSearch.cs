using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Poe2ItemCompanion
{
    internal sealed class TradeStat
    {
        public string Id;
        public string Text;
        public string Type;
    }

    // Official trade stat list as shipped (generated from api/trade(2)/data/stats) in the user's PoB install.
    // Read only; never downloaded or guessed.
    internal sealed class TradeStatIndex
    {
        private static readonly Dictionary<string, Tuple<DateTime, TradeStatIndex>> Cache = new Dictionary<string, Tuple<DateTime, TradeStatIndex>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<TradeStat>> _byKey = new Dictionary<string, List<TradeStat>>(StringComparer.Ordinal);

        public GameId Game { get; private set; }
        public int Count { get; private set; }
        public string SourcePath { get; private set; }

        public static string FilePath(AppConfig config)
        {
            return Path.Combine(config.PobPath ?? "", "Data", "TradeSiteStats.lua");
        }

        public static TradeStatIndex Load(AppConfig config, out string error)
        {
            error = null;
            string path = FilePath(config);
            try
            {
                if (!File.Exists(path))
                {
                    error = "Official trade stat list not found (" + path + "). Stat filters need the " + config.GameName + " Path of Building install.";
                    return null;
                }
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                Tuple<DateTime, TradeStatIndex> cached;
                lock (Cache)
                    if (Cache.TryGetValue(path, out cached) && cached.Item1 == stamp) return cached.Item2;
                var index = Parse(File.ReadAllText(path), config.Game, out error);
                if (index == null) return null;
                index.SourcePath = path;
                lock (Cache) Cache[path] = Tuple.Create(stamp, index);
                return index;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)) throw;
                error = "Cannot read the trade stat list: " + ex.Message;
                return null;
            }
        }

        internal static TradeStatIndex Parse(string lua, GameId game, out string error)
        {
            error = null;
            string marker = game == GameId.Poe2 ? "/api/trade2/data/stats" : "/api/trade/data/stats";
            if ((lua ?? "").IndexOf(marker, StringComparison.Ordinal) < 0)
            {
                error = "The trade stat list does not belong to " + GameIdentity.Name(game) + ".";
                return null;
            }
            var index = new TradeStatIndex { Game = game };
            var groups = new LuaLiteralReader(lua).ReadReturn() as LuaTable;
            if (groups == null) throw new FormatException("trade stat list has no table");
            foreach (var group in groups.Items.OfType<LuaTable>())
            {
                object entries;
                if (!group.Fields.TryGetValue("entries", out entries) || !(entries is LuaTable)) continue;
                foreach (var entry in ((LuaTable)entries).Items.OfType<LuaTable>())
                {
                    string id = entry.Get("id") as string, text = entry.Get("text") as string, type = entry.Get("type") as string;
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(text) || string.IsNullOrEmpty(type)) continue;
                    var stat = new TradeStat { Id = id, Text = text, Type = type };
                    List<TradeStat> list;
                    string key = type + "|" + text;
                    if (!index._byKey.TryGetValue(key, out list)) index._byKey[key] = list = new List<TradeStat>();
                    list.Add(stat);
                    index.Count++;
                }
            }
            if (index.Count == 0) { error = "The trade stat list is empty."; return null; }
            return index;
        }

        public List<TradeStat> Find(string type, string text)
        {
            List<TradeStat> list;
            return _byKey.TryGetValue(type + "|" + text, out list) ? list : new List<TradeStat>();
        }
    }

    internal sealed class LuaTable
    {
        public readonly List<object> Items = new List<object>();
        public readonly Dictionary<string, object> Fields = new Dictionary<string, object>(StringComparer.Ordinal);
        public object Get(string key) { object v; return Fields.TryGetValue(key, out v) ? v : null; }
    }

    // Minimal reader for generated Lua data literals ("return { ... }"): tables, strings, numbers, booleans.
    internal sealed class LuaLiteralReader
    {
        private readonly string _s;
        private int _p;

        public LuaLiteralReader(string text) { _s = text ?? ""; }

        public object ReadReturn()
        {
            Skip();
            if (string.CompareOrdinal(_s, _p, "return", 0, 6) == 0) _p += 6;
            return ReadValue();
        }

        private void Skip()
        {
            while (_p < _s.Length)
            {
                if (char.IsWhiteSpace(_s[_p])) _p++;
                else if (_p + 1 < _s.Length && _s[_p] == '-' && _s[_p + 1] == '-')
                {
                    while (_p < _s.Length && _s[_p] != '\n') _p++;
                }
                else break;
            }
        }

        private Exception Fail(string what) { return new FormatException(what + " at offset " + _p); }

        private object ReadValue()
        {
            Skip();
            if (_p >= _s.Length) throw Fail("unexpected end");
            char c = _s[_p];
            if (c == '{') return ReadTable();
            if (c == '"' || c == '\'') return ReadString();
            if (c == '-' || char.IsDigit(c) || c == '.')
            {
                int start = _p++;
                while (_p < _s.Length && (char.IsLetterOrDigit(_s[_p]) || _s[_p] == '.' || ((_s[_p] == '-' || _s[_p] == '+') && (_s[_p - 1] == 'e' || _s[_p - 1] == 'E')))) _p++;
                double d;
                if (!double.TryParse(_s.Substring(start, _p - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) throw Fail("bad number");
                return d;
            }
            string word = ReadName();
            if (word == "true") return true;
            if (word == "false") return false;
            if (word == "nil") return null;
            throw Fail("unexpected token '" + word + "'");
        }

        private string ReadName()
        {
            int start = _p;
            while (_p < _s.Length && (char.IsLetterOrDigit(_s[_p]) || _s[_p] == '_')) _p++;
            if (_p == start) throw Fail("unexpected character");
            return _s.Substring(start, _p - start);
        }

        private string ReadString()
        {
            char quote = _s[_p++];
            var sb = new StringBuilder();
            while (true)
            {
                if (_p >= _s.Length) throw Fail("unterminated string");
                char c = _s[_p++];
                if (c == quote) break;
                if (c != '\\') { sb.Append(c); continue; }
                char e = _s[_p++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    default:
                        if (char.IsDigit(e))
                        {
                            int start = _p - 1;
                            while (_p < _s.Length && _p - start < 3 && char.IsDigit(_s[_p])) _p++;
                            sb.Append((char)int.Parse(_s.Substring(start, _p - start), CultureInfo.InvariantCulture));
                        }
                        else sb.Append(e);
                        break;
                }
            }
            return sb.ToString();
        }

        private LuaTable ReadTable()
        {
            _p++;
            var table = new LuaTable();
            while (true)
            {
                Skip();
                if (_p >= _s.Length) throw Fail("unterminated table");
                if (_s[_p] == '}') { _p++; return table; }
                if (_s[_p] == '[')
                {
                    _p++;
                    object key = ReadValue();
                    Skip();
                    if (_p >= _s.Length || _s[_p] != ']') throw Fail("expected ]");
                    _p++;
                    Expect('=');
                    object value = ReadValue();
                    var name = key as string;
                    if (name != null) table.Fields[name] = value;
                    else table.Items.Add(value);
                }
                else if (char.IsLetter(_s[_p]) || _s[_p] == '_')
                {
                    int mark = _p;
                    string name = ReadName();
                    Skip();
                    if (_p < _s.Length && _s[_p] == '=' && name != "true" && name != "false" && name != "nil")
                    {
                        _p++;
                        table.Fields[name] = ReadValue();
                    }
                    else { _p = mark; table.Items.Add(ReadValue()); }
                }
                else table.Items.Add(ReadValue());
                Skip();
                if (_p < _s.Length && (_s[_p] == ',' || _s[_p] == ';')) _p++;
            }
        }

        private void Expect(char c)
        {
            Skip();
            if (_p >= _s.Length || _s[_p] != c) throw Fail("expected " + c);
            _p++;
        }
    }

    internal sealed class TradeFilter
    {
        public bool Enabled;
        public string Source;       // final mod text from PoB
        public string Kind;         // explicit, implicit, crafted, fractured, enchant, rune
        public TradeStat Stat;
        public double Value;        // guide value as the trade site compares it (average for "# to #")
        public double? Min;
        public double? Max;
        public string Note;
    }

    internal sealed class TradeIssue
    {
        public string Text;
        public string Reason;
    }

    internal sealed class TradeSearchPlan
    {
        public GameId Game;
        public string Slot;
        public string DisplayName;
        public string Name;          // unique name; null for other rarities
        public string BaseType;
        public string Rarity;        // trade rarity option: normal, magic, rare, unique, nonunique; null = any
        public int? ItemLevel;
        public bool UseItemLevel;
        public int Sockets;
        public bool UseSockets;
        public int Links;
        public bool UseLinks;
        public int ThresholdPercent = 80;
        public string StatSource;    // where stat IDs came from (or why they are unavailable)
        public readonly List<TradeFilter> Filters = new List<TradeFilter>();
        public readonly List<TradeIssue> Unmatched = new List<TradeIssue>();
        public readonly List<string> Notes = new List<string>();

        public int EnabledFilterCount { get { return Filters.Count(f => f.Enabled); } }
        public bool HasConstraintsBeyondBase { get { return EnabledFilterCount > 0 || UseItemLevel || UseSockets || UseLinks || !string.IsNullOrEmpty(Name); } }
    }

    internal static class TradeSearch
    {
        public const string Poe1SearchRoot = "https://www.pathofexile.com/trade/search/";
        public const string Poe2SearchRoot = "https://www.pathofexile.com/trade2/search/poe2/";
        private static readonly Regex Number = new Regex(@"([+-]?)(\d+(?:\.\d+)?)", RegexOptions.CultureInvariant);
        private static readonly Regex Markup = new Regex(@"^\s*(\{[^}]*\})+\s*", RegexOptions.CultureInvariant);
        private static readonly Regex Metadata = new Regex(@"^\s*(Prefix|Suffix|Implicits|LevelReq|Item ?Level|Quality|Sockets|Unique ID|Rarity|Crafted|Selected Variant|Variant|League|Requires)\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly string[] WeaponSlots = { "Weapon 1", "Weapon 2", "Weapon 1 Swap", "Weapon 2 Swap" };
        private static readonly string[] ArmourSlots = { "Helmet", "Body Armour", "Gloves", "Boots" };
        private static readonly Regex DefenceStat = new Regex(@"Armour|Evasion|Energy Shield|Ward|Block", RegexOptions.CultureInvariant);

        // Why an item cannot be searched, or null when it can.
        public static string UnsearchableReason(GearEntry gear)
        {
            if (gear == null || (string.IsNullOrEmpty(gear.ItemName) && string.IsNullOrEmpty(gear.BaseName))) return "Empty slot.";
            if (gear.FromNotes)
                return "From .build guide notes: not a concrete item (no base type or rolled mods), so there is nothing exact to search for.";
            if (string.IsNullOrEmpty(gear.BaseName)) return "PoB did not report a base type for this item.";
            return null;
        }

        public static string TradeRarity(string pobRarity)
        {
            switch ((pobRarity ?? "").ToUpperInvariant())
            {
                case "UNIQUE": case "RELIC": return "unique";
                case "RARE": return "rare";
                case "MAGIC": return "magic";
                case "NORMAL": return "normal";
                default: return null;
            }
        }

        public static TradeSearchPlan Plan(GearEntry gear, GameId game, TradeStatIndex index, string indexError)
        {
            var plan = new TradeSearchPlan { Game = game, Slot = gear.SlotLabel ?? gear.SlotName, BaseType = StripVariant(gear.BaseName) };
            plan.Rarity = TradeRarity(gear.Rarity);
            bool unique = plan.Rarity == "unique";
            plan.DisplayName = string.IsNullOrEmpty(gear.ItemName) ? plan.BaseType : gear.ItemName;
            if (unique)
            {
                plan.Name = UniqueName(gear);
                if (plan.Name != gear.ItemName) plan.Notes.Add("PoB item title '" + gear.ItemName + "' searched as unique name '" + plan.Name + "'.");
            }
            if (gear.ItemLevel.HasValue && gear.ItemLevel.Value > 0) plan.ItemLevel = gear.ItemLevel;
            if (game == GameId.Poe1)
            {
                plan.Sockets = gear.SocketCount;
                plan.Links = gear.MaxLinks;
                if (gear.SocketCount > 0 && gear.MaxLinks < 2) plan.Links = 0;
            }
            else
            {
                plan.Sockets = gear.SocketCount;
                plan.Links = 0;
            }
            if (gear.Influences != null && gear.Influences.Count > 0)
                plan.Notes.Add("Influence (" + string.Join(", ", gear.Influences.ToArray()) + ") is not added as a filter; influenced mods below still are.");
            if (gear.Corrupted) plan.Notes.Add("Guide item is corrupted; corruption is not filtered.");
            if (plan.BaseType != gear.BaseName) plan.Notes.Add("PoB base '" + gear.BaseName + "' searched as '" + plan.BaseType + "'.");

            plan.StatSource = index != null ? "Official stat IDs from " + index.SourcePath : (indexError ?? "Official trade stat list unavailable.");
            foreach (var affix in gear.Affixes ?? new List<ItemAffix>())
            {
                string text = CleanModText(affix.Text);
                if (text == null) continue;
                if (Metadata.IsMatch(text)) { plan.Unmatched.Add(new TradeIssue { Text = text, Reason = "PoB item metadata, not a trade stat." }); continue; }
                if (index == null) { plan.Unmatched.Add(new TradeIssue { Text = text, Reason = "No official trade stat list available." }); continue; }
                string kind = AffixKind(affix);
                string type = TradeType(kind, game);
                string reason;
                var filter = Match(text, type, kind, gear.SlotName, index, out reason);
                if (filter == null) { plan.Unmatched.Add(new TradeIssue { Text = text, Reason = reason }); continue; }
                filter.Enabled = !unique && (kind == "explicit" || kind == "implicit" || kind == "fractured") && filter.Value > 0;
                if (kind == "crafted") filter.Note = "Crafted (bench) mod: off by default because you can craft it yourself.";
                else if (kind == "enchant") filter.Note = "Enchant: off by default.";
                else if (kind == "rune") filter.Note = "Socketed rune/augment: off by default (you can socket your own).";
                else if (unique) filter.Note = "Unique: name and base already identify it; enable to require a roll.";
                else if (filter.Value <= 0) filter.Note = "Negative/reduced value: set a maximum yourself.";
                plan.Filters.Add(filter);
            }
            ApplyThreshold(plan, plan.ThresholdPercent);
            return plan;
        }

        public static void ApplyThreshold(TradeSearchPlan plan, int percent)
        {
            plan.ThresholdPercent = Math.Max(1, Math.Min(100, percent));
            foreach (var f in plan.Filters)
            {
                if (f.Value > 0)
                {
                    double min = f.Value * plan.ThresholdPercent / 100.0;
                    bool whole = Math.Abs(f.Value - Math.Round(f.Value)) < 1e-9;
                    f.Min = whole ? Math.Floor(min) : Math.Floor(min * 10) / 10;
                    f.Max = null;
                }
                else { f.Min = null; f.Max = null; }
            }
        }

        // PoB reports a unique as "<title>, <base>", and guide authors often prefix the title with a stage label
        // ("Lvl 90, The Stampede, Assassin's Boots"; "Lvl 68-96 - The Yielding Mortality, Imperial Staff").
        private static readonly Regex StageLabel = new Regex(@"^(?:lvl|level|act|stage)\s*[\d\s+\-\u2013]*?\s*(?:,|:|\s-|\s\u2013)\s*", RegexOptions.IgnoreCase);

        public static string UniqueName(GearEntry gear)
        {
            string name = (gear == null ? null : gear.ItemName ?? "").Trim();
            if (name.Length == 0) return null;
            string baseName = gear.BaseName;
            if (!string.IsNullOrEmpty(baseName) && name.Length > baseName.Length && name.EndsWith(", " + baseName, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - baseName.Length - 2).TrimEnd();
            string stripped;
            while ((stripped = StageLabel.Replace(name, "", 1)) != name && stripped.Length > 0) name = stripped;
            return name;
        }

        public static string StripVariant(string baseName)
        {
            if (string.IsNullOrEmpty(baseName)) return baseName;
            return Regex.Replace(baseName, @"\s*\([^)]*\)\s*$", "").Trim();
        }

        public static string CleanModText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = Markup.Replace(text.Trim(), "");
            t = Regex.Replace(t, @"\s+", " ").Trim();
            return t.Length == 0 ? null : t;
        }

        public static string AffixKind(ItemAffix affix)
        {
            string kind = (affix.Kind ?? "explicit").ToLowerInvariant();
            if (kind == "explicit")
            {
                if (affix.Fractured) return "fractured";
                if (affix.Crafted) return "crafted";
            }
            return kind;
        }

        public static string TradeType(string kind, GameId game)
        {
            if (kind == "rune") return game == GameId.Poe2 ? "augment" : null; // PoB2: json label "augment", id prefix "rune"
            return kind;
        }

        private static TradeFilter Match(string text, string type, string kind, string slot, TradeStatIndex index, out string reason)
        {
            reason = null;
            if (type == null) { reason = "No trade stat group for '" + kind + "' mods in this game."; return null; }
            var tried = new List<Tuple<string, double, double>>();
            Candidates(text, 1, tried);
            if (Regex.IsMatch(text, @"\breduced\b")) Candidates(Regex.Replace(text, @"\breduced\b", "increased"), -1, tried);
            if (Regex.IsMatch(text, @"\bless\b")) Candidates(Regex.Replace(text, @"\bless\b", "more"), -1, tried);
            bool weapon = WeaponSlots.Contains(slot ?? "");
            bool armour = ArmourSlots.Contains(slot ?? "") || (slot == "Weapon 2" || slot == "Weapon 2 Swap");
            foreach (var group in tried.GroupBy(t => t.Item3).OrderByDescending(g => g.Key))
            {
                var hits = new List<Tuple<TradeStat, double>>();
                foreach (var candidate in group)
                {
                    string template = candidate.Item1;
                    bool localPreferred = weapon || (armour && DefenceStat.IsMatch(template));
                    var global = index.Find(type, template);
                    var local = index.Find(type, template + " (Local)");
                    var chosen = localPreferred && local.Count > 0 ? local : global.Count > 0 ? global : localPreferred ? local : new List<TradeStat>();
                    if (chosen.Count > 1)
                    {
                        reason = "Ambiguous: " + chosen.Count + " official " + type + " stats share the text '" + template + "'.";
                        return null;
                    }
                    if (chosen.Count == 1 && !hits.Any(h => h.Item1.Id == chosen[0].Id)) hits.Add(Tuple.Create(chosen[0], candidate.Item2));
                }
                if (hits.Count == 1) return new TradeFilter { Source = text, Kind = kind, Stat = hits[0].Item1, Value = hits[0].Item2 };
                if (hits.Count > 1)
                {
                    reason = "Ambiguous: matches " + string.Join(", ", hits.Select(h => h.Item1.Id).ToArray()) + ".";
                    return null;
                }
            }
            reason = Number.IsMatch(text) ? "No official " + type + " trade stat with this wording." : "No official " + type + " trade stat with this wording (no numeric value).";
            return null;
        }

        // Adds (template, value, replaced-count) for every way of turning numbers into '#', with and without a sign.
        private static void Candidates(string text, int direction, List<Tuple<string, double, double>> output)
        {
            var matches = Number.Matches(text).Cast<Match>().ToList();
            if (matches.Count == 0) { output.Add(Tuple.Create(text, 0.0, 0.0)); return; }
            if (matches.Count > 4) return;
            for (int mask = (1 << matches.Count) - 1; mask > 0; mask--)
            {
                int replaced = 0;
                for (int i = 0; i < matches.Count; i++) if ((mask & (1 << i)) != 0) replaced++;
                if (replaced > 2) continue;
                foreach (bool absorbSign in new[] { false, true })
                {
                    var sb = new StringBuilder();
                    int last = 0;
                    var values = new List<double>();
                    bool signSeen = false;
                    for (int i = 0; i < matches.Count; i++)
                    {
                        var m = matches[i];
                        sb.Append(text, last, m.Index - last);
                        last = m.Index + m.Length;
                        if ((mask & (1 << i)) == 0) { sb.Append(m.Value); continue; }
                        string sign = m.Groups[1].Value;
                        if (sign.Length > 0) signSeen = true;
                        sb.Append(absorbSign ? "#" : sign + "#");
                        double v = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                        values.Add(sign == "-" ? -v : v);
                    }
                    if (absorbSign && !signSeen) continue;
                    sb.Append(text, last, text.Length - last);
                    double value = values.Average() * direction;
                    output.Add(Tuple.Create(sb.ToString(), value, (double)replaced));
                }
            }
        }

        public static Dictionary<string, object> Query(TradeSearchPlan plan)
        {
            var query = new Dictionary<string, object> { { "status", new Dictionary<string, object> { { "option", "online" } } } };
            if (!string.IsNullOrEmpty(plan.Name)) query["name"] = plan.Name;
            if (!string.IsNullOrEmpty(plan.BaseType)) query["type"] = plan.BaseType;
            var filters = new List<object>();
            foreach (var f in plan.Filters.Where(f => f.Enabled))
            {
                var value = new Dictionary<string, object>();
                if (f.Min.HasValue) value["min"] = f.Min.Value;
                if (f.Max.HasValue) value["max"] = f.Max.Value;
                var filter = new Dictionary<string, object> { { "id", f.Stat.Id }, { "disabled", false } };
                if (value.Count > 0) filter["value"] = value;
                filters.Add(filter);
            }
            query["stats"] = new List<object> { new Dictionary<string, object> { { "type", "and" }, { "filters", filters } } };
            var groups = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(plan.Rarity))
                groups["type_filters"] = Group("rarity", new Dictionary<string, object> { { "option", plan.Rarity } });
            if (plan.UseItemLevel && plan.ItemLevel.HasValue)
                groups["misc_filters"] = Group("ilvl", new Dictionary<string, object> { { "min", plan.ItemLevel.Value } });
            if (plan.Game == GameId.Poe1 && ((plan.UseSockets && plan.Sockets > 0) || (plan.UseLinks && plan.Links > 0)))
            {
                var socket = new Dictionary<string, object>();
                if (plan.UseSockets && plan.Sockets > 0) socket["sockets"] = new Dictionary<string, object> { { "min", plan.Sockets } };
                if (plan.UseLinks && plan.Links > 0) socket["links"] = new Dictionary<string, object> { { "min", plan.Links } };
                groups["socket_filters"] = new Dictionary<string, object> { { "disabled", false }, { "filters", socket } };
            }
            if (plan.Game == GameId.Poe2 && plan.UseSockets && plan.Sockets > 0)
                groups["equipment_filters"] = Group("rune_sockets", new Dictionary<string, object> { { "min", plan.Sockets } });
            if (groups.Count > 0) query["filters"] = groups;
            return new Dictionary<string, object> { { "query", query }, { "sort", new Dictionary<string, object> { { "price", "asc" } } } };
        }

        private static Dictionary<string, object> Group(string key, object value)
        {
            return new Dictionary<string, object> { { "disabled", false }, { "filters", new Dictionary<string, object> { { key, value } } } };
        }

        public static string QueryJson(TradeSearchPlan plan)
        {
            return new JavaScriptSerializer().Serialize(Query(plan));
        }

        // Same encoding as PoB's trade link builder: unreserved characters kept, spaces as '+'.
        public static string EncodeLeague(string league)
        {
            var sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(league ?? ""))
            {
                char c = (char)b;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '.' || c == '_' || c == '~') sb.Append(c);
                else if (c == ' ') sb.Append('+');
                else sb.Append('%').Append(b.ToString("X2"));
            }
            return sb.ToString();
        }

        public static string Url(TradeSearchPlan plan, string league)
        {
            if (string.IsNullOrWhiteSpace(league)) throw new ArgumentException("Choose a trade league first.");
            string root = plan.Game == GameId.Poe2 ? Poe2SearchRoot : Poe1SearchRoot;
            return root + EncodeLeague(league.Trim()) + "?q=" + Uri.EscapeDataString(QueryJson(plan));
        }

        public static string LeaguesEndpoint(GameId game)
        {
            return game == GameId.Poe2 ? "https://www.pathofexile.com/api/trade2/data/leagues" : "https://www.pathofexile.com/api/trade/data/leagues";
        }

        private static readonly Dictionary<GameId, List<string>> LeagueCache = new Dictionary<GameId, List<string>>();

        // Public league list; only called when the user presses the button, then cached for this session.
        public static List<string> FetchLeagues(GameId game, out string error)
        {
            error = null;
            List<string> cached;
            lock (LeagueCache) if (LeagueCache.TryGetValue(game, out cached)) return cached;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                string json;
                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[HttpRequestHeader.UserAgent] = "Poe2ItemCompanion/1.0";
                    json = client.DownloadString(LeaguesEndpoint(game));
                }
                var leagues = ParseLeagues(json, game);
                if (leagues.Count == 0) { error = "The league list was empty."; return null; }
                lock (LeagueCache) LeagueCache[game] = leagues;
                return leagues;
            }
            catch (Exception ex)
            {
                if (!(ex is WebException || ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException)) throw;
                error = "Could not load the league list: " + ex.Message + " Type the league name instead.";
                return null;
            }
        }

        internal static List<string> ParseLeagues(string json, GameId game)
        {
            var result = new List<string>();
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            object list;
            if (root == null || !root.TryGetValue("result", out list) || !(list is object[])) return result;
            string realm = game == GameId.Poe2 ? "poe2" : "pc";
            foreach (var entry in ((object[])list).OfType<Dictionary<string, object>>())
            {
                object id, entryRealm;
                if (!entry.TryGetValue("id", out id) || !(id is string)) continue;
                if (entry.TryGetValue("realm", out entryRealm) && entryRealm is string && (string)entryRealm != realm) continue;
                if (!result.Contains((string)id)) result.Add((string)id);
            }
            return result;
        }
    }
}
