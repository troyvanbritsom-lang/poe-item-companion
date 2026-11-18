using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    internal static class UiTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr key, IntPtr data);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static IEnumerable<Control> Children(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var descendant in Children(child)) yield return descendant;
            }
        }

        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, Private).GetValue(instance);
        }

        private static void Invoke(object instance, string method, params object[] args)
        {
            instance.GetType().GetMethod(method, Private).Invoke(instance, args);
        }

        private static void Capture(Form form, string folder, string name)
        {
            if (string.IsNullOrEmpty(folder)) return;
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(folder, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static void Layout(Form form, Size size)
        {
            form.Size = size;
            form.PerformLayout();
            Application.DoEvents();
        }

        private static void Contained(Control control)
        {
            if (control is Label && string.IsNullOrEmpty(control.Text)) return;
            Require(control.Width > 0 && control.Height > 0, "Collapsed control: " + control.Text);
            Require(control.Left >= 0 && control.Top >= 0 && control.Right <= control.Parent.ClientSize.Width + 2
                && control.Bottom <= control.Parent.ClientSize.Height + 2,
                "Control outside its layout: " + control.Text + " " + control.Bounds + " parent " + control.Parent.ClientSize);
        }

        private static void CheckRows(Control root)
        {
            foreach (var table in Children(root).OfType<TableLayoutPanel>())
            {
                foreach (Control child in table.Controls) Contained(child);
                var children = table.Controls.Cast<Control>().ToArray();
                for (int i = 0; i < children.Length; i++)
                    for (int j = i + 1; j < children.Length; j++)
                        Require(!children[i].Bounds.IntersectsWith(children[j].Bounds), "Overlapping table controls: " + children[i].Text + " / " + children[j].Text);
            }
            foreach (var flow in Children(root).OfType<FlowLayoutPanel>())
                foreach (Control child in flow.Controls) Contained(child);
        }

        private static double Luminance(Color color)
        {
            Func<byte, double> linear = v => v / 255.0 <= 0.04045 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4);
            return 0.2126 * linear(color.R) + 0.7152 * linear(color.G) + 0.0722 * linear(color.B);
        }

        private static void Contrast(Color foreground, Color background)
        {
            Require((Luminance(foreground) + 0.05) / (Luminance(background) + 0.05) >= 4.5, "Theme text contrast below 4.5:1.");
        }

        public static int Run(string screenshots)
        {
            byte[] original = File.Exists(AppConfig.GetConfigFilePath()) ? File.ReadAllBytes(AppConfig.GetConfigFilePath()) : null;
            string directory = Path.Combine(Path.GetTempPath(), "CompanionUI-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (!string.IsNullOrEmpty(screenshots)) Directory.CreateDirectory(screenshots);
                Contrast(UiTheme.Text, UiTheme.Canvas);
                Contrast(UiTheme.Muted, UiTheme.Surface);
                Contrast(UiTheme.Accent, UiTheme.Surface);
                Contrast(UiTheme.Text, UiTheme.Input);
                Contrast(UiTheme.Text, UiTheme.Primary);
                Require(GemVisual.Border(0) == UiTheme.Muted && GemVisual.Border(99) == UiTheme.Muted &&
                    GemVisual.Border(1) != GemVisual.Border(2) && GemVisual.Border(2) != GemVisual.Border(3),
                    "Gem color fallback or attribute colors changed.");
                TestChrome(screenshots);
                TestGemIcons(directory);
                using (var picture = new Bitmap(28, 14))
                using (var graphics = Graphics.FromImage(picture))
                {
                    graphics.Clear(Color.Magenta);
                    picture.Save(Path.Combine(directory, "Fireball.png"));
                }
                foreach (GameId game in new[] { GameId.Poe1, GameId.Poe2 })
                {
                    var config = new AppConfig { Game = game, DisableSave = true, BuildDirectory = directory,
                        GemIconDirectory = directory,
                        PobPath = directory, GameLogPath = Path.Combine(directory, "missing.log"),
                        AutoDetectLatestBuild = false, AutoReloadBuild = false, GemAutoShow = false, PassiveAutoShow = false };
                    TestSettings(config, screenshots);
                    TestDashboard(config, screenshots);
                    TestOverlays(config, screenshots);
                    QuestPlannerTests.TestUi(config, screenshots);
                }
                using (var log = new LogViewerForm())
                {
                    log.Show();
                    Layout(log, log.MinimumSize);
                    foreach (var button in Children(log).OfType<Button>()) Contained(button);
                    log.Hide();
                }
                Require(original == null ? !File.Exists(AppConfig.GetConfigFilePath()) : original.SequenceEqual(File.ReadAllBytes(AppConfig.GetConfigFilePath())),
                    "UI harness changed user configuration.");
                Console.WriteLine("PASSED: both games, source separation, settings cancel, table/flow containment, resize, scaled layout, dark controls, contrast, tabs, trees, overlay rows and non-activation.");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("FAILED UI: " + ex); return 1; }
            finally { File.Delete(Path.Combine(directory, "Fireball.png")); Directory.Delete(directory); }
        }

        private static void TestGemIcons(string directory)
        {
            string folder = Path.Combine(directory, "icon-fixtures");
            Directory.CreateDirectory(folder);
            try
            {
                string app = Path.Combine(folder, "bin");
                Directory.CreateDirectory(app);
                string poe1 = Path.Combine(folder, "poe 1 gem");
                Directory.CreateDirectory(poe1);
                Require(GemIconCache.DiscoverDirectory(null, GameId.Poe1, app) == poe1, "Parent gem folder discovery failed.");
                Require(GemIconCache.DiscoverDirectory(null, GameId.Poe2, app) != poe1, "PoE1 folder leaked into PoE2.");
                Directory.CreateDirectory(Path.Combine(app, "poe 1 gem"));
                Require(GemIconCache.DiscoverDirectory(null, GameId.Poe1, app) == Path.Combine(app, "poe 1 gem"), "Executable-side directory should take precedence.");
                Require(GemIconCache.DiscoverDirectory("..\\poe 1 gem", GameId.Poe1, app) == poe1, "Relative configured path failed.");
                string poe2 = Path.Combine(folder, "poe2_gems");
                Directory.CreateDirectory(poe2);
                try
                {
                    Require(GemIconCache.DiscoverDirectory(null, GameId.Poe2, app) == poe2,
                        "PoE2 underscore-named gem folder was not discovered in the app's parent directory.");
                    Require(GemIconCache.DiscoverDirectory(null, GameId.Poe1, app) != poe2,
                        "PoE2 underscore-named images leaked into PoE1.");
                    string beside = Path.Combine(app, "poe2_gems");
                    Directory.CreateDirectory(beside);
                    try
                    {
                        Require(GemIconCache.DiscoverDirectory(null, GameId.Poe2, app) == beside,
                            "Executable-side underscore folder did not take precedence.");
                        Require(GemIconCache.DiscoverDirectory(poe2, GameId.Poe2, app) == poe2,
                            "Automatic discovery overrode the explicitly configured gem folder.");
                    }
                    finally { Directory.Delete(beside); }
                }
                finally { Directory.Delete(poe2); }
                foreach (string category in new[] { "skill_gems", "support_gems", "awakened_support_gems", "transfigured_skill_gems", "vaal_skill_gems" })
                    Directory.CreateDirectory(Path.Combine(poe1, category));
                using (var picture = new Bitmap(24, 12))
                {
                    using (var graphics = Graphics.FromImage(picture)) graphics.Clear(Color.Magenta);
                    foreach (string name in new[] { "skill_gems\\Fireball inventory icon.png",
                        "support_gems\\Faster_Attacks_Support_inventory_icon.png",
                        "awakened_support_gems\\Awakened Added Fire Damage Support inventory icon.png",
                        "transfigured_skill_gems\\Ice Nova of Frostbolts inventory icon.png",
                        "vaal_skill_gems\\Vaal Fireball inventory icon.png", "Ice Nova.png",
                        "Barrage.png", "Duplicate.png", "skill_gems\\Duplicate_inventory_icon.png" })
                        picture.Save(Path.Combine(poe1, name));
                }
                File.WriteAllText(Path.Combine(poe1, "Broken.png"), "not an image");
                using (var cache = new GemIconCache(new AppConfig { Game = GameId.Poe1, GemIconDirectory = poe1 }))
                {
                    var image = cache.Get("Fireball");
                    Require(image != null && image.Width == 24 && image.Height == 12 && image.GetPixel(0, 0).ToArgb() == Color.Magenta.ToArgb(),
                        "Local image content/dimensions lost.");
                    Require(object.ReferenceEquals(image, cache.Get("FIREBALL")), "Image was not cached.");
                    Require(cache.Get("Faster Attacks Support") != null, "Exact support image lookup failed.");
                    Require(object.ReferenceEquals(cache.Get("Faster Attacks", "Support"), cache.Get("Faster Attacks Support", "Support")) &&
                        cache.Get("Faster Attacks", "Support") != null, "PoE1 support metadata did not select the canonical support image.");
                    Require(cache.Get("Faster Attacks", "Skill") == null && cache.Get("Faster Attacks") == null &&
                        cache.Get("Barrage", "Support") == null && cache.Get("Barrage", "Skill") != null,
                        "Support artwork matching guessed an alias without metadata or reused active-gem artwork.");
                    Require(cache.Get("Awakened Added Fire Damage", "Support") != null &&
                        cache.Get("Ice Nova of Frostbolts", "Skill") != null && cache.Get("Vaal Fireball", "Skill") != null,
                        "Categorized awakened, transfigured or Vaal artwork was not found recursively.");
                    Require(cache.Get("Missing") == null && cache.Get("Duplicate") == null && cache.Get("Broken") == null &&
                        cache.Get("Barrage Support") == null && cache.Get("Ice Nova of Deep Freeze") == null, "Missing/invalid/ambiguous/unsafe-alias fallback failed.");
                    File.Delete(Path.Combine(poe1, "skill_gems", "Fireball inventory icon.png"));
                    Require(cache.Get("Fireball") == image, "Cached source was locked or lost.");
                    using (var output = new Bitmap(40, 40))
                    using (var graphics = Graphics.FromImage(output))
                    {
                        graphics.Clear(Color.Magenta);
                        GemVisual.Draw(graphics, new Rectangle(0, 0, 40, 40), 3, "Skill");
                        for (int x = 0; x < output.Width; x++)
                            for (int y = 0; y < output.Height; y++)
                                Require(output.GetPixel(x, y).ToArgb() == Color.Magenta.ToArgb(),
                                    "Missing artwork drew a placeholder instead of leaving the row unchanged.");
                        graphics.Clear(Color.Transparent);
                        GemVisual.Draw(graphics, new Rectangle(0, 0, 40, 40), 3, "Skill", image);
                        Require(output.GetPixel(20, 20).ToArgb() == Color.Magenta.ToArgb() &&
                            output.GetPixel(20, 5).ToArgb() != Color.Magenta.ToArgb(), "Local artwork was not drawn with aspect-preserving placement.");
                    }
                }
                using (var cache = new GemIconCache(new AppConfig { Game = GameId.Poe2, GemIconDirectory = poe1 }))
                    Require(cache.Get("Faster Attacks", "Support") == null && cache.Get("Faster Attacks Support", "Support") != null,
                        "PoE1 support suffix handling leaked into PoE2.");
            }
            finally
            {
                string poe1 = Path.Combine(folder, "poe 1 gem");
                foreach (string file in Directory.GetFiles(poe1, "*", SearchOption.AllDirectories)) File.Delete(file);
                foreach (string category in Directory.GetDirectories(poe1)) Directory.Delete(category);
                Directory.Delete(poe1);
                Directory.Delete(Path.Combine(folder, "bin", "poe 1 gem"));
                Directory.Delete(Path.Combine(folder, "bin"));
                Directory.Delete(folder);
            }
        }

        private static void TestChrome(string screenshots)
        {
            using (var form = new Form { Text = "Companion theme verification", Size = new Size(640, 420) })
            using (var menu = new ContextMenuStrip())
            {
                var combo = new ComboBox { Location = new Point(16, 16), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
                for (int i = 0; i < 40; i++) combo.Items.Add("Stage " + i);
                combo.SelectedIndex = 0;
                var text = new TextBox { Location = new Point(16, 60), Size = new Size(590, 280), Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false };
                text.Text = string.Join("\r\n", Enumerable.Range(1, 100).Select(i => "Stat " + i + new string(' ', 100) + "end"));
                form.Controls.AddRange(new Control[] { combo, text });
                UiTheme.Apply(form);
                UiTheme.Apply(form); // repeated theming must not create duplicate hooks/menus
                form.Show();
                Application.DoEvents();
                combo.Focus();
                SendMessage(combo.Handle, 0x100, new IntPtr((int)Keys.Down), IntPtr.Zero);
                Require(combo.SelectedIndex == 1, "Skinned combo lost keyboard selection.");
                combo.DroppedDown = true;
                Application.DoEvents();
                Require(combo.DroppedDown, "Skinned dropdown cannot open.");
                combo.DroppedDown = false;
                text.Focus();
                text.Select(text.TextLength, 0);
                SendMessage(text.Handle, 0xB6, IntPtr.Zero, new IntPtr(100));
                Application.DoEvents();
                Require(text.GetCharIndexFromPosition(new Point(3, 3)) > 0, "Skinned text cannot scroll to the end.");
                SendMessage(text.Handle, 0x115, IntPtr.Zero, IntPtr.Zero); // native line-up
                text.Select(0, 0);
                SendMessage(text.Handle, 0xB6, IntPtr.Zero, new IntPtr(-100));
                Application.DoEvents();
                Require(text.GetCharIndexFromPosition(new Point(3, 3)) < 200, "Skinned text cannot scroll back to the start.");
                text.ReadOnly = true;
                text.ContextMenuStrip.Show(text, new Point(20, 20));
                Application.DoEvents();
                Require(!text.ContextMenuStrip.Items[2].Enabled && !text.ContextMenuStrip.Items[4].Enabled,
                    "Read-only context menu permits edits.");
                text.ContextMenuStrip.Close();
                var nested = new ToolStripMenuItem("Companion game");
                nested.DropDownItems.Add(new ToolStripMenuItem("Path of Exile 1") { Checked = true });
                nested.DropDownItems.Add("Path of Exile 2");
                menu.Items.Add(nested);
                menu.Items.Add(new ToolStripMenuItem("Unavailable") { Enabled = false });
                UiTheme.ApplyMenu(menu);
                Require(menu.Renderer is CompanionMenuRenderer && nested.DropDown.Renderer is CompanionMenuRenderer,
                    "Nested popup menu is not themed.");
                menu.Show(form, new Point(300, 20));
                Application.DoEvents();
                using (var bitmap = new Bitmap(menu.Width, menu.Height))
                {
                    menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
                    if (!string.IsNullOrEmpty(screenshots)) bitmap.Save(Path.Combine(screenshots, "dark-menu.png"));
                }
                menu.Close();
                Capture(form, screenshots, "dark-native-controls");
                form.Hide();
            }
        }

        private static void TestSettings(AppConfig config, string screenshots)
        {
            string originalPath = config.PobPath;
            int confirmations = 0, calls = 0;
            bool approve = false, wasRepair = false, success = false;
            string confirmation = null, result = null, target = null;
            GameId selectedGame = default(GameId);
            var pending = new TaskCompletionSource<bool>();
            using (var form = new SettingsForm(config, c => { throw new InvalidOperationException("Settings unexpectedly saved."); },
                text => { confirmation = text; confirmations++; return approve; },
                (text, ok) => { result = text; success = ok; },
                (repair, game, path) => { calls++; wasRepair = repair; selectedGame = game; target = path; return pending.Task; }))
            {
                form.Show();
                foreach (var size in new[] { form.MinimumSize, new Size(760, 720), new Size(960, 780) })
                {
                    Layout(form, size);
                    CheckRows(form);
                    foreach (var input in new Control[] { Field<TextBox>(form, "_pobPathBox"), Field<TextBox>(form, "_buildDirectoryBox"), Field<TextBox>(form, "_gemIconDirectoryBox"), Field<TextBox>(form, "_buildPathBox"), Field<ComboBox>(form, "_buildDropdown") })
                    {
                        var table = (TableLayoutPanel)input.Parent;
                        Require(table.GetColumn(input) == 1 && table.GetRow(input) > 0 && table.ColumnCount == 3, "Settings input is not in its labelled row.");
                    }
                    Require(Children(form).OfType<TextBox>().All(c => c.BackColor == UiTheme.Input), "Unthemed settings input.");
                }
                Layout(form, new Size(760, 720));
                Capture(form, screenshots, config.Game + "-settings");
                var repairBtn = Field<Button>(form, "_repairBridgeBtn");
                var checkBtn = Field<Button>(form, "_checkBridgeBtn");
                repairBtn.PerformClick();
                Require(confirmations == 1 && calls == 0 && confirmation.Contains(config.GameName)
                    && confirmation.Contains(originalPath), "Repair confirmation failed to identify the target or did not honor Cancel.");
                approve = true;
                repairBtn.PerformClick();
                Require(calls == 1 && wasRepair && selectedGame == config.Game && target == originalPath,
                    "Repair button did not route to the saved selected-game folder.");
                form.Close();
                Require(!form.IsDisposed && !repairBtn.Enabled && !checkBtn.Enabled && !Field<Button>(form, "_saveBtn").Enabled,
                    "Settings allowed closing or another action during repair.");
                pending.SetResult(true);
                Application.DoEvents();
                Require(success && repairBtn.Enabled && checkBtn.Enabled && result.Contains("Authentication has not yet been verified"),
                    "Repair result did not require a separate check or left Settings busy.");
                pending = new TaskCompletionSource<bool>();
                checkBtn.PerformClick();
                Require(calls == 2 && !wasRepair && selectedGame == config.Game && target == originalPath && confirmations == 2,
                    "Check Bridge button did not route to the read-only action.");
                pending.SetResult(true);
                Application.DoEvents();
                Require(success && result.Contains("Authenticated HELLO"), "Check Bridge result missing.");
                Field<TextBox>(form, "_pobPathBox").Text = "edited test copy";
                repairBtn.PerformClick();
                Require(!success && result.Contains("Save & Apply") && confirmations == 2 && calls == 2,
                    "Repair accepted an unsaved directory change.");
                Require(Field<TextBox>(form, "_gemIconDirectoryBox").Text == config.GemIconDirectory, "Gem directory setting was not populated.");
                Field<TextBox>(form, "_gemIconDirectoryBox").Text = "edited icon path";
                Require(config.GemIconDirectory != "edited icon path", "Gem directory edits leaked into active profile.");
                Require(config.PobPath == originalPath, "Settings edits leaked into active profile.");
                var selectorKey = Field<ComboBox>(form, "_selectorKeyDropdown");
                Require((config.OverlaySelectorHotkeyKey == 0 ? selectorKey.SelectedIndex == 0
                        : selectorKey.SelectedItem is Keys && (int)(Keys)selectorKey.SelectedItem == config.OverlaySelectorHotkeyKey)
                    && Field<CheckBox>(form, "_selectorCtrlBox").Checked == ((config.OverlaySelectorHotkeyModifiers & 2) != 0),
                    "Overlay selector shortcut did not populate from the active profile.");
                Require(Field<TextBox>(form, "_tradeLeagueBox").Text == (config.TradeLeague ?? ""), "Trade league did not populate.");
                int selectorBefore = config.OverlaySelectorHotkeyKey;
                selectorKey.SelectedIndex = 0;
                Require(config.OverlaySelectorHotkeyKey == selectorBefore, "Disabling the selector shortcut before saving changed the active profile.");
                Require(form.GetType().GetField("_questKeyDropdown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) == null,
                    "Separate overlay toggle shortcut rows are still present.");
                form.Scale(new SizeF(1.5f, 1.5f));
                Application.DoEvents();
                CheckRows(form);
                Require(form.AutoScaleMode == AutoScaleMode.Dpi && form.AcceptButton != null && form.CancelButton != null, "Dialog scaling/keyboard actions missing.");
                Field<Button>(form, "_cancelBtn").PerformClick();
                Require(config.PobPath == originalPath, "Cancel changed profile.");
            }
        }

        private static void TestDashboard(AppConfig config, string screenshots)
        {
            var openedBuildSites = new List<string>();
            using (var engine = new PobLuaEngine())
            using (var form = new MainWindow(config, engine, null, null, null, null, null, openedBuildSites.Add))
            {
                form.Show();
                foreach (var size in new[] { form.MinimumSize, new Size(1080, 760), new Size(1440, 960) })
                {
                    Layout(form, size);
                    CheckRows(form);
                    AssertBridgeVisible(form);
                    var sample = Field<TextBox>(form, "_sampleItemBox");
                    var results = Field<Panel>(form, "_comparisonContainer");
                    Contained(sample);
                    Contained(results);
                    Require(results.Top >= sample.Bottom + 30, "Comparison input/results overlap.");
                }
                var tabs = Field<TabControl>(form, "_tabs");
                Require(tabs.TabPages.Count == 3 && tabs.TabPages[1].Text == "Current character" && tabs.TabPages[2].Text == "Build", "Current/Guide tab separation changed.");
                tabs.Focus();
                SendMessage(tabs.Handle, 0x100, new IntPtr((int)Keys.Right), IntPtr.Zero);
                Require(tabs.SelectedIndex == 1, "Dark tabs lost native arrow-key navigation.");
                tabs.SelectedIndex = 0;
                Require(Field<TextBox>(form, "_shareCodeBox").MaxLength == 0, "Guide code capacity changed.");
                var findBuilds = Children(form).OfType<Button>().Single(b => b.Name == "FindBuildsButton");
                var guideCode = Field<TextBox>(form, "_shareCodeBox");
                var originalCode = guideCode.Text;
                var originalGuide = config.GetBuildSourceDisplay();
                var currentPath = config.CurrentBuildFilePath;
                guideCode.Text = "unsaved guide text";
                findBuilds.PerformClick();
                var buildSites = findBuilds.ContextMenuStrip;
                Require(buildSites.Visible && buildSites.Items.Count == 3 && openedBuildSites.Count == 0,
                    "Find builds should open a three-site menu, not a browser.");
                foreach (ToolStripMenuItem site in buildSites.Items) site.PerformClick();
                buildSites.Close();
                var expectedSites = config.Game == GameId.Poe1
                    ? new[] { "https://mobalytics.gg/poe/builds", "https://maxroll.gg/poe/build-guides", "https://poe.ninja/poe1/builds" }
                    : new[] { "https://mobalytics.gg/poe-2/builds", "https://maxroll.gg/poe2/build-guides", "https://poe.ninja/poe2/builds" };
                Require(openedBuildSites.SequenceEqual(expectedSites), "Find builds did not open the correct sites for the selected game.");
                Require(guideCode.Text == "unsaved guide text" && config.GetBuildSourceDisplay() == originalGuide
                    && config.CurrentBuildFilePath == currentPath, "Browsing builds changed a build source.");
                guideCode.Text = originalCode;
                var info = new BuildInfo { Success = true, CharacterName = "Fixture character", ClassName = "Witch",
                    AscendClassName = "Infernalist", Level = 72, MainSkill = "Fireball", Baseline = new Dictionary<string, double> {
                        { "Life", 2480 }, { "EnergyShield", 980 }, { "TotalDPS", 125400 }, { "TotalEHP", 18000 }, { "FireResist", 75 }, { "ColdResist", 75 }, { "LightningResist", 75 } } };
                Invoke(form, "UpdateBuildOverview", info);
                var current = Fixture(config, false);
                typeof(MainWindow).GetField("_details", Private).SetValue(form, current);
                typeof(MainWindow).GetField("_detailsStale", Private).SetValue(form, false);
                Invoke(form, "RenderBuildTab");
                Invoke(form, "UpdateAssumptionLabel");
                Layout(form, new Size(1080, 820));
                var name = Field<Label>(form, "_charNameVal");
                Require(name.Width >= TextRenderer.MeasureText(name.Text, name.Font).Width, "Ordinary character name truncated at default dashboard width.");
                Require(!((Panel)name.Parent.Parent).HorizontalScroll.Visible, "Overview needs unnecessary horizontal scrolling.");
                Require(Field<Label>(form, "_combinedDpsVal").ForeColor == UiTheme.Muted, "Unavailable stat looks like a loss.");
                Require(Field<Panel>(form, "_comparisonContainer").Controls.OfType<Label>().Any(l => l.Text.Contains("does not change")), "Empty comparison lost its explanation.");
                Capture(form, screenshots, config.Game + "-dashboard");
                tabs.SelectedIndex = 1;
                Application.DoEvents();
                Capture(form, screenshots, config.Game + "-current");
                tabs.SelectedIndex = 2;
                Application.DoEvents();
                var guideView = Field<GuideBuildView>(form, "_guideBuildView");
                typeof(GuideBuildView).GetField("_currentDetails", Private).SetValue(guideView, current);
                guideView.Render(Fixture(config, true));
                CheckRows(form);
                Capture(form, screenshots, config.Game + "-guide");
                Require(Field<RichTextBox>(guideView, "_summary").Text.Contains("GUIDE BUILD") && Field<ListBox>(guideView, "_gear").Items.Count == 1,
                    "Themed guide lost content.");
                var guideTabs = Children(guideView).OfType<TabControl>().First();
                guideTabs.SelectedIndex = 2;
                Application.DoEvents();
                var skills = Field<SkillRows>(guideView, "_skills");
                Require(skills.Items.Cast<SkillRows.Row>().Count(r => r.Gem != null) == 2 &&
                    skills.Items.Cast<SkillRows.Row>().Any(r => r.Text.Contains("Support") && r.Gem != null && r.Gem.Color == 2),
                    "Skill rows lost active/support gems or their color metadata.");
                Require(skills.Items.Cast<SkillRows.Row>().Any(r => r.Text.Contains("Level 15, Quality 0%")) &&
                    skills.AccessibleName == "Guide skills", "Skill details or accessibility label lost.");
                Capture(form, screenshots, config.Game + "-skills");
                guideTabs.SelectedIndex = 4;
                Application.DoEvents();
                Capture(form, screenshots, config.Game + "-guide-tree");
                guideTabs.SelectedIndex = 5;
                Application.DoEvents();
                var gems = Field<ListView>(guideView, "_gemList");
                Require(gems.Items.Count == 0, "Default needed list retained equipped fixture gems.");
                Field<CheckBox>(guideView, "_showEquipped").Checked = true;
                Require(gems.Items.Count == 2 && gems.OwnerDraw && gems.ShowItemToolTips, "Themed gem checklist lost rows/tooltips.");
                Capture(form, screenshots, config.Game + "-checklist");
                Require(Children(form).OfType<TabControl>().All(t => t.DrawMode == TabDrawMode.OwnerDrawFixed), "Unthemed nested tabs.");
                Invoke(form, "SetBusy", true, "Fixture operation");
                Require(!Field<Button>(form, "_loadShareBtn").Enabled && !Field<Button>(form, "_reloadBuildBtn").Enabled, "Busy state lost.");
                Require(!Field<Button>(form, "_pobImportBtn").Enabled && !Field<ComboBox>(form, "_buildDropdown").Enabled
                    && !Field<Button>(form, "_settingsBtn").Enabled, "PoB import allows profile/source changes while busy.");
                Invoke(form, "SetBusy", false, null);
                Require(Field<Button>(form, "_pobImportBtn").Enabled, "PoB import button did not recover after operation.");
                form.Scale(new SizeF(1.5f, 1.5f));
                Application.DoEvents();
                CheckRows(form);
                AssertBridgeVisible(form);
                form.Hide();
            }
        }

        internal static void AssertBridgeVisible(MainWindow form)
        {
            var button = Children(form).OfType<Button>().Single(c => c.Name == "PoB import bridge button");
            Require(form.Text.Contains("PoB import bridge"), "Bridge window cannot be distinguished from another companion instance.");
            Require(button.Visible && button.Width > 0 && button.Height > 0, "PoB import button is hidden or collapsed.");
            var bounds = button.RectangleToScreen(button.ClientRectangle);
            for (Control parent = button.Parent; parent != null; parent = parent.Parent)
            {
                Require(parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds),
                    "PoB import button is clipped by " + parent.Name + " at " + form.Size);
            }
            var center = new Point(button.Left + button.Width / 2, button.Top + button.Height / 2);
            Require(button.Parent.GetChildAtPoint(center) == button, "Another control covers the PoB import button.");
        }

        private static BuildDetails Fixture(AppConfig config, bool guide)
        {
            var details = new BuildDetails { Game = config.Game == GameId.Poe1 ? "poe1" : "poe2", Success = true,
                BuildName = guide ? "Fixture guide" : "Fixture character", TreeVersion = "fixture", ActiveLoadout = "Fixture stage",
                ClassName = "Witch", Level = guide ? 72 : 40, MainSkill = "Fireball" };
            details.Loadouts.Add("Fixture stage");
            details.Assumptions.Add(new LabelValue { Label = "Main skill", Value = "Fireball" });
            details.Stats.Add(new StatValue { Key = "Life", Label = "Life", Value = 2480 });
            details.Gear.Add(new GearEntry { SlotName = "Boots", SlotLabel = "Boots", ItemName = "Fixture Boots", Rarity = "Rare", Raw = "Rarity: Rare\r\nFixture Boots\r\nDragonscale Boots\r\n+150 to maximum Life" });
            details.Skills.Add(new SkillGroup { Label = "Fireball", Main = true, Enabled = true, Slot = "Boots", Gems = new List<SkillGem> {
                new SkillGem { Name = "Fireball", Kind = "Skill", Color = 3, Tier = 1, Level = guide ? 15 : 5, Quality = 0, Enabled = true },
                new SkillGem { Name = "Fixture Support", Kind = "Support", Color = 2, Tier = 1, Level = 1, Quality = 0, Enabled = true } } });
            details.Tree.Nodes.Add(new TreeNode { Id = 1, Name = "Witch", Type = "ClassStart", Alloc = true, X = 0, Y = 0, Stats = new List<string>() });
            details.Tree.Nodes.Add(new TreeNode { Id = 2, Name = "Small passive", Type = "Normal", Alloc = guide, X = 100, Y = 50, Stats = new List<string> { "+10 to Intelligence" } });
            details.Tree.Nodes.Add(new TreeNode { Id = 3, Name = "Fixture notable", Type = "Notable", Alloc = guide, X = 200, Y = 100, Stats = new List<string> { "10% increased maximum Life" } });
            details.Tree.Edges.Add(new List<int> { 1, 2 });
            details.Tree.Edges.Add(new List<int> { 2, 3 });
            return details;
        }

        private static void TestOverlays(AppConfig config, string screenshots)
        {
            var guide = Fixture(config, true);
            var current = Fixture(config, false);
            using (var gems = new GemOverlay(config))
            using (var passives = new PassiveOverlay(config))
            {
                foreach (OverlayForm overlay in new OverlayForm[] { gems, passives })
                {
                    Invoke(overlay, "LoadData", guide, current, null, null);
                    typeof(OverlayForm).GetMethod("Render", Private).Invoke(overlay, null);
                    overlay.Show();
                    Application.DoEvents();
                    Require((overlay.CreateParamsForTest() & 0x08000000) != 0, "Overlay lost non-activating behavior.");
                    Require(overlay.TopMost && !overlay.ShowInTaskbar, "Overlay window behavior changed.");
                    var title = (Label)typeof(OverlayForm).GetField("_title", Private).GetValue(overlay);
                    Contained(title);
                    foreach (Control action in title.Parent.Controls) Contained(action);
                    var body = (Panel)typeof(OverlayForm).GetField("_body", Private).GetValue(overlay);
                    Require(body.AutoScroll && body.AutoScrollMinSize.Height > 0, "Overlay lost scrollable rows.");
                    if (overlay is GemOverlay)
                    {
                        Require(!overlay.BodyText.Contains("Fireball") && !overlay.BodyText.Contains("Fixture Support"),
                            "Default gem overlay retained equipped fixture gems.");
                        config.GemOverlayShowAll = true;
                        typeof(OverlayForm).GetMethod("Render", Private).Invoke(overlay, null);
                        Require(overlay.BodyText.Contains("Fireball") && overlay.BodyText.Contains("Fixture Support") &&
                            GemCutting.PlanGroups(guide, current)[0].Entries.Any(e => e.Kind == "Support" && e.Color == 2),
                            "Opt-in gem overlay lost rows or color metadata.");
                        config.GemOverlayShowAll = false;
                    }
                    Capture(overlay, screenshots, config.Game + (overlay is GemOverlay ? "-gems" : "-passives"));
                    overlay.Hide();
                }
            }
            using (var popup = new ComparisonPopup())
            {
                var result = new ComparisonResult { Success = true, ItemName = "Fixture Boots", ItemRarity = "Rare", ItemBase = "Dragonscale Boots" };
                result.Comparisons.Add(new SlotComparison { SlotName = "Boots", SlotLabel = "Boots", ReplacedItemName = "Current boots", ReplacedItemRarity = "Rare",
                    Lines = new List<string> { "^x33FF77+150 Life", "^xDD0022-10% Fire resistance" } });
                var popupConfig = config.Clone();
                popupConfig.PinPopup = true;
                popup.DisplayResult(result, popupConfig, new Point(100, 100));
                Application.DoEvents();
                foreach (var button in Children(popup).OfType<Button>()) Contained(button);
                Capture(popup, screenshots, config.Game + "-comparison");
                popup.Hide();
            }
        }

        private static int CreateParamsForTest(this Form form)
        {
            return ((CreateParams)typeof(Form).GetProperty("CreateParams", Private).GetValue(form, null)).ExStyle;
        }
    }
}
