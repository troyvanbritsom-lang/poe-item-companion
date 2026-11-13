using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Poe2ItemCompanion
{
    public sealed class GemAcquisitionInfo
    {
        public string Requirements { get; set; }
        public string Where { get; set; }
        public string SourceUrl { get; set; }
        public int? MinimumLevel { get; set; }
        public bool? IsVendorGem { get; set; }
        public string CompactLevel
        {
            get
            {
                if (IsVendorGem == false) return "not sold";
                return MinimumLevel.HasValue ? "L" + MinimumLevel + (IsVendorGem == true ? "*" : "?") : "unknown";
            }
        }
    }

    public static class GemAcquisition
    {
        public const string DataNotice = "Offline PoENavi acquisition snapshot, bundled 2026-11-11. Quest completion is unknown; verify unlocks in game. Double-click a gem for its wiki source.";
        private sealed class Quest
        {
            public int act { get; set; }
            public string npc { get; set; }
        }
        private sealed class Availability
        {
            public List<string> vendor { get; set; }
            public List<string> quest { get; set; }
        }
        private sealed class Gem
        {
            public int? level { get; set; }
            public Dictionary<string, Availability> quests { get; set; }
        }
        private sealed class Data
        {
            public Dictionary<string, Quest> Quests;
            public Dictionary<string, Gem> Gems;
            public string Error;
        }
        private static readonly Lazy<Data> Database = new Lazy<Data>(Load);

        private static Data Load()
        {
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var raw = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "poe1_gem_acquisition.json")));
                var data = new Data { Gems = new Dictionary<string, Gem>(StringComparer.OrdinalIgnoreCase),
                    Quests = serializer.ConvertToType<Dictionary<string, Quest>>(raw["_quests"]) };
                foreach (var pair in raw.Where(p => !p.Key.StartsWith("_", StringComparison.Ordinal)))
                {
                    var gem = serializer.ConvertToType<Gem>(pair.Value);
                    foreach (var quest in gem.quests ?? new Dictionary<string, Availability>())
                    {
                        if (quest.Value == null || !data.Quests.ContainsKey(quest.Key))
                            throw new InvalidDataException("Invalid acquisition quest for " + pair.Key);
                        if (quest.Value.vendor != null)
                            Vendor(data.Quests[quest.Key].act, quest.Key);
                    }
                    if (gem.level.HasValue) data.Gems.Add(pair.Key, gem);
                }
                if (data.Gems.Count < 300 || data.Quests.Count < 10) throw new InvalidDataException("Gem acquisition dataset is incomplete.");
                return data;
            }
            catch (Exception ex)
            {
                Logger.Error("Cannot load PoE1 gem acquisition data.", ex);
                return new Data { Error = "Purchase data unavailable: " + ex.Message };
            }
        }

        public static GemAcquisitionInfo Lookup(SkillGem gem, string characterClass, int? characterLevel)
        {
            string name = gem.Name ?? "";
            var data = Database.Value;
            Gem record = null;
            if (data.Error == null) data.Gems.TryGetValue(name, out record);
            if (data.Error == null && record == null && gem.Kind == "Support" && !name.EndsWith(" Support", StringComparison.OrdinalIgnoreCase))
            {
                data.Gems.TryGetValue(name + " Support", out record);
                if (record != null) name += " Support";
            }
            string url = "https://www.poewiki.net/wiki/" + Uri.EscapeDataString(name.Replace(' ', '_'));
            int? required = gem.RequiredLevel ?? (record == null ? null : record.level);
            string requirements = required.HasValue ? "Use from character L" + required + " (gem L1)" : "Required character level unknown";
            if (required.HasValue && characterLevel.HasValue && characterLevel.Value < required.Value)
                requirements += "; Current L" + characterLevel + " is too low to use";
            if (gem.TargetRequiredLevel.HasValue)
                requirements += "; guide gem L" + (gem.Level ?? 1) + " needs character L" + gem.TargetRequiredLevel;
            var result = new GemAcquisitionInfo { Requirements = requirements, SourceUrl = url,
                MinimumLevel = required, IsVendorGem = false };
            if (name.StartsWith("Awakened ", StringComparison.OrdinalIgnoreCase))
                result.Where = "Not a campaign vendor gem. Awakened gem: special endgame drops/rewards or player trade; check this gem's wiki.";
            else if (name.StartsWith("Vaal ", StringComparison.OrdinalIgnoreCase))
                result.Where = "Not sold by campaign gem vendors. Vaal version: Vaal side areas, corruption or player trade; check this gem's wiki.";
            else if (new[] { "Empower Support", "Enhance Support", "Enlighten Support" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                result.Where = "Exceptional support: drops, appropriate divination-card rewards or player trade; not sold by ordinary campaign gem vendors.";
            else if (name.Equals("Portal", StringComparison.OrdinalIgnoreCase))
                result.Where = "Portal gem: drops, divination-card rewards or player trade; not an ordinary campaign vendor gem.";
            else if (name.IndexOf(" of ", StringComparison.OrdinalIgnoreCase) >= 0 && (record == null || record.quests == null || record.quests.Count == 0))
                result.Where = "Transfigured version: Divine Font in the Labyrinth or player trade, not ordinary campaign vendors. Check the exact variant.";
            else if (data.Error != null)
            {
                result.IsVendorGem = null;
                result.Where = data.Error;
            }
            else if (record == null)
            {
                result.IsVendorGem = null;
                result.Where = "No verified purchase entry for this exact gem/variant; it may be item-granted, special or newer than the dataset. Check wiki; do not assume Lilly sells it.";
            }
            else
            {
                var rows = new List<Tuple<int, string>>();
                bool sold = false;
                foreach (var pair in record.quests ?? new Dictionary<string, Availability>())
                {
                    Quest quest;
                    if (!data.Quests.TryGetValue(pair.Key, out quest))
                    {
                        Logger.Error("Unknown acquisition quest: " + pair.Key);
                        result.IsVendorGem = null;
                        result.Where = "Purchase data invalid for this gem: unknown quest " + pair.Key + ". Check wiki.";
                        return result;
                    }
                    if (pair.Value.vendor == null) continue;
                    sold = true;
                    string restriction = pair.Value.vendor.Count == 0 ? "all classes" : string.Join(", ", pair.Value.vendor.Select(Title));
                    bool eligible = pair.Value.vendor.Count == 0 || (!string.IsNullOrEmpty(characterClass)
                        && pair.Value.vendor.Contains(characterClass.ToLowerInvariant()));
                    string row = Vendor(quest.act, pair.Key) + " after " + QuestName(pair.Key) + " [" + restriction + "]";
                    if (!eligible) row += string.IsNullOrEmpty(characterClass) ? " (Current class unknown)" : " (not for Current " + characterClass + ")";
                    rows.Add(Tuple.Create(eligible ? quest.act : quest.act + 100, row));
                }
                if (sold)
                {
                    result.IsVendorGem = true;
                    if (!rows.Any(r => r.Item2.StartsWith("Act 6", StringComparison.Ordinal)))
                        rows.Add(Tuple.Create(6, "Act 6 Lilly Roth, Lioneye's Watch / hideout after Fallen from Grace [all classes]"));
                    result.Where = string.Join("; ", rows.OrderBy(r => r.Item1).Select(r => r.Item2));
                }
                else
                    result.Where = "No campaign vendor purchase recorded. Special/drop/reward/trade acquisition; check the gem's wiki (not assumed available at Lilly).";
            }
            return result;
        }

        private static string Title(string text)
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text ?? "");
        }
        private static string QuestName(string key)
        {
            return Title(key.TrimEnd('1', '2'));
        }
        private static string Vendor(int act, string quest)
        {
            // Dataset NPCs are quest-givers; ordinary gem purchases use the town gem vendor.
            if (quest == "a fixture of fate") return "Act 3 Siosa, Library";
            switch (act)
            {
                case 1: return "Act 1 Nessa, Lioneye's Watch";
                case 2: return "Act 2 Yeena, Forest Encampment";
                case 3: return "Act 3 Clarissa, Sarn Encampment";
                case 4: return "Act 4 Petarus and Vanja, Highgate";
                case 6: return "Act 6 Lilly Roth, Lioneye's Watch / hideout";
                default: throw new InvalidDataException("Unsupported vendor act in gem acquisition data: " + act);
            }
        }
    }
}
