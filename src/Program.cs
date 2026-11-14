using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace Poe2ItemCompanion
{
    public static class Program
    {
        private static GameId? _requestedGame;

        [STAThread]
        public static int Main(string[] args)
        {
            UiTheme.EnableDpiAwareness();
            // Set up global exception handling to capture any unexpected failures
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Logger.Error("Unhandled AppDomain exception: " + (ex != null ? ex.Message : "Unknown"), ex);
            };

            Application.ThreadException += (s, e) =>
            {
                Logger.Error("Unhandled Application thread exception: " + e.Exception.Message, e.Exception);
            };

            Logger.Info("Path of Exile Companion starting.");
            var filtered = new System.Collections.Generic.List<string>();
            for (int i = 0; i < (args ?? new string[0]).Length; i++)
            {
                if (args[i].Equals("--game", StringComparison.OrdinalIgnoreCase))
                {
                    if (++i >= args.Length || (args[i] != "poe1" && args[i] != "poe2"))
                    { Console.WriteLine("Use --game poe1 or --game poe2."); return 1; }
                    _requestedGame = args[i] == "poe1" ? GameId.Poe1 : GameId.Poe2;
                }
                else filtered.Add(args[i]);
            }
            args = filtered.ToArray();

            if (args != null && args.Length > 0)
            {
                string firstArg = args[0].ToLowerInvariant();
                if (firstArg == "--probe-pob-bridge")
                {
                    if (!_requestedGame.HasValue)
                    { Console.WriteLine("Specify --game poe1 or --game poe2 for the read-only probe."); return 1; }
                    try
                    {
                        PobImportBridge.ProbeAsync(_requestedGame.Value, args.Length > 1
                            ? Path.GetFullPath(args[1]) : GameIdentity.DefaultPobPath(_requestedGame.Value)).GetAwaiter().GetResult();
                        Console.WriteLine("PASSED: authenticated PoB bridge handshake. No import command sent; no build or configuration changed.");
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("FAILED READ-ONLY PROBE: " + ex.Message);
                        return 1;
                    }
                }
                if (firstArg == "--test-ui") return UiTests.Run(args.Length > 1 ? Path.GetFullPath(args[1]) : null);
                if (firstArg == "--test-pob-bridge") return PobBridgeTests.Run(
                    args.Length > 1 ? Path.GetFullPath(args[1]) : null, args.Length > 2 ? Path.GetFullPath(args[2]) : null);
                if (firstArg == "--test-dual-game") return DualGameTests.Run();
                if (firstArg == "--test-gem-acquisition") return DualGameTests.RunAcquisition();
                if (firstArg == "--test-sources")
                {
                    return RunSourceTests();
                }
                if (firstArg == "--test" || firstArg == "-t")
                {
                    return RunTests();
                }
                else if (firstArg == "--compare" || firstArg == "-c")
                {
                    return RunCliComparison(args);
                }
                else if (firstArg == "--export" || firstArg == "-e")
                {
                    return RunCliExport(args);
                }
                else if (firstArg == "--log" || firstArg == "-l")
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new LogViewerForm());
                    return 0;
                }
                else if (firstArg == "--help" || firstArg == "-h" || firstArg == "/?")
                {
                    PrintHelp();
                    return 0;
                }
            }

            // Normal GUI mode: runs in Windows System Tray
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppConfig config;
            try { config = LoadConfig(); }
            catch (Exception ex)
            {
                MessageBox.Show("The companion could not load its configuration:\n" + ex.Message + "\n\nThe original configuration was preserved. Correct or restore config.json before restarting.", "Configuration error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            var context = new TrayApplicationContext(config);
            Application.Run(context);
            Logger.Info("Path of Exile Companion shutting down cleanly.");
            return 0;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Path of Exile 1 / 2 Companion");
            Console.WriteLine("============================================");
            Console.WriteLine("Usage:");
            Console.WriteLine("  Poe2ItemCompanion.exe              Launch system tray companion app");
            Console.WriteLine("  Poe2ItemCompanion.exe --test       Run automated verification and calculation tests");
            Console.WriteLine("  Poe2ItemCompanion.exe --test-sources");
            Console.WriteLine("                                     Verify read-only Current/Guide sources, tabs and stages");
            Console.WriteLine("  Poe2ItemCompanion.exe --compare    Compare item from clipboard or file");
            Console.WriteLine("  Poe2ItemCompanion.exe --export <itemFile> <slot> [outFile]");
            Console.WriteLine("                                     Write a NEW matching PoB build file with the item in <slot>");
            Console.WriteLine("  Poe2ItemCompanion.exe --game poe1|poe2 [command]");
            Console.WriteLine("                                     Select the game's saved profile (also available in dashboard/tray)");
            Console.WriteLine("  Poe2ItemCompanion.exe --test-dual-game");
            Console.WriteLine("                                     Verify both installed engines and isolated dual-game fixtures");
            Console.WriteLine("  Poe2ItemCompanion.exe --test-gem-acquisition");
            Console.WriteLine("  Poe2ItemCompanion.exe --test-ui [screenshot-directory]");
            Console.WriteLine("                                     Verify isolated UI themes/layouts without loading user builds");
            Console.WriteLine("                                     Verify PoE1 gem purchase guidance and required levels");
            Console.WriteLine("  Poe2ItemCompanion.exe --test-pob-bridge [poe1-current.xml] [poe2-current.xml]");
            Console.WriteLine("                                     Verify local bridge and native imports with throwaway builds");
            Console.WriteLine("  Poe2ItemCompanion.exe --probe-pob-bridge --game poe1|poe2 [installation-path]");
            Console.WriteLine("                                     Read-only authenticated handshake; never sends IMPORT");
            Console.WriteLine("  Poe2ItemCompanion.exe --log        Open the companion diagnostic log file");
            Console.WriteLine("  Poe2ItemCompanion.exe --help       Show this help screen");
            Console.WriteLine();
            Console.WriteLine("Features:");
            Console.WriteLine("  - Global hotkey (default Ctrl+D) captures hovered item in the selected game via Ctrl+C");
            Console.WriteLine("  - Evaluates item against a separate Current character build");
            Console.WriteLine("  - Shows guide-relative item fit without replacing the Current character baseline");
            Console.WriteLine("  - Reuses the matching installed Path of Building calculation engine");
            Console.WriteLine("  - Displays compact red/green stat diff popup near mouse cursor");
            Console.WriteLine("  - Never equips in-game; explicit PoB bridge saves only matching Current, export creates a NEW file");
        }

        private static AppConfig LoadConfig()
        {
            var config = AppConfig.Load();
            return _requestedGame.HasValue && config.Game != _requestedGame.Value ? config.SelectGame(_requestedGame.Value) : config;
        }

        private static int RunTests()
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("Running PoE 2 Item Companion Verification Tests");
            Console.WriteLine("=================================================");

            var config = LoadConfig();
            if (config.Game == GameId.Poe1) return DualGameTests.Run();
            string pobDir = config.PobPath;
            string buildPath = Environment.GetEnvironmentVariable("POE2COMPANION_TEST_BUILD");
            if (string.IsNullOrEmpty(buildPath)) buildPath = config.BuildFilePath;

            Console.WriteLine("[1/8] Verifying PoB 2 installation directory...");
            if (!Directory.Exists(pobDir))
            {
                Console.WriteLine("FAILED: PoB 2 directory does not exist: " + pobDir);
                return 1;
            }
            Console.WriteLine("  PASSED: Found PoB 2 at " + pobDir);

            Console.WriteLine("[2/8] Verifying saved build XML file...");
            if (!File.Exists(buildPath))
            {
                Console.WriteLine("FAILED: Build file does not exist: " + buildPath);
                return 1;
            }
            Console.WriteLine("  PASSED: Found build file at " + buildPath);

            Console.WriteLine("[3/8] Initializing LuaJIT Engine & PoB modules...");
            string scriptPath = AppConfig.GetWrapperScriptPath();
            using (var engine = new PobLuaEngine())
            {
                string initErr;
                if (!engine.Initialize(pobDir, scriptPath, out initErr))
                {
                    Console.WriteLine("FAILED: Engine initialization failed: " + initErr);
                    return 1;
                }
                Console.WriteLine("  PASSED: PoB 2 LuaJIT engine initialized successfully.");

                string buildErr;
                var buildInfo = engine.LoadBuildFile(buildPath, out buildErr);
                if (buildInfo == null || !buildInfo.Success)
                {
                    Console.WriteLine("FAILED: Loading build failed: " + buildErr);
                    return 1;
                }
                Console.WriteLine("  PASSED: Build loaded successfully.");
                Console.WriteLine("    Character Level: " + buildInfo.Level);
                Console.WriteLine("    Main Skill: " + buildInfo.MainSkill);
                if (buildInfo.Baseline != null && buildInfo.Baseline.ContainsKey("TotalDPS"))
                {
                    Console.WriteLine("    Baseline Total DPS: " + buildInfo.Baseline["TotalDPS"].ToString("F1"));
                    Console.WriteLine("    Baseline Life: " + buildInfo.Baseline["Life"].ToString("F0"));
                    Console.WriteLine("    Baseline Energy Shield: " + buildInfo.Baseline["EnergyShield"].ToString("F0"));
                    Console.WriteLine("    Baseline EHP: " + buildInfo.Baseline["TotalEHP"].ToString("F0"));
                }

                Console.WriteLine("[4/8] Testing Item Comparison Preview (Boots)...");
                string sampleItem = @"Item Class: Boots
Rarity: Rare
Storm Tread
Dragonscale Boots
--------
Evasion Rating: 420
Armour: 250
--------
Requirements:
Level: 75
Dex: 62
Str: 62
--------
Item Level: 82
--------
+150 to maximum Life
+45% to Fire Resistance
+45% to Lightning Resistance
35% increased Movement Speed
";
                string cmpErr;
                var cmp = engine.CompareItem(sampleItem, out cmpErr);
                if (cmp == null || !cmp.Success)
                {
                    Console.WriteLine("FAILED: Item comparison failed: " + cmpErr);
                    return 1;
                }

                Console.WriteLine("  PASSED: Item comparison completed successfully.");
                Console.WriteLine("    Item Name: " + cmp.ItemName);
                Console.WriteLine("    Item Rarity: " + cmp.ItemRarity);
                Console.WriteLine("    Item Base: " + cmp.ItemBase);
                Console.WriteLine("    Slots Compared: " + cmp.Comparisons.Count);

                foreach (var slot in cmp.Comparisons)
                {
                    Console.WriteLine("    -> Slot: " + slot.SlotLabel + " (Replaces: " + slot.ReplacedItemName + ")");
                    Console.WriteLine("       Stat differences count: " + slot.Lines.Count);
                    for (int i = 0; i < Math.Min(6, slot.Lines.Count); i++)
                    {
                        Console.WriteLine("         " + PobStatParser.StripEscapes(slot.Lines[i]));
                    }
                    if (slot.Lines.Count > 6)
                    {
                        Console.WriteLine("         ... (" + (slot.Lines.Count - 6) + " more stats)");
                    }
                }
                // Step 5: share code round-trip (same format as PoB2 Export > Share code)
                Console.WriteLine("[5/8] Testing PoB2 share code decode + comparison...");
                string xml = File.ReadAllText(buildPath);
                string code = PobShareCode.Encode(xml);
                string e5;
                string tXml, tSrc, tErr;
                if (PobShareCode.TryResolve(code.Substring(0, code.Length * 2 / 3), out tXml, out tSrc, out tErr)) { Console.WriteLine("FAILED: truncated share code accepted"); return 1; }
                Console.WriteLine("  PASSED: truncated share code rejected: " + tErr);
                var broken = engine.LoadBuildXml("<PathOfBuilding2><Build level=\"1\"></PathOfBuilding2>", "broken", out e5);
                if ((broken != null && broken.Success) || e5 == null || e5.Contains("ShowErrMsg")) { Console.WriteLine("FAILED: malformed XML error path: " + e5); return 1; }
                Console.WriteLine("  PASSED: malformed build XML reports PoB's error: " + e5);
                string extraCodeFile = Environment.GetEnvironmentVariable("POE2COMPANION_TEST_SHARECODE");
                if (!string.IsNullOrEmpty(extraCodeFile) && File.Exists(extraCodeFile))
                {
                    var ubi = engine.LoadShareCode(File.ReadAllText(extraCodeFile), out e5);
                    if (ubi == null || !ubi.Success) { Console.WriteLine("FAILED: user share code: " + e5); return 1; }
                    var ucmp = engine.CompareItem(sampleItem, out e5);
                    if (ucmp == null || !ucmp.Success) { Console.WriteLine("FAILED: user share code comparison: " + e5); return 1; }
                    Console.WriteLine("  PASSED: user share code loaded: L" + ubi.Level + " " + ubi.MainSkill + "; boots compare -> " + ucmp.Comparisons.Count + " slot(s), "
                        + (ucmp.Comparisons.Count > 0 ? ucmp.Comparisons[0].Lines.Count + " lines" : "0 lines"));
                    var udet = engine.GetBuildDetails(out e5);
                    if (udet == null || !udet.Success) { Console.WriteLine("FAILED: user share code details: " + e5); return 1; }
                    Console.WriteLine("    " + udet.ClassName + "/" + udet.AscendClassName + ", gear " + udet.Gear.Count + ", tree nodes " + udet.Tree.Nodes.Count);
                }
                var sbi = engine.LoadShareCode(code, out e5);
                if (sbi == null || !sbi.Success) { Console.WriteLine("FAILED: share code load: " + e5); return 1; }
                var cmp2 = engine.CompareItem(sampleItem, out e5);
                if (cmp2 == null || !cmp2.Success || cmp2.Comparisons.Count != cmp.Comparisons.Count) { Console.WriteLine("FAILED: share code comparison: " + e5); return 1; }
                for (int i = 0; i < cmp.Comparisons.Count; i++)
                {
                    if (string.Join("|", cmp.Comparisons[i].Lines.ToArray()) != string.Join("|", cmp2.Comparisons[i].Lines.ToArray()))
                    {
                        Console.WriteLine("FAILED: share code build gives different deltas than the file build in " + cmp.Comparisons[i].SlotLabel);
                        return 1;
                    }
                }
                string badXml, badSrc, badErr;
                if (PobShareCode.TryResolve("not a share code!!", out badXml, out badSrc, out badErr)) { Console.WriteLine("FAILED: garbage accepted as share code"); return 1; }
                Console.WriteLine("  PASSED: share code (" + code.Length + " chars) decoded; deltas identical to file build. Bad input rejected: " + badErr);

                Console.WriteLine("[6/8] Testing Build tab export (gear, tree, assumptions)...");
                var det = engine.GetBuildDetails(out e5);
                if (det == null || !det.Success) { Console.WriteLine("FAILED: build details: " + e5); return 1; }
                int alloc = 0, asc = 0;
                foreach (var n in det.Tree.Nodes) { if (n.Alloc) { alloc++; if (n.Ascendancy) asc++; } }
                Console.WriteLine("    Build: " + det.BuildName + " | L" + det.Level + " " + det.ClassName + "/" + det.AscendClassName + " | " + det.MainSkill);
                Console.WriteLine("    Tree nodes: " + det.Tree.Nodes.Count + " (allocated " + alloc + ", ascendancy " + asc + "), edges: " + det.Tree.Edges.Count);
                Console.WriteLine("    Gear items: " + det.Gear.Count + ", stats: " + det.Stats.Count + ", non-default config: " + det.Config.Count);
                foreach (var a in det.Assumptions) Console.WriteLine("    Assumption: " + a.Label + " = " + a.Value);
                foreach (var c in det.Config) Console.WriteLine("    Config: [" + c.Section + "] " + c.Label + " = " + c.Value);
                foreach (var g in det.Gear) Console.WriteLine("    Gear: " + g.SlotLabel + " = " + g.ItemName + (g.InactiveWeaponSet ? " (inactive set)" : ""));
                if (det.Tree.Nodes.Count < 100 || alloc == 0 || det.Gear.Count == 0 || det.Assumptions.Count == 0) { Console.WriteLine("FAILED: build details incomplete"); return 1; }
                if (File.ReadAllText(buildPath) != xml) { Console.WriteLine("FAILED: build file was modified!"); return 1; }
                Console.WriteLine("  PASSED: details exported; build file untouched.");

                Console.WriteLine("[7/8] Testing export of NEW PoB2 build with candidate + character-import detection...");
                var rbi = engine.LoadBuildFile(buildPath, out e5);
                if (rbi == null || !rbi.Success) { Console.WriteLine("FAILED: reload source: " + e5); return 1; }
                var before = engine.CompareItem(sampleItem, out e5);
                var exp = engine.ExportWithCandidate(sampleItem, "Boots", out e5);
                if (exp == null || !exp.Success || !exp.Validated || string.IsNullOrEmpty(exp.Xml)) { Console.WriteLine("FAILED: export: " + e5); return 1; }
                if (!exp.Xml.Contains("Storm Tread")) { Console.WriteLine("FAILED: exported XML lacks candidate item"); return 1; }
                var after = engine.CompareItem(sampleItem, out e5);
                if (before == null || after == null || before.Comparisons.Count != after.Comparisons.Count) { Console.WriteLine("FAILED: compare after export: " + e5); return 1; }
                for (int i = 0; i < before.Comparisons.Count; i++)
                    if (string.Join("|", before.Comparisons[i].Lines.ToArray()) != string.Join("|", after.Comparisons[i].Lines.ToArray())) { Console.WriteLine("FAILED: loaded source build changed by export (" + before.Comparisons[i].SlotLabel + ")"); return 1; }
                Console.WriteLine("  PASSED: exported '" + exp.ItemName + "' into " + exp.SlotLabel + " replacing '" + exp.ReplacedItemName + "'; reload-validated L" + exp.ValidatedLevel + ", Life " + exp.ValidatedLife.ToString("F0") + ", DPS " + exp.ValidatedTotalDPS.ToString("F1"));
                Console.WriteLine("  PASSED: loaded source build unchanged after export (identical deltas).");

                string tmpDir = Path.Combine(Path.GetTempPath(), "poe2companion-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmpDir);
                try
                {
                    if (PobLuaEngine.WriteNewBuildFile(exp.Xml, buildPath, buildPath, out e5) != null) { Console.WriteLine("FAILED: writing over source build was allowed"); return 1; }
                    string target = Path.Combine(tmpDir, "export.xml");
                    File.WriteAllText(target, "existing");
                    string written = PobLuaEngine.WriteNewBuildFile(exp.Xml, target, buildPath, out e5);
                    if (written == null || written == target || File.ReadAllText(target) != "existing") { Console.WriteLine("FAILED: existing file not protected: " + e5); return 1; }
                    var vbi = engine.LoadBuildXml(File.ReadAllText(written), "export-check", out e5);
                    if (vbi == null || !vbi.Success) { Console.WriteLine("FAILED: written export does not load: " + e5); return 1; }
                    var vdet = engine.GetBuildDetails(out e5);
                    bool hasBoots = false;
                    if (vdet != null) foreach (var g in vdet.Gear) if (g.SlotLabel == exp.SlotLabel && g.ItemName != null && g.ItemName.Contains("Storm Tread")) hasBoots = true;
                    if (!hasBoots) { Console.WriteLine("FAILED: candidate not equipped in written export"); return 1; }
                    Console.WriteLine("  PASSED: wrote new file " + Path.GetFileName(written) + " (refused source path, kept existing file); it loads with the candidate equipped.");
                }
                finally { try { Directory.Delete(tmpDir, true); } catch { } }

                if (!AppConfig.XmlHasImportedCharacter("<PathOfBuilding2><Import lastCharacterHash=\"abc\" lastRealm=\"PoE2\"/></PathOfBuilding2>")
                    || AppConfig.XmlHasImportedCharacter("<PathOfBuilding2><Import importLink=\"x\"/></PathOfBuilding2>"))
                { Console.WriteLine("FAILED: character-import detection"); return 1; }
                bool srcIsChar = AppConfig.XmlHasImportedCharacter(xml);
                if (srcIsChar != exp.Xml.Contains("lastCharacterHash=")) { Console.WriteLine("FAILED: export did not preserve the character-import marker"); return 1; }
                string latestChar = AppConfig.FindLatestImportedCharacterBuild(pobDir);
                Console.WriteLine("  PASSED: import detection works; source is " + (srcIsChar ? "an imported character" : "not an imported character") + "; newest imported character build: " + (latestChar ?? "(none)"));
                if (File.ReadAllText(buildPath) != xml) { Console.WriteLine("FAILED: build file was modified!"); return 1; }
                Console.WriteLine("  PASSED: source build file untouched after export.");

                Console.WriteLine("[8/8] Testing independent GUIDE goal vs Current character baseline...");
                string savedConfig = File.ReadAllText(AppConfig.GetConfigFilePath());
                var dual = config.Clone();
                dual.BuildSource = AppConfig.SourceShareCode;
                dual.ShareCode = PobShareCode.Encode(exp.Xml);
                dual.ShareCodeResolved = dual.ShareCode;
                dual.CurrentBuildFilePath = buildPath;
                dual.CurrentCharacterCode = null;
                string guideCode = dual.ShareCode;
                var dualResult = GuideComparison.Compare(dual, engine, sampleItem, out e5);
                if (dualResult == null || !dualResult.Success || dualResult.Comparisons.Count != before.Comparisons.Count)
                { Console.WriteLine("FAILED: dual-source comparison: " + e5); return 1; }
                for (int i = 0; i < before.Comparisons.Count; i++)
                {
                    var slot = dualResult.Comparisons[i];
                    if (slot.ItemStats == null || slot.ItemStats.Count != 4 ||
                        slot.ItemStats.Exists(r => r.Rating != "Green") || slot.GuideItemName != exp.ItemName)
                    { Console.WriteLine("FAILED: parsed same-item affix ratings: " + new JavaScriptSerializer().Serialize(slot.ItemStats)); return 1; }
                    if (slot.ReplacedItemName != before.Comparisons[i].ReplacedItemName ||
                        string.Join("|", slot.Lines.ToArray()) != string.Join("|", before.Comparisons[i].Lines.ToArray()))
                    { Console.WriteLine("FAILED: guide gear leaked into current-gear deltas"); return 1; }
                    if (slot.Before == null || slot.After == null || slot.Before.Count < 4 ||
                        !slot.GuideFit.StartsWith("Guide slot goal") || !slot.GuideFit.Contains(": " + exp.ItemName + "\n") || !slot.GuideFit.Contains("not BIS"))
                    { Console.WriteLine("FAILED: missing guide-relative fit/goal: " + slot.GuideFit); return 1; }
                    Console.WriteLine("    Current replacement: " + slot.ReplacedItemName + "; " + slot.GuideFit.Replace("\n", " | "));
                }
                if (dual.ShareCode != guideCode || dual.CurrentBuildFilePath != buildPath)
                { Console.WriteLine("FAILED: guide/current source was changed"); return 1; }
                if (!TestGuideBuildTab(dual, engine, sampleItem)) return 1;
                if (!TestGuideStages(config, buildPath, engine, sampleItem, before)) return 1;
                if (!TestEquipIntoSource(config, buildPath, engine, sampleItem, before)) return 1;
                if (!TestGameLogStages()) return 1;
                if (!TestGemCutting(config, buildPath, engine)) return 1;
                if (!TestPassivePlan(config, buildPath)) return 1;
                if (!TestGggBuildGuide(config, buildPath, engine, sampleItem, before)) return 1;
                var currentExport = engine.ExportWithCandidate(sampleItem, "Boots", out e5);
                if (currentExport == null || !currentExport.Success || currentExport.ReplacedItemName != exp.ReplacedItemName)
                { Console.WriteLine("FAILED: export used guide instead of Current character build: " + e5); return 1; }
                dual.CurrentBuildFilePath = null;
                if (GuideComparison.Compare(dual, engine, sampleItem, out e5) != null || !e5.Contains("CURRENT"))
                { Console.WriteLine("FAILED: missing Current character baseline silently used loaded build/guide"); return 1; }
                dual.CurrentCharacterCode = code;
                var codeCurrent = dual.LoadConfiguredBuild(engine, out e5);
                if (codeCurrent == null || !codeCurrent.Success || dual.ShareCode != guideCode)
                { Console.WriteLine("FAILED: current-code import did not preserve guide"); return 1; }
                var codeDeltas = engine.CompareItem(sampleItem, out e5);
                if (codeDeltas == null || !codeDeltas.Success ||
                    string.Join("|", codeDeltas.Comparisons[0].Lines.ToArray()) != string.Join("|", before.Comparisons[0].Lines.ToArray()))
                { Console.WriteLine("FAILED: current-code deltas differ from current file"); return 1; }
                dual.CurrentCharacterCode = null;
                dual.CurrentBuildFilePath = buildPath;
                dual.ShareCode = dual.ShareCodeResolved = "invalid";
                var noGuide = GuideComparison.Compare(dual, engine, sampleItem, out e5);
                if (noGuide == null || !noGuide.Success || !noGuide.Comparisons[0].GuideFit.Contains("unavailable"))
                { Console.WriteLine("FAILED: invalid guide corrupted Current character comparison"); return 1; }
                var targets = new System.Collections.Generic.Dictionary<string, double> { { "Life", 200 }, { "FireResist", 75 } };
                var wornStats = new System.Collections.Generic.Dictionary<string, double> { { "Life", 100 }, { "FireResist", 50 } };
                var partial = GuideComparison.Assess(targets, wornStats,
                    new System.Collections.Generic.Dictionary<string, double> { { "Life", 150 }, { "FireResist", 50 } });
                var regression = GuideComparison.Assess(targets, wornStats,
                    new System.Collections.Generic.Dictionary<string, double> { { "Life", 50 }, { "FireResist", 50 } });
                var overshoot = GuideComparison.Assess(targets, wornStats,
                    new System.Collections.Generic.Dictionary<string, double> { { "Life", 1000 }, { "FireResist", 1000 } });
                var met = GuideComparison.Assess(targets,
                    new System.Collections.Generic.Dictionary<string, double> { { "Life", 250 }, { "FireResist", 100 } },
                    new System.Collections.Generic.Dictionary<string, double> { { "Life", 150 }, { "FireResist", 100 } });
                if (!partial.Contains("+25%") || !regression.Contains("-25%") ||
                    !overshoot.Contains("+100%") || !met.Contains("falls below guide"))
                { Console.WriteLine("FAILED: partial/regression/capping/met-target assessment"); return 1; }
                if (File.ReadAllText(buildPath) != xml || File.ReadAllText(AppConfig.GetConfigFilePath()) != savedConfig)
                { Console.WriteLine("FAILED: dual-source test changed source or persisted config"); return 1; }
                Console.WriteLine("  PASSED: current-file/code, guide preservation, Current character deltas/export, missing baseline, invalid guide, partial fit, regression and capped credit.");
                if (!TestItemStatRatings()) return 1;
            }

            Console.WriteLine("=================================================");
            Console.WriteLine("ALL TESTS PASSED SUCCESSFULLY! (Verified End-to-End)");
            Console.WriteLine("=================================================");
            return 0;
        }

        private static ItemAffix TestAffix(string text)
        {
            return new ItemAffix { Text = text, Understood = true, Scalable = true };
        }

        private static int RunSourceTests()
        {
            var config = LoadConfig().Clone();
            if (config.Game == GameId.Poe1) return DualGameTests.Run();
            config.DisableSave = true;
            config.FollowGameStage = false;
            string path = Environment.GetEnvironmentVariable("POE2COMPANION_TEST_BUILD");
            if (string.IsNullOrWhiteSpace(path)) path = config.CurrentBuildFilePath;
            if (string.IsNullOrWhiteSpace(path)) path = config.BuildFilePath;
            string sample = "Item Class: Boots\nRarity: Rare\nStorm Tread\nDragonscale Boots\n--------\nItem Level: 82\n--------\n"
                + "+150 to maximum Life\n+45% to Fire Resistance\n+45% to Lightning Resistance\n35% increased Movement Speed\n";
            string error;
            string originalConfig = File.ReadAllText(AppConfig.GetConfigFilePath());
            string originalBuild = null;
            using (var engine = new PobLuaEngine())
            {
                if (!engine.Initialize(config.PobPath, AppConfig.GetWrapperScriptPath(), out error))
                { Console.WriteLine("FAILED: source test engine: " + error); return 1; }
                var info = engine.LoadBuildFile(path, out error);
                if (info == null || !info.Success)
                { Console.WriteLine("FAILED: source test build: " + error); return 1; }
                originalBuild = File.ReadAllText(path);
                var before = engine.CompareItem(sample, out error);
                var export = engine.ExportWithCandidate(sample, "Boots", out error);
                if (before == null || !before.Success || export == null || !export.Success)
                { Console.WriteLine("FAILED: source test fixture: " + error); return 1; }
                var dual = config.Clone();
                dual.BuildSource = AppConfig.SourceShareCode;
                dual.ShareCode = dual.ShareCodeResolved = PobShareCode.Encode(export.Xml);
                dual.CurrentBuildFilePath = path;
                dual.CurrentCharacterCode = null;
                dual.SetGuideLoadout(null);
                if (!TestGuideBuildTab(dual, engine, sample)) return 1;
                string guideInput = config.UsesShareCode
                    ? (!string.IsNullOrWhiteSpace(config.ShareCodeResolved) ? config.ShareCodeResolved : config.ShareCode) : null;
                if (PobShareCode.IsUrl(guideInput))
                    Console.WriteLine("  SKIPPED: configured guide stages: load the guide link through the source controls first (offline source tests never download links).");
                else if (!TestGuideStages(config, path, engine, sample, before)) return 1;
                if (!TestGggBuildGuide(config, path, engine, sample, before)) return 1;
            }
            if (File.ReadAllText(path) != originalBuild || File.ReadAllText(AppConfig.GetConfigFilePath()) != originalConfig)
            { Console.WriteLine("FAILED: source tests changed source build or persisted config"); return 1; }
            Console.WriteLine("ALL SOURCE TESTS PASSED.");
            return 0;
        }

        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control control in root.Controls)
            {
                yield return control;
                foreach (var child in Descendants(control)) yield return child;
            }
        }

        private static bool TestGuideBuildTab(AppConfig config, PobLuaEngine currentEngine, string sampleItem)
        {
            string error;
            var before = currentEngine.CompareItem(sampleItem, out error);
            var guide = GuideBuildView.ReadGuide(config, out error);
            if (guide == null || !guide.Success || guide.Skills.Count == 0 ||
                !guide.Skills.Exists(g => g.Main) || !guide.Skills.Exists(g => g.Gems.Count > 0) ||
                !guide.Gear.Exists(g => g.SlotName == "Boots" && g.ItemName.Contains("Storm Tread")))
            { Console.WriteLine("FAILED: guide skills/gear details unavailable: " + error); return false; }
            using (var window = new MainWindow(config, currentEngine, null, null, null, null))
            {
                window.Show();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var tabs = (TabControl)typeof(MainWindow).GetField("_tabs", flags).GetValue(window);
                if (tabs.TabPages.Count != 3 || tabs.TabPages[1].Text != "Current character" || tabs.TabPages[2].Text != "Build")
                { Console.WriteLine("FAILED: distinct Current character and Build tabs"); return false; }
                tabs.SelectedIndex = 2;
                Application.DoEvents();
                var view = (GuideBuildView)typeof(MainWindow).GetField("_guideBuildView", flags).GetValue(window);
                var controls = new System.Collections.Generic.List<Control>(Descendants(view));
                if (!controls.Exists(c => c.Text.Contains("GUIDE BUILD (target")) ||
                    !controls.Exists(c => c.Text.Contains("No leveling order")) ||
                    !controls.Exists(c => c is ListBox && ((ListBox)c).Items.Count > 0) ||
                    !controls.Exists(c => c.Text.Contains("Quality ")) ||
                    !controls.Exists(c => c is PassiveTreeControl))
                { Console.WriteLine("FAILED: guide tab content missing"); return false; }
                foreach (Control control in Descendants(window))
                {
                    if ((control is Label || control is Button || control is TabPage || control is CheckBox)
                        && control.Text.IndexOf("worn", StringComparison.OrdinalIgnoreCase) >= 0)
                    { Console.WriteLine("FAILED: old user-facing label: " + control.Text); return false; }
                }
                var bad = config.Clone();
                bad.ShareCodeResolved = bad.ShareCode = "invalid";
                view.SetSource(bad);
                view.EnsureLoaded();
                if (!new System.Collections.Generic.List<Control>(Descendants(view)).Exists(c => c.Text.Contains("Guide details unavailable")))
                { Console.WriteLine("FAILED: missing guide not explained"); return false; }
                view.Render(new BuildDetails { Success = true });
                var emptyControls = new System.Collections.Generic.List<Control>(Descendants(view));
                if (!emptyControls.Exists(c => c.Text.Contains("No skill groups reported")) ||
                    !emptyControls.Exists(c => c.Text.Contains("No equipped jewels reported")) ||
                    !emptyControls.Exists(c => c.Text.Contains("No allocated nodes reported")))
                { Console.WriteLine("FAILED: missing skills not explained"); return false; }
                var fixture = new BuildDetails { Success = true, BuildName = "Guide display fixture" };
                fixture.Gear.Add(new GearEntry { SlotName = "Socket 1", SlotLabel = "Jewel socket 1", ItemName = "Fixture jewel", IsJewel = true, Raw = "Fixture jewel text" });
                fixture.Skills.Add(new SkillGroup { Label = "Fixture skill group", Main = true, Enabled = false,
                    Gems = new System.Collections.Generic.List<SkillGem> {
                        new SkillGem { Name = "Fixture gem", Level = 12, Quality = 7, Enabled = false },
                        new SkillGem { Name = "Unavailable gem", Enabled = true } } });
                view.Render(fixture);
                var fixtureControls = new System.Collections.Generic.List<Control>(Descendants(view));
                if (!fixtureControls.Exists(c => c is ListBox && ((ListBox)c).Items.Count == 1 && ((GearEntry)((ListBox)c).Items[0]).IsJewel) ||
                    !fixtureControls.Exists(c => c.Text.Contains("Level 12, Quality 7% [Disabled]") &&
                        c.Text.Contains("Level unavailable, Quality unavailable") && c.Text.Contains("[Main]")))
                { Console.WriteLine("FAILED: guide jewel/skill fields incorrectly rendered"); return false; }
                string sourcesBefore = new JavaScriptSerializer().Serialize(config);
                tabs.SelectedIndex = 0;
                tabs.SelectedIndex = 2;
                Application.DoEvents();
                if (new JavaScriptSerializer().Serialize(config) != sourcesBefore)
                { Console.WriteLine("FAILED: tab switching changed Current/Guide sources"); return false; }
                window.Hide();
            }
            var after = currentEngine.CompareItem(sampleItem, out error);
            if (before == null || after == null || !after.Success ||
                string.Join("|", before.Comparisons[0].Lines.ToArray()) != string.Join("|", after.Comparisons[0].Lines.ToArray()))
            { Console.WriteLine("FAILED: Build tab changed Current character baseline"); return false; }
            var independent = config.Clone();
            independent.ShareCode = "https://pobb.in/never-download-source-test";
            independent.ShareCodeResolved = null;
            var independentCurrent = ReadSourceDetails(independent, false, out error);
            if (independentCurrent == null || independentCurrent.Gear.Count == 0 || error != null)
            { Console.WriteLine("FAILED: missing guide blocked Current character: " + error); return false; }
            independent = config.Clone();
            independent.CurrentBuildFilePath = null;
            independent.CurrentCharacterCode = null;
            var independentGuide = ReadSourceDetails(independent, true, out error);
            if (independentGuide == null || independentGuide.Gear.Count == 0 || error != null)
            { Console.WriteLine("FAILED: missing Current character blocked Guide: " + error); return false; }
            Console.WriteLine("  PASSED: guide Build tab skills/gear/tree, independent Current/Guide sources, unchanged Current character baseline, renamed labels and explicit unavailable states.");
            return true;
        }

        private static BuildDetails ReadSourceDetails(AppConfig config, bool guide, out string error)
        {
            var copy = config.Clone();
            copy.DisableSave = true;
            string input = guide ? (copy.UsesShareCode
                ? (!string.IsNullOrWhiteSpace(copy.ShareCodeResolved) ? copy.ShareCodeResolved : copy.ShareCode) : null)
                : copy.CurrentCharacterCode;
            if (PobShareCode.IsUrl(input))
            {
                error = "Load this source link through the source controls first; offline source tests never download links.";
                return null;
            }
            using (var engine = new PobLuaEngine())
            {
                if (!engine.Initialize(copy.PobPath, AppConfig.GetWrapperScriptPath(), out error)) return null;
                var info = guide ? copy.LoadGuideBuild(engine, out error) : copy.LoadConfiguredBuild(engine, out error);
                if (info == null || !info.Success) return null;
                var details = engine.GetBuildDetails(out error);
                if (details == null || !details.Success) return null;
                if (guide) GggGuide.Decorate(info, details);
                return details;
            }
        }

        // GGG / Maxroll .build guide: synthetic fixture in the official structure with unknown fields, both
        // level_interval forms, a weapon set, a unique, a stat priority note and unmapped ids. Read only.
        private static bool TestGggBuildGuide(AppConfig config, string currentBuildPath, PobLuaEngine engine, string sampleItem, ComparisonResult before)
        {
            string fixture = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "tests", "fixtures", "sample.build"));
            if (!File.Exists(fixture)) { Console.WriteLine("FAILED: missing fixture " + fixture); return false; }
            string fixtureText = File.ReadAllText(fixture);
            string error, notice;
            var file = GggBuildFile.Load(fixture, out error);
            if (file == null || file.RawJson != fixtureText || file.Name != "Fixture Deadeye" || file.Author != "companion-tests")
            { Console.WriteLine("FAILED: .build parse: " + error); return false; }
            foreach (var expected in new[] { "x_tool_version = ", "x_meta = ", "passives[2].x_order = 3", "skills[0].x_slot = ", "skills[0].support_skills[0].x_note = ", "inventory_slots[0].x_color = " })
                if (!file.UnknownFields.Exists(u => u.StartsWith(expected)))
                { Console.WriteLine("FAILED: unknown field not preserved: " + expected + " / " + string.Join("; ", file.UnknownFields)); return false; }
            if (!file.Slots[0].Fields.ContainsKey("x_color") || !file.Build.ContainsKey("x_meta") || !file.UsesSingleLevelValues)
            { Console.WriteLine("FAILED: raw fields / single-value intervals"); return false; }
            var stages = file.GetStages().Select(s => s.Name).ToList();
            if (string.Join("|", stages) != "Level 1-40|Level 41+") { Console.WriteLine("FAILED: stages " + string.Join("|", stages)); return false; }

            var g = config.Clone();
            g.DisableSave = true;
            g.BuildSource = AppConfig.SourceGggBuild;
            g.GggBuildFilePath = fixture;
            g.CurrentBuildFilePath = currentBuildPath;
            g.CurrentCharacterCode = null;
            g.FollowGameStage = false;
            g.SetGuideLoadout("Level 1-40");
            var early = GuideBuildView.ReadGuide(g, out error, out notice);
            if (early == null || !early.Success || early.Ggg == null || early.ActiveLoadout != "Level 1-40" || string.Join("|", early.Loadouts) != "Level 1-40|Level 41+")
            { Console.WriteLine("FAILED: .build early stage load: " + error); return false; }
            if (early.Gear.Count == 0 || early.Gear.Any(n => !n.FromNotes || n.Rarity != "NOTE" || n.Affixes.Count != 0))
            { Console.WriteLine("FAILED: .build early stage notes fabricated items"); return false; }
            int earlyAlloc = early.Tree.Nodes.Count(n => n.Alloc);
            var earlyGems = early.Skills.SelectMany(s => s.Gems).ToList();
            if (early.ClassName != "Ranger" || early.AscendClassName != "Deadeye" || early.Ggg.MappedPassives != 5 || string.Join(",", early.Ggg.DroppedPassives) != "dexterity34,azmerianimals40" ||
                !earlyGems.Exists(x => x.Name == "Ice Shot") || !earlyGems.Exists(x => x.Name == "Deliberation") || earlyGems.Exists(x => x.Name.StartsWith("Elemental Armament")) ||
                earlyGems.Exists(x => !x.Level.HasValue || x.Level.Value < 1) || !early.Ggg.UnmappedGems.Exists(x => x.Contains("DoesNotExistFixture")))
            { Console.WriteLine("FAILED: early stage tree/gems: alloc " + earlyAlloc + " [" + string.Join(",", early.Tree.Nodes.Where(n => n.Alloc).Select(n => n.Name).ToArray()) + "] of mapped [" +
                string.Join(",", early.Ggg.ActivePassives.Select(p => p.Id).ToArray()) + "], gems " + string.Join(",", earlyGems.Select(x => x.Name + " L" + x.Level).ToArray())); return false; }
            var ring = early.Gear.FirstOrDefault(x => x.SlotName == "Ring 1");
            if (early.Gear.Count != 2 || ring == null || !ring.FromNotes || ring.Affixes.Count != 0 || ring.Priorities.Count != 3 || ring.Rarity != "NOTE" ||
                !ring.Raw.Contains("none are invented") || early.Gear.Exists(x => x.SlotName == "Weapon 1"))
            { Console.WriteLine("FAILED: early stage slot notes"); return false; }

            g.SetGuideLoadout("Level 41+");
            var late = GuideBuildView.ReadGuide(g, out error, out notice);
            if (late == null || !late.Success) { Console.WriteLine("FAILED: .build late stage load: " + error); return false; }
            int lateAlloc = late.Tree.Nodes.Count(n => n.Alloc);
            var weapon = late.Gear.FirstOrDefault(x => x.SlotName == "Weapon 1");
            if (late.Ggg.DroppedPassives.Count != 0 || lateAlloc < late.Ggg.MappedPassives || late.Ggg.MappedPassives < 100 || !late.Ggg.UnmappedPassives.Contains("not_a_real_passive_123") ||
                weapon == null || weapon.ItemName != "Widowhail" || weapon.Rarity != "UNIQUE" ||
                !late.Skills.SelectMany(s => s.Gems).Any(x => x.Name.StartsWith("Elemental Armament")) || !late.Tree.Nodes.Exists(n => n.Alloc && n.Ascendancy))
            { Console.WriteLine("FAILED: late stage: alloc " + lateAlloc + " of mapped " + late.Ggg.MappedPassives + ", weapon " + (weapon == null ? null : weapon.ItemName)); return false; }
            Console.WriteLine("    .build stages: Level 1-40 = " + earlyAlloc + " passives (PoB2 dropped unconnected " + string.Join(", ", early.Ggg.DroppedPassives) + "), gems " + string.Join(", ", earlyGems.Select(x => x.Name + " L" + x.Level).ToArray()) +
                "; Level 41+ = " + lateAlloc + " passives (" + late.Ggg.MappedPassives + " mapped), unique " + weapon.ItemName + ", unmapped passives " + late.Ggg.UnmappedPassives.Count);

            var ratings = GggGuide.RateAgainstNotes(new System.Collections.Generic.List<ItemAffix> {
                TestAffix("+50 to maximum Life"), TestAffix("+20% to Fire Resistance"), TestAffix("+30% to Cold Resistance"), TestAffix("10% increased Attack Speed") }, ring);
            if (string.Join(",", ratings.Select(r => r.Rating).ToArray()) != "Green,Red,Green,Green" ||
                GggGuide.RateAgainstNotes(new System.Collections.Generic.List<ItemAffix> { TestAffix("+50 to maximum Life") }, null)[0].Rating != "Unrated")
            { Console.WriteLine("FAILED: heuristic note ratings " + string.Join(",", ratings.Select(r => r.Rating).ToArray())); return false; }

            var compared = GuideComparison.Compare(g, engine, sampleItem, out error);
            if (compared == null || !compared.Success || compared.Comparisons.Count != before.Comparisons.Count)
            { Console.WriteLine("FAILED: comparison with .build guide: " + error); return false; }
            for (int i = 0; i < before.Comparisons.Count; i++)
            {
                var slot = compared.Comparisons[i];
                if (string.Join("|", slot.Lines.ToArray()) != string.Join("|", before.Comparisons[i].Lines.ToArray()) ||
                    slot.GuideStage != "Level 41+" || slot.GuideFit == null || !slot.GuideFit.Contains(".build"))
                { Console.WriteLine("FAILED: .build guide changed PoB2 deltas or fit missing: " + slot.GuideFit); return false; }
                Console.WriteLine("    .build comparison " + slot.SlotName + ": " + slot.GuideFit.Replace("\n", " | "));
            }
            string reason;
            if (GameLog.SuggestStage(stages, new GameLogState { Phase = GamePhase.Act2, CharacterLevel = 45 }, 80, 90, out reason) != "Level 41+" ||
                GameLog.SuggestStage(stages, new GameLogState { Phase = GamePhase.Act1, CharacterLevel = 12 }, 80, 90, out reason) != "Level 1-40")
            { Console.WriteLine("FAILED: level-range stage suggestion"); return false; }

            using (var window = new MainWindow(g, engine, null, null, null, null))
            {
                window.Show();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var tabs = (TabControl)typeof(MainWindow).GetField("_tabs", flags).GetValue(window);
                tabs.SelectedIndex = 2;
                Application.DoEvents();
                var view = (GuideBuildView)typeof(MainWindow).GetField("_guideBuildView", flags).GetValue(window);
                view.SetSource(g);
                view.EnsureLoaded();
                var controls = Descendants(view).ToList();
                if (!controls.Exists(c => c.Text.Contains("GUIDE BUILD from a .build file") && c.Text.Contains("LIMITATION") && c.Text.Contains("UNKNOWN FIELDS") && c.Text.Contains("not_a_real_passive_123")) ||
                    !controls.Exists(c => c.Text == "Stage (.build level range):") || !controls.Exists(c => c.Text == "Load .build guide...") ||
                    !controls.Exists(c => c.Text.Contains("GUIDE NOTES") && c.Text.Contains("Take this after Act 3")))
                { Console.WriteLine("FAILED: .build Build tab rendering"); return false; }
                window.Hide();
            }
            var after = engine.CompareItem(sampleItem, out error);
            if (File.ReadAllText(fixture) != fixtureText || after == null ||
                string.Join("|", after.Comparisons[0].Lines.ToArray()) != string.Join("|", before.Comparisons[0].Lines.ToArray()))
            { Console.WriteLine("FAILED: .build fixture modified or Current character baseline changed"); return false; }
            Console.WriteLine("  PASSED: .build import (unknown fields kept, level-range stages, PoB2 tree/gems, note-only gear, heuristic fit, Build tab, unchanged file and Current deltas).");
            return true;
        }

        // Guide stages = PoB2 loadouts. Uses the configured guide code read-only (a copy that cannot save).
        private static bool TestGuideStages(AppConfig config, string currentBuildPath, PobLuaEngine currentEngine, string sampleItem, ComparisonResult before)
        {
            string code = !string.IsNullOrWhiteSpace(config.ShareCodeResolved) ? config.ShareCodeResolved : config.ShareCode;
            if (!config.UsesShareCode || string.IsNullOrWhiteSpace(code))
            { Console.WriteLine("  SKIPPED: guide stages (no guide share code configured)"); return true; }
            var g = config.Clone();
            g.DisableSave = true;
            g.ShareCode = g.ShareCodeResolved = code.Trim();
            g.CurrentBuildFilePath = currentBuildPath;
            g.CurrentCharacterCode = null;
            g.FollowGameStage = false;
            g.SetGuideLoadout(null);
            string error, notice;
            var saved = GuideBuildView.ReadGuide(g, out error, out notice);
            if (saved == null || !saved.Success) { Console.WriteLine("FAILED: guide load: " + error); return false; }
            Console.WriteLine("    Guide loadouts (" + saved.Loadouts.Count + "): " + string.Join(", ", saved.Loadouts) + "; saved active: " + (saved.ActiveLoadout ?? "(none)"));
            if (saved.Loadouts.Count < 2) { Console.WriteLine("  SKIPPED: guide has fewer than 2 PoB2 loadouts"); return true; }

            var signatures = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var stage in saved.Loadouts)
            {
                g.SetGuideLoadout(stage);
                var d = GuideBuildView.ReadGuide(g, out error, out notice);
                if (d == null || !d.Success || d.ActiveLoadout != stage || notice != null)
                { Console.WriteLine("FAILED: stage '" + stage + "' not activated: " + error + " / " + (d == null ? null : d.ActiveLoadout)); return false; }
                int alloc = d.Tree.Nodes.Count(n => n.Alloc);
                var main = d.Skills.FirstOrDefault(s => s.Main);
                signatures[stage] = "L" + d.Level + " tree " + alloc + " nodes, main skill " + d.MainSkill + ", gear " +
                    string.Join("/", d.Gear.Where(x => !x.IsJewel && x.ItemName != null).Select(x => x.ItemName).ToArray());
                Console.WriteLine("    " + stage + ": " + alloc + " passives, main skill " + d.MainSkill + (main != null ? " (" + main.Label + ")" : "") +
                    ", Boots " + (d.Gear.Where(x => x.SlotName == "Boots").Select(x => x.ItemName).FirstOrDefault() ?? "(empty)"));
            }
            if (signatures.Values.Distinct().Count() < 2) { Console.WriteLine("FAILED: switching stages did not change guide gear/tree"); return false; }

            string first = saved.Loadouts[0];
            g.SetGuideLoadout(first);
            var staged = GuideComparison.Compare(g, currentEngine, sampleItem, out error);
            if (staged == null || !staged.Success || staged.Comparisons.Count != before.Comparisons.Count)
            { Console.WriteLine("FAILED: comparison with guide stage: " + error); return false; }
            for (int i = 0; i < before.Comparisons.Count; i++)
                if (string.Join("|", staged.Comparisons[i].Lines.ToArray()) != string.Join("|", before.Comparisons[i].Lines.ToArray())
                    || staged.Comparisons[i].GuideStage != first)
                { Console.WriteLine("FAILED: guide stage changed Current character deltas or stage not reported"); return false; }

            g.GuideLoadout = "Missing stage {99}";
            var fallback = GuideBuildView.ReadGuide(g, out error, out notice);
            if (fallback == null || !fallback.Success || notice == null || fallback.ActiveLoadout != saved.ActiveLoadout)
            { Console.WriteLine("FAILED: missing stage did not fall back to saved sets"); return false; }
            g.SetGuideLoadout(first);
            var other = g.Clone();
            other.ShareCode = "different guide";
            if (other.GetSelectedGuideLoadout() != null) { Console.WriteLine("FAILED: stage leaked to a different guide"); return false; }

            g.SetGuideLoadout(null);
            using (var window = new MainWindow(g, currentEngine, null, null, null, null))
            {
                window.Show();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var tabs = (TabControl)typeof(MainWindow).GetField("_tabs", flags).GetValue(window);
                tabs.SelectedIndex = 2;
                Application.DoEvents();
                var view = (GuideBuildView)typeof(MainWindow).GetField("_guideBuildView", flags).GetValue(window);
                var combo = Descendants(view).OfType<ComboBox>().FirstOrDefault();
                if (combo == null || !combo.Enabled || combo.Items.Count != saved.Loadouts.Count + (saved.ActiveLoadout == null ? 1 : 0)
                    || (saved.ActiveLoadout != null && (string)combo.SelectedItem != saved.ActiveLoadout))
                { Console.WriteLine("FAILED: stage dropdown does not mirror PoB2 loadouts"); return false; }
                string uiStage = saved.Loadouts.First(s => s != saved.ActiveLoadout);
                combo.SelectedItem = uiStage;
                Application.DoEvents();
                if (g.GetSelectedGuideLoadout() != uiStage || !Descendants(view).Any(c => c is RichTextBox && c.Text.Contains("Stage: " + uiStage)))
                { Console.WriteLine("FAILED: selecting a stage in the Build tab did not switch the guide"); return false; }
                window.Hide();
            }
            var after = currentEngine.CompareItem(sampleItem, out error);
            if (after == null || string.Join("|", after.Comparisons[0].Lines.ToArray()) != string.Join("|", before.Comparisons[0].Lines.ToArray()))
            { Console.WriteLine("FAILED: stage UI changed Current character baseline"); return false; }
            Console.WriteLine("  PASSED: all " + saved.Loadouts.Count + " PoB2 guide stages selectable; stage drives guide gear/tree/ratings; Current character deltas unchanged; missing stage falls back.");
            return true;
        }

        // Equip into the Current character SOURCE file: uses a temp copy of the fixture, never the user's file.
        private static bool TestEquipIntoSource(AppConfig config, string buildPath, PobLuaEngine engine, string sampleItem, ComparisonResult before)
        {
            string dir = Path.Combine(Path.GetTempPath(), "poe2companion-equip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, "current.xml");
            File.Copy(buildPath, copy);
            byte[] original = File.ReadAllBytes(copy);
            var c = config.Clone();
            c.DisableSave = true;
            c.CurrentBuildFilePath = copy;
            c.CurrentCharacterCode = null;
            string error;
            EquipResult r = null;
            try
            {
                c.CurrentCharacterCode = "some code";
                if (SourceEquipper.EquipIntoSource(engine, c, sampleItem, "Boots", out error) != null || !error.Contains("import code"))
                { Console.WriteLine("FAILED: equip into code-based Current character was not refused"); return false; }
                c.CurrentCharacterCode = null;
                if (SourceEquipper.EquipIntoSource(engine, c, sampleItem, "Not A Slot", out error) != null || !File.ReadAllBytes(copy).SequenceEqual(original))
                { Console.WriteLine("FAILED: invalid slot changed the source"); return false; }

                r = SourceEquipper.EquipIntoSource(engine, c, sampleItem, "Boots", out error);
                if (r == null) { Console.WriteLine("FAILED: equip into source: " + error); return false; }
                if (!File.ReadAllBytes(r.BackupPath).SequenceEqual(original) || File.ReadAllBytes(copy).SequenceEqual(original))
                { Console.WriteLine("FAILED: backup/source contents wrong"); return false; }
                var after = engine.CompareItem(sampleItem, out error);
                if (after == null || !after.Success || after.Comparisons[0].ReplacedItemName != r.Export.ItemName)
                { Console.WriteLine("FAILED: comparison baseline not updated to the equipped item"); return false; }
                using (var fresh = new PobLuaEngine())
                {
                    if (!fresh.Initialize(c.PobPath, AppConfig.GetWrapperScriptPath(), out error)) { Console.WriteLine("FAILED: " + error); return false; }
                    var info = fresh.LoadBuildFile(copy, out error);
                    var det = info != null && info.Success ? fresh.GetBuildDetails(out error) : null;
                    if (det == null || !det.Gear.Exists(g => g.SlotName == "Boots" && g.ItemName == r.Export.ItemName))
                    { Console.WriteLine("FAILED: written source does not load with the item in PoB2: " + error); return false; }
                    Console.WriteLine("    Equipped " + r.Export.ItemName + " (replaced " + r.Export.ReplacedItemName + "); fresh PoB2 load L" + info.Level + "; backup " + Path.GetFileName(r.BackupPath));
                }
            }
            finally
            {
                if (r != null && File.Exists(r.BackupPath)) File.Delete(r.BackupPath);
                try { if (Directory.GetFileSystemEntries(SourceEquipper.GetBackupFolder()).Length == 0) Directory.Delete(SourceEquipper.GetBackupFolder()); } catch { }
                try { Directory.Delete(dir, true); } catch { }
                engine.LoadBuildFile(buildPath, out error);
            }
            Console.WriteLine("  PASSED: explicit equip into Current character file (backup first, PoB2-verified, baseline updated, code source and bad slot refused).");
            return true;
        }

        // Client.txt parsing and stage suggestion; the real log is only read (shared, read-only).
        private static bool TestGameLogStages()
        {
            var names = new System.Collections.Generic.List<string> { "Act 1 {1}", "Act 2 {2}", "Act3 {3}", "Act 4+interlude {4}", "Early {5}", "mid {6}", "Endgame {7}", "Min-max{8}" };
            string reason;
            Func<GamePhase, int?, string> suggest = (phase, level) =>
                GameLog.SuggestStage(names, new GameLogState { Phase = phase, CharacterLevel = level }, 80, 90, out reason);
            if (suggest(GamePhase.Act1, 5) != "Act 1 {1}" || suggest(GamePhase.Act2, 20) != "Act 2 {2}" || suggest(GamePhase.Act3, 30) != "Act3 {3}"
                || suggest(GamePhase.Act4, 40) != "Act 4+interlude {4}" || suggest(GamePhase.Interlude, 45) != "Act 4+interlude {4}"
                || suggest(GamePhase.Endgame, 70) != "Early {5}" || suggest(GamePhase.Endgame, 85) != "mid {6}" || suggest(GamePhase.Endgame, 95) != "Endgame {7}"
                || suggest(GamePhase.Endgame, null) != null || suggest(GamePhase.Unknown, 50) != null)
            { Console.WriteLine("FAILED: stage suggestion mapping"); return false; }
            if (GameLog.ClassifyArea("G1_town") != GamePhase.Act1 || GameLog.ClassifyArea("G3_8") != GamePhase.Act3 || GameLog.ClassifyArea("P2_town") != GamePhase.Interlude
                || GameLog.ClassifyArea("MapBloodwood") != GamePhase.Endgame || GameLog.ClassifyArea("G_Endgame_town") != GamePhase.Endgame
                || GameLog.ClassifyArea("HideoutArenaIncursion") != GamePhase.Unknown || GameLog.ClassifyArea("ExpeditionSubArea_Kalguur_Act1") != GamePhase.Unknown)
            { Console.WriteLine("FAILED: area classification"); return false; }

            string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "poe2companion-log-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                System.IO.File.WriteAllText(temp,
                    "2026/10/19 20:00:00 1 abc [DEBUG Client 1] Generating level 1 area \"G1_1\" with seed 1\n" +
                    "2026/10/19 20:00:05 2 abc [INFO Client 1] : Tester (Ranger) is now level 2\n" +
                    "2026/10/19 20:00:06 3 abc [INFO Client 1] @From Someone: Hey (Ranger) is now level 99\n");
                var watcher = new GameLogWatcher(temp);
                string error;
                if (!watcher.Poll(out error) || watcher.State.Phase != GamePhase.Act1 || watcher.State.CharacterLevel != 2 || watcher.State.CharacterName != "Tester")
                { Console.WriteLine("FAILED: log parse: " + error + " " + watcher.State.Describe()); return false; }
                using (var fs = new System.IO.FileStream(temp, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
                using (var w = new System.IO.StreamWriter(fs))
                {
                    w.Write("2026/10/19 21:00:00 4 abc [DEBUG Client 1] Generating level 65 area \"HideoutArenaIncursion\" with seed 1\n");
                    w.Write("2026/10/19 21:00:01 5 abc [INFO Client 1] : Tester (Ranger) is now level 84\n");
                    w.Write("2026/10/19 21:00:02 6 abc [DEBUG Client 1] Generating level 79 area \"MapHiddenGrotto\" with seed 2\n");
                    w.Write("2026/10/19 21:00:03 7 abc [DEBUG Client 1] Generating level 65 area \"HideoutArena"); // partial line
                    w.Flush();
                    if (!watcher.Poll(out error) || watcher.State.Phase != GamePhase.Endgame || watcher.State.CharacterLevel != 84 || watcher.State.AreaCode != "MapHiddenGrotto")
                    { Console.WriteLine("FAILED: appended log lines: " + watcher.State.Describe()); return false; }
                    if (GameLog.SuggestStage(names, watcher.State, 80, 90, out reason) != "mid {6}") { Console.WriteLine("FAILED: live suggestion"); return false; }
                    w.Write("Incursion\" with seed 1\n");
                    w.Flush();
                    watcher.Poll(out error);
                    if (watcher.State.AreaCode != "HideoutArenaIncursion" || watcher.State.Phase != GamePhase.Endgame)
                    { Console.WriteLine("FAILED: partial line / neutral zone handling"); return false; }
                }
            }
            finally { try { System.IO.File.Delete(temp); } catch (Exception) { } }

            string real = GameLog.FindLogPath(null);
            if (real != null)
            {
                var watcher = new GameLogWatcher(real);
                string error;
                watcher.Poll(out error);
                if (error != null) { Console.WriteLine("FAILED: reading game log " + real + ": " + error); return false; }
                Console.WriteLine("    Real PoE2 log " + real + ": " + watcher.State.Describe());
            }
            else Console.WriteLine("    No PoE2 Client.txt found (skipped real-log read).");
            Console.WriteLine("  PASSED: Client.txt zone/level parsing, append + partial lines, stage suggestion (never Min-Max).");
            return true;
        }

        private static bool TestGemCutting(AppConfig config, string currentBuildPath, PobLuaEngine engine)
        {
            var g = config.Clone();
            g.DisableSave = true;
            g.FollowGameStage = false;
            g.CurrentBuildFilePath = currentBuildPath;
            g.CurrentCharacterCode = null;
            string error, notice, currentError;
            BuildDetails current;
            var guide = GuideBuildView.ReadGuide(g, true, out error, out notice, out current, out currentError);
            if (guide == null || !guide.Success) { Console.WriteLine("FAILED: guide for gem cutting: " + error); return false; }
            if (current == null) { Console.WriteLine("FAILED: Current character for gem cutting: " + currentError); return false; }
            var gems = guide.Skills.SelectMany(s => s.Gems).ToList();
            if (!gems.Any(x => x.Kind == "Skill" && x.Tier > 0) || gems.Any(x => x.Kind == "Support" && !x.Tier.HasValue))
            { Console.WriteLine("FAILED: PoB2 gem tier/kind not exported"); return false; }
            var plan = GemCutting.Plan(guide, current);
            if (plan.Count == 0 || plan.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Count)
            { Console.WriteLine("FAILED: gem plan empty or duplicated"); return false; }
            Console.WriteLine("    Guide stage " + (guide.ActiveLoadout ?? "saved sets") + ": " + plan.Count + " gems, " + plan.Count(p => p.NeedsCut) + " missing on Current character");
            foreach (var p in plan.Take(6))
                Console.WriteLine("      " + p.Name + " [" + p.Kind + " tier " + GemCutting.Roman(p.Tier) + ", tab: " + p.CutterTab + "] " + p.Status);
            var one = plan.First(p => p.Tier > 0);
            var fake = new BuildDetails { Success = true };
            fake.Skills.Add(new SkillGroup { Gems = new System.Collections.Generic.List<SkillGem> { new SkillGem { Name = one.Name, Level = 99, Enabled = true } } });
            var withFake = GemCutting.Plan(guide, fake).First(p => p.Name == one.Name);
            var noCurrent = GemCutting.Plan(guide, null).First(p => p.Name == one.Name);
            if (withFake.NeedsCut || !withFake.Status.StartsWith("Have") || noCurrent.Status != "Current character not loaded")
            { Console.WriteLine("FAILED: gem have/missing status"); return false; }
            if (GemCutting.Roman(13) != "XIII" || GemCutting.Roman(9) != "IX" || GemCutting.Roman(14) != "XIV")
            { Console.WriteLine("FAILED: roman tiers"); return false; }

            using (var window = new MainWindow(g, engine, null, null, null, null))
            {
                window.Show();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                ((TabControl)typeof(MainWindow).GetField("_tabs", flags).GetValue(window)).SelectedIndex = 2;
                Application.DoEvents();
                var view = (GuideBuildView)typeof(MainWindow).GetField("_guideBuildView", flags).GetValue(window);
                var list = Descendants(view).OfType<ListView>().FirstOrDefault();
                var log = Descendants(view).OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Game log:"));
                if (list == null || list.Items.Count != plan.Count || log == null)
                { Console.WriteLine("FAILED: Gem cutting tab / game log row missing"); return false; }
                Console.WriteLine("    UI: " + log.Text);
                window.Hide();
            }
            Console.WriteLine("  PASSED: gem cutting list (PoB2 tiers, weapon tabs, have/missing vs Current character) and game log row.");

            g.GemOverlayX = g.GemOverlayY = -1;
            g.GemOverlayShowAll = false;
            using (var overlay = new GemOverlay(g))
            {
                overlay.ShowOverlay();
                Application.DoEvents();
                int missingCount = plan.Count(p => p.NeedsCut);
                if (!overlay.Visible || !overlay.TitleText.Contains("(" + missingCount + " missing)") || !overlay.TitleText.Contains(guide.ActiveLoadout ?? "saved sets")
                    || (missingCount > 0 && !overlay.BodyText.Contains("+ " + plan.First(p => p.NeedsCut).Name)))
                { Console.WriteLine("FAILED: gem overlay content: " + overlay.TitleText + " / " + overlay.BodyText); return false; }
                if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(overlay.Bounds))) { Console.WriteLine("FAILED: overlay off-screen"); return false; }
                Console.WriteLine("    Overlay: " + overlay.TitleText);
                string shot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "poe2companion-gemoverlay.png");
                using (var bmp = new System.Drawing.Bitmap(overlay.Width, overlay.Height))
                {
                    overlay.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, overlay.Width, overlay.Height));
                    bmp.Save(shot);
                }
                Console.WriteLine("    Overlay snapshot: " + shot);
                foreach (var line in overlay.BodyText.Split('\n').Take(5)) Console.WriteLine("      | " + line.TrimEnd('\r'));
                g.GemOverlayShowAll = true;
                overlay.Reload(false);
                var allBody = overlay.BodyText;
                var groupsAll = GemCutting.PlanGroups(guide, current);
                if (plan.Any(p => !allBody.Split('\n').Any(l => l.Contains(" " + p.Name + " ") && l.TrimStart().StartsWith("+") == p.NeedsCut)) || groupsAll.Any(gr => !allBody.Contains(gr.Label)))
                { Console.WriteLine("FAILED: overlay 'all' mode, missing: " + string.Join(", ", plan.Where(p => !allBody.Split('\n').Any(l => l.Contains(" " + p.Name + " ") && l.TrimStart().StartsWith("+") == p.NeedsCut)).Select(p => p.Name + "/" + p.NeedsCut).Concat(groupsAll.Where(gr => !allBody.Contains(gr.Label)).Select(gr => "group " + gr.Label)).ToArray())); return false; }
                overlay.Toggle();
                if (overlay.Visible) { Console.WriteLine("FAILED: overlay toggle hide"); return false; }
            }

            var groups = GemCutting.PlanGroups(guide, current);
            if (groups.Count == 0 || groups.Any(gr => string.IsNullOrEmpty(gr.Label) || gr.Entries.Count == 0)) { Console.WriteLine("FAILED: gem groups empty"); return false; }
            foreach (var gr in groups)
            {
                int firstSupport = gr.Entries.FindIndex(e => e.Kind == "Support");
                if (firstSupport >= 0 && gr.Entries.Skip(firstSupport).Any(e => e.Kind != "Support"))
                { Console.WriteLine("FAILED: supports not listed after their skill in group " + gr.Label); return false; }
            }
            foreach (var gr in groups.Take(4))
                Console.WriteLine("      [" + gr.Label + (gr.Main ? ", main" : "") + "] " + string.Join(", ", gr.Entries.Select(e => e.Name).ToArray()));
            Console.WriteLine("  PASSED: gems grouped per skill with their supports (" + groups.Count + " groups).");

            string spotDir = GemCutterDetector.GetFolder();
            bool spotDirExisted = System.IO.Directory.Exists(spotDir);
            var d = config.Clone();
            d.DisableSave = true;
            d.GemDetectSpots = new System.Collections.Generic.List<string>();
            using (var detector = new GemCutterDetector(d))
            using (var a = new System.Drawing.Bitmap(GemCutterDetector.SpotWidth, GemCutterDetector.SpotHeight))
            using (var b = new System.Drawing.Bitmap(GemCutterDetector.SpotWidth, GemCutterDetector.SpotHeight))
            {
                using (var ga = System.Drawing.Graphics.FromImage(a)) ga.Clear(System.Drawing.Color.FromArgb(30, 25, 20));
                using (var gb = System.Drawing.Graphics.FromImage(b)) gb.Clear(System.Drawing.Color.FromArgb(200, 180, 120));
                if (GemCutterDetector.Difference(a, a) != 0 || GemCutterDetector.Difference(a, (System.Drawing.Bitmap)a.Clone()) != 0 || GemCutterDetector.Difference(a, b) < 100) { Console.WriteLine("FAILED: spot difference"); return false; }
                var r1 = GemCutterDetector.SpotAround(new System.Drawing.Point(-100000, -100000));
                if (!Screen.AllScreens.Any(s => s.Bounds.Contains(r1))) { Console.WriteLine("FAILED: spot clamped to screen"); return false; }
                // Plain dark areas (like the passive tree background) are too featureless to identify a window.
                using (var detailed = new System.Drawing.Bitmap(a))
                {
                    using (var gd = System.Drawing.Graphics.FromImage(detailed))
                    using (var f = new System.Drawing.Font("Segoe UI", 10f))
                        gd.DrawString("Type keywords here...", f, System.Drawing.Brushes.Wheat, 4, 8);
                    double plainDetail = GemCutterDetector.Detail(a), textDetail = GemCutterDetector.Detail(detailed);
                    Console.WriteLine("    Spot detail: plain " + plainDetail.ToString("0.0") + ", text " + textDetail.ToString("0.0") + " (minimum " + GemCutterDetector.MinDetail + ")");
                    if (plainDetail >= GemCutterDetector.MinDetail || textDetail < GemCutterDetector.MinDetail) { Console.WriteLine("FAILED: spot detail measure"); return false; }
                    detector.AddSpot(a, new System.Drawing.Rectangle(0, 0, a.Width, a.Height));
                    if (detector.SpotCount != 0 || detector.PlainSpotCount != 1 || detector.IsGemCutterOpen(System.Drawing.Rectangle.Empty) != null)
                    { Console.WriteLine("FAILED: plain saved spot must be ignored"); return false; }
                    for (int i = 0; i < 4; i++) detector.AddSpot(detailed, new System.Drawing.Rectangle(i * 10, 0, a.Width, a.Height));
                    if (detector.SpotCount != GemCutterDetector.MaxSpots || detector.PlainSpotCount != 0 || d.GemDetectSpots.Count != GemCutterDetector.MaxSpots
                        || System.IO.Directory.GetFiles(spotDir, "spot-*.png").Length < GemCutterDetector.MaxSpots)
                    { Console.WriteLine("FAILED: spot limit/storage (plain spot replaced first)"); return false; }
                }
                var all = new System.Drawing.Rectangle(-100000, -100000, 300000, 300000);
                if (detector.IsGemCutterOpen(all) != null) { Console.WriteLine("FAILED: overlay-covered spots must be skipped"); return false; }
                // Real screen capture against a non-matching reference: must not report the gem cutter as open.
                // Use a part of the screen with detail (plain areas are refused as spots).
                var live = GemCutterDetector.SpotAround(Cursor.Position);
                var primary = Screen.PrimaryScreen.Bounds;
                for (int gy = 0; gy < 8; gy++)
                    for (int gx = 0; gx < 8; gx++)
                    {
                        var r = GemCutterDetector.SpotAround(new System.Drawing.Point(primary.Left + 80 + gx * (primary.Width - 160) / 7, primary.Top + 40 + gy * (primary.Height - 80) / 7));
                        using (var c1 = GemCutterDetector.Capture(r))
                        using (var c2 = GemCutterDetector.Capture(r))
                            if (GemCutterDetector.Detail(c1) >= GemCutterDetector.MinDetail + 3 && GemCutterDetector.Difference(c1, c2) < 1) { live = r; gx = gy = 99; }
                    }
                using (var cap = GemCutterDetector.Capture(live))
                {
                    if (GemCutterDetector.Detail(cap) < GemCutterDetector.MinDetail) { Console.WriteLine("FAILED: no detailed, static screen area for the live detector test"); return false; }
                    var inverted = new System.Drawing.Bitmap(cap.Width, cap.Height);
                    for (int x = 0; x < cap.Width; x++) for (int y = 0; y < cap.Height; y++)
                    { var c = cap.GetPixel(x, y); inverted.SetPixel(x, y, System.Drawing.Color.FromArgb(255 - c.R, 255 - c.G, 255 - c.B)); }
                    detector.ClearSpots();
                    double invDiff = GemCutterDetector.Difference(cap, inverted);
                    detector.AddSpot(inverted, live);
                    inverted.Dispose();
                    bool? diffOpen = detector.IsGemCutterOpen(System.Drawing.Rectangle.Empty);
                    detector.ClearSpots();
                    detector.AddSpot(cap, live);
                    bool? sameOpen = detector.IsGemCutterOpen(System.Drawing.Rectangle.Empty);
                    // One matching + one non-matching spot: every spot must match, so not open.
                    using (var inv2 = (System.Drawing.Bitmap)cap.Clone())
                    {
                        using (var gi = System.Drawing.Graphics.FromImage(inv2)) gi.FillRectangle(System.Drawing.Brushes.White, 0, 0, inv2.Width, inv2.Height / 2);
                        using (var gi = System.Drawing.Graphics.FromImage(inv2)) gi.DrawString("other", System.Drawing.SystemFonts.DefaultFont, System.Drawing.Brushes.Black, 2, 2);
                        detector.AddSpot(inv2, new System.Drawing.Rectangle(live.X, live.Y, live.Width, live.Height));
                    }
                    bool? mixedOpen = detector.IsGemCutterOpen(System.Drawing.Rectangle.Empty);
                    Console.WriteLine("    Detector on live screen: matching spot=" + sameOpen + ", inverted spot=" + diffOpen + ", matching+different spot=" + mixedOpen);
                    if (sameOpen != true || diffOpen != (invDiff <= d.GemDetectThreshold) || mixedOpen != false)
                    { Console.WriteLine("FAILED: live spot detection (all spots must match)"); return false; }
                }
                detector.ClearSpots();
                if (detector.SpotCount != 0 || d.GemDetectSpots.Count != 0) { Console.WriteLine("FAILED: clear spots"); return false; }
            }
            if (!spotDirExisted && System.IO.Directory.Exists(spotDir) && System.IO.Directory.GetFiles(spotDir).Length == 0) System.IO.Directory.Delete(spotDir);
            Console.WriteLine("  PASSED: gem cutter screen-spot detection (store/limit/clear, overlay exclusion, live capture match).");
            using (var h1 = new HotkeyManager())
            using (var h2 = new HotkeyManager(9002))
            using (var h3 = new HotkeyManager(9003))
            {
                string e1, e2, e3;
                bool ok = h1.Register(7, (int)Keys.F11, out e1) && h2.Register(7, (int)Keys.F12, out e2) && h3.Register(7, (int)Keys.F10, out e3);
                if (!ok) { Console.WriteLine("FAILED: two independent global hotkeys"); return false; }
            }
            Console.WriteLine("  PASSED: in-game gem overlay (top-most, non-activating, missing/lower-level gems by tier) and separate overlay hotkey.");
            return true;
        }

        private static TreeNode TN(int id, string type, bool alloc, string name)
        {
            return new TreeNode { Id = id, Type = type, Alloc = alloc, Name = name, Stats = new System.Collections.Generic.List<string> { name + " stat" } };
        }

        private static bool TestPassivePlan(AppConfig config, string currentBuildPath)
        {
            // Synthetic tree: S - a - N1(Notable); S - b - c - K(Keystone); S - d - e (small only); X(Notable) isolated; Asc start - A1 - A2(asc notable)
            var guide = new BuildDetails { Success = true, Tree = new TreeData() };
            guide.Tree.Nodes.AddRange(new[] {
                TN(1, "ClassStart", true, "Start"), TN(2, "Normal", true, "a"), TN(3, "Notable", true, "N1"),
                TN(4, "Normal", true, "b"), TN(5, "Normal", true, "c"), TN(6, "Keystone", true, "K"),
                TN(7, "Normal", true, "d"), TN(8, "Normal", true, "e"), TN(9, "Notable", true, "X"), TN(10, "Notable", false, "Unused"),
                new TreeNode { Id = 20, Type = "AscendClassStart", Alloc = true, Ascendancy = true, Name = "AscStart" },
                new TreeNode { Id = 21, Type = "Normal", Alloc = true, Ascendancy = true, Name = "A1" },
                new TreeNode { Id = 22, Type = "Notable", Alloc = true, Ascendancy = true, Name = "A2" } });
            foreach (var e in new[] { new[] { 1, 2 }, new[] { 2, 3 }, new[] { 1, 4 }, new[] { 4, 5 }, new[] { 5, 6 }, new[] { 1, 7 }, new[] { 7, 8 }, new[] { 3, 10 }, new[] { 20, 21 }, new[] { 21, 22 } })
                guide.Tree.Edges.Add(new System.Collections.Generic.List<int>(e));
            var fresh = PassivePlan.Plan(guide, null);
            string order = string.Join(" | ", fresh.Targets.Select(t => (t.Target == null ? "small" : t.Target.Name) + ":" + string.Join(">", t.Path.Select(n => n.Name).ToArray())).ToArray());
            Console.WriteLine("    Synthetic plan from class start: " + order + "; unreachable: " + string.Join(",", fresh.Unreachable.Select(n => n.Name).ToArray()));
            if (order != "N1:a>N1 | K:b>c>K | small:d>e" || fresh.Unreachable.Count != 1 || fresh.Unreachable[0].Name != "X"
                || fresh.AscendancyTargets.Count != 1 || fresh.AscendancyTargets[0].Target.Name != "A2" || fresh.MissingCount != 8 || fresh.MissingAscendancyCount != 2 || fresh.GuidePoints != 8)
            { Console.WriteLine("FAILED: synthetic passive plan order"); return false; }

            var have = new BuildDetails { Success = true, Tree = new TreeData() };
            have.Tree.Nodes.AddRange(new[] { TN(1, "ClassStart", true, "Start"), TN(4, "Normal", true, "b"), TN(5, "Normal", true, "c"), TN(10, "Notable", true, "Unused") });
            var partial = PassivePlan.Plan(guide, have);
            if (partial.Targets.Count == 0 || partial.Targets[0].Target.Name != "K" || partial.Targets[0].Path.Count != 1 || partial.CurrentPoints != 3
                || partial.NotInGuide.Count != 1 || partial.NotInGuide[0].Name != "Unused")
            { Console.WriteLine("FAILED: synthetic plan from a partial Current tree"); return false; }
            Console.WriteLine("  PASSED: passive plan picks the nearest notable/keystone path, keeps ascendancy separate, flags unconnected and not-in-guide nodes.");

            // Real guide stage vs Current character
            var g = config.Clone();
            g.DisableSave = true;
            g.FollowGameStage = false;
            g.CurrentBuildFilePath = currentBuildPath;
            g.CurrentCharacterCode = null;
            string error, notice, currentError;
            BuildDetails current;
            var real = GuideBuildView.ReadGuide(g, true, out error, out notice, out current, out currentError);
            if (real == null || !real.Success || current == null) { Console.WriteLine("FAILED: guide/current for passive plan: " + error + " " + currentError); return false; }
            var plan = PassivePlan.Plan(real, current);
            var guideIds = new System.Collections.Generic.HashSet<int>(real.Tree.Nodes.Where(n => n.Alloc && !PassivePlan.IsStart(n)).Select(n => n.Id));
            var curIds = new System.Collections.Generic.HashSet<int>(current.Tree.Nodes.Where(n => n.Alloc).Select(n => n.Id));
            var expected = new System.Collections.Generic.HashSet<int>(guideIds.Where(id => !curIds.Contains(id)));
            var planned = plan.Targets.Concat(plan.AscendancyTargets).SelectMany(t => t.Path).Select(n => n.Id).Concat(plan.Unreachable.Select(n => n.Id)).ToList();
            if (planned.Count != expected.Count || !expected.SetEquals(planned)) { Console.WriteLine("FAILED: real passive plan does not cover exactly the missing guide nodes"); return false; }
            // Every planned node must connect to what is already allocated at that point.
            var adj = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<int>>();
            foreach (var e in real.Tree.Edges)
            {
                if (!adj.ContainsKey(e[0])) adj[e[0]] = new System.Collections.Generic.HashSet<int>();
                if (!adj.ContainsKey(e[1])) adj[e[1]] = new System.Collections.Generic.HashSet<int>();
                adj[e[0]].Add(e[1]); adj[e[1]].Add(e[0]);
            }
            var allocated = new System.Collections.Generic.HashSet<int>(curIds.Concat(real.Tree.Nodes.Where(n => n.Alloc && PassivePlan.IsStart(n)).Select(n => n.Id)));
            foreach (var t in plan.Targets.Concat(plan.AscendancyTargets))
                foreach (var n in t.Path)
                {
                    if (!adj.ContainsKey(n.Id) || !adj[n.Id].Any(allocated.Contains)) { Console.WriteLine("FAILED: planned node " + n.Name + " is not connected when allocated"); return false; }
                    allocated.Add(n.Id);
                }
            Console.WriteLine("    Guide " + (real.ActiveLoadout ?? "saved sets") + " vs Current: you " + plan.CurrentPoints + " pts, guide " + plan.GuidePoints + ", " + plan.MissingCount + " to go in " + plan.Targets.Count + " steps, ascendancy " + plan.MissingAscendancyCount + " to go, " + plan.NotInGuide.Count + " not in stage, " + plan.Unreachable.Count + " unconnected");
            var self = PassivePlan.Plan(real, real);
            if (self.MissingCount != 0 || self.MissingAscendancyCount != 0 || self.NotInGuide.Count != 0) { Console.WriteLine("FAILED: guide vs itself should need nothing"); return false; }

            g.PassiveOverlayX = g.PassiveOverlayY = -1;
            g.PassiveOverlayShowAll = false;
            using (var overlay = new PassiveOverlay(g))
            {
                overlay.ShowOverlay();
                Application.DoEvents();
                if (!overlay.Visible || !overlay.TitleText.Contains("(" + plan.MissingCount + " to go)") || overlay.PlanResult == null)
                { Console.WriteLine("FAILED: passive overlay title: " + overlay.TitleText); return false; }
                if (plan.Targets.Count > 0 && plan.Targets[0].Target != null && !overlay.BodyText.Contains("Next: " + plan.Targets[0].Target.Name))
                { Console.WriteLine("FAILED: passive overlay first target: " + overlay.BodyText); return false; }
                Console.WriteLine("    Overlay: " + overlay.TitleText);
                foreach (var line in overlay.BodyText.Split('\n').Take(8)) Console.WriteLine("      | " + line.TrimEnd('\r'));
                string shot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "poe2companion-passiveoverlay.png");
                using (var bmp = new System.Drawing.Bitmap(overlay.Width, overlay.Height))
                {
                    overlay.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, overlay.Width, overlay.Height));
                    bmp.Save(shot);
                }
                Console.WriteLine("    Overlay snapshot: " + shot);
                int shortLines = overlay.BodyText.Split('\n').Length;
                g.PassiveOverlayShowAll = true;
                overlay.Reload(false);
                if (plan.Targets.Count > PassiveOverlay.DefaultTargets && overlay.BodyText.Split('\n').Length <= shortLines) { Console.WriteLine("FAILED: passive overlay 'all' mode"); return false; }
                overlay.Toggle();
                if (overlay.Visible) { Console.WriteLine("FAILED: passive overlay toggle"); return false; }
            }

            // Settings replace the config object: the overlay feature must use the new config (spots, auto-show).
            var c1 = g.Clone(); c1.TreeDetectSpots = new System.Collections.Generic.List<string>();
            var c2 = g.Clone(); c2.TreeDetectSpots = new System.Collections.Generic.List<string> { "0|0|10|10|missing-file.png" }; c2.PassiveAutoShow = false;
            using (var feature = new OverlayFeature { Title = "Passive overlay", SpotPrefix = "tree-", Create = c => new PassiveOverlay(c), Spots = c => c.TreeDetectSpots,
                GetAutoShow = c => c.PassiveAutoShow, SetAutoShow = (c, v) => c.PassiveAutoShow = v,
                ToggleModifiers = c => 7, ToggleKey = c => (int)Keys.F9, MarkModifiers = c => 7, MarkKey = c => (int)Keys.F9 })
            {
                feature.Start(c1, 9004, 9005, null, () => { });
                if (!feature.AutoShow) { Console.WriteLine("FAILED: feature auto-show"); return false; }
                feature.SetConfig(c2);
                if (feature.AutoShow) { Console.WriteLine("FAILED: feature not rebound to new config"); return false; }
                var taken = new System.Collections.Generic.HashSet<int>();
                feature.RegisterHotkeys(taken);
                if (taken.Count != 1) { Console.WriteLine("FAILED: duplicate overlay hotkey should be skipped"); return false; }
            }
            Console.WriteLine("  PASSED: passive overlay (guide stage vs Current tree, connected allocation order, 'all' mode, toggle, settings rebinding, duplicate hotkeys skipped).");
            return true;
        }

        private static bool TestItemStatRatings()
        {
            var guide = new System.Collections.Generic.List<ItemAffix> { TestAffix("+100 to maximum Life"), TestAffix("+40% to Fire Resistance") };
            var candidate = new System.Collections.Generic.List<ItemAffix>
            {
                TestAffix("+89.9 to maximum Life"), TestAffix("+35% to Lightning Resistance"),
                TestAffix("+40% to Fire Resistance")
            };
            var rows = ItemStatRatings.Rate(candidate, guide);
            if (rows.Count != 3 || rows[0].Rating != "Orange" || rows[1].Rating != "Red" || rows[2].Rating != "Green")
            { Console.WriteLine("FAILED: orange/red/green item stats"); return false; }
            foreach (double value in new[] { 90.0, 99.9, 100.0, 100.1, 150.0 })
            {
                var rating = ItemStatRatings.Rate(new System.Collections.Generic.List<ItemAffix>
                    { TestAffix("+" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " to maximum Life") }, guide);
                if (rating[0].Rating != (value > 100 ? "Gold" : "Green"))
                { Console.WriteLine("FAILED: 90/100% item stat boundary: " + value); return false; }
            }
            if (ItemStatRatings.Rate(candidate, null).Exists(r => r.Rating != "Unrated"))
            { Console.WriteLine("FAILED: missing guide received colored ratings"); return false; }
            var allRes = new System.Collections.Generic.List<ItemAffix> { TestAffix("+40% to all Elemental Resistances") };
            var fireRes = new System.Collections.Generic.List<ItemAffix> { TestAffix("+36% to Fire Resistance") };
            var elementalRes = new System.Collections.Generic.List<ItemAffix> { TestAffix("+35% to Fire Resistance"), TestAffix("+40% to Cold Resistance"), TestAffix("+45% to Lightning Resistance") };
            if (ItemStatRatings.Rate(fireRes, allRes)[0].Rating != "Green" ||
                ItemStatRatings.Rate(allRes, elementalRes)[0].Rating != "Orange" ||
                ItemStatRatings.Rate(allRes, fireRes)[0].Rating != "Unrated")
            { Console.WriteLine("FAILED: combined and individual resistance matching"); return false; }
            var duplicates = new System.Collections.Generic.List<ItemAffix> { TestAffix("+20% to Fire Resistance"), TestAffix("+10% to Fire Resistance") };
            if (ItemStatRatings.Rate(duplicates, guide).Exists(r => r.Rating != "Unrated") ||
                ItemStatRatings.Rate(candidate, duplicates)[2].Rating != "Unrated")
            { Console.WriteLine("FAILED: repeated modifier was guessed"); return false; }
            var conditional = new System.Collections.Generic.List<ItemAffix> { TestAffix("20% increased Damage while on Low Life") };
            var conditionalGoal = new System.Collections.Generic.List<ItemAffix> { TestAffix("10% increased Damage while on Low Life") };
            if (ItemStatRatings.Rate(conditional, conditionalGoal)[0].Rating != "Unrated")
            { Console.WriteLine("FAILED: ambiguous conditional modifier was rated"); return false; }
            var unsupported = TestAffix("+50 to maximum Life");
            unsupported.Understood = false;
            if (ItemStatRatings.Rate(new System.Collections.Generic.List<ItemAffix> { unsupported }, guide)[0].Rating != "Unrated")
            { Console.WriteLine("FAILED: unsupported modifier received a rating"); return false; }
            using (var view = ItemStatRatings.CreateView(new SlotComparison { GuideItemName = "Test guide boots", ItemStats = rows }, 324, 10))
            {
                view.CreateControl();
                view.PerformLayout();
                if (view.PreferredSize.Height < 80 || view.PreferredSize.Width > 324)
                { Console.WriteLine("FAILED: item stat view dimensions"); return false; }
                int stars = 0;
                foreach (Control row in view.Controls)
                    foreach (Control label in row.Controls)
                        if (label.Text == "*" && !string.IsNullOrEmpty(label.AccessibleName)) stars++;
                if (stars != 3) { Console.WriteLine("FAILED: item stat asterisk rendering"); return false; }
            }
            Console.WriteLine("  PASSED: item stat asterisks, red/orange/green/gold, 90/100% boundaries, unavailable/ambiguous stats and compact layout.");
            return true;
        }

        private static int RunCliExport(string[] args)
        {
            if (args.Length < 3 || !File.Exists(args[1]))
            {
                Console.WriteLine("Usage: --export <itemFile> <slotName> [outFile]");
                return 1;
            }
            string itemText = File.ReadAllText(args[1]);
            string slot = args[2];
            var config = LoadConfig();
            using (var engine = new PobLuaEngine())
            {
                string err;
                if (!engine.Initialize(config.PobPath, AppConfig.GetWrapperScriptPath(), out err)) { Console.WriteLine("Error initializing engine: " + err); return 1; }
                var bi = config.LoadConfiguredBuild(engine, out err);
                if (bi == null || !bi.Success) { Console.WriteLine("Error loading build: " + err); return 1; }
                var exp = engine.ExportWithCandidate(itemText, slot, out err);
                if (exp == null || !exp.Success) { Console.WriteLine("Export failed: " + err); return 1; }
                string outPath = args.Length > 3 ? args[3]
                    : Path.Combine(AppConfig.GetExportFolder(), PobLuaEngine.SuggestExportFileName(bi.CharacterName, exp.ItemName, exp.SlotLabel));
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string source = engine.CurrentBuildPath;
                string written = PobLuaEngine.WriteNewBuildFile(exp.Xml, outPath, source, out err);
                if (written == null) { Console.WriteLine("Write failed: " + err); return 1; }
                Console.WriteLine("Wrote " + written);
                Console.WriteLine("  " + exp.ItemName + " -> " + exp.SlotLabel + " (replaced " + exp.ReplacedItemName + "); validated=" + exp.Validated
                    + " L" + exp.ValidatedLevel + " Life " + exp.ValidatedLife.ToString("F0") + " DPS " + exp.ValidatedTotalDPS.ToString("F1"));
                return 0;
            }
        }

        private static int RunCliComparison(string[] args)
        {
            string itemText = null;
            if (args.Length > 1)
            {
                string first = args[1].Trim('"', ' ');
                if (File.Exists(first))
                {
                    itemText = File.ReadAllText(first);
                }
                else
                {
                    string combined = string.Join(" ", args, 1, args.Length - 1).Trim('"', ' ');
                    if (File.Exists(combined))
                    {
                        itemText = File.ReadAllText(combined);
                    }
                    else
                    {
                        itemText = combined;
                    }
                }
            }
            
            if (string.IsNullOrEmpty(itemText))
            {
                itemText = InputSimulator.GetClipboardTextSafe();
            }

            if (string.IsNullOrEmpty(itemText))
            {
                Console.WriteLine("Error: No item text provided or found on clipboard.");
                return 1;
            }

            var config = LoadConfig();
            string scriptPath = AppConfig.GetWrapperScriptPath();

            using (var engine = new PobLuaEngine())
            {
                string err;
                if (!engine.Initialize(config.PobPath, scriptPath, out err))
                {
                    Console.WriteLine("Error initializing engine: " + err);
                    return 1;
                }

                var buildInfo = config.LoadConfiguredBuild(engine, out err);
                if (buildInfo == null || !buildInfo.Success)
                {
                    Console.WriteLine("Error loading build: " + err);
                    return 1;
                }

                var cmp = GuideComparison.Compare(config, engine, itemText, out err);
                if (cmp == null || !cmp.Success)
                {
                    Console.WriteLine("Error comparing item: " + err);
                    return 1;
                }

                var json = new JavaScriptSerializer();
                Console.WriteLine(json.Serialize(cmp));
                return 0;
            }
        }
    }
}
