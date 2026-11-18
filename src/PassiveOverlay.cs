using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Poe2ItemCompanion
{
    // In-game panel: which guide-stage passives to allocate next, starting from the Current character's tree.
    public class PassiveOverlay : OverlayForm
    {
        public const int DefaultTargets = 4;
        private static readonly Color TargetColor = UiTheme.Accent;
        private static readonly Color PathColor = UiTheme.Text;
        private static readonly Color InfoColor = UiTheme.Muted;
        private static readonly Color RespecColor = Color.FromArgb(235, 90, 80);

        private PassivePlanResult _plan;
        private string _stage;
        private string _error;
        private int _currentLevel;

        public PassiveOverlay(AppConfig config) : base(config, 520) { }

        public PassivePlanResult PlanResult { get { return _plan; } }
        public BuildDetails GuideDetails { get; private set; }
        public BuildDetails CurrentDetails { get; private set; }

        protected override string LoadingTitle { get { return "Passives - loading..."; } }

        protected override bool ShowAll
        {
            get { return _config.PassiveOverlayShowAll; }
            set { _config.PassiveOverlayShowAll = value; }
        }

        protected override Point SavedPosition
        {
            get { return new Point(_config.PassiveOverlayX, _config.PassiveOverlayY); }
            set { _config.PassiveOverlayX = value.X; _config.PassiveOverlayY = value.Y; _config.Save(); }
        }

        protected override Point DefaultPosition(Rectangle area)
        {
            return new Point(area.Left + 20, area.Top + 120);
        }

        protected override void LoadData(BuildDetails guide, BuildDetails current, string guideError, string currentError)
        {
            GuideDetails = guide;
            CurrentDetails = current;
            if (guide == null || guide.Tree == null || guide.Tree.Nodes.Count == 0)
            {
                _plan = null;
                _error = "Guide unavailable: " + (guideError ?? "no passive tree");
                return;
            }
            _plan = PassivePlan.Plan(guide, current);
            _stage = guide.ActiveLoadout ?? "saved sets";
            _currentLevel = current != null ? current.Level : 0;
            _error = current == null ? "Current character not loaded" + (string.IsNullOrEmpty(currentError) ? "" : ": " + currentError) + " - showing the guide tree from the start." : null;
        }

        private static string Row(int number, TreeNode n)
        {
            string name = n.Name ?? "Unnamed";
            if (n.WeaponSet > 0) name += " [set " + n.WeaponSet + "]";
            return " " + number.ToString().PadLeft(2) + ". " + name.PadRight(28) + " " + PassivePlan.Describe(n);
        }

        private int AddTargets(List<PassiveTarget> targets, int number, int limit)
        {
            foreach (var t in targets.Take(limit))
            {
                string head = t.Target != null
                    ? "Next: " + t.Target.Name + "  [" + t.Target.Type + "]"
                    : "Remaining small passives";
                AddLine(head + "  (" + t.Path.Count + (t.Path.Count == 1 ? " point)" : " points)"), HeaderColor, true);
                foreach (var n in t.Path) AddLine(Row(number++, n), n == t.Target ? TargetColor : PathColor, false);
            }
            return number;
        }

        protected override void RenderContent()
        {
            if (_plan == null)
            {
                SetTitle("Passives");
                AddLine(_error ?? "No guide loaded.", HeaderColor, false);
                SetFooter("Load a guide (and a Current character) in the companion.");
                return;
            }
            if (_plan.Notice != null)
            {
                SetTitle("Passives - incompatible snapshots");
                AddLine(_plan.Notice, RespecColor, false);
                SetFooter("Select matching game/tree/cluster contexts. No allocation or refund advice is shown.");
                return;
            }
            int toGo = _plan.MissingCount;
            SetTitle("Passives - " + _stage + " (" + toGo + " to go)");
            AddLine(" You: " + _plan.CurrentPoints + " points" + (_currentLevel > 0 ? " (level " + _currentLevel + ")" : "") +
                "   Guide stage: " + _plan.GuidePoints + "   Ascendancy " + _plan.CurrentAscendancyPoints + "/" + _plan.GuideAscendancyPoints, InfoColor, false);

            int limit = ShowAll ? int.MaxValue : DefaultTargets;
            int number = AddTargets(_plan.Targets, 1, limit);
            if (!ShowAll && _plan.Targets.Count > limit)
                AddLine(" ... " + (_plan.Targets.Count - limit) + " more steps (" + _plan.Targets.Skip(limit).Sum(t => t.Path.Count) + " points) - click 'all'", InfoColor, false);
            if (_plan.Targets.Count == 0 && _plan.Unreachable.Count(n => !n.Ascendancy) == 0 && _plan.MasteryChanges.Count == 0)
                AddLine(" You have every passive of this guide stage.", HeaderColor, false);
            if (_plan.MasteryChanges.Count > 0)
            {
                AddLine("Mastery effects to select/change", HeaderColor, true);
                foreach (var node in _plan.MasteryChanges)
                    AddLine((node.Name ?? "Mastery") + " [effect " + node.MasteryEffect + "] " + PassivePlan.Describe(node), PathColor, false);
            }

            if (_plan.AscendancyTargets.Count > 0)
            {
                AddLine("Ascendancy (" + _plan.MissingAscendancyCount + " to go)", HeaderColor, true);
                int a = 1;
                foreach (var t in _plan.AscendancyTargets.Take(ShowAll ? int.MaxValue : 2))
                    foreach (var n in t.Path) AddLine(Row(a++, n), n == t.Target ? TargetColor : PathColor, false);
            }

            if (_plan.Unreachable.Count > 0)
            {
                AddLine("Not connected to your tree (" + _plan.Unreachable.Count + ")", HeaderColor, true);
                foreach (var n in _plan.Unreachable.Where(x => ShowAll || PassivePlan.IsImportant(x)).Take(ShowAll ? int.MaxValue : 6))
                    AddLine("     " + (n.Name ?? "Unnamed").PadRight(28) + " " + n.Type, PathColor, false);
            }

            if (_plan.NotInGuide.Count > 0)
            {
                var important = _plan.NotInGuide.Where(PassivePlan.IsImportant).ToList();
                AddLine("On your character, not in this stage (" + _plan.NotInGuide.Count + ")", HeaderColor, true);
                foreach (var n in (ShowAll ? _plan.NotInGuide : important.Take(5).ToList()))
                    AddLine("  x  " + (n.Name ?? "Unnamed").PadRight(28) + " " + n.Type, RespecColor, false);
                if (!ShowAll && _plan.NotInGuide.Count > Math.Min(5, important.Count))
                    AddLine("     + " + (_plan.NotInGuide.Count - Math.Min(5, important.Count)) + " more (refund later if you need the points)", InfoColor, false);
            }

            SetFooter("Gold = node the path leads to. Order: shortest path from your tree; the guide has no levelling order." + Environment.NewLine +
                (_error ?? "Selector " + EquipmentOverlay.SelectorKeyText(_config) + " shows/hides; based on your Current character file - re-import it in the matching PoB after levelling."));
        }
    }
}
