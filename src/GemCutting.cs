using System;
using System.Collections.Generic;
using System.Linq;

namespace Poe2ItemCompanion
{
    public class GemCutEntry
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public int Color { get; set; }
        public int Tier { get; set; }
        public string WeaponRequirements { get; set; }
        public string Tags { get; set; }
        public int GuideLevel { get; set; }
        public bool GuideEnabled { get; set; }
        public int? CurrentLevel { get; set; }
        public string Status { get; set; }
        public bool NeedsCut { get; set; }
        public string UncutGem { get; set; }
        public string CutterTab { get; set; }
        public bool Poe1 { get; set; }
        public int GuideQuality { get; set; }
        public bool IsUnderQuality { get; set; }
        public GemAcquisitionInfo Acquisition { get; set; }

        public bool IsUnderLevel
        {
            get { return CurrentLevel.HasValue && (Poe1 || Kind != "Support") && CurrentLevel.Value < GuideLevel; }
        }
    }

    // Lists the guide stage's gems with the PoB2 tier needed in the in-game gem cutting window.
    public static class GemCutting
    {
        public static string Roman(int n)
        {
            if (n <= 0) return "-";
            var map = new[] { new KeyValuePair<int, string>(10, "X"), new KeyValuePair<int, string>(9, "IX"), new KeyValuePair<int, string>(5, "V"), new KeyValuePair<int, string>(4, "IV"), new KeyValuePair<int, string>(1, "I") };
            string s = "";
            foreach (var kv in map) while (n >= kv.Key) { s += kv.Value; n -= kv.Key; }
            return s;
        }

        public static List<GemCutEntry> Plan(BuildDetails guide, BuildDetails current)
        {
            if (guide == null) return new List<GemCutEntry>();
            if (current != null && !string.Equals(guide.Game, current.Game, StringComparison.Ordinal))
                throw new InvalidOperationException("Current and Guide gems must belong to the same game.");
            if (guide.Game == "poe1") return PlanPoe1(guide, current);
            var result = new List<GemCutEntry>();
            var available = AvailableGems(current);
            foreach (var group in guide.Skills)
                foreach (var gem in group.Gems)
                {
                    var match = Match(available, gem);
                    bool required = group.Enabled && gem.Enabled;
                    var e = new GemCutEntry { Name = gem.Name, Kind = gem.Kind, Color = gem.Color, Tier = gem.Tier ?? 0,
                        WeaponRequirements = gem.WeaponRequirements, Tags = gem.Tags, GuideLevel = gem.Level ?? 1,
                        GuideEnabled = required, CurrentLevel = match == null ? null : match.Level };
                    if (required && match != null) available.Remove(match);
                    e.NeedsCut = required && match == null && e.Tier > 0;
                    e.Status = !required ? "Disabled in guide" : current == null ? "Current character not loaded"
                        : match != null ? "Have level " + match.Level + (e.IsUnderLevel ? ", guide uses " + e.GuideLevel : "")
                        : e.Tier <= 0 ? "Not cuttable / item-granted; check equipment" : "Missing instance - cut this";
                    result.Add(e);
                }

            foreach (var e in result)
            {
                if (e.Tier <= 0) e.UncutGem = "Not cut from an uncut gem (PoB2 tier 0, e.g. item-granted)";
                else if (e.Kind == "Support") e.UncutGem = "Uncut Support Gem level " + e.Tier + "+";
                else e.UncutGem = "Uncut " + e.Kind + " Gem level " + e.Tier + "+ (cut gem level = uncut gem level; guide uses " + e.GuideLevel + ")";
                e.CutterTab = e.Kind == "Support" ? "Support gems" : e.Kind == "Spirit" ? "Spirit gems"
                    : !string.IsNullOrEmpty(e.WeaponRequirements) ? e.WeaponRequirements : "No weapon tab in PoB2 data - type the name in the search box";
            }
            return result;
        }

        // Same entries, arranged like the guide's skill groups (skill first, then its supports).
        public static List<GemCutGroup> PlanGroups(BuildDetails guide, BuildDetails current)
        {
            var groups = new List<GemCutGroup>();
            if (guide == null) return groups;
            var entries = Plan(guide, current);
            int index = 0;
            foreach (var group in guide.Skills)
            {
                var result = new GemCutGroup { Label = group.Label + (string.IsNullOrEmpty(group.Slot) ? "" : " [" + group.Slot + "]"),
                    Enabled = group.Enabled, Main = group.Main };
                foreach (var gem in group.Gems) result.Entries.Add(entries[index++]);
                if (guide.Game != "poe1")
                {
                    result.Entries = result.Entries.Where(e => e.Kind != "Support").Concat(result.Entries.Where(e => e.Kind == "Support")).ToList();
                }
                if (string.IsNullOrWhiteSpace(result.Label))
                    result.Label = result.Entries.Where(e => e.Kind != "Support").Select(e => e.Name).FirstOrDefault() ?? "Unlabeled group";
                if (result.Entries.Count > 0) groups.Add(result);
            }
            return groups;
        }

        private static List<SkillGem> AvailableGems(BuildDetails current)
        {
            return current == null ? new List<SkillGem>() : current.Skills
                .Where(g => g.Enabled).SelectMany(g => g.Gems).Where(g => g.Enabled).ToList();
        }

        private static SkillGem Match(List<SkillGem> available, SkillGem gem)
        {
            return available.Where(g => ((!string.IsNullOrEmpty(gem.Id) && !string.IsNullOrEmpty(g.Id))
                        ? string.Equals(g.Id, gem.Id, StringComparison.OrdinalIgnoreCase)
                        : !string.IsNullOrEmpty(gem.Name) && string.Equals(g.Name, gem.Name, StringComparison.OrdinalIgnoreCase))
                    && string.Equals(g.Variant ?? "", gem.Variant ?? "", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(g.Kind ?? "", gem.Kind ?? "", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(g => (g.Level ?? 0) >= (gem.Level ?? 1) && (g.Quality ?? 0) >= (gem.Quality ?? 0))
                .ThenByDescending(g => g.Level ?? 0).FirstOrDefault();
        }

        private static List<GemCutEntry> PlanPoe1(BuildDetails guide, BuildDetails current)
        {
            var available = AvailableGems(current);
            var result = new List<GemCutEntry>();
            foreach (var group in guide.Skills)
                foreach (var gem in group.Gems)
                {
                    var match = Match(available, gem);
                    bool required = group.Enabled && gem.Enabled;
                    var e = new GemCutEntry { Name = gem.Name, Kind = gem.Kind ?? "Skill", Color = gem.Color, Poe1 = true,
                        GuideLevel = gem.Level ?? 1, GuideQuality = gem.Quality ?? 0, GuideEnabled = required,
                        CurrentLevel = match == null ? (int?)null : match.Level, NeedsCut = required && match == null,
                        IsUnderQuality = match != null && (match.Quality ?? 0) < (gem.Quality ?? 0),
                        CutterTab = group.Slot ?? "Unassigned socket group",
                        UncutGem = "Level " + (gem.Level ?? 1) + ", quality " + (gem.Quality ?? 0) + "%; check equipment colours and links",
                        Status = !required ? "Disabled in guide" : current == null ? "Current character not loaded"
                            : match == null ? "Missing instance"
                            : "Have level " + match.Level + ", quality " + (match.Quality ?? 0) + "%"
                                + (((match.Level ?? 0) < (gem.Level ?? 1) || (match.Quality ?? 0) < (gem.Quality ?? 0)) ? " (below guide)" : "") };
                    if (required && match != null) available.Remove(match);
                    e.Acquisition = GemAcquisition.Lookup(gem, current == null ? null : current.ClassName,
                        current == null || current.Level <= 0 ? (int?)null : current.Level);
                    result.Add(e);
                }
            return result;
        }
    }

    public class GemCutGroup
    {
        public string Label { get; set; }
        public bool Main { get; set; }
        public bool Enabled { get; set; }
        public List<GemCutEntry> Entries { get; set; }

        public GemCutGroup() { Entries = new List<GemCutEntry>(); }

        public bool NeedsWork
        {
            get { return Entries.Any(e => e.GuideEnabled && (e.NeedsCut || e.IsUnderLevel || e.IsUnderQuality)); }
        }
    }
}
