using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    // --test-equipment-trade [screenshot dir]: overlay selector migration/routing, guide equipment slots and trade search plans.
    internal static class EquipmentTradeTests
    {
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        public static int Run(string screenshots)
        {
            string directory = Path.Combine(Path.GetTempPath(), "CompanionTrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                Application.EnableVisualStyles();
                TestMigration();
                TestSelector();
                TestSlots();
                TestItemArtPoe2();
                TestTradePoe1();
                TestTradePoe2();
                TestLeagues();
                TestUi(directory, screenshots ?? directory);
                TestItemCards(screenshots ?? directory);
                Console.WriteLine("PASSED: overlay selector migration/routing, guide-only equipment slots, trade plans (kinds, thresholds, ilvl vs level, sockets vs links, per-game URL/league, unmatched/ambiguous) and layouts.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static AppConfig Legacy(int gem, int passive, int quest)
        {
            return new AppConfig
            {
                DisableSave = true, HotkeyModifiers = 2, HotkeyKey = (int)Keys.D,
                GemOverlayHotkeyModifiers = 2, GemOverlayHotkeyKey = gem,
                PassiveOverlayHotkeyModifiers = 2, PassiveOverlayHotkeyKey = passive,
                QuestOverlayHotkeyModifiers = 2, QuestOverlayHotkeyKey = quest,
                GemDetectHotkeyKey = 0, TreeDetectHotkeyKey = 0
            };
        }

        private static void TestMigration()
        {
            var config = Legacy((int)Keys.G, (int)Keys.P, (int)Keys.Q);
            config.MigrateOverlaySelector();
            Require(config.OverlaySelectorMigrated && config.OverlaySelectorHotkeyModifiers == 2 && config.OverlaySelectorHotkeyKey == (int)Keys.G,
                "Legacy gem toggle key was not migrated to the selector.");
            Require(config.GemOverlayHotkeyKey == (int)Keys.G && config.PassiveOverlayHotkeyKey == (int)Keys.P && config.QuestOverlayHotkeyKey == (int)Keys.Q,
                "Migration erased the legacy overlay keys.");
            config.OverlaySelectorHotkeyKey = (int)Keys.K;
            config.MigrateOverlaySelector();
            Require(config.OverlaySelectorHotkeyKey == (int)Keys.K, "Migration is not idempotent; it replaced the user's selector key.");

            var disabledGem = Legacy(0, (int)Keys.P, (int)Keys.Q);
            disabledGem.MigrateOverlaySelector();
            Require(disabledGem.OverlaySelectorHotkeyKey == (int)Keys.P, "Disabled gem key should fall through to the passive key.");

            var conflict = Legacy((int)Keys.G, 0, 0);
            conflict.GemDetectHotkeyModifiers = 2; conflict.GemDetectHotkeyKey = (int)Keys.G;
            conflict.MigrateOverlaySelector();
            Require(conflict.OverlaySelectorHotkeyKey == (int)Keys.O && conflict.OverlaySelectorHotkeyModifiers == 2,
                "A legacy key that collides with a detection-spot key was reused for the selector.");

            var compare = Legacy((int)Keys.D, 0, 0);
            compare.MigrateOverlaySelector();
            Require(!(compare.OverlaySelectorHotkeyKey == (int)Keys.D && compare.OverlaySelectorHotkeyModifiers == 2), "Selector took the compare hotkey.");

            var off = Legacy(0, 0, 0);
            off.MigrateOverlaySelector();
            Require(off.OverlaySelectorMigrated && off.OverlaySelectorHotkeyKey == 0, "All overlay keys were disabled; the selector must stay disabled.");

            var json = new JavaScriptSerializer().Serialize(config);
            Require(!json.Contains("OverlaySelectorJustMigrated"), "Runtime-only migration flag is saved to config.json.");
            var restored = new JavaScriptSerializer().Deserialize<AppConfig>(json);
            restored.MigrateOverlaySelector();
            Require(restored.OverlaySelectorHotkeyKey == (int)Keys.K && !restored.OverlaySelectorJustMigrated, "Saved selector settings re-migrated after reload.");
        }

        private static void TestSelector()
        {
            Require(OverlaySelector.IndexForKey(Keys.D1) == 0 && OverlaySelector.IndexForKey(Keys.NumPad4) == 3 &&
                OverlaySelector.IndexForKey(Keys.D5) == 4 && OverlaySelector.IndexForKey(Keys.NumPad5) == 4 && OverlaySelector.IndexForKey(Keys.A) == -1,
                "Selector number keys are not mapped.");
            var toggled = new List<string>();
            var entries = new[] { "Gems", "Passives", "Quests", "Recommended equipment" }
                .Select(n => new OverlaySelector.Entry { Name = n, IsOpen = () => n == "Passives", Toggle = () => toggled.Add(n) }).ToList();
            using (var selector = new OverlaySelector(entries, "Ctrl+G"))
            {
                selector.ShowNear(Cursor.Position);
                Application.DoEvents();
                var buttons = selector.Controls[0].Controls.OfType<Button>().ToList();
                Require(buttons.Count == 4 && buttons[1].Text.Contains("open - hide") && !buttons[0].Text.Contains("open"), "Selector buttons/open state wrong.");
                Require(!selector.Controls[0].Controls.OfType<ComboBox>().Single().Enabled,
                    "Selector offers stage selection without a loaded Guide.");
                Require(!selector.Choose(7) && toggled.Count == 0, "Out-of-range choice toggled something.");
                Require(selector.Choose(3) && toggled.SequenceEqual(new[] { "Recommended equipment" }) && selector.Chosen == "Recommended equipment",
                    "Selector routed the choice to the wrong overlay.");
                Require(selector.IsDisposed || !selector.Visible, "Selector stayed open after a choice.");
            }
            entries.Add(new OverlaySelector.Entry { Name = "Update character", Toggle = () => toggled.Add("Update character") });
            using (var selector = new OverlaySelector(entries, "Ctrl+G"))
            {
                selector.ShowNear(Cursor.Position);
                Application.DoEvents();
                var button = selector.Controls[0].Controls.OfType<Button>().Last();
                Require(button.Text.StartsWith("5   Update character") && button.AccessibleName.Contains("press to run") &&
                    !button.Text.Contains("open - hide"), "Character update is not presented as action 5.");
                Require(selector.Choose(OverlaySelector.IndexForKey(Keys.NumPad5)) && toggled.Last() == "Update character" &&
                    toggled.Count == 2 && (selector.IsDisposed || !selector.Visible), "Character update action did not run once and close the selector.");
            }
            using (var view = new GuideBuildView())
            {
                view.SetSource(new AppConfig { DisableSave = true, PobPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) });
                using (var selector = new OverlaySelector(entries, "Ctrl+G", view))
                {
                    var picker = selector.Controls[0].Controls.OfType<ComboBox>().Single();
                    Require(!picker.Enabled && picker.Items.Count == 0, "Unreadable Guide retained selectable stages.");
                    var details = Guide();
                    details.Loadouts.AddRange(new[] { "Early {1}", "Late {2}" });
                    details.ActiveLoadout = "Early {1}";
                    view.Render(details);
                    Require(picker.Enabled && (string)picker.SelectedItem == "Early {1}" && picker.Items.Contains("Late {2}"),
                        "Selector did not synchronize newly loaded Guide stages.");
                    view.Render(Guide());
                    Require(!picker.Enabled && picker.Items.Count == 1,
                        "A Guide without named stages offers a selectable stage.");
                    view.SetSource(new AppConfig { Game = GameId.Poe1, DisableSave = true });
                    Require(!picker.Enabled && picker.Items.Count == 0, "Selector retained the previous game's stages after a source change.");
                }
                // Disposing the selector must detach its Guide event subscription.
                view.Render(Guide());
            }
        }

        private static GearEntry Item(string slot, string name, string rarity, string baseName, params ItemAffix[] affixes)
        {
            return new GearEntry { SlotName = slot, SlotLabel = slot, ItemName = name, Rarity = rarity, BaseName = baseName, Affixes = affixes.ToList() };
        }

        private static ItemAffix Mod(string text, string kind = "explicit") { return new ItemAffix { Text = text, Kind = kind }; }

        private static BuildDetails Guide()
        {
            var gear = new List<GearEntry>
            {
                Item("Helmet", "Glyph Crest", "RARE", "Eternal Burgonet", Mod("+80 to maximum Life")),
                Item("Body Armour", "Kaom's Heart", "UNIQUE", "Glorious Plate", Mod("+500 to maximum Life")),
                Item("Gloves", "Doom Grasp", "RARE", "Leviathan Gauntlets", Mod("+100 to maximum Life")),
                Item("Boots", "Gale Track", "RARE", "Two-Toned Boots (Armour/Energy Shield)", Mod("30% increased Movement Speed")),
                Item("Amulet", "Rune Beads", "RARE", "Onyx Amulet"),
                Item("Ring 1", "Storm Loop", "RARE", "Two-Stone Ring (Cold/Lightning)"),
                Item("Ring 2", "Vise Coil", "MAGIC", "Diamond Ring"),
                Item("Belt", "Dread Cord", "RARE", "Stygian Vise"),
                Item("Weapon 1", "Main Axe", "RARE", "Vaal Axe"),
                Item("Weapon 1 Swap", "Swap Bow", "RARE", "Thicket Bow"),
                Item("Flask 1", "Divine Life Flask", "NORMAL", "Divine Life Flask"),
                Item("Flask 2", "Quicksilver Flask", "MAGIC", "Quicksilver Flask"),
                new GearEntry { SlotName = "Jewel 1", ItemName = "Tree Jewel", Rarity = "RARE", BaseName = "Cobalt Jewel", IsJewel = true },
                new GearEntry { SlotName = "Gloves", ItemName = "Later duplicate", Rarity = "RARE", BaseName = "Wool Gloves" },
            };
            return new BuildDetails { BuildName = "Guide build", Gear = gear };
        }

        private static void TestSlots()
        {
            int jewels;
            var cells = EquipmentOverlay.BuildCells(Guide(), false, out jewels);
            var slots = cells.Select(c => c.Slot).ToList();
            foreach (var slot in new[] { "Helmet", "Body Armour", "Gloves", "Boots", "Amulet", "Ring 1", "Ring 2", "Belt", "Weapon 1", "Weapon 2", "Flask 1", "Flask 2" })
                Require(slots.Contains(slot), "Equipment layout is missing slot " + slot + ".");
            Require(!slots.Contains("Ring 3") && !slots.Contains("Weapon 1 Swap") && jewels == 1 && !slots.Any(s => s.StartsWith("Jewel")),
                "Optional ring, swap weapons or tree jewels leaked into the main layout.");
            var cell = cells.ToDictionary(c => c.Slot);
            Require(cell["Helmet"].Units.Bottom <= cell["Body Armour"].Units.Top && cell["Belt"].Units.Top >= cell["Body Armour"].Units.Bottom
                && cell["Weapon 1"].Units.Right <= cell["Body Armour"].Units.Left && cell["Weapon 2"].Units.Left >= cell["Body Armour"].Units.Right
                && cell["Gloves"].Units.Right <= cell["Body Armour"].Units.Left && cell["Boots"].Units.Left >= cell["Body Armour"].Units.Right
                && cell["Flask 1"].Units.Top >= cell["Belt"].Units.Bottom,
                "Slots are not arranged like the in-game inventory (helmet above body, belt below, weapons/gloves/boots beside, flasks underneath).");
            Require(cell["Gloves"].Gear.ItemName == "Doom Grasp", "First guide item per slot should win.");
            Require(cell["Weapon 2"].Gear == null, "Empty weapon slot should stay empty, not borrow another item.");
            var swap = EquipmentOverlay.BuildCells(Guide(), true, out jewels).Select(c => c.Slot).ToList();
            Require(swap.Contains("Weapon 1 Swap") && !swap.Contains("Weapon 1"), "Swap mode did not show the swap weapon set.");

            var current = new BuildDetails { BuildName = "Current", Gear = new List<GearEntry> { Item("Helmet", "Current Hat", "RARE", "Iron Hat"), Item("Weapon 2", "Current Shield", "RARE", "Pine Buckler") } };
            using (var overlay = new EquipmentOverlay(new AppConfig { DisableSave = true }))
            {
                overlay.LoadForTest(Guide(), current);
                Require(overlay.Cells.Single(c => c.Slot == "Helmet").Gear.ItemName == "Glyph Crest" && overlay.Cells.Single(c => c.Slot == "Weapon 2").Gear == null,
                    "Equipment overlay used the Current character instead of the selected guide stage.");
                overlay.LoadForTest(null, current);
                Require(overlay.Cells.All(c => c.Gear == null), "Without a guide the overlay showed Current items.");
            }

            string root = Path.Combine(Path.GetTempPath(), "CompanionArt-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, ItemArtCache.Poe1Folder));
                Directory.CreateDirectory(Path.Combine(root, "bin"));
                Require(ItemArtCache.DiscoverDirectory(GameId.Poe1, Path.Combine(root, "bin") + Path.DirectorySeparatorChar) == Path.Combine(root, ItemArtCache.Poe1Folder),
                    "Item art folder beside the bin directory (executable path with trailing slash) was not found.");
                string uniques = Path.Combine(root, ItemArtCache.Poe1UniqueFolder);
                Directory.CreateDirectory(uniques);
                Require(ItemArtCache.DiscoverDirectory(GameId.Poe1, Path.Combine(root, "bin"), ItemArtCache.Poe1UniqueFolder) == uniques, "Unique item art folder was not found.");
                File.WriteAllBytes(Path.Combine(uniques, "The Stampede.png"), new byte[0]);
                File.WriteAllBytes(Path.Combine(root, ItemArtCache.Poe1Folder, "Assassin's Boots.png"), new byte[0]);
                using (var both = new ItemArtCache(GameId.Poe1, Path.Combine(root, ItemArtCache.Poe1Folder), uniques))
                {
                    string why;
                    Require(both.ArtName(new GearEntry { SlotName = "Boots", ItemName = "Lvl 90, The Stampede, Assassin's Boots", BaseName = "Assassin's Boots", Rarity = "UNIQUE" }, out why) == "The Stampede"
                        && both.ArtName(new GearEntry { SlotName = "Boots", ItemName = "Seven-League Step", BaseName = "Assassin's Boots", Rarity = "UNIQUE" }, out why) == null,
                        "Unique art from the second folder was not used, or a unique fell back to base art.");
                }
            }
            finally { Directory.Delete(root, true); }

            var note = new GearEntry { SlotName = "Helmet", ItemName = "Goldrim", Rarity = "UNIQUE", FromNotes = true };
            Require(TradeSearch.UnsearchableReason(note) != null && TradeSearch.UnsearchableReason(new GearEntry { SlotName = "Ring 1" }) != null
                && TradeSearch.UnsearchableReason(Guide().Gear[2]) == null, ".build notes or empty slots were offered as searchable items.");
            string reason;
            var art = new ItemArtCache(GameId.Poe1, null);
            Require(art.ArtName(note, out reason) == null && reason != null, ".build note received item art.");
            var poe2 = new ItemArtCache(GameId.Poe2, null);
            Require(poe2.ArtName(Guide().Gear[2], out reason) == null && poe2.Unavailable.Contains(ItemArtCache.Poe2Folder),
                "Missing PoE2 art folder was not reported.");
            string tip = EquipmentOverlay.ToolTipFor(new GearEntry { SlotName = "Gloves", ItemName = "Doom Grasp", Rarity = "RARE", BaseName = "Leviathan Gauntlets",
                ItemLevel = 86, LevelRequirement = 70, SocketCount = 4, MaxLinks = 3, Affixes = new List<ItemAffix> { Mod("+100 to maximum Life") } }, art);
            Require(tip.Contains("86") && tip.Contains("70") && tip.Contains("+100 to maximum Life"), "Tooltip lost item level/required level/mod details: " + tip);
        }

        private static void TestItemArtPoe2()
        {
            string root = Path.Combine(Path.GetTempPath(), "CompanionPoe2Art-" + Guid.NewGuid().ToString("N"));
            string bin = Path.Combine(root, "bin") + Path.DirectorySeparatorChar;
            string poe1 = Path.Combine(root, ItemArtCache.Poe1Folder), poe2 = Path.Combine(root, ItemArtCache.Poe2Folder);
            Action<string, Color> save = (path, color) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var bitmap = new Bitmap(12, 24))
                {
                    using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(color);
                    bitmap.Save(path);
                }
            };
            try
            {
                Directory.CreateDirectory(bin);
                save(Path.Combine(poe1, "Amethyst Ring.png"), Color.Red);
                Require(ItemArtCache.DiscoverDirectory(GameId.Poe2, bin) == null, "PoE2 borrowed a PoE1 folder when its art was missing.");
                save(Path.Combine(poe2, "Ring", "Amethyst Ring.png"), Color.Blue);
                save(Path.Combine(poe2, "Gloves", "nested", "Massive Mitts.png"), Color.Green);
                save(Path.Combine(poe2, "Unique", "Hand of Wisdom and Action.png"), Color.Gold);
                save(Path.Combine(poe2, "Unique", "Briarpatch.png"), Color.Purple);
                save(Path.Combine(poe2, "Active Skill Gem", "Briarpatch.png"), Color.Red);
                save(Path.Combine(poe2, "Shield", "Runemastered Venerable Defender.png"), Color.Gray);
                save(Path.Combine(poe2, "Buckler", "Runemastered Venerable Defender.png"), Color.Silver);
                save(Path.Combine(poe2, "LifeFlask", "Giant Life Flask.png"), Color.Brown);
                save(Path.Combine(poe2, "UtilityFlask", "Thawing Charm.png"), Color.Cyan);
                Require(ItemArtCache.DiscoverDirectory(GameId.Poe2, bin) == poe2 &&
                    ItemArtCache.DiscoverDirectory(GameId.Poe1, bin) == poe1 &&
                    ItemArtCache.DiscoverDirectory(GameId.Poe2, bin, ItemArtCache.Poe1Folder) == null &&
                    ItemArtCache.DiscoverDirectory(GameId.Poe1, bin, ItemArtCache.Poe2Folder) == null,
                    "Executable-parent discovery is not game-isolated, including a trailing separator.");
                string beside = Path.Combine(bin, ItemArtCache.Poe2Folder);
                Directory.CreateDirectory(beside);
                Require(ItemArtCache.DiscoverDirectory(GameId.Poe2, bin) == beside, "Executable-side PoE2 art did not take precedence.");
                Directory.Delete(beside);
                using (var first = new ItemArtCache(GameId.Poe1, ItemArtCache.DiscoverDirectory(GameId.Poe1, bin)))
                using (var second = new ItemArtCache(GameId.Poe2, ItemArtCache.DiscoverDirectory(GameId.Poe2, bin)))
                {
                    string reason;
                    var ring = Item("Ring 1", "Rare ring", "RARE", "Amethyst Ring");
                    Require(first.Get(first.ArtName(ring, out reason)).GetPixel(0, 0).ToArgb() == Color.Red.ToArgb() &&
                        second.Get(second.ArtName(ring, out reason)).GetPixel(0, 0).ToArgb() == Color.Blue.ToArgb(),
                        "A shared base name leaked art across games.");
                    Require(second.ArtName(Item("Gloves", "Rare gloves", "RARE", "Massive Mitts (variant)"), out reason) == "Massive Mitts" &&
                        second.Get("Massive Mitts") != null, "Nested PoE2 base art or the PoB base variant suffix was not handled.");
                    Require(second.ArtName(Item("Gloves", "Lvl 90, Hand of Wisdom and Action, Furtive Wraps", "UNIQUE", "Furtive Wraps"), out reason) ==
                        "Hand of Wisdom and Action", "PoE2 unique art failed PoB stage/title/base normalisation.");
                    Require(second.ArtName(Item("Body Armour", "Briarpatch, Leather Vest", "UNIQUE", "Leather Vest"), out reason) == "Briarpatch" &&
                        second.Get("Briarpatch").GetPixel(0, 0).ToArgb() == Color.Purple.ToArgb(),
                        "A skill gem was used for unique equipment with the same name.");
                    Require(second.ArtName(Item("Shield", "Rare shield", "RARE", "Runemastered Venerable Defender"), out reason) == null &&
                        reason.Contains("Ambiguous"), "Duplicate equipment art was silently selected instead of reported.");
                    Require(second.ArtName(Item("Gloves", "Missing unique", "UNIQUE", "Massive Mitts"), out reason) == null &&
                        second.ArtName(Item("Helmet", "Missing base", "RARE", "Missing Helmet"), out reason) == null,
                        "Missing PoE2 art borrowed an unrelated image.");
                    Require(second.ArtName(Item("Flask 1", "Magic flask", "MAGIC", "Giant Life Flask"), out reason) == "Giant Life Flask" &&
                        second.ArtName(Item("Charm 1", "Magic charm", "MAGIC", "Thawing Charm"), out reason) == "Thawing Charm",
                        "PoE2 flask/charm category art was not indexed.");
                    var note = Item("Gloves", "Hand of Wisdom and Action", "UNIQUE", "Furtive Wraps");
                    note.FromNotes = true;
                    Require(second.ArtName(note, out reason) == null && reason.Contains("note"), "A PoE2 guide note received rolled-item art.");
                }
            }
            finally { Directory.Delete(root, true); }
        }

        private const string Poe1Stats = @"-- This file contains the trade site data from https://www.pathofexile.com/api/trade/data/stats
return {
	{ [""entries""] = {
		{ [""id""] = ""explicit.stat_3299347043"", [""text""] = ""+# to maximum Life"", [""type""] = ""explicit"", },
		{ [""id""] = ""explicit.stat_803737631"", [""text""] = ""+# to Accuracy Rating"", [""type""] = ""explicit"", },
		{ [""id""] = ""explicit.stat_2923486259"", [""text""] = ""+#% to Chaos Resistance"", [""type""] = ""explicit"", },
		{ [""id""] = ""explicit.stat_681332047"", [""text""] = ""#% increased Attack Speed"", [""type""] = ""explicit"", },
		{ [""id""] = ""explicit.stat_dup_a"", [""text""] = ""#% increased Duplicate"", [""type""] = ""explicit"", },
		{ [""id""] = ""explicit.stat_dup_b"", [""text""] = ""#% increased Duplicate"", [""type""] = ""explicit"", },
	}, },
	{ [""entries""] = {
		{ [""id""] = ""implicit.stat_681332047"", [""text""] = ""#% increased Attack Speed"", [""type""] = ""implicit"", },
		{ [""id""] = ""implicit.stat_3739863694"", [""text""] = ""#% chance to Impale Enemies on Hit with Attacks"", [""type""] = ""implicit"", },
	}, },
	{ [""entries""] = {
		{ [""id""] = ""crafted.stat_3032590688"", [""text""] = ""Adds # to # Physical Damage to Attacks"", [""type""] = ""crafted"", },
	}, },
}";

        private const string Poe2Stats = @"-- This file contains the trade site data from https://www.pathofexile.com/api/trade2/data/stats
return {
	{ [""entries""] = {
		{ [""id""] = ""explicit.stat_3299347043"", [""text""] = ""+# to maximum Life"", [""type""] = ""explicit"", },
		{ [""id""] = ""explicit.stat_3489782002"", [""text""] = ""+# to maximum Energy Shield"", [""type""] = ""explicit"", },
		{ [""id""] = ""explicit.stat_4052037485"", [""text""] = ""+# to maximum Energy Shield (Local)"", [""type""] = ""explicit"", },
	}, },
	{ [""entries""] = {
		{ [""id""] = ""rune.stat_2901986750"", [""text""] = ""+#% to all Elemental Resistances"", [""type""] = ""augment"", },
	}, },
}";

        private static GearEntry Leviathan()
        {
            return new GearEntry
            {
                SlotName = "Gloves", SlotLabel = "Gloves", ItemName = "Doom Grasp", Rarity = "RARE", BaseName = "Leviathan Gauntlets",
                ItemLevel = 86, LevelRequirement = 70, SocketCount = 4, MaxLinks = 3,
                Affixes = new List<ItemAffix>
                {
                    new ItemAffix { Text = "9% increased Attack Speed", Kind = "implicit", Exarch = true },
                    new ItemAffix { Text = "15% chance to Impale Enemies on Hit with Attacks", Kind = "implicit", Eater = true },
                    Mod("{tags:life}+100 to maximum Life"),
                    Mod("{range:0.5}+451 to Accuracy Rating"),
                    Mod("+25% to Chaos Resistance"),
                    Mod("10% increased Attack Speed"),
                    new ItemAffix { Text = "{range:0.5}Adds 4 to 8 Physical Damage to Attacks", Kind = "explicit", Crafted = true },
                    Mod("Prefix: {range:0.5}IncreasedLife7"),
                    Mod("12% increased Duplicate"),
                    Mod("Totally unknown wording 5"),
                }
            };
        }

        private static void TestTradePoe1()
        {
            string error;
            Require(TradeStatIndex.Parse(Poe1Stats, GameId.Poe2, out error) == null && error != null, "A PoE1 stat list was accepted for PoE2.");
            var index = TradeStatIndex.Parse(Poe1Stats, GameId.Poe1, out error);
            Require(index != null && index.Count == 9, "Lua stat fixture parse failed: " + error);
            var plan = TradeSearch.Plan(Leviathan(), GameId.Poe1, index, null);
            Func<string, TradeFilter> by = id => plan.Filters.SingleOrDefault(f => f.Stat.Id == id);
            Require(by("implicit.stat_681332047") != null && by("implicit.stat_681332047").Kind == "implicit" && by("implicit.stat_681332047").Value == 9
                && by("explicit.stat_681332047") != null && by("explicit.stat_681332047").Value == 10,
                "Implicit and explicit attack speed were conflated.");
            Require(by("implicit.stat_3739863694") != null && by("explicit.stat_3299347043").Value == 100 && by("explicit.stat_803737631").Value == 451
                && by("explicit.stat_2923486259").Value == 25, "Final values lost (or {range}/{tags} markup not stripped).");
            var crafted = by("crafted.stat_3032590688");
            Require(crafted != null && crafted.Kind == "crafted" && !crafted.Enabled && crafted.Value == 6, "Crafted mod not mapped to crafted group, averaged, and off by default.");
            Require(plan.Filters.Where(f => f.Kind != "crafted").All(f => f.Enabled), "Rare explicit/implicit filters should start enabled.");
            Require(by("explicit.stat_3299347043").Min == 80 && by("explicit.stat_803737631").Min == 360, "80% threshold minimums wrong.");
            Require(plan.Unmatched.Any(u => u.Text.StartsWith("Prefix:") && u.Reason.Contains("metadata"))
                && plan.Unmatched.Any(u => u.Text.Contains("Duplicate") && u.Reason.StartsWith("Ambiguous"))
                && plan.Unmatched.Any(u => u.Text.StartsWith("Totally unknown")), "Metadata/ambiguous/unknown mods were not reported.");
            Require(plan.ItemLevel == 86 && plan.Sockets == 4 && plan.Links == 3, "Item level/sockets/links not taken from the item.");

            string json = TradeSearch.QueryJson(plan);
            Require(!json.Contains("ilvl") && !json.Contains("socket_filters") && !json.Contains("\"lvl\""), "Optional ilvl/socket filters were added without being chosen.");
            plan.UseItemLevel = true; plan.UseLinks = true;
            json = TradeSearch.QueryJson(plan);
            Require(json.Contains("\"misc_filters\"") && json.Contains("\"ilvl\":{\"min\":86}") && !json.Contains(":70}") && !json.Contains("\"lvl\""), "Item level filter wrong or level requirement leaked into it.");
            Require(json.Contains("\"links\":{\"min\":3}") && !json.Contains("\"sockets\":{"), "Links filter wrong or sockets added without being chosen.");
            Require(json.Contains("\"type\":\"Leviathan Gauntlets\"") && json.Contains("\"option\":\"rare\"") && !json.Contains("crafted.stat_") && !json.Contains("Doom Grasp"),
                "Rare query must use base + rarity, skip the unchecked crafted mod and not search the random rare name.");
            ApplyAndCheckThreshold(plan);

            string url = TradeSearch.Url(plan, "Mercenaries of Trarthus");
            Require(url.StartsWith("https://www.pathofexile.com/trade/search/Mercenaries+of+Trarthus?q="), "PoE1 trade URL/league wrong: " + url);
            Require(TradeSearch.EncodeLeague("Hardcore Ünïque/x") == "Hardcore+%C3%9Cn%C3%AFque%2Fx", "League encoding wrong.");
            bool threw = false;
            try { TradeSearch.Url(plan, "  "); } catch (ArgumentException) { threw = true; }
            Require(threw, "A trade URL was built without a league.");

            var unique = TradeSearch.Plan(new GearEntry { SlotName = "Body Armour", ItemName = "Kaom's Heart", Rarity = "UNIQUE", BaseName = "Glorious Plate",
                Affixes = new List<ItemAffix> { Mod("+500 to maximum Life") } }, GameId.Poe1, index, null);
            Require(unique.Name == "Kaom's Heart" && unique.Rarity == "unique" && unique.Filters.All(f => !f.Enabled), "Unique search should use name/base with roll filters off.");
            // Real PoB/guide titles: "<stage label>, <unique>, <base>".
            foreach (var pair in new[] {
                new[] { "Lvl 90, The Stampede, Assassin's Boots", "Assassin's Boots", "The Stampede" },
                new[] { "Lvl 68-96 - The Yielding Mortality, Imperial Staff", "Imperial Staff", "The Yielding Mortality" },
                new[] { "Rumi's Concoction, Granite Flask", "Granite Flask", "Rumi's Concoction" },
                new[] { "Lvl 96, Lethal Pride, Timeless Jewel", "Timeless Jewel", "Lethal Pride" } })
                Require(TradeSearch.Plan(new GearEntry { SlotName = "Boots", ItemName = pair[0], Rarity = "UNIQUE", BaseName = pair[1] }, GameId.Poe1, index, null).Name == pair[2],
                    "Unique trade name not derived from PoB title '" + pair[0] + "'.");
            var noIndex = TradeSearch.Plan(Leviathan(), GameId.Poe1, null, "Official trade stat list not found.");
            Require(noIndex.Filters.Count == 0 && noIndex.Unmatched.Count >= 8 && noIndex.StatSource.Contains("not found") && !noIndex.HasConstraintsBeyondBase,
                "Missing stat list must surface every mod as unmatched (base-only search needs confirmation).");
        }

        private static void ApplyAndCheckThreshold(TradeSearchPlan plan)
        {
            TradeSearch.ApplyThreshold(plan, 100);
            Require(plan.Filters.Single(f => f.Stat.Id == "explicit.stat_3299347043").Min == 100, "100% threshold should require the guide roll.");
            TradeSearch.ApplyThreshold(plan, 80);
        }

        private static void TestTradePoe2()
        {
            string error;
            var index = TradeStatIndex.Parse(Poe2Stats, GameId.Poe2, out error);
            Require(index != null, "PoE2 fixture parse failed: " + error);
            var gear = new GearEntry
            {
                SlotName = "Body Armour", ItemName = "Grim Shell", Rarity = "RARE", BaseName = "Expert Hexer's Robe", ItemLevel = 82, SocketCount = 2, MaxLinks = 2,
                Affixes = new List<ItemAffix> { Mod("+90 to maximum Life"), Mod("+40 to maximum Energy Shield"), Mod("+12% to all Elemental Resistances", "rune") }
            };
            var plan = TradeSearch.Plan(gear, GameId.Poe2, index, null);
            Require(plan.Filters.Any(f => f.Stat.Id == "explicit.stat_4052037485") && !plan.Filters.Any(f => f.Stat.Id == "explicit.stat_3489782002"),
                "Body armour energy shield should use the local stat.");
            var rune = plan.Filters.Single(f => f.Stat.Id == "rune.stat_2901986750");
            Require(rune.Kind == "rune" && !rune.Enabled, "PoE2 rune mod should map to the augment group and start off.");
            Require(plan.Links == 0, "PoE2 has no links but a links value was kept.");
            plan.UseSockets = true; plan.UseLinks = true;
            string json = TradeSearch.QueryJson(plan);
            Require(json.Contains("\"rune_sockets\":{\"min\":2}") && !json.Contains("socket_filters") && !json.Contains("links"), "PoE2 socket filter wrong.");
            string url = TradeSearch.Url(plan, "Rise of the Abyssal");
            Require(url.StartsWith("https://www.pathofexile.com/trade2/search/poe2/Rise+of+the+Abyssal?q="), "PoE2 trade URL wrong: " + url);
            var poe1Rune = TradeSearch.Plan(gear, GameId.Poe1, TradeStatIndex.Parse(Poe1Stats, GameId.Poe1, out error), null);
            Require(poe1Rune.Unmatched.Any(u => u.Text.Contains("Elemental Resistances") && u.Reason.Contains("rune")), "PoE1 must report rune mods as unsupported.");
        }

        private static void TestLeagues()
        {
            string json = "{\"result\":[{\"id\":\"Standard\",\"realm\":\"pc\"},{\"id\":\"Rise of the Abyssal\",\"realm\":\"poe2\"},{\"id\":\"Hardcore\",\"realm\":\"pc\"},{\"id\":\"Standard\",\"realm\":\"poe2\"},{\"id\":\"Ruthless\",\"realm\":\"xbox\"}]}";
            Require(TradeSearch.ParseLeagues(json, GameId.Poe1).SequenceEqual(new[] { "Standard", "Hardcore" }), "PoE1 league list filtering wrong.");
            Require(TradeSearch.ParseLeagues(json, GameId.Poe2).SequenceEqual(new[] { "Rise of the Abyssal", "Standard" }), "PoE2 league list filtering wrong.");
            Require(TradeSearch.ParseLeagues("{}", GameId.Poe1).Count == 0, "Bad league JSON should give an empty list.");
            Require(TradeSearch.LeaguesEndpoint(GameId.Poe1).EndsWith("/api/trade/data/leagues") && TradeSearch.LeaguesEndpoint(GameId.Poe2).EndsWith("/api/trade2/data/leagues"),
                "League endpoints wrong.");
        }

        private static void Capture(Form form, string directory, string name)
        {
            Application.DoEvents();
            using (var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height)))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(directory, name + ".png"));
            }
        }

        private static void TestItemCards(string screenshots)
        {
            var rare = Leviathan();
            rare.ItemLevel = 86;
            rare.LevelRequirement = 70;
            rare.Quality = 20;
            rare.SocketCount = 4;
            rare.MaxLinks = 4;
            var unique = new GearEntry
            {
                SlotName = "Boots", ItemName = "Lvl 90, The Stampede, Assassin's Boots", BaseName = "Assassin's Boots", Rarity = "UNIQUE",
                LevelRequirement = 63, Affixes = new List<ItemAffix> { Mod("Your Movement Speed is 150% of its base value") }
            };
            foreach (var gear in new[] { rare, unique })
            using (var card = new EquipmentItemCard(gear.SlotName, gear, null))
            {
                Require(card.Lines.Any(l => l.Header && l.Text == gear.BaseName), "Item card lost the base-type header.");
                if (gear == unique)
                    Require(card.Lines.First().Text == "The Stampede" && card.Rarity == PobStatParser.GetRarityColor("UNIQUE"),
                        "Unique item card did not use the canonical name and unique colour.");
                else
                {
                    Require(card.Lines.Any(l => l.Text.Contains("Item level 86")) && card.Lines.Any(l => l.Text.Contains("Requires level 70")) &&
                        card.Lines.Any(l => l.Text.Contains("4 sockets, 4 linked")), "Item card lost item level, requirements or links.");
                    Require(card.Lines.Any(l => l.Divider && l.Text == "Implicit") && card.Lines.Any(l => l.Divider && l.Text == "Explicit"),
                        "Item card does not separate implicit and explicit modifiers.");
                    Require(card.Lines.Any(l => l.Text.Contains("crafted") && l.Color == EquipmentItemCard.Crafted) &&
                        card.Lines.Any(l => l.Text.Contains("maximum Life") && l.Color == EquipmentItemCard.Modifier),
                        "Crafted and ordinary mods have lost their distinct colours.");
                }
                foreach (float dpi in new[] { 96f, 120f, 144f })
                using (var bitmap = new Bitmap(1000, 1200))
                {
                    bitmap.SetResolution(dpi, dpi);
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        var size = card.Measure(graphics, new Size(1000, 1200));
                        Require(size.Width <= 1000 && size.Height <= 1200 && card.Lines.All(l => new Rectangle(Point.Empty, size).Contains(l.Bounds)),
                            "Item card text is clipped at DPI " + dpi + ".");
                        using (var capture = new Bitmap(size.Width, size.Height))
                        {
                            capture.SetResolution(dpi, dpi);
                            using (var draw = Graphics.FromImage(capture)) card.Draw(draw, new Rectangle(Point.Empty, size));
                            Require(capture.GetPixel(size.Width / 2, size.Height - 8).ToArgb() == EquipmentItemCard.Background.ToArgb(),
                                "Item tooltip has reverted to the white Windows background.");
                            capture.Save(Path.Combine(screenshots, "item-card-" + gear.Rarity.ToLowerInvariant() + "-" + (int)(dpi / 96f * 100) + ".png"));
                        }
                    }
                }
                using (var bitmap = new Bitmap(360, 800))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    var size = card.Measure(graphics, bitmap.Size);
                    Require(size.Width < bitmap.Width && card.Lines.All(l => l.Bounds.Right <= size.Width && l.Bounds.Bottom <= size.Height),
                        "Item tooltip does not wrap within a narrow working area.");
                }
            }
            using (var card = new EquipmentItemCard("Helmet", new GearEntry
            {
                SlotName = "Helmet", ItemName = "Life and resistance priorities", FromNotes = true, Priorities = new List<string> { "Life" }
            }, null))
                Require(card.Lines.Any(l => l.Text.Contains("Not a concrete rolled item")) && card.Lines.Any(l => l.Text.Contains("Life")) &&
                    !card.Lines.Any(l => l.Text.Contains("Right-click")), "Styled guide note claims to be a searchable rolled item.");
            using (var card = new EquipmentItemCard("Weapon 2", null, null))
                Require(card.Lines.Any(l => l.Text.Contains("Empty in this guide stage")), "Styled empty slot lost its explanation.");
        }

        private static void TestUi(string directory, string screenshots)
        {
            Directory.CreateDirectory(screenshots);
            string pob = Path.Combine(directory, "pob");
            Directory.CreateDirectory(Path.Combine(pob, "Data"));
            File.WriteAllText(Path.Combine(pob, "Data", "TradeSiteStats.lua"), Poe1Stats);
            var config = new AppConfig { Game = GameId.Poe1, DisableSave = true, PobPath = pob, TradeLeague = "", OverlaySelectorHotkeyModifiers = 2, OverlaySelectorHotkeyKey = (int)Keys.G };

            var guide = Guide();
            guide.Gear[2] = Leviathan();
            guide.Gear.Add(new GearEntry { SlotName = "Ring 3", ItemName = "Berek's Grip", Rarity = "UNIQUE", FromNotes = true });
            using (var overlay = new EquipmentOverlay(config))
            {
                overlay.Show();
                overlay.LoadForTest(guide, null);
                using (var tooltip = new EquipmentItemToolTip())
                {
                    bool popped = false, drawn = false;
                    tooltip.Popup += (s, e) => { popped = !e.Cancel && e.ToolTipSize.Width > 200 && e.ToolTipSize.Height > 100; };
                    tooltip.Draw += (s, e) => { drawn = true; };
                    tooltip.SetItem(overlay, "Gloves", Leviathan(), null);
                    tooltip.Show(tooltip.GetToolTip(overlay), overlay, new Point(20, 40), 30000);
                    var timeout = DateTime.UtcNow.AddSeconds(2);
                    while ((!popped || !drawn) && DateTime.UtcNow < timeout)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(10);
                    }
                    Require(tooltip.OwnerDraw && popped && drawn, "The live hover popup is not using the styled item-card renderer.");
                    tooltip.Clear(overlay);
                    Require(string.IsNullOrEmpty(tooltip.GetToolTip(overlay)), "Item tooltip remains attached after clearing the hover.");
                }
                float applied = 1f;
                foreach (var scale in new[] { 1f, 1.25f, 1.5f })
                {
                    if (scale > applied) { overlay.Scale(new SizeF(scale / applied, scale / applied)); applied = scale; }
                    Application.DoEvents();
                    var body = overlay.Controls.OfType<Control>().SelectMany(c => new[] { c }.Concat(c.Controls.Cast<Control>())).FirstOrDefault(c => c.GetType().Name == "EquipmentPanel");
                    Require(body != null && body.Visible && body.Height > 0 && body.Bottom <= overlay.ClientSize.Height, "Equipment grid is clipped or hidden at scale " + scale + ".");
                    float unit = Math.Max(24f, (body.ClientSize.Width - 4) / EquipmentOverlay.GridUnits);
                    Require(overlay.Cells.Max(c => c.Units.Bottom) * unit + 2 <= body.ClientSize.Height + 1, "Lower equipment slots (belt/flasks) are cut off at scale " + scale + ".");
                    Capture(overlay, screenshots, "equipment-overlay-" + (int)(scale * 100));
                }
                Require(overlay.Cells.Any(c => c.Slot == "Ring 3" && c.Gear != null && c.Gear.FromNotes), "Third ring guide note not shown.");
            }

            using (var dialog = new TradeSearchDialog(config, Leviathan()))
            {
                dialog.Show();
                foreach (var size in new[] { dialog.MinimumSize, new Size(860, 640) })
                {
                    dialog.Size = size;
                    Application.DoEvents();
                    var grid = Descendants(dialog).OfType<DataGridView>().Single();
                    Require(grid.Rows.Count == dialog.Plan.Filters.Count && grid.Height > 60, "Trade filter editor rows missing or squashed at " + size + ".");
                    var buttons = Descendants(dialog).OfType<Button>().Where(b => b.Visible).ToList();
                    Require(buttons.All(b => b.Bottom <= dialog.ClientSize.Height && b.Right <= dialog.ClientSize.Width), "Trade dialog buttons are cut off at " + size + ".");
                    Capture(dialog, screenshots, "trade-dialog-" + size.Width);
                }
                Require(Descendants(dialog).OfType<TextBox>().Any(t => t.Text.Contains("NOT SEARCHED") && t.Text.Contains("Totally unknown")), "Unmatched mods are not shown before searching.");
                var league = Descendants(dialog).OfType<ComboBox>().First();
                Require(string.IsNullOrEmpty(league.Text), "A trade league was preselected without the user choosing one.");
                dialog.Scale(new SizeF(1.5f, 1.5f));
                Application.DoEvents();
                Capture(dialog, screenshots, "trade-dialog-150");
                Require(dialog.LastUrl == null, "Trade dialog opened a URL without the user pressing search.");
            }

            var entries = new[] { "Gems", "Passives", "Quests", "Recommended equipment", "Update character" }.Select(n => new OverlaySelector.Entry { Name = n, Toggle = () => { } }).ToList();
            using (var selector = new OverlaySelector(entries, "Ctrl+G"))
            {
                selector.ShowNear(Cursor.Position);
                Application.DoEvents();
                var area = Screen.FromControl(selector).WorkingArea;
                Require(area.Contains(selector.Bounds), "Overlay selector is not inside the screen working area.");
                Capture(selector, screenshots, "overlay-selector");
                selector.Scale(new SizeF(1.5f, 1.5f));
                Application.DoEvents();
                Require(selector.Controls[0].Controls.OfType<Button>().All(b => b.Right <= selector.ClientSize.Width), "Selector clipped at 150%.");
                Capture(selector, screenshots, "overlay-selector-150");
            }
            using (var view = new GuideBuildView())
            {
                view.SetSource(config);
                view.EnsureLoaded();
                var staged = Guide();
                staged.Loadouts.AddRange(Enumerable.Range(1, 24).Select(n => "Stage " + n + " - equipment, skills and passives {" + n + "}"));
                staged.ActiveLoadout = staged.Loadouts[0];
                view.Render(staged);
                using (var selector = new OverlaySelector(entries, "Ctrl+G", view))
                {
                    selector.ShowNear(Cursor.Position);
                    float applied = 1f;
                    foreach (float scale in new[] { 1f, 1.25f, 1.5f })
                    {
                        if (scale > applied) { selector.Scale(new SizeF(scale / applied, scale / applied)); applied = scale; }
                        Application.DoEvents();
                        var frame = selector.Controls[0];
                        Require(frame.Controls.Cast<Control>().All(c => frame.ClientRectangle.Contains(c.Bounds)),
                            "Stage-enabled selector clips a control at scale " + scale + ".");
                        var picker = frame.Controls.OfType<ComboBox>().Single();
                        Require(picker.Enabled && picker.Items.Count == 24 && picker.DropDownWidth > picker.Width,
                            "Long Guide stage names/list are unavailable in the selector.");
                        Capture(selector, screenshots, "overlay-selector-stages-" + (int)(scale * 100));
                    }
                }
            }
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
    }
}
