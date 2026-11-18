using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    internal static class QuestPlannerTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, Private).GetValue(instance);
        }

        public static int Run()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CompanionQuests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                Application.EnableVisualStyles();
                foreach (GameId game in new[] { GameId.Poe1, GameId.Poe2 })
                {
                    TestGems(game);
                    TestStoryFollowing(game, directory);
                    TestUi(new AppConfig { Game = game, DisableSave = true, PobPath = directory, BuildDirectory = directory,
                        GameLogPath = Path.Combine(directory, "missing.log"), GemIconDirectory = directory }, null);
                }
                TestPersistence(directory);
                Console.WriteLine("PASSED: both games' gem filtering, quest routes, live log act following/manual override, hidden-dashboard updates, resize grip/edges, saved dimensions and isolated profiles.");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("FAILED: " + ex); return 1; }
            finally
            {
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }

        private static SkillGem Gem(string name, int level)
        {
            return new SkillGem { Id = name, Name = name, Variant = "normal", Kind = "Skill", Enabled = true, Level = level, Quality = 20, Tier = 1 };
        }

        private static BuildDetails Build(GameId game, params SkillGem[] gems)
        {
            var build = new BuildDetails { Game = game == GameId.Poe1 ? "poe1" : "poe2", Success = true };
            build.Skills.Add(new SkillGroup { Enabled = true, Label = "Test sockets", Gems = gems.ToList() });
            return build;
        }

        private static void TestGems(GameId game)
        {
            var guide = Build(game, Gem("Fireball", 20), Gem("Fireball", 20), Gem("Ice Nova", 20));
            var current = Build(game, Gem("Fireball", 3));
            current.Skills[0].Gems[0].Quality = 0;
            var plan = GemCutting.Plan(guide, current);
            Require(plan.Count == 3 && plan.Count(e => e.NeedsCut) == 2 && plan.Count(e => e.CurrentLevel == 3) == 1,
                "A single equipped copy suppressed an unmet duplicate in " + game);
            Require(plan[0].IsUnderLevel && (game != GameId.Poe1 || plan[0].IsUnderQuality), "Existing upgrade status lost.");
            Require(GemCutting.PlanGroups(guide, current).SelectMany(g => g.Entries).Count(e => e.NeedsCut) == 2, "Grouped plan lost quantities.");
            current.Skills[0].Gems[0].Variant = "different";
            Require(GemCutting.Plan(guide, current).All(e => e.NeedsCut), "A different variant matched.");
            current.Skills[0].Gems[0].Variant = "normal";
            current.Skills[0].Gems[0].Id = "different-id";
            Require(GemCutting.Plan(guide, current).All(e => e.NeedsCut), "Matching names overrode conflicting identifiers.");
            current.Skills[0].Gems[0].Id = null;
            current.Skills[0].Gems[0].Name = "FIREBALL";
            Require(GemCutting.Plan(guide, current).Count(e => e.NeedsCut) == 2, "Name fallback for missing identifiers failed.");
            current.Skills[0].Enabled = false;
            Require(GemCutting.Plan(guide, current).All(e => e.NeedsCut), "Inactive Current setup supplied equipped instances.");
            Require(GemCutting.Plan(guide, null).All(e => e.NeedsCut && e.Status == "Current character not loaded"),
                "Missing Current silently claimed ownership.");
            var otherGame = Build(game == GameId.Poe1 ? GameId.Poe2 : GameId.Poe1, Gem("Fireball", 20));
            bool rejected = false;
            try { GemCutting.Plan(guide, otherGame); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Wrong-game gems suppressed guide requirements.");

            var mixedGuide = Build(game, Gem("Fireball", 20), Gem("Ice Nova", 20));
            current = Build(game, Gem("Fireball", 3));
            var config = new AppConfig { Game = game, DisableSave = true };
            using (var overlay = new GemOverlay(config))
            {
                typeof(GemOverlay).GetMethod("LoadData", Private).Invoke(overlay, new object[] { mixedGuide, current, null, null });
                typeof(OverlayForm).GetMethod("Render", Private).Invoke(overlay, null);
                Require(!overlay.BodyText.Contains("Fireball") && overlay.BodyText.Contains("Ice Nova"),
                    "Equipped lower-level gem appeared beside a missing gem in the default overlay.");
                config.GemOverlayShowAll = true;
                typeof(OverlayForm).GetMethod("Render", Private).Invoke(overlay, null);
                Require(overlay.BodyText.Contains("Fireball") && overlay.BodyText.Contains("Ice Nova"), "Opt-in equipped status disappeared.");
            }
            using (var view = new GuideBuildView())
            {
                view.SetSource(config);
                view.RenderGems(mixedGuide, current, null);
                var list = Field<ListView>(view, "_gemList");
                Require(list.Items.Count == 1 && list.Items[0].Text == "Ice Nova", "Dashboard gem-needed list retained equipped gems.");
                Field<CheckBox>(view, "_showEquipped").Checked = true;
                view.RenderGems(mixedGuide, current, null);
                Require(list.Items.Count == 2 && list.Items.Cast<ListViewItem>().Any(item => item.SubItems[1].Text.Contains("Have level 3")),
                    "Dashboard opt-in lost upgrade status.");
                view.RenderGems(mixedGuide, null, "Not imported");
                Require(list.Items.Count == 2, "Missing Current suppressed guide requirements.");
            }
        }

        private static void TestPersistence(string directory)
        {
            string path = Path.Combine(directory, "config.json");
            var one = new AppConfig { Game = GameId.Poe1, PobPath = directory, BuildDirectory = directory };
            var two = new AppConfig { Game = GameId.Poe2, PobPath = directory, BuildDirectory = directory };
            var route = new QuestPlan(one);
            var optional = route.Quests.First(q => q.Optional);
            one.DisableSave = true;
            route.SetIncluded(optional, false);
            one.DisableSave = false;
            one.QuestOverlayShowExcluded = true;
            one.QuestOverlayHotkeyModifiers = 3;
            one.QuestOverlayHotkeyKey = (int)Keys.O;
            one.QuestOverlayFollowStory = false;
            one.QuestOverlayWidth = 540;
            one.QuestOverlayHeight = 430;
            two.QuestOverlayHotkeyKey = 0;
            one.ProfileStore = new CompanionProfiles { ActiveGame = GameId.Poe1 };
            one.ProfileStore.Profiles.Add("Poe2", two);
            one.Save(path);
            var restored = AppConfig.Load(path);
            var restoredRoute = new QuestPlan(restored);
            Require(!restoredRoute.IsIncluded(restoredRoute.Quests.First(q => q.Id == optional.Id))
                && restored.QuestOverlayShowExcluded, "Excluded quests/show-excluded option did not survive a fresh load.");
            Require(restored.QuestOverlayHotkeyModifiers == 3 && restored.QuestOverlayHotkeyKey == (int)Keys.O,
                "Customized quest shortcut did not survive a fresh load.");
            Require(!restored.QuestOverlayFollowStory && restored.QuestOverlayWidth == 540 && restored.QuestOverlayHeight == 430,
                "Follow preference and overlay dimensions did not survive a fresh load.");
            var switched = restored.SelectGame(GameId.Poe2, path);
            Require(switched.ExcludedQuestIds.Count == 0 && !switched.QuestOverlayShowExcluded, "PoE1 route choices leaked into PoE2.");
            Require(switched.QuestOverlayHotkeyKey == 0, "Disabled PoE2 quest shortcut was replaced by the PoE1 shortcut.");
            Require(switched.QuestOverlayFollowStory && switched.QuestOverlayWidth != 540 && switched.QuestOverlayHeight != 430,
                "Overlay dimensions/follow preference leaked into the other game.");
            var back = switched.SelectGame(GameId.Poe1, path);
            Require(back.ExcludedQuestIds.Contains(optional.Id) && back.QuestOverlayHotkeyKey == (int)Keys.O,
                "Switching back lost the route or shortcut.");

            // Corrupt/stale exclusions must not be able to remove fixed quests.
            var story = restoredRoute.Quests.First(q => !q.Optional);
            restored.ExcludedQuestIds.Add(story.Id);
            Require(restoredRoute.IsIncluded(story) && restoredRoute.Visible(false, null).Contains(story), "Saved data hid a story quest.");
        }

        private static void TestStoryFollowing(GameId game, string directory)
        {
            string path = Path.Combine(directory, game + "-Client.txt");
            var config = new AppConfig { Game = game, DisableSave = true, PobPath = directory, BuildDirectory = directory,
                GameLogPath = path, FollowGameStage = false };
            using (var guide = new GuideBuildView())
            {
                guide.SetSource(config);
                guide.ToggleQuestOverlay();
                var overview = Field<QuestPlannerView>(guide, "_quests");
                var overlay = Field<QuestOverlay>(overview, "_overlay");
                var compact = overlay.Controls.OfType<QuestPlannerView>().Single();
                var chapter = Field<ComboBox>(compact, "_chapter");
                var follow = Field<CheckBox>(compact, "_followStory");
                var note = Field<Label>(compact, "_note");
                Action poll = () => typeof(GuideBuildView).GetMethod("PollGameLog", Private).Invoke(guide, null);
                Action<string> enter = code =>
                {
                    File.AppendAllText(path, "Generating level 25 area \"" + code + "\"\n");
                    poll();
                };
                Require(follow.Checked && chapter.SelectedIndex == 0 && note.Text.Contains("not found"),
                    "Missing log did not report unavailable progress without guessing an act.");
                enter(game == GameId.Poe1 ? "1_2_town" : "G2_town");
                Require((string)chapter.SelectedItem == "Act 2", "Follow mode did not recover when the log appeared.");
                overlay.Hide();
                enter(game == GameId.Poe1 ? "1_10_town" : "G4_town");
                Require((string)chapter.SelectedItem == (game == GameId.Poe1 ? "Act 10" : "Act 4"),
                    "Story act did not update with the dashboard/overlay hidden.");
                if (game == GameId.Poe2)
                {
                    enter("P1_town");
                    Require((string)chapter.SelectedItem == "Interludes", "PoE2 Interludes did not follow the log.");
                }
                guide.ToggleQuestOverlay();
                chapter.SelectedItem = "Act 1";
                Require(!follow.Checked && !config.QuestOverlayFollowStory, "Manual browsing did not pause story following.");
                enter(game == GameId.Poe1 ? "1_2_town" : "G2_town");
                Require((string)chapter.SelectedItem == "Act 1", "Log updates overrode manual browsing.");
                follow.Checked = true;
                Require((string)chapter.SelectedItem == "Act 2", "Resuming follow mode did not apply the latest act immediately.");
                enter("Hideout");
                Require((string)chapter.SelectedItem == "Act 2", "A hideout reset the last campaign act.");
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    poll();
                    Require(note.Text.Contains("unavailable") && (string)chapter.SelectedItem == "Act 2",
                        "Unreadable log was not reported while retaining the last known act.");
                }
                poll();
                Require(!note.Text.Contains("unavailable"), "Story status did not recover after the log became readable.");
                enter("MapWorldsTower");
                Require((string)chapter.SelectedItem == GameLog.PhaseText(GamePhase.Endgame)
                    && Field<DataGridView>(compact, "_grid").Rows.Count == 0 && note.Text.Contains("no campaign quests"),
                    "Maps were displayed as a campaign act.");
                var route = Field<QuestPlan>(overview, "_plan");
                bool rejected = false;
                try { route.UpdateProgress(new GameLogState { Game = game == GameId.Poe1 ? GameId.Poe2 : GameId.Poe1, Phase = GamePhase.Act1 }, null); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected && config.ExcludedQuestIds.Count == 0 && !config.FollowGameStage
                    && config.GuideLoadout == null && Field<BuildDetails>(guide, "_details") == null,
                    "Quest following mixed games, changed route choices or loaded/changed the guide.");
            }
        }

        private static void TestResize(QuestOverlay overlay, AppConfig config, QuestPlan route, string screenshots)
        {
            var grip = overlay.Controls.OfType<Panel>().Single(c => c.Name == "Quest overlay resize grip");
            Size before = overlay.Size;
            typeof(Control).GetMethod("OnMouseDown", Private).Invoke(grip, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 3, 3, 0) });
            typeof(Control).GetMethod("OnMouseMove", Private).Invoke(grip, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 83, 43, 0) });
            typeof(Control).GetMethod("OnMouseUp", Private).Invoke(grip, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 3, 3, 0) });
            Require(overlay.Width == before.Width + 80 && overlay.Height == before.Height + 40
                && config.QuestOverlayWidth == overlay.Width && config.QuestOverlayHeight == overlay.Height,
                "Dragging the resize grip did not resize and save the overlay.");
            Size released = overlay.Size;
            typeof(Control).GetMethod("OnMouseDown", Private).Invoke(grip, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 3, 3, 0) });
            grip.Capture = false;
            Application.DoEvents();
            typeof(Control).GetMethod("OnMouseMove", Private).Invoke(grip, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 83, 43, 0) });
            Require(overlay.Size == released, "Resize continued after the grip lost mouse capture.");
            foreach (var corner in new[] { new Point(1, 1), new Point(overlay.ClientSize.Width - 2, overlay.ClientSize.Height - 2) })
            {
                Point screen = overlay.PointToScreen(corner);
                var args = new object[] { Message.Create(overlay.Handle, 0x84, IntPtr.Zero,
                    new IntPtr(unchecked((screen.Y << 16) | (screen.X & 0xFFFF)))) };
                typeof(QuestOverlay).GetMethod("WndProc", Private).Invoke(overlay, args);
                Require(((Message)args[0]).Result.ToInt32() == (corner.X == 1 ? 13 : 17), "Native resize corner hit test is missing.");
            }
            overlay.Size = new Size(100, 100);
            Application.DoEvents();
            var compact = overlay.Controls.OfType<QuestPlannerView>().Single();
            var grid = Field<DataGridView>(compact, "_grid");
            Require(overlay.Width >= overlay.MinimumSize.Width && overlay.Height >= overlay.MinimumSize.Height
                && grid.Height > 40 && !grid.Controls.OfType<HScrollBar>().Any(bar => bar.Visible)
                && Field<CheckBox>(compact, "_followStory").Right <= Field<CheckBox>(compact, "_followStory").Parent.ClientSize.Width,
                "Small quest overlay has collapsed/clipped controls.");
            if (!string.IsNullOrEmpty(screenshots))
                using (var bitmap = new Bitmap(overlay.Width, overlay.Height))
                {
                    overlay.DrawToBitmap(bitmap, new Rectangle(Point.Empty, overlay.Size));
                    bitmap.Save(Path.Combine(screenshots, config.Game + "-quests-small.png"));
                }
            overlay.Size = before;
            typeof(QuestOverlay).GetMethod("OnResizeEnd", Private).Invoke(overlay, new object[] { EventArgs.Empty });
            using (var restored = new QuestOverlay(config, route))
                Require(restored.Size == overlay.Size, "Reopening the overlay lost its saved dimensions.");
            var oversized = config.Clone();
            oversized.QuestOverlayWidth = oversized.QuestOverlayHeight = int.MaxValue;
            oversized.QuestOverlayX = oversized.QuestOverlayY = int.MaxValue;
            using (var restored = new QuestOverlay(oversized, new QuestPlan(oversized)))
                Require(Screen.PrimaryScreen.WorkingArea.Contains(restored.Bounds), "Oversized/offscreen saved bounds were not clamped.");
        }

        internal static void TestUi(AppConfig config, string screenshots)
        {
            var route = new QuestPlan(config);
            string game = config.Game == GameId.Poe1 ? "poe1" : "poe2";
            Require(route.Quests.Count >= (config.Game == GameId.Poe1 ? 80 : 40) && route.Quests.All(q => q.Game == game
                && !string.IsNullOrWhiteSpace(q.Reward) && !string.IsNullOrWhiteSpace(q.Requirement)
                && q.SourceUrl.StartsWith("https://www." + (game == "poe1" ? "poewiki.net" : "poe2wiki.net") + "/wiki/")),
                "Quest catalogue/reward/source shape is incomplete or cross-game.");
            for (int act = 1; act <= (config.Game == GameId.Poe1 ? 10 : 4); act++)
                Require(route.Quests.Any(q => q.Chapter == "Act " + act && !q.Optional), "Missing fixed act " + act);
            if (config.Game == GameId.Poe2)
                Require(route.Quests.Any(q => q.Chapter == "Interludes" && !q.Optional), "PoE2 Interludes missing.");
            var optional = route.Quests.First(q => q.Optional);
            var story = route.Quests.First(q => !q.Optional);
            bool rejected = false;
            try { route.SetIncluded(story, false); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && route.IsIncluded(story), "Main quest was deselectable.");
            using (var host = new Form { Size = new Size(1000, 650) })
            using (var view = new QuestPlannerView())
            using (var overlay = new QuestOverlay(config, route))
            {
                view.SetSource(config, route);
                host.Controls.Add(view);
                host.Show();
                overlay.Show();
                Application.DoEvents();
                var compact = overlay.Controls.OfType<QuestPlannerView>().Single();
                var compactGrid = Field<DataGridView>(compact, "_grid");
                var grid = Field<DataGridView>(view, "_grid");
                Action<DataGridView, CampaignQuest> click = (target, quest) =>
                {
                    int row = target.Rows.Cast<DataGridViewRow>().First(r => ReferenceEquals(r.Tag, quest)).Index;
                    typeof(DataGridView).GetMethod("OnCellContentClick", Private).Invoke(target, new object[] { new DataGridViewCellEventArgs(0, row) });
                };
                click(compactGrid, story);
                Require(route.IsIncluded(story), "Locked UI checkbox changed the story route.");
                click(compactGrid, optional);
                Require(!route.IsIncluded(optional) && !compactGrid.Rows.Cast<DataGridViewRow>().Any(r => ReferenceEquals(r.Tag, optional)),
                    "Deselected optional quest remained visible with show-excluded off.");
                Require(!(bool)grid.Rows.Cast<DataGridViewRow>().First(r => ReferenceEquals(r.Tag, optional)).Cells[0].Value,
                    "Overview did not refresh after overlay exclusion.");
                var show = Field<CheckBox>(compact, "_showExcluded");
                show.Checked = true;
                Require(compactGrid.Rows.Cast<DataGridViewRow>().Any(r => ReferenceEquals(r.Tag, optional))
                    && config.QuestOverlayShowExcluded, "Show deselected checkbox did not reveal excluded quests.");
                click(compactGrid, optional);
                Require(route.IsIncluded(optional), "Deselected quest could not be restored.");
                var chapters = Field<ComboBox>(compact, "_chapter");
                chapters.SelectedItem = optional.Chapter;
                Require(compactGrid.Rows.Cast<DataGridViewRow>().All(r => ((CampaignQuest)r.Tag).Chapter == optional.Chapter),
                    "Act selector showed quests from a different act.");
                Require(compactGrid.Rows.Cast<DataGridViewRow>().All(r => !string.IsNullOrWhiteSpace((string)r.Cells[3].Value)),
                    "Overlay lost concrete reward text.");
                Require(compactGrid.Width > 500 && compactGrid.Height > 300 && show.Visible && show.Right <= show.Parent.ClientSize.Width,
                    "Quest overlay controls are clipped.");
                if (!string.IsNullOrEmpty(screenshots))
                {
                    using (var bitmap = new Bitmap(overlay.Width, overlay.Height))
                    {
                        overlay.DrawToBitmap(bitmap, new Rectangle(Point.Empty, overlay.Size));
                        bitmap.Save(Path.Combine(screenshots, config.Game + "-quests.png"));
                    }
                }
                show.Checked = false;
                TestResize(overlay, config, route, screenshots);
                overlay.Hide();
                host.Hide();
            }
        }
    }
}
