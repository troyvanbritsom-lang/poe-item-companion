using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    public class ItemAffix
    {
        public string Text { get; set; }
        public bool Understood { get; set; }
        public bool Scalable { get; set; }
    }

    public class ItemStatRating
    {
        public string Text { get; set; }
        public string Rating { get; set; }
        public string Explanation { get; set; }
    }

    public static class ItemStatRatings
    {
        private static readonly Regex Numbers = new Regex(@"[+-]?\d+(?:\.\d+)?");

        private static string Key(string text)
        {
            return Regex.Replace(Numbers.Replace(text.ToLowerInvariant(), "#"), @"\s+", " ").Trim();
        }

        private static string[] Keys(string text)
        {
            var match = Regex.Match(text ?? "", @"^\+[\d.]+% to (Fire|Cold|Lightning|Chaos|all Elemental|Fire and Cold|Fire and Lightning|Cold and Lightning) Resistances?$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string types = match.Groups[1].Value.ToLowerInvariant();
                if (types == "all elemental") types = "fire and cold and lightning";
                return types.Split(new[] { " and " }, StringSplitOptions.None).Select(t => "resistance:" + t).ToArray();
            }
            match = Regex.Match(text ?? "", @"^\+[\d.]+ to (Strength|Dexterity|Intelligence|all Attributes)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string type = match.Groups[1].Value.ToLowerInvariant();
                return type == "all attributes" ? new[] { "attribute:strength", "attribute:dexterity", "attribute:intelligence" } : new[] { "attribute:" + type };
            }
            return new[] { Key(text ?? "") };
        }

        public static List<ItemStatRating> Rate(List<ItemAffix> candidate, List<ItemAffix> guide)
        {
            var ratings = new List<ItemStatRating>();
            foreach (var affix in candidate ?? new List<ItemAffix>())
            {
                string text = affix.Text ?? "";
                var row = new ItemStatRating { Text = text, Rating = "Unrated", Explanation = "No guide item or comparable parsed stat." };
                ratings.Add(row);
                if (guide == null) continue;
                if (!affix.Understood || !affix.Scalable)
                {
                    row.Explanation = "Unrated: unsupported modifier or catalyst scaling.";
                    continue;
                }
                var keys = Keys(text);
                var matches = guide.Where(g => Keys(g.Text).Intersect(keys).Any()).ToList();
                if (matches.Count == 0)
                {
                    row.Rating = "Red";
                    row.Explanation = "Not present on the guide item in this slot; may still help your character.";
                    continue;
                }
                if (keys.Any(k => !matches.Any(g => Keys(g.Text).Contains(k))))
                {
                    row.Explanation = "Partial match only: some stats in this combined modifier are absent on the guide item.";
                    continue;
                }
                // Repeated affixes can come from implicits, runes or explicit mods: do not guess a total.
                if (keys.Any(k => guide.Count(g => Keys(g.Text).Contains(k)) > 1) ||
                    matches.Any(g => !g.Scalable || !g.Understood) ||
                    keys.Any(k => candidate.Count(c => Keys(c.Text).Contains(k)) > 1))
                {
                    row.Explanation = "Unrated: repeated stat lines or catalyst scaling need manual comparison.";
                    continue;
                }
                row.Explanation = "Guide: " + string.Join("; ", matches.Select(g => g.Text).ToArray());
                var values = Numbers.Matches(text).Cast<Match>().Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
                var targets = matches.SelectMany(g => Numbers.Matches(g.Text).Cast<Match>().Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))).ToArray();
                if (values.Length == 0 && matches.Count == 1 && string.Equals(text, matches[0].Text, StringComparison.OrdinalIgnoreCase))
                {
                    row.Rating = "Green";
                    continue;
                }
                if (keys[0].StartsWith("resistance:") || keys[0].StartsWith("attribute:"))
                {
                    // One combined line contributes the same magnitude to each of its named stats.
                    if (values.Length == 1 && targets.Length > 1) values = targets.Select(t => values[0]).ToArray();
                }
                if (values.Length != targets.Length || values.Length == 0 || targets.Any(v => v <= 0) || values.Any(v => v < 0))
                    continue;
                // Compare only familiar beneficial magnitudes. Conditional, inverse and mixed effects stay unrated.
                if (!keys[0].StartsWith("resistance:") && !keys[0].StartsWith("attribute:") &&
                    !Regex.IsMatch(text, @"^(?:\+[\d.]+ to maximum (?:Life|Mana|Energy Shield)|[\d.]+% increased (?:Movement Speed|Attack Speed|Cast Speed|Armour|Evasion Rating|Energy Shield|maximum Life|maximum Mana)|Adds [\d.]+ to [\d.]+ (?:Physical|Fire|Cold|Lightning|Chaos) Damage)$", RegexOptions.IgnoreCase))
                {
                    if (values.SequenceEqual(targets)) row.Rating = "Green";
                    else row.Explanation += " (unrated: modifier direction or conditions need manual comparison)";
                    continue;
                }
                if (values.Where((v, i) => v < targets[i] * 0.9 - 0.000001).Any())
                    row.Rating = "Orange";
                else if (values.Where((v, i) => v < targets[i] - 0.000001).Any())
                    row.Rating = "Green";
                else row.Rating = values.Where((v, i) => v > targets[i] + 0.000001).Any() ? "Gold" : "Green";
            }
            return ratings;
        }

        public static Control CreateView(SlotComparison slot, int width, float fontSize)
        {
            var view = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Width = width, MaximumSize = new Size(width, 0), Margin = Padding.Empty };
            view.Controls.Add(new Label { Text = "Guide item" + (string.IsNullOrEmpty(slot.GuideStage) ? "" : " (" + slot.GuideStage + ")") + ": " + (slot.GuideItemName ?? "unavailable"), ForeColor = Color.LightSkyBlue, AutoSize = true, MaximumSize = new Size(width, 0) });
            if (!string.IsNullOrEmpty(slot.CompatibilityNotice))
                view.Controls.Add(new Label { Text = slot.CompatibilityNotice, ForeColor = Color.Orange, AutoSize = true, MaximumSize = new Size(width, 0) });
            if (!string.IsNullOrEmpty(slot.ItemRatingNotice))
                view.Controls.Add(new Label { Text = slot.ItemRatingNotice, ForeColor = Color.Silver, AutoSize = true, MaximumSize = new Size(width, 0) });
            foreach (var stat in slot.ItemStats ?? new List<ItemStatRating>())
            {
                Color color = stat.Rating == "Red" ? Color.Salmon : stat.Rating == "Orange" ? Color.Orange :
                    stat.Rating == "Green" ? Color.LightGreen : stat.Rating == "Gold" ? Color.Gold : Color.Silver;
                var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, MaximumSize = new Size(width, 0), Margin = Padding.Empty };
                var text = new Label { Text = stat.Text, ForeColor = Color.FromArgb(205, 205, 240), AutoSize = true, MaximumSize = new Size(width - 35, 0), Font = new Font("Segoe UI", fontSize), Margin = new Padding(0, 2, 2, 2) };
                var star = new Label { Text = stat.Rating == "Unrated" ? "?" : "*", ForeColor = color, AutoSize = true, Font = new Font("Segoe UI", fontSize + 2, FontStyle.Bold), AccessibleName = stat.Rating + ": " + stat.Explanation, Margin = Padding.Empty };
                var tip = new ToolTip();
                tip.SetToolTip(star, stat.Rating + " - " + stat.Explanation);
                tip.SetToolTip(text, stat.Rating + " - " + stat.Explanation);
                row.Disposed += (s, e) => tip.Dispose();
                row.Controls.Add(text);
                row.Controls.Add(star);
                view.Controls.Add(row);
            }
            string legend = "Red: absent | Orange: <90% | Green: 90-100% | Gold: above guide\n?: unrated. Same-slot guide fit, not universal BIS.";
            if (slot.ItemStats == null || slot.ItemStats.Count == 0) legend = "No item modifiers available to rate.\n" + legend;
            view.Controls.Add(new Label { Text = legend, ForeColor = Color.Gray, AutoSize = true, MaximumSize = new Size(width, 0), Font = new Font("Segoe UI", 8) });
            view.Controls.Add(new Label { Text = "PoB changes vs Current gear:", ForeColor = Color.White, AutoSize = true });
            return view;
        }
    }
}
