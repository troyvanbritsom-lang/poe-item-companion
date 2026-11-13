using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Poe2ItemCompanion
{
    // In-game panel: the guide stage's skill groups with the gems still needed.
    public class GemOverlay : OverlayForm
    {
        private static readonly Color MissingColor = UiTheme.Accent;
        private static readonly Color UnderColor = Color.FromArgb(255, 160, 60);
        private static readonly Color HaveColor = UiTheme.Muted;

        private List<GemCutEntry> _plan;
        private List<GemCutGroup> _groups;
        private string _stage;
        private string _error;

        public GemOverlay(AppConfig config) : base(config, 460) { }

        protected override string LoadingTitle { get { return "Gems needed - loading..."; } }

        protected override bool ShowAll
        {
            get { return _config.GemOverlayShowAll; }
            set { _config.GemOverlayShowAll = value; }
        }

        protected override Point SavedPosition
        {
            get { return new Point(_config.GemOverlayX, _config.GemOverlayY); }
            set { _config.GemOverlayX = value.X; _config.GemOverlayY = value.Y; _config.Save(); }
        }

        protected override Point DefaultPosition(Rectangle area)
        {
            return new Point(area.Right - Width - 20, area.Top + 120);
        }

        protected override void LoadData(BuildDetails guide, BuildDetails current, string guideError, string currentError)
        {
            if (guide == null)
            {
                _plan = null;
                _groups = null;
                _error = "Guide unavailable: " + guideError;
                return;
            }
            _plan = GemCutting.Plan(guide, current);
            _groups = GemCutting.PlanGroups(guide, current);
            _stage = guide.ActiveLoadout ?? "saved sets";
            _error = current == null ? "Current character not loaded" + (string.IsNullOrEmpty(currentError) ? "" : ": " + currentError) : null;
        }

        private static string Row(GemCutEntry e)
        {
            string mark = e.NeedsCut ? "+" : e.IsUnderLevel || e.IsUnderQuality ? "^" : " ";
            string tab = e.Kind == "Support" ? "Support" : e.Kind == "Spirit" ? "Spirit" : string.IsNullOrEmpty(e.WeaponRequirements) ? "search name" : e.WeaponRequirements;
            string name = (e.Kind == "Support" ? "  " : "") + e.Name;
            if (e.Poe1)
                return " " + mark + " " + name.PadRight(30) + " " + (e.Acquisition == null ? "unknown" : e.Acquisition.CompactLevel);
            string text = " " + mark + " " + name.PadRight(26) + " " + GemCutting.Roman(e.Tier).PadRight(5) + tab;
            if (!e.NeedsCut && e.CurrentLevel.HasValue) text += e.IsUnderLevel ? "  (L" + e.CurrentLevel + "/" + e.GuideLevel + ")" : "  (have)";
            return text;
        }

        protected override void RenderContent()
        {
            if (_plan == null)
            {
                SetTitle("Gems needed");
                AddLine(_error ?? "No guide loaded.", HeaderColor, false);
                SetFooter("Load a guide and a Current character in the companion.");
                return;
            }
            int missing = _plan.Count(p => p.NeedsCut);
            SetTitle("Gems needed - " + _stage + " (" + missing + " missing)");
            foreach (var g in _groups)
            {
                if (!ShowAll && !g.NeedsWork) continue;
                AddLine(g.Label + (g.Main ? "  [main]" : "") + (g.Enabled ? "" : "  [disabled in guide]"), HeaderColor, true);
                foreach (var e in g.Entries)
                {
                    string details = e.Poe1 && e.Acquisition != null
                        ? e.Name + Environment.NewLine + e.Acquisition.Requirements + Environment.NewLine + Environment.NewLine
                            + e.Acquisition.Where.Replace("; ", Environment.NewLine) + Environment.NewLine + Environment.NewLine
                            + "Quest completion is unknown; check your quest log."
                        : null;
                    AddLine(Row(e), e.NeedsCut ? MissingColor : e.IsUnderLevel || e.IsUnderQuality ? UnderColor : HaveColor, false, details);
                }
            }
            if (LineCount == 0)
                AddLine(missing == 0 ? "You have every gem this stage uses." : "Nothing to show.", HeaderColor, false);
            SetFooter((_config.Game == GameId.Poe1 ? "Hover gems for vendor/quest details. * quest unlock required. ? purchase unverified."
                : "+ missing (gold)  ^ lower level (orange)  then: tier, gem cutter tab") + Environment.NewLine +
                (_error ?? "Hotkey " + _config.GetGemOverlayHotkeyDisplayString() + " toggles; drag to move; 'all' shows complete groups."));
        }
    }
}
