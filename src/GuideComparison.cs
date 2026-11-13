using System;
using System.Collections.Generic;

namespace Poe2ItemCompanion
{
    public static class GuideComparison
    {
        public static ComparisonResult Compare(AppConfig config, PobLuaEngine wornEngine, string item, out string error)
        {
            var worn = config.LoadConfiguredBuild(wornEngine, out error, true);
            if (worn == null || !worn.Success) return null;
            var result = wornEngine.CompareItem(item, out error);
            if (result == null || !result.Success) return result;
            string guideError;
            BuildInfo guide;
            BuildDetails guideDetails = null;
            using (var guideEngine = new PobLuaEngine())
            {
                if (!guideEngine.Initialize(config.PobPath, AppConfig.GetWrapperScriptPath(), out guideError, config.Game))
                    guide = null;
                else
                {
                    guide = config.LoadGuideBuild(guideEngine, out guideError);
                    if (guide != null && guide.Success) { guideDetails = guideEngine.GetBuildDetails(out guideError); GggGuide.Decorate(guide, guideDetails); }
                }
            }
            foreach (var slot in result.Comparisons)
            {
                if (guideDetails != null && guideDetails.Success && guideDetails.Ggg != null) { RateAgainstBuildNotes(slot, guideDetails, result.Affixes); continue; }
                List<ItemAffix> goalAffixes = null;
                if (guideDetails != null && guideDetails.Success) slot.GuideStage = guideDetails.ActiveLoadout;
                slot.GuideFit = guide == null || !guide.Success
                    ? "Guide fit unavailable: " + guideError + ". Current-gear deltas remain valid."
                    : Assess(guide.GuideTargets, slot.Before, slot.After);
                if (guideDetails != null && guideDetails.Success)
                    foreach (var gear in guideDetails.Gear)
                        if (gear.SlotName == slot.SlotName && !gear.InactiveWeaponSet)
                        {
                            slot.GuideFit = "Guide slot goal" + (guideDetails.ActiveLoadout != null ? " (" + guideDetails.ActiveLoadout + ")" : "") + ": " + gear.ItemName + "\n" + slot.GuideFit;
                            slot.GuideItemName = gear.ItemName;
                            if (!string.IsNullOrEmpty(gear.ItemName)) goalAffixes = gear.Affixes;
                        }
                slot.ItemStats = ItemStatRatings.Rate(result.Affixes, goalAffixes);
                if (goalAffixes == null)
                    slot.ItemRatingNotice = guide == null || !guide.Success
                        ? "Guide item unavailable: " + guideError
                        : "No comparable guide item in this slot. Stats are unrated.";
                if (guide != null && guide.Success && (guideDetails == null || !guideDetails.Success))
                    slot.GuideFit += "\nGuide slot goal unavailable: " + guideError;
            }
            return result;
        }

        // GGG .build guides hold slot notes (unique name or base + stat priority), not items with rolls.
        private static void RateAgainstBuildNotes(SlotComparison slot, BuildDetails guide, List<ItemAffix> affixes)
        {
            slot.GuideStage = guide.ActiveLoadout;
            GearEntry note = null;
            foreach (var gear in guide.Gear)
                if (gear.SlotName == slot.SlotName && !gear.InactiveWeaponSet) note = gear;
            string head = "Guide (.build, " + guide.ActiveLoadout + ")";
            if (note == null)
            {
                slot.GuideFit = head + ": no note for this slot at this stage. Current-gear deltas remain valid.";
                slot.ItemStats = GggGuide.RateAgainstNotes(affixes, null);
                slot.ItemRatingNotice = "No .build note for this slot. Stats are unrated.";
                return;
            }
            slot.GuideItemName = note.ItemName;
            slot.GuideFit = head + " slot goal: " + note.ItemName +
                (note.Rarity == "UNIQUE" ? " (unique named by the guide; its rolls are not in the .build)" : "") +
                "\nGuide stat totals unavailable: a .build has no rare rolls, so PoB2 cannot calculate the guide's targets." +
                "\nHEURISTIC guide fit: keyword match against this slot's stat priority note, not BIS.";
            slot.ItemStats = GggGuide.RateAgainstNotes(affixes, note);
            slot.ItemRatingNotice = note.Priorities == null || note.Priorities.Count == 0
                ? "The .build note for this slot lists no stat priority. Stats are unrated."
                : "HEURISTIC (.build note): green = named in the slot's stat priority, red = not named. No roll targets, so no orange/gold.";
        }

        public static string Assess(Dictionary<string, double> target, Dictionary<string, double> before, Dictionary<string, double> after)
        {
            if (target == null || before == null || after == null)
                return "Guide fit unavailable: calculated stat totals are missing.";
            var lines = new List<string>();
            double progress = 0;
            int gaps = 0;
            int losses = 0;
            foreach (var stat in target)
            {
                double oldValue, newValue;
                if (!before.TryGetValue(stat.Key, out oldValue) || !after.TryGetValue(stat.Key, out newValue)
                    || double.IsNaN(stat.Value) || double.IsInfinity(stat.Value)
                    || double.IsNaN(oldValue) || double.IsInfinity(oldValue)
                    || double.IsNaN(newValue) || double.IsInfinity(newValue)) continue;
                double gap = stat.Value - oldValue;
                if (newValue < oldValue - 0.001) losses++;
                if (gap <= 0.001)
                {
                    if (newValue < oldValue - 0.001)
                        lines.Add(stat.Key + ": loses " + (oldValue - newValue).ToString("0.##") +
                            (newValue < stat.Value ? " (falls below guide)" : " (still meets guide)"));
                    continue;
                }
                double closed = Math.Max(-1, Math.Min(1, (newValue - oldValue) / gap));
                gaps++;
                progress += closed;
                lines.Add(stat.Key + ": " + oldValue.ToString("0.##") + " -> " + newValue.ToString("0.##") +
                    " / guide " + stat.Value.ToString("0.##") + " (" + closed.ToString("+0%;-0%;0%") + " of gap)");
            }
            string summary = gaps == 0 ? "No measured guide stat gaps."
                : "Guide gap closure: " + (progress / gaps).ToString("+0%;-0%;0%") + " (equal-weight, " + gaps + " stat gaps).";
            if (losses > 0) summary += " Tradeoff: " + losses + " measured stat loss(es); see below.";
            return summary + "\n" + string.Join("\n", lines.ToArray()) +
                "\nGuide-relative heuristic, not BIS. Different trees/configs affect targets; DPS, special affixes and skill synergies are not scored.";
        }
    }
}
