using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    public static class DualGameTests
    {
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static string Build(GameId game, bool stages)
        {
            string root = GameIdentity.Root(game);
            string version = game == GameId.Poe1 ? "3_29" : "0_5";
            string xml = "<" + root + "><Build level=\"40\" className=\"Witch\" ascendClassName=\"None\" targetVersion=\"" +
                (game == GameId.Poe1 ? "3_0" : "0_1") + "\"/>";
            if (stages)
            {
                xml += "<Tree activeSpec=\"1\"><Spec title=\"Early {1}\" treeVersion=\"" + version + "\" classId=\"3\" ascendClassId=\"0\" nodes=\"\"/>" +
                    "<Spec title=\"Late {2}\" treeVersion=\"" + version + "\" classId=\"3\" ascendClassId=\"0\" nodes=\"\"/></Tree>" +
                    "<Items activeItemSet=\"1\"><Item id=\"1\">Rarity: Rare\nEarly Boots\nDragonscale Boots\nItem Level: 82\n+20 to maximum Life</Item>" +
                    "<Item id=\"2\">Rarity: Rare\nLate Boots\nDragonscale Boots\nItem Level: 82\n+100 to maximum Life</Item>" +
                    "<ItemSet id=\"1\" title=\"Early {1}\"><Slot name=\"Boots\" itemId=\"1\"/></ItemSet>" +
                    "<ItemSet id=\"2\" title=\"Late {2}\"><Slot name=\"Boots\" itemId=\"2\"/></ItemSet></Items>" +
                    "<Skills activeSkillSet=\"1\"><SkillSet id=\"1\" title=\"Early {1}\"><Skill enabled=\"true\" slot=\"Boots\" mainActiveSkill=\"1\">" +
                    "<Gem nameSpec=\"Fireball\" level=\"5\" quality=\"0\" enabled=\"true\"/></Skill></SkillSet>" +
                    "<SkillSet id=\"2\" title=\"Late {2}\"><Skill enabled=\"true\" slot=\"Boots\" mainActiveSkill=\"1\">" +
                    "<Gem nameSpec=\"Fireball\" level=\"15\" quality=\"20\" enabled=\"true\"/></Skill></SkillSet></Skills>" +
                    "<Config activeConfigSet=\"1\"><ConfigSet id=\"1\" title=\"Early {1}\"/><ConfigSet id=\"2\" title=\"Late {2}\"/></Config>";
            }
            else xml += "<Tree activeSpec=\"1\"><Spec title=\"Default\" treeVersion=\"" + version + "\" classId=\"3\" ascendClassId=\"0\" nodes=\"\"/></Tree>";
            if (game == GameId.Poe2) xml = xml.Replace("classId=\"3\"", "classInternalId=\"1\"");
            return xml + "</" + root + ">";
        }

        public static int Run()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CompanionDualGame-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                TestProfiles(directory);
                TestPlansAndLogs();
                TestTimelessJewel();
                foreach (GameId game in new[] { GameId.Poe1, GameId.Poe2 }) TestEngine(game, directory);
                TestSwitch(directory);
                using (var one = new PobLuaEngine())
                using (var two = new PobLuaEngine())
                {
                    string error;
                    Require(one.Initialize(GameIdentity.DefaultPobPath(GameId.Poe1), AppConfig.GetWrapperScriptPath(), out error, GameId.Poe1), error);
                    Require(two.Initialize(GameIdentity.DefaultPobPath(GameId.Poe2), AppConfig.GetWrapperScriptPath(), out error, GameId.Poe2), error);
                    Require(one.LoadBuildXml(Build(GameId.Poe1, false), "One", out error) != null, error);
                    Require(two.LoadBuildXml(Build(GameId.Poe2, false), "Two", out error) != null, error);
                    Require(one.GetBuildDetails(out error).Game == "poe1" && two.GetBuildDetails(out error).Game == "poe2", "Concurrent engine contexts leaked.");
                }

                Console.WriteLine("PASSED: isolated migration/profiles, wrong-game guards, both real engines, loadouts, gems/passives/logs, comparisons and new-file exports.");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("FAILED: " + ex); return 1; }
            finally
            {
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }

        public static int RunAcquisition()
        {
            try
            {
                var fireball = GemAcquisition.Lookup(new SkillGem { Name = "Fireball", Level = 20, RequiredLevel = 1, TargetRequiredLevel = 70 }, "Witch", 10);
                Require(fireball.Where.StartsWith("Act 1 Nessa") && fireball.Where.Contains("Enemy At The Gate")
                    && fireball.Where.Contains("Act 3 Siosa, Library") && fireball.Where.Contains("Act 6 Lilly Roth")
                    && fireball.Requirements.Contains("character L1") && fireball.Requirements.Contains("character L70"),
                    "Fireball vendor/quest/level guidance incorrect: " + fireball.Where);
                var ranger = GemAcquisition.Lookup(new SkillGem { Name = "Fireball" }, "Ranger", 1);
                Require(ranger.Where.StartsWith("Act 3 Siosa") && ranger.Where.Contains("not for Current Ranger"), "Class restrictions ignored.");
                var unknownClass = GemAcquisition.Lookup(new SkillGem { Name = "Fireball" }, null, null);
                Require(unknownClass.Where.Contains("Current class unknown"), "Missing Current was treated as Guide class.");
                var blast = GemAcquisition.Lookup(new SkillGem { Name = "Blade Blast" }, "Shadow", 5);
                Require(blast.Where.StartsWith("Act 2 Yeena, Forest Encampment") && blast.Where.Contains("Intruders In Black")
                    && blast.Requirements.Contains("too low"), "Quest-giver was confused with purchase vendor or required level.");
                var multistrike = GemAcquisition.Lookup(new SkillGem { Name = "Multistrike Support" }, "Duelist", 90);
                Require(multistrike.Where.StartsWith("Act 4 Petarus and Vanja") && !multistrike.Where.Contains("Siosa"), "Act 4 availability incorrectly added to Siosa.");
                Require(fireball.CompactLevel == "L1*" && blast.CompactLevel == "L16*" && multistrike.CompactLevel == "L38*",
                    "Compact labels confused starting character level with guide gem level.");
                var support = GemAcquisition.Lookup(new SkillGem { Name = "Arcane Surge", Kind = "Support" }, "Witch", 1);
                Require(support.IsVendorGem == true && support.CompactLevel == "L1*" && support.SourceUrl.EndsWith("Arcane_Surge_Support"),
                    "Native support name without suffix did not resolve the purchase entry.");
                foreach (string special in new[] { "Empower Support", "Enlighten Support", "Vaal Fireball", "Awakened Added Fire Damage Support", "Fireball of Radiation" })
                {
                    var result = GemAcquisition.Lookup(new SkillGem { Name = special }, "Witch", 90);
                    Require(!result.Where.Contains("Act 6 Lilly") && !result.Where.Contains("Act 1 Nessa"), "Special gem got ordinary vendor instructions: " + special);
                    Require(result.CompactLevel == "not sold", "Special gem displayed a misleading purchase level: " + special);
                }
                Require(GemAcquisition.Lookup(new SkillGem { Name = "Empower", Kind = "Support" }, "Witch", 90).CompactLevel == "not sold",
                    "Exceptional support without suffix displayed a vendor level.");
                Require(GemAcquisition.Lookup(new SkillGem { Name = "Unknown New Gem", RequiredLevel = 12 }, "Witch", 90).CompactLevel == "L12?",
                    "Unknown gem purchase level was presented as verified.");
                Require(GemAcquisition.Lookup(new SkillGem { Name = "Unknown New Gem" }, "Witch", 90).Where.Contains("No verified"),
                    "Unknown gem silently claimed vendor support.");
                using (var engine = new PobLuaEngine())
                {
                    string error;
                    Require(engine.Initialize(GameIdentity.DefaultPobPath(GameId.Poe1), AppConfig.GetWrapperScriptPath(), out error, GameId.Poe1), error);
                    var config = new AppConfig { Game = GameId.Poe1, DisableSave = true };
                    Require(engine.LoadBuildXml(Build(GameId.Poe1, true), "Purchase UI fixture", out error) != null, error);
                    var guide = engine.GetBuildDetails(out error);
                    var gem = guide.Skills.SelectMany(g => g.Gems).First(g => g.Name == "Fireball");
                    Require(gem.RequiredLevel == 1 && gem.TargetRequiredLevel == 11, "Native required character levels unavailable or confused with gem level.");
                    var current = new BuildDetails { Game = "poe1", Success = true, ClassName = "Witch", Level = 10 };
                    var plan = GemCutting.Plan(guide, current);
                    Require(plan.All(g => g.Acquisition != null), "PoE1 checklist lacks acquisition data.");
                    using (var overlay = new GemOverlay(config))
                    {
                        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        typeof(GemOverlay).GetMethod("LoadData", flags).Invoke(overlay, new object[] { guide, current, null, null });
                        overlay.CreateControl();
                        overlay.PerformLayout();
                        typeof(OverlayForm).GetMethod("Render", flags).Invoke(overlay, null);
                        Require(overlay.BodyText.Contains("L1*") && !overlay.BodyText.Contains("Nessa")
                            && !overlay.BodyText.Contains("Buy/get:") && !overlay.BodyText.Contains("character L"),
                            "In-game overlay is not compact gem/level-only guidance.");
                        var panel = (Panel)typeof(OverlayForm).GetField("_body", flags).GetValue(overlay);
                        Require(panel.AutoScroll && panel.AutoScrollMinSize.Height > 0, "Gem rows cannot scroll.");
                        var panelType = panel.GetType();
                        var toolTip = (ToolTip)panelType.GetField("_toolTip", flags).GetValue(panel);
                        var mouseMove = panelType.GetMethod("OnMouseMove", flags);
                        var lines = (System.Collections.IList)panelType.GetField("Lines").GetValue(panel);
                        int headerHeight = (int)panelType.GetMethod("RowHeight").Invoke(panel, new object[] { lines[0] });
                        int gemHeight = (int)panelType.GetMethod("RowHeight").Invoke(panel, new object[] { lines[1] });
                        int gemY = 2 + headerHeight + gemHeight / 2;
                        mouseMove.Invoke(panel, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, 5, 0) });
                        Require(string.IsNullOrEmpty(toolTip.GetToolTip(panel)), "Group headings show a gem tooltip.");
                        mouseMove.Invoke(panel, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, gemY, 0) });
                        string hoverText = toolTip.GetToolTip(panel);
                        Require(hoverText.Contains("Fireball") && hoverText.Contains("Act 1 Nessa")
                            && hoverText.Contains("Enemy At The Gate") && hoverText.Contains("Quest completion is unknown"),
                            "Hovering the gem does not show its vendor, act and quest unlock.");
                        mouseMove.Invoke(panel, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 10, gemY, 0) });
                        Require(string.IsNullOrEmpty(toolTip.GetToolTip(panel)), "Dragging leaves a tooltip visible.");
                        mouseMove.Invoke(panel, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, gemY, 0) });
                        panelType.GetMethod("OnMouseLeave", flags).Invoke(panel, new object[] { EventArgs.Empty });
                        Require(string.IsNullOrEmpty(toolTip.GetToolTip(panel)), "Tooltip remains after leaving the gem.");
                        mouseMove.Invoke(panel, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, gemY, 0) });
                        typeof(OverlayForm).GetMethod("Render", flags).Invoke(overlay, null);
                        Require(string.IsNullOrEmpty(toolTip.GetToolTip(panel)), "Rerender retains stale hover information.");
                        int chromeHeight = overlay.Height - panel.Height;
                        overlay.Height = chromeHeight + gemHeight;
                        panel.AutoScrollPosition = new System.Drawing.Point(0, headerHeight);
                        Require(panel.AutoScrollPosition.Y < 0, "Tooltip scrolling fixture did not scroll.");
                        mouseMove.Invoke(panel, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, 5, 0) });
                        Require(toolTip.GetToolTip(panel).Contains("Fireball"), "Scrolled tooltip hit testing used unscrolled rows.");
                        panelType.GetMethod("OnScroll", flags).Invoke(panel,
                            new object[] { new ScrollEventArgs(ScrollEventType.SmallIncrement, headerHeight) });
                        Require(string.IsNullOrEmpty(toolTip.GetToolTip(panel)), "Scrolling retains a stale gem tooltip.");
                    }
                    using (var view = new GuideBuildView())
                    {
                        view.SetSource(config);
                        view.RenderGems(guide, current, null);
                        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        var list = (ListView)typeof(GuideBuildView).GetField("_gemList", flags).GetValue(view);
                        Require(list.Columns[6].Width > 0 && list.Columns[7].Width > 0
                            && list.Items.Cast<ListViewItem>().Any(i => i.SubItems[7].Text.Contains("Nessa") && i.Tag is GemAcquisitionInfo),
                            "Purchase guidance/source links missing from dashboard.");
                        view.SetSource(new AppConfig { Game = GameId.Poe2, DisableSave = true });
                        Require(list.Columns[6].Width == 0 && list.Columns[7].Width == 0, "PoE1 purchase columns leaked into PoE2.");
                    }
                }
                Console.WriteLine("PASSED: PoE1 gem vendors, quest/class unlocks, required levels, special/unknown acquisition and dashboard integration.");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("FAILED: " + ex); return 1; }
        }

        private static void TestProfiles(string directory)
        {
            string path = Path.Combine(directory, "config.json");
            string guide = Path.Combine(directory, "guide2.xml"), current = Path.Combine(directory, "current2.xml");
            File.WriteAllText(guide, Build(GameId.Poe2, false));
            File.WriteAllText(current, Build(GameId.Poe2, false));
            var legacy = new AppConfig { BuildFilePath = guide, CurrentBuildFilePath = current, BuildDirectory = directory };
            legacy.SetGuideLoadout("Preserved {1}");
            legacy.GemDetectSpots.Add("1|2|3|4|legacy.png");
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(legacy));
            string original = File.ReadAllText(path);
            var two = AppConfig.Load(path);
            Require(two.Game == GameId.Poe2 && Directory.GetFiles(directory, "*.legacy-backup").Length == 1, "Legacy migration/backup failed.");
            Require(File.ReadAllText(Directory.GetFiles(directory, "*.legacy-backup")[0]) == original, "Legacy backup changed bytes.");
            var one = two.SelectGame(GameId.Poe1, path);
            Require(one.CurrentBuildFilePath == null && one.CurrentCharacterCode == null && one.GemDetectSpots.Count == 0, "PoE1 inherited PoE2 sources/detection.");
            one.CurrentCharacterCode = PobShareCode.Encode(Build(GameId.Poe1, false));
            one.HotkeyKey = (int)Keys.F;
            one.Save(path);
            var restored = one.SelectGame(GameId.Poe2, path);
            Require(restored.CurrentBuildFilePath == current && restored.BuildFilePath == guide && restored.GuideLoadout == "Preserved {1}"
                && restored.GemDetectSpots.Count == 1 && restored.HotkeyKey == (int)Keys.D, "PoE2 profile data lost during switching.");
            var again = AppConfig.Load(path).SelectGame(GameId.Poe1, path);
            Require(again.CurrentCharacterCode == one.CurrentCharacterCode && again.HotkeyKey == (int)Keys.F, "PoE1 did not round-trip.");
            var store = again.ProfileStore;
            try { again.Save(directory); throw new InvalidOperationException("Save should have failed."); }
            catch (IOException) { }
            Require(object.ReferenceEquals(store, again.ProfileStore), "Failed save mutated the profile envelope.");
            string corrupt = Path.Combine(directory, "broken.json");
            File.WriteAllText(corrupt, "{ broken");
            try { AppConfig.Load(corrupt); throw new InvalidOperationException("Broken config accepted."); }
            catch (ArgumentException) { }
            Require(File.ReadAllText(corrupt) == "{ broken", "Broken config overwritten.");
            string error;
            Require(!GameIdentity.ValidateBuild(Build(GameId.Poe1, false), GameId.Poe2, out error), "Wrong-game XML accepted.");
            string resolved, code, description;
            Require(!PobShareCode.TryResolve(PobShareCode.Encode(Build(GameId.Poe2, false)), GameId.Poe1, out resolved, out code, out description, out error), "Wrong-game code accepted.");
            Console.WriteLine("PASSED: migration backup, independent profiles, failed save and corrupt config preservation.");
        }

        private static BuildDetails Snapshot(params TreeNode[] nodes)
        {
            var d = new BuildDetails { Game = "poe1", TreeVersion = "3_29", Success = true };
            d.Tree.Nodes.AddRange(nodes);
            return d;
        }

        private static void TestTimelessJewel()
        {
            using (var engine = new PobLuaEngine())
            {
                string error;
                Require(engine.Initialize(GameIdentity.DefaultPobPath(GameId.Poe1), AppConfig.GetWrapperScriptPath(), out error, GameId.Poe1), error);
                string baseXml = Build(GameId.Poe1, false);
                Require(engine.LoadBuildXml(baseXml, "Jewel baseline", out error) != null, error);
                var tree = engine.GetBuildDetails(out error).Tree;
                const int socket = 54127;
                var parents = new Dictionary<int, int>();
                var queue = new Queue<int>();
                int start = tree.Nodes.First(n => n.Alloc && n.Type == "ClassStart").Id;
                parents[start] = start;
                queue.Enqueue(start);
                while (queue.Count > 0 && !parents.ContainsKey(socket))
                {
                    int node = queue.Dequeue();
                    foreach (var edge in tree.Edges.Where(e => e.Contains(node)))
                    {
                        int next = edge[0] == node ? edge[1] : edge[0];
                        if (parents.ContainsKey(next)) continue;
                        parents[next] = node;
                        queue.Enqueue(next);
                    }
                }
                Require(parents.ContainsKey(socket), "Timeless-jewel test socket is unreachable.");
                var nodes = new List<int>();
                for (int node = socket; node != start; node = parents[node]) nodes.Add(node);
                string xml = baseXml.Replace("nodes=\"\"", "nodes=\"" + string.Join(",", nodes) + "\"")
                    .Replace("/></Tree>", "><Sockets><Socket nodeId=\"54127\" itemId=\"1\"/></Sockets></Spec></Tree>")
                    .Replace("</PathOfBuilding>", "<Items activeItemSet=\"1\"><Item id=\"1\">Rarity: UNIQUE\nLethal Pride\nTimeless Jewel\nRadius: Large\nImplicits: 0\nCommanded leadership over 10000 warriors under Rakiata\nPassives in radius are Conquered by the Karui\nHistoric</Item><ItemSet id=\"1\"/></Items></PathOfBuilding>");
                var info = engine.LoadBuildXml(xml, "Timeless-jewel regression", out error);
                Require(info != null && info.Success, "Installed timeless-jewel data could not load: " + error);
                Require(engine.GetBuildDetails(out error).Gear.Any(g => g.ItemName != null && g.ItemName.Contains("Lethal Pride")), "Timeless jewel was dropped.");
                var invalid = engine.LoadBuildXml(baseXml.Replace("classId=\"3\"", "classId=\"999\""), "Invalid partial tree", out error);
                Require(invalid == null || !invalid.Success, "Invalid test tree was accepted.");
                info = engine.LoadBuildXml(xml, "Retry after failed tree", out error);
                Require(info != null && info.Success, "Partially initialized tree contaminated the retry: " + error);
                info = engine.LoadBuildXml(baseXml, "Replace timeless build", out error);
                Require(info != null && info.Success, "Timeless build could not be replaced: " + error);
                Console.WriteLine("PASSED: native compressed timeless-jewel lookup and recovery after a partially initialized build.");
            }
        }

        private static void TestPlansAndLogs()
        {
            Require(GameIdentity.IdentifyProcess("PathOfExileSteam", @"D:\SteamLibrary\steamapps\common\Path of Exile 2\PathOfExileSteam.exe") == GameId.Poe2
                && GameIdentity.IdentifyProcess("PathOfExileSteam", @"D:\SteamLibrary\steamapps\common\Path of Exile\PathOfExileSteam.exe") == GameId.Poe1
                && GameIdentity.IdentifyProcess("PathOfExileSteam", @"D:\Custom\Client\PathOfExileSteam.exe") == null, "Shared executable names were incorrectly treated as a game identity.");
            var guide = Snapshot(new TreeNode { Id = 1, Type = "Mastery", Alloc = true, MasteryEffect = 11 });
            var current = Snapshot(new TreeNode { Id = 1, Type = "Mastery", Alloc = true, MasteryEffect = 12 });
            Require(PassivePlan.Plan(guide, current).MasteryChanges.Count == 1, "Mastery choice difference lost.");
            current.TreeVersion = "3_28";
            Require(PassivePlan.Plan(guide, current).Notice != null, "Different tree versions accepted.");
            current.TreeVersion = guide.TreeVersion;
            guide.Tree.Nodes.Add(new TreeNode { Id = 65536, Type = "Notable", Alloc = true, GraphContext = "Cluster A" });
            Require(PassivePlan.Plan(guide, current).Notice != null, "Missing cluster context produced allocation advice.");
            guide = Snapshot();
            current = Snapshot();
            guide.Skills.Add(new SkillGroup { Enabled = true, Gems = new List<SkillGem> {
                new SkillGem { Id = "support", Name = "Support", Variant = "normal", Level = 20, Quality = 20, Enabled = true, Kind = "Support" },
                new SkillGem { Id = "support", Name = "Support", Variant = "normal", Level = 20, Quality = 20, Enabled = true, Kind = "Support" } } });
            current.Skills.Add(new SkillGroup { Enabled = true, Gems = new List<SkillGem> {
                new SkillGem { Id = "support", Name = "Support", Variant = "normal", Level = 10, Quality = 0, Enabled = true, Kind = "Support" } } });
            var gems = GemCutting.Plan(guide, current);
            Require(gems.Count == 2 && gems.Count(g => g.NeedsCut) == 1 && gems.Count(g => g.IsUnderLevel && g.IsUnderQuality) == 1, "PoE1 gem multiplicity/quality/level failed.");
            current.Skills[0].Gems[0].Variant = "transfigured";
            Require(GemCutting.Plan(guide, current).All(g => g.NeedsCut), "Different gem variants matched.");
            for (int act = 1; act <= 10; act++)
                Require(GameLog.ClassifyArea("1_" + act + "_town", GameId.Poe1).ToString() == "Act" + act, "PoE1 act " + act + " misclassified.");
            Require(GameLog.ClassifyArea("2_11_town", GameId.Poe1) == GamePhase.Endgame
                && GameLog.ClassifyArea("G1_town", GameId.Poe1) == GamePhase.Unknown
                && GameLog.ClassifyArea("G4_town", GameId.Poe2) == GamePhase.Act4, "Game-specific logs mixed.");
            Console.WriteLine("PASSED: mastery effects, cluster/tree mismatch guards, instance-aware gems and all ten PoE1 acts.");
        }

        private static void TestEngine(GameId game, string directory)
        {
            using (var engine = new PobLuaEngine())
            {
                string error;
                Require(engine.Initialize(GameIdentity.DefaultPobPath(game), AppConfig.GetWrapperScriptPath(), out error, game), error);
                string xml = Build(game, true);
                string source = Path.Combine(directory, game + ".xml");
                File.WriteAllText(source, xml);
                var info = engine.LoadBuildFile(source, out error);
                Require(info != null && info.Success, "Load " + game + ": " + error);
                var details = engine.GetBuildDetails(out error);
                Require(details != null && details.Success && details.Tree.Nodes.Count > 1000 && details.Game == (game == GameId.Poe1 ? "poe1" : "poe2"), "Tree/game export: " + error);
                Require(details.ClassName == "Witch", "Fixture class did not load correctly.");
                Require(details.Loadouts.Count == 2, "Expected two real loadouts, got " + string.Join(", ", details.Loadouts));
                Require(details.Skills.SelectMany(g => g.Gems).Any(g => g.Name == "Fireball"), "Native gem resolution failed.");
                var first = engine.SetLoadout("Early {1}", out error);
                Require(first != null && first.Success && first.ActiveLoadout == "Early {1}", "Early loadout: " + error);
                var early = engine.GetBuildDetails(out error);
                var last = engine.SetLoadout("Late {2}", out error);
                Require(last != null && last.Success && last.ActiveLoadout == "Late {2}", "Late loadout: " + error);
                var late = engine.GetBuildDetails(out error);
                Require(early.Gear.First(g => g.SlotName == "Boots").ItemName != late.Gear.First(g => g.SlotName == "Boots").ItemName
                    && early.Skills.SelectMany(g => g.Gems).First().Level != late.Skills.SelectMany(g => g.Gems).First().Level, "Native loadout did not change gear and gems.");
                Require(engine.LoadBuildFile(source, out error) != null, error);
                string candidate = "Item Class: Boots\nRarity: Rare\nProbe Boots\nDragonscale Boots\n--------\nItem Level: 82\n--------\n+150 to maximum Life\n35% increased Movement Speed\n";
                var compared = engine.CompareItem(candidate, out error);
                Require(compared != null && compared.Success && compared.Comparisons.Count == 1, "Candidate comparison: " + error);
                var slot = compared.Comparisons[0];
                Require(slot.After["Life"] - slot.Before["Life"] >= 120, "Expected real life gain of at least 120.");
                if (game == GameId.Poe1)
                {
                    Require(slot.CompatibilityNotice != null && slot.CompatibilityNotice.Contains("SOCKET/LINK WARNING"), "Missing candidate socket/link warning.");
                    foreach (string colour in new[] { "B", "W", "R", "A" })
                    {
                        var sockets = engine.CompareItem(candidate.Replace("Item Level: 82", "Sockets: " + colour + "\nItem Level: 82"), out error);
                        Require(sockets != null && sockets.Success, "Socket candidate parsing: " + error);
                        bool warning = sockets.Comparisons[0].CompatibilityNotice.Contains("SOCKET/LINK WARNING");
                        Require(warning == (colour == "R" || colour == "A"), "Socket colour/white/Abyss compatibility failed: " + colour);
                    }
                }
                var export = engine.ExportWithCandidate(candidate, "Boots", out error);
                Require(export != null && export.Success && export.Validated && GameIdentity.ValidateBuild(export.Xml, game, out error), "Candidate export: " + error);
                string output = Path.Combine(directory, game + "-export.xml");
                Require(PobLuaEngine.WriteNewBuildFile(export.Xml, output, source, out error) != null, error);
                Require(File.ReadAllText(source) == xml, "Read-only comparison/export modified source.");
                string another = PobLuaEngine.WriteNewBuildFile(export.Xml, output, source, out error);
                Require(another != null && another != output && File.ReadAllText(output) == export.Xml, "Export overwrote an existing file.");
                Require(PobLuaEngine.WriteNewBuildFile(export.Xml, source, source, out error) == null, "Export overwrote source.");
                var config = new AppConfig { Game = game, PobPath = GameIdentity.DefaultPobPath(game), CurrentBuildFilePath = source,
                    BuildFilePath = output, BuildDirectory = directory, DisableSave = true };
                var wrong = config.Clone();
                wrong.Game = game == GameId.Poe1 ? GameId.Poe2 : GameId.Poe1;
                Require(SourceEquipper.EquipIntoSource(engine, wrong, candidate, "Boots", out error) == null
                    && File.ReadAllText(source) == xml, "Wrong-game source write was not blocked.");
                var equipped = SourceEquipper.EquipIntoSource(engine, config, candidate, "Boots", out error);
                Require(equipped != null && File.ReadAllText(equipped.BackupPath) == xml
                    && File.ReadAllText(output) == export.Xml, "Source equip backup or independent Guide protection: " + error);
                File.Delete(equipped.BackupPath);
                Require(engine.LoadBuildXml(Build(game == GameId.Poe1 ? GameId.Poe2 : GameId.Poe1, false), "Wrong game", out error) == null, "Native engine accepted wrong-game XML.");
                Console.WriteLine("PASSED: " + game + " real tree/gems/loadouts, measured life delta, socket warning and verified source-safe export.");
            }
        }

        private static void TestSwitch(string directory)
        {
            var store = new CompanionProfiles { ActiveGame = GameId.Poe2 };
            foreach (GameId game in new[] { GameId.Poe1, GameId.Poe2 })
            {
                string current = Path.Combine(directory, game + "-switch-current.xml");
                string guide = Path.Combine(directory, game + "-switch-guide.xml");
                File.WriteAllText(current, Build(game, false));
                File.WriteAllText(guide, Build(game, true));
                store.Profiles[game.ToString()] = new AppConfig { Game = game, PobPath = GameIdentity.DefaultPobPath(game),
                    BuildFilePath = guide, CurrentBuildFilePath = current, BuildDirectory = directory, ProfileStore = store, DisableSave = true,
                    HotkeyKey = (int)(game == GameId.Poe1 ? Keys.F23 : Keys.F24), HotkeyModifiers = 15,
                    GemOverlayHotkeyKey = 0, PassiveOverlayHotkeyKey = 0, GemDetectHotkeyKey = 0, TreeDetectHotkeyKey = 0,
                    GemAutoShow = false, PassiveAutoShow = false };
            }
            string path = AppConfig.GetConfigFilePath();
            byte[] original = File.Exists(path) ? File.ReadAllBytes(path) : null;
            using (var context = new TrayApplicationContext(store.Profiles[GameId.Poe2.ToString()]))
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var field = typeof(TrayApplicationContext).GetField("_mainWindow", flags);
                var engineField = typeof(TrayApplicationContext).GetField("_engine", flags);
                var old = (MainWindow)field.GetValue(context);
                var oldEngine = (PobLuaEngine)engineField.GetValue(context);
                Application.DoEvents();
                UiTests.AssertBridgeVisible(old);
                foreach (GameId game in new[] { GameId.Poe1, GameId.Poe2, GameId.Poe1 })
                {
                    if (game == GameId.Poe1)
                    {
                        var selector = old.Controls.Cast<Control>().SelectMany(c => c.Controls.Cast<Control>()).OfType<ComboBox>()
                            .First(c => c.Name == "Companion game selector");
                        selector.SelectedIndex = 0;
                    }
                    else context.SwitchGame(game);
                    Application.DoEvents();
                    var window = (MainWindow)field.GetValue(context);
                    var engine = (PobLuaEngine)engineField.GetValue(context);
                    Require(old.IsDisposed && !oldEngine.IsInitialized && engine.Game == game && engine.IsBuildLoaded
                        && window.Text.Contains(GameIdentity.Name(game)), "UI switch did not dispose/recreate the correct profile engine/view.");
                    foreach (var size in new[] { window.MinimumSize, new System.Drawing.Size(1240, 900) })
                    {
                        window.Size = size;
                        window.PerformLayout();
                        Application.DoEvents();
                        UiTests.AssertBridgeVisible(window);
                    }
                    old = window;
                    oldEngine = engine;
                }
            }
            Require(original == null ? !File.Exists(path) : File.ReadAllBytes(path).SequenceEqual(original), "UI fixture wrote user configuration.");
            Console.WriteLine("PASSED: repeated dashboard/tray profile switches retain a visible, unclipped PoB import button at minimum/default sizes, recreate views/engines and preserve user config.");
        }
    }
}
