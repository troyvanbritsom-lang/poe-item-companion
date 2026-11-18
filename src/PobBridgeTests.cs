using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Poe2ItemCompanion
{
    internal static class PobBridgeTests
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibrary(string path);
        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr luaL_newstate();
        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void luaL_openlibs(IntPtr state);
        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int luaL_loadstring(IntPtr state, string code);
        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int lua_pcall(IntPtr state, int args, int results, int handler);
        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr lua_tolstring(IntPtr state, int index, out UIntPtr size);
        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void lua_close(IntPtr state);
        [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void lua_settop(IntPtr state, int top);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string link, string target, IntPtr security);

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            catch (InvalidDataException) { return; }
            catch (IOException) { return; }
            catch (TimeoutException) { return; }
            throw new Exception(message);
        }

        private static string LuaString(string text) { return "[=[" + text + "]=]"; }

        public static int Run(string poe1Build = null, string poe2Build = null, bool fixturesOnly = false)
        {
            string directory = Path.Combine(Path.GetTempPath(), "CompanionBridgeTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string originalDirectory = Directory.GetCurrentDirectory();
            try
            {
                string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
                TestProtocol(directory);
                TestInstaller(directory);
                if (fixturesOnly) return 0;
                string pob1 = GameIdentity.DefaultPobPath(GameId.Poe1);
                string pob2 = GameIdentity.DefaultPobPath(GameId.Poe2);
                SetDllDirectory(pob1);
                Require(LoadLibrary(Path.Combine(pob1, "lua51.dll")) != IntPtr.Zero, "Could not load installed LuaJIT.");
                IntPtr state = luaL_newstate();
                Require(state != IntPtr.Zero, "Could not create test Lua state.");
                try
                {
                    luaL_openlibs(state);
                    string code = "package.path = " + LuaString(pob1 + "\\lua\\?.lua;") + " .. package.path;"
                        + " package.cpath = " + LuaString(pob1 + "\\?.dll;") + " .. package.cpath;"
                        + " local run = assert(loadfile(" + LuaString(Path.Combine(root, "tests", "pob_bridge_tests.lua")) + "))();"
                        + " local count = run(" + LuaString(root) + "," + LuaString(pob1) + "," + LuaString(pob2) + "," + LuaString(directory) + ");"
                        + " return tostring(count)";
                    int status = luaL_loadstring(state, code);
                    if (status == 0) status = lua_pcall(state, 0, 1, 0);
                    UIntPtr size;
                    string result = Marshal.PtrToStringAnsi(lua_tolstring(state, -1, out size));
                    Require(status == 0, "Lua bridge regression failed: " + result);
                    Console.WriteLine("PASSED: " + result + " Lua assertions covering both games, UI-thread sequencing, loopback binding, authentication, invalid commands, build guards, option preservation, async errors, timeout/disconnect and fragmented I/O.");
                }
                finally { lua_close(state); }
                TestNativeTransport(root, directory);
                Directory.SetCurrentDirectory(originalDirectory);
                if (!string.IsNullOrEmpty(poe1Build)) TestExistingBuild(GameId.Poe1, poe1Build);
                if (!string.IsNullOrEmpty(poe2Build)) TestExistingBuild(GameId.Poe2, poe2Build);
                Console.WriteLine("PASSED: companion protocol validation, both game ports, matching-file checks and mock TCP completion/error handling.");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("FAILED BRIDGE: " + ex); return 1; }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
                foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)) File.Delete(file);
                foreach (string folder in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(path => path.Length)) Directory.Delete(folder);
                Directory.Delete(directory);
            }
        }

        private static string RunLua(IntPtr state, string code)
        {
            lua_settop(state, 0);
            int status = luaL_loadstring(state, code);
            if (status == 0) status = lua_pcall(state, 0, 1, 0);
            UIntPtr size;
            string result = Marshal.PtrToStringAnsi(lua_tolstring(state, -1, out size));
            Require(status == 0, "Native socket fixture failed: " + result);
            return result;
        }

        public static int RunRepairFixtures()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Companion Bridge Repair-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { TestInstaller(directory); return 0; }
            catch (Exception ex) { Console.WriteLine("FAILED BRIDGE REPAIR: " + ex); return 1; }
            finally { Directory.Delete(directory, true); }
        }

        private static string MakeInstall(string directory, string name, GameId game)
        {
            string path = Path.Combine(directory, name);
            string modules = Path.Combine(path, "Modules");
            Directory.CreateDirectory(modules);
            Directory.CreateDirectory(Path.Combine(path, "lua"));
            File.WriteAllText(Path.Combine(modules, "Main.lua"), "LoadModule(\"GameVersions\")\nfunction main:Shutdown()\n\tself:CallMode(\"Shutdown\")\nend\n"
                + "function main:OnFrame()\n\tself.viewPort = { x = 0, y = 0, width = self.screenW, height = self.screenH }\nend\n");
            File.WriteAllText(Path.Combine(modules, "Build.lua"), "dbXML[1].elem ~= \"" + (game == GameId.Poe1 ? "PathOfBuilding" : "PathOfBuilding2")
                + "\"\n\tfile:write(xmlText)\n\tfile:close()\n");
            File.WriteAllText(Path.Combine(modules, "Common.lua"), "active_coroutines[co] = true\nfunction coroutine._list()\nend\n");
            File.WriteAllText(Path.Combine(path, "lua", "sha2.lua"), "-- fixture module");
            File.WriteAllText(Path.Combine(path, "test build.xml"), "untouched build fixture");
            return path;
        }

        private static void RefuseRepair(GameId game, string path, string expected)
        {
            try { PobBridgeRepair.RunAsync(game, path).GetAwaiter().GetResult(); }
            catch (IOException ex)
            {
                Require(ex.Message.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0,
                    "Expected rejection for " + expected + ", got: " + ex.Message);
                return;
            }
            throw new InvalidOperationException("Repair accepted a fixture that should fail: " + expected);
        }

        private static void TestInstaller(string directory)
        {
            string[] paths = { MakeInstall(directory, "PoB 1 [offline] & fixture's", GameId.Poe1),
                MakeInstall(directory, "PoB 2 (offline)", GameId.Poe2) };
            for (int game = 0; game < paths.Length; game++)
            {
                var gameId = game == 0 ? GameId.Poe1 : GameId.Poe2;
                string modules = Path.Combine(paths[game], "Modules");
                string mainPath = Path.Combine(modules, "Main.lua");
                string keyPath = Path.Combine(modules, "CompanionBridge.key");
                string buildPath = Path.Combine(modules, "Build.lua");
                string originalMain = File.ReadAllText(mainPath);
                string originalBuild = File.ReadAllText(buildPath);
                string otherModules = Path.Combine(paths[1 - game], "Modules");
                string[] otherFiles = Directory.GetFiles(otherModules).OrderBy(p => p).ToArray();
                string otherMain = File.ReadAllText(Path.Combine(otherModules, "Main.lua"));
                string otherBuild = File.ReadAllText(Path.Combine(otherModules, "Build.lua"));
                RefuseRepair(game == 0 ? GameId.Poe2 : GameId.Poe1, paths[game], "Wrong game");
                File.WriteAllText(mainPath, originalMain.Replace("LoadModule(\"GameVersions\")", "-- unexpected new loader"));
                RefuseRepair(gameId, paths[game], "Unsupported PoB source");
                File.WriteAllText(mainPath, originalMain);
                using (File.Open(buildPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    RefuseRepair(gameId, paths[game], "locked or not writable");
                Require(Directory.GetFiles(modules).Length == 3 && File.ReadAllText(mainPath) == originalMain
                    && File.ReadAllText(buildPath) == originalBuild, "Rejected repair wrote to the target.");
                if (game == 0)
                {
                    string launcher = Path.Combine(paths[game], "fixture launcher.exe");
                    File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), launcher);
                    try
                    {
                        using (var running = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(launcher, "-n 6 127.0.0.1")
                        { UseShellExecute = false, CreateNoWindow = true }))
                        {
                            try { RefuseRepair(gameId, paths[game], "Close the running PoB"); }
                            finally { running.WaitForExit(); }
                        }
                    }
                    finally { File.Delete(launcher); }
                    Require(Directory.GetFiles(modules).Length == 3, "Running-instance refusal wrote to the target.");
                }
                PobBridgeRepair.RunAsync(gameId, paths[game] + "\\").GetAwaiter().GetResult();
                Require(File.GetAccessControl(keyPath).AreAccessRulesProtected, "New fixture credential inherited folder access.");
                var keyWriteTime = File.GetLastWriteTimeUtc(keyPath);
                var keyAcl = File.GetAccessControl(keyPath).GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.All);
                File.WriteAllText(mainPath, File.ReadAllText(mainPath).Replace("\tcompanionBridge:Shutdown()\n", ""));
                PobBridgeRepair.RunAsync(gameId, paths[game]).GetAwaiter().GetResult();
                {
                    string main = File.ReadAllText(mainPath);
                    Require(main.Split(new[] { "companionBridge:Shutdown()" }, StringSplitOptions.None).Length == 2
                        && main.Contains("function main:Shutdown()\n\tcompanionBridge:Shutdown()"),
                        "Installer missed or duplicated the shutdown hook.");
                    Require(main.Split(new[] { "companionBridge:Tick(self)" }, StringSplitOptions.None).Length == 2,
                        "Installer duplicated the frame hook.");
                    Require(main.Contains("LoadModule(\"Modules/CompanionBridge\", \"" + (game == 0 ? "poe1" : "poe2") + "\")"),
                        "Installer selected the wrong game hook.");
                    Require(File.ReadAllText(Path.Combine(modules, "CompanionBridge.lua")) ==
                        File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", "pob_import_bridge.lua")),
                        "Wrong module installed in fixture.");
                    Require(File.GetLastWriteTimeUtc(keyPath) == keyWriteTime && File.GetAccessControl(keyPath)
                        .GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.All) == keyAcl,
                        "Fixture installer changed the existing credential metadata.");
                    Require(Directory.GetFiles(modules, "Main.lua.companion-backup-*").Any(p => File.ReadAllText(p) == originalMain)
                        && Directory.GetFiles(modules, "Build.lua.companion-backup-*").Any(p => File.ReadAllText(p) == originalBuild)
                        && Directory.GetFiles(modules, "CompanionBridge.lua.companion-backup-*").Length == 1,
                        "Missing pre-write source backups.");
                }
                Require(Directory.GetFiles(modules, "*.companion-stage-*").Length == 0, "Staging files were left behind.");
                Require(File.ReadAllText(Path.Combine(paths[game], "test build.xml")) == "untouched build fixture", "Repair touched a build.");
                Require(otherFiles.SequenceEqual(Directory.GetFiles(otherModules).OrderBy(p => p))
                    && File.ReadAllText(Path.Combine(otherModules, "Main.lua")) == otherMain
                    && File.ReadAllText(Path.Combine(otherModules, "Build.lua")) == otherBuild,
                    "Targeted repair touched the other game.");
            }
            TestRepairRollback(MakeInstall(directory, "PoB 2 rollback fixture", GameId.Poe2));
            Console.WriteLine("PASSED: isolated repairs for both games; spaced/special-character paths; wrong-game, unsupported, locked and running-instance refusals; late-failure rollback, backups, credential metadata, reapply, and no unrelated writes.");
        }

        private static void TestRepairRollback(string path)
        {
            string modules = Path.Combine(path, "Modules");
            string mainPath = Path.Combine(modules, "Main.lua");
            string buildPath = Path.Combine(modules, "Build.lua");
            var originalMain = File.ReadAllBytes(mainPath);
            var originalBuild = File.ReadAllBytes(buildPath);
            var locked = new TaskCompletionSource<FileStream>();
            using (var watcher = new FileSystemWatcher(modules, "Main.lua.companion-backup-*"))
            {
                watcher.Created += (s, e) =>
                {
                    // Allow backup and integrity reads, but deny replacement after the module and Build.lua are applied.
                    try
                    {
                        var stream = File.Open(mainPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (!locked.TrySetResult(stream)) stream.Dispose();
                    }
                    catch (IOException ex) { locked.TrySetException(ex); }
                };
                watcher.EnableRaisingEvents = true;
                try
                {
                    RefuseRepair(GameId.Poe2, path, "no bridge changes remain");
                    Require(locked.Task.Wait(3000), "Rollback fixture never acquired its late lock.");
                    Require(File.ReadAllBytes(mainPath).SequenceEqual(originalMain)
                        && File.ReadAllBytes(buildPath).SequenceEqual(originalBuild)
                        && !File.Exists(Path.Combine(modules, "CompanionBridge.lua"))
                        && !File.Exists(Path.Combine(modules, "CompanionBridge.key"))
                        && Directory.GetFiles(modules, "*.companion-stage-*").Length == 0,
                        "Rollback did not restore original sources or remove files added during repair.");
                }
                finally
                {
                    watcher.EnableRaisingEvents = false;
                    if (locked.Task.Status == TaskStatus.RanToCompletion) locked.Task.Result.Dispose();
                }
            }
        }

        private static void TestNativeTransport(string root, string directory)
        {
            foreach (GameId game in new[] { GameId.Poe1, GameId.Poe2 })
            {
                string pob = GameIdentity.DefaultPobPath(game);
                IntPtr state = luaL_newstate();
                Require(state != IntPtr.Zero, "Could not create native socket fixture state.");
                try
                {
                    luaL_openlibs(state);
                    string token = File.ReadAllText(Path.Combine(directory, "Modules", "CompanionBridge.key"));
                    string file = Path.Combine(directory, "current.xml");
                    File.WriteAllText(file, "<" + GameIdentity.Root(game) + "><Build/></" + GameIdentity.Root(game) + ">");
                    File.WriteAllText(Path.Combine(directory, "Modules", "Build.lua"), "dbXML[1].elem ~= \"" + GameIdentity.Root(game) + "\"");
                    string setup = "package.path = " + LuaString(pob + "\\lua\\?.lua;") + " .. package.path;"
                        + "package.cpath = " + LuaString(pob + "\\?.dll;") + " .. package.cpath;"
                        + "local socket = require('socket'); local realOpen = io.open;"
                        + "local commonFile=assert(realOpen(" + LuaString(Path.Combine(pob, "Modules", "Common.lua")) + ",'r'));"
                        + "local text=commonFile:read('*a'); commonFile:close();"
                        + "local first=assert(text:find('-- Optimize coroutines',1,true));"
                        + "local last=assert(text:find('-- Class library',first,true));"
                        + "assert(loadstring(text:sub(first,last-1)))();"
                        + "local ffi=require('ffi'); local realLoad=ffi.load;"
                        + "ffi.load=function(name,...) if name=='kernel32' then return {GetCurrentProcessId=function() return 101 end} end;"
                        + "if name=='user32' then return {GetForegroundWindow=function() return 202 end,"
                        + "GetWindowThreadProcessId=function(window,owner) owner[0]=window; return 1 end,"
                        + "GetCursorPos=function() return 1 end,WindowFromPoint=function() return 202 end} end;"
                        + "return realLoad(name,...) end;"
                        + "io.open = function(path, mode) if path:match('CompanionBridge.key$') then return {"
                        + "read=function() return " + LuaString(token) + " end, close=function() end} end return realOpen(path,mode) end;"
                        + "GetScriptPath=function() return " + LuaString(pob) + " end; ConPrintf=function() end;"
                        + "package.loaded.socket={gettime=socket.gettime,sleep=socket.sleep,tcp4=function() testListener=assert(socket.tcp4()); return {"
                        + "settimeout=function(_,n) return testListener:settimeout(n) end,"
                        + "bind=function(_,host) return testListener:bind(host,0) end,"
                        + "listen=function(_,n) return testListener:listen(n) end,"
                        + "accept=function() return testListener:accept() end,"
                        + "close=function() return testListener:close() end} end};"
                        + "testMain={mode='LIST',modes={},popups={}};"
                        + "testBridge=assert(loadfile(" + LuaString(Path.Combine(root, "scripts", "pob_import_bridge.lua"))
                        + "))('" + (game == GameId.Poe1 ? "poe1" : "poe2") + "');"
                        + "ffi.load=realLoad;"
                        + "testFrame=function() local active=false; for thread in pairs(coroutine._list()) do "
                        + "if coroutine.status(thread)=='suspended' then active=true end end;"
                        + "if not active then return 'skipped' end; testBridge:Tick(testMain); return 'serviced' end;"
                        + "assert(testFrame()=='skipped','Inactive fixture gate did not reproduce pre-wake suspension');"
                        + "testBridge:Tick(testMain); local _,port=testListener:getsockname(); return tostring(port)";
                    int port = int.Parse(RunLua(state, setup));
                    var cadence = System.Diagnostics.Stopwatch.StartNew();
                    double cpuBefore = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
                    for (int frame = 0; frame < 10; frame++)
                        Require(RunLua(state, "return testFrame()") == "serviced", "Native coroutine gate suspended background servicing.");
                    cadence.Stop();
                    double cpuDelta = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds - cpuBefore;
                    Require(cadence.ElapsedMilliseconds >= 900 && cadence.ElapsedMilliseconds < 4000 && cpuDelta < 400,
                        "Background fixture failed its 100ms idle cadence or CPU budget.");
                    using (var idle = new TcpClient(AddressFamily.InterNetwork))
                    {
                        idle.Connect(IPAddress.Loopback, port);
                        RunLua(state, "return testFrame()");
                        var timelyProbe = PobImportBridge.ProbeAsync(game, directory, port);
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        while (!timelyProbe.IsCompleted && watch.ElapsedMilliseconds < 2000)
                        {
                            Require(RunLua(state, "return testFrame()") == "serviced", "Background gate lost idle-peer wake.");
                            System.Threading.Thread.Sleep(5);
                        }
                        Require(timelyProbe.IsCompleted, "Idle peer delayed the C# authenticated probe.");
                        timelyProbe.GetAwaiter().GetResult();
                    }
                    var probe = PobImportBridge.ProbeAsync(game, directory, port);
                    DateTime probeDeadline = DateTime.UtcNow.AddSeconds(10);
                    while (!probe.IsCompleted && DateTime.UtcNow < probeDeadline)
                    {
                        Require(RunLua(state, "return testFrame()") == "serviced", "Background gate lost handshake wake.");
                        System.Threading.Thread.Sleep(5);
                    }
                    Require(probe.IsCompleted, "Read-only native handshake did not finish.");
                    probe.GetAwaiter().GetResult();
                    Require(RunLua(state, "testBridge:Tick(testMain); return tostring(testBridge:IsBusy())") == "false",
                        "Read-only probe started a build import.");
                    var config = new AppConfig { Game = game, PobPath = directory, CurrentBuildFilePath = file };
                    var import = PobImportBridge.ImportAtPortAsync(config, new Progress<string>(), port);
                    DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!import.IsCompleted && DateTime.UtcNow < deadline)
                    {
                        Require(RunLua(state, "return testFrame()") == "serviced", "Background gate lost authenticated request wake.");
                        System.Threading.Thread.Sleep(5);
                    }
                    Require(import.IsCompleted, "Native transport handshake did not finish.");
                    try { import.GetAwaiter().GetResult(); throw new Exception("Build-less native fixture accepted import."); }
                    catch (InvalidOperationException ex)
                    {
                        Require(ex.Message == PobImportBridge.ErrorText("BUILD"),
                            "C# to native LuaSocket authenticated exchange failed: " + ex.Message);
                    }
                    Require(RunLua(state, "testBridge:Shutdown(); testBridge:Shutdown(); testBridge:Tick(testMain); return testFrame()") == "skipped",
                        "Shutdown left a yielded wake or resurrected the listener.");
                    using (var peer = new TcpClient(AddressFamily.InterNetwork))
                    {
                        try { peer.Connect(IPAddress.Loopback, port); throw new Exception("Shutdown left listener open."); }
                        catch (SocketException) { }
                    }
                    Console.WriteLine("PASSED: " + GameIdentity.Name(game) + " background fixture: 10 frames in "
                        + cadence.ElapsedMilliseconds + " ms, " + cpuDelta + " ms process CPU; shutdown restored native idle gate.");
                }
                finally { lua_close(state); }
            }
            Console.WriteLine("PASSED: actual C# to LuaSocket authenticated exchange for both games behind the native coroutine/background gate predicate, including idle-peer isolation, build-less fixture refusal and no character API calls.");
        }

        private static void TestExistingBuild(GameId game, string path)
        {
            byte[] before = File.ReadAllBytes(path);
            using (var engine = new PobLuaEngine())
            {
                string error;
                Require(engine.Initialize(GameIdentity.DefaultPobPath(game), AppConfig.GetWrapperScriptPath(), out error, game), error);
                var config = new AppConfig { Game = game, PobPath = GameIdentity.DefaultPobPath(game),
                    CurrentBuildFilePath = path, DisableSave = true };
                var info = config.LoadConfiguredBuild(engine, out error);
                Require(info != null && info.Success, "Existing Current load failed: " + error);
                var reloaded = config.LoadConfiguredBuild(engine, out error);
                Require(reloaded != null && reloaded.Success && reloaded.Level == info.Level,
                    "Existing Current reload failed: " + error);
                Require(Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(path)), "Existing build was modified.");
                Console.WriteLine("PASSED: " + GameIdentity.Name(game) + " existing CURRENT load/reload, original file unchanged.");
            }
        }

        private static void TestProtocol(string directory)
        {
            string token = PobImportBridge.NewNonce();
            Require(token.Length == 64 && token != PobImportBridge.NewNonce(), "Credential entropy/shape wrong.");
            string file = Path.Combine(directory, "current.xml");
            var config = new AppConfig { Game = GameId.Poe1, CurrentBuildFilePath = file, PobPath = directory };
            File.WriteAllText(file, "<PathOfBuilding><Build/></PathOfBuilding>");
            Require(PobImportBridge.Request(config, token, token, token).Contains(" " + PobImportBridge.Hex(Encoding.UTF8.GetBytes(file)) + " "),
                "Path not encoded in bounded request.");
            Require(PobImportBridge.Mac("key", "The quick brown fox jumps over the lazy dog") ==
                "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8",
                "HMAC-SHA256 known vector failed.");
            config.CurrentCharacterCode = "code";
            Reject(() => PobImportBridge.Request(config, token, token, token), "Character-code source accepted.");
            config.CurrentCharacterCode = null;
            config.Game = GameId.Poe2;
            Reject(() => PobImportBridge.Request(config, token, token, token), "Wrong-game file accepted.");
            Reject(() => PobImportBridge.ReadLine(new MemoryStream(Encoding.ASCII.GetBytes(new string('x', 258) + "\n")), 256),
                "Oversized response accepted.");
            Reject(() => PobImportBridge.ReadLine(new MemoryStream(Encoding.ASCII.GetBytes("PIC1")), 256), "Truncated response accepted.");
            Reject(() => PobImportBridge.ReadLine(new MemoryStream(new byte[] { 0, 10 }), 256), "Control byte accepted.");
            Reject(() => PobImportBridge.ReadLine(new MemoryStream(new byte[] { 10 }), 256, DateTime.UtcNow.AddSeconds(-1)),
                "Expired deadline accepted.");
            Require(!PobImportBridge.EqualMac(new string('a', 64), new string('b', 64)), "Incorrect HMAC accepted.");
            var beforeRequest = new PobBridgeConnectionException("connecting", false,
                new SocketException((int)SocketError.ConnectionRefused));
            Require(!beforeRequest.ImportMayHaveBeenSent && beforeRequest.Message.Contains("No import command was sent")
                && beforeRequest.Message.Contains("ConnectionRefused"), "Pre-import socket failure lost phase or error code.");
            var afterRequest = new PobBridgeConnectionException("waiting for import/save completion", true,
                new IOException("private payload", new SocketException((int)SocketError.ConnectionReset)));
            Require(afterRequest.ImportMayHaveBeenSent && afterRequest.Message.Contains("completion is unconfirmed")
                && afterRequest.Message.Contains("ConnectionReset") && !afterRequest.Message.Contains("private payload"),
                "Post-import socket failure misreported completion or exposed inner payload.");
            Directory.CreateDirectory(Path.Combine(directory, "Modules"));
            File.WriteAllText(Path.Combine(directory, "Modules", "CompanionBridge.key"), token);
            foreach (GameId game in new[] { GameId.Poe1, GameId.Poe2 })
            {
                config.Game = game;
                File.WriteAllText(file, "<" + GameIdentity.Root(game) + "><Build/></" + GameIdentity.Root(game) + ">");
                File.WriteAllText(Path.Combine(directory, "Modules", "Build.lua"), "dbXML[1].elem ~= \"" + GameIdentity.Root(game) + "\"");
                config.BuildSource = AppConfig.SourceFile;
                config.BuildFilePath = Path.Combine(directory, ".", "CURRENT.XML");
                Reject(() => PobImportBridge.Request(config, token, token, token), "Same-file GUIDE overwrite accepted.");
                var blocked = PobImportBridge.ImportAtPortAsync(config, new Progress<string>(), 1);
                config.BuildFilePath = null;
                try { blocked.GetAwaiter().GetResult(); throw new Exception("GUIDE snapshot protection was lost."); }
                catch (InvalidOperationException ex) { Require(ex.Message.Contains("same file"), "GUIDE refused after networking or lost snapshot."); }
                string alias = Path.Combine(directory, "guide-alias.xml");
                Require(CreateHardLink(alias, file, IntPtr.Zero), "Could not create same-file hardlink fixture.");
                try
                {
                    config.BuildFilePath = alias;
                    Reject(() => PobImportBridge.Request(config, token, token, token), "Hardlinked GUIDE overwrite accepted.");
                }
                finally { File.Delete(alias); }
                string separate = Path.Combine(directory, "separate-guide.xml");
                File.Copy(file, separate, true);
                config.BuildFilePath = separate;
                PobImportBridge.Request(config, token, token, token);
                config.BuildSource = AppConfig.SourceShareCode;
                config.BuildFilePath = file;
                PobImportBridge.Request(config, token, token, token);
                config.BuildSource = AppConfig.SourceGggBuild;
                config.GggBuildFilePath = file;
                Reject(() => PobImportBridge.Request(config, token, token, token), "Active GGG guide overwrite accepted.");
                config.GggBuildFilePath = null;
                config.BuildFilePath = null;
                config.BuildSource = AppConfig.SourceFile;
                foreach (string response in new[] { "STATUS DONE", "ERROR BUILD", "STATUS INVALID", "SPOOF", "BADSTATUS",
                    "BEFORESEND", "AFTERSEND" })
                {
                    var listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    try
                    {
                        var server = Task.Run(() =>
                        {
                            using (var peer = listener.AcceptTcpClient())
                            using (var stream = peer.GetStream())
                            {
                                string[] fields = PobImportBridge.ReadLine(stream, 8192).Split(' ');
                                string gameName = game == GameId.Poe1 ? "poe1" : "poe2";
                                Require(fields.Length == 4 && fields[1] == "HELLO" && fields[2] == gameName, "Wrong hello/game sent.");
                                if (response == "BEFORESEND") return;
                                string nonce = fields[3], challenge = PobImportBridge.NewNonce();
                                string signature = PobImportBridge.Mac(token, "HELLO|" + gameName + "|" + nonce + "|" + challenge);
                                byte[] answer = Encoding.ASCII.GetBytes("PIC1 CHALLENGE " + nonce + " " + challenge + " "
                                    + (response == "SPOOF" ? new string('0', 64) : signature) + "\n");
                                stream.Write(answer, 0, answer.Length);
                                if (response == "SPOOF") return;
                                fields = PobImportBridge.ReadLine(stream, 8192).Split(' ');
                                Require(fields.Length == 7 && fields[1] == nonce && fields[2] == "IMPORT" && fields[3] == gameName
                                    && fields[4] == challenge, "Wrong command/game sent.");
                                Require(fields[6] == PobImportBridge.Mac(token, "IMPORT|" + gameName + "|" + nonce + "|" + challenge + "|" + fields[5]),
                                    "Import signature wrong.");
                                if (response == "AFTERSEND") return;
                                string text = "PIC1 " + nonce + " " + (response == "BADSTATUS" ? "STATUS DONE" : response);
                                answer = Encoding.ASCII.GetBytes(text + " " + (response == "BADSTATUS" ? new string('0', 64)
                                    : PobImportBridge.Mac(token, "STATUS|" + gameName + "|" + challenge + "|" + text)) + "\n");
                                stream.Write(answer, 0, answer.Length);
                            }
                        });
                        var import = PobImportBridge.ImportAtPortAsync(config, new Progress<string>(),
                            ((IPEndPoint)listener.LocalEndpoint).Port);
                        if (response == "BEFORESEND" || response == "AFTERSEND")
                        {
                            try { import.GetAwaiter().GetResult(); throw new Exception("Disconnect reported success."); }
                            catch (PobBridgeConnectionException ex)
                            {
                                Require(ex.ImportMayHaveBeenSent == (response == "AFTERSEND"),
                                    "Real network disconnect misreported whether import was sent.");
                            }
                        }
                        else if (response == "STATUS DONE") import.GetAwaiter().GetResult();
                        else Reject(() => import.GetAwaiter().GetResult(), "Failed/unrecognized PoB status reported success.");
                        Require(server.Wait(5000), "Mock bridge did not complete.");
                    }
                    finally { listener.Stop(); }
                }
            }
            var unused = new TcpListener(IPAddress.Loopback, 0);
            unused.Start();
            int closedPort = ((IPEndPoint)unused.LocalEndpoint).Port;
            unused.Stop();
            try
            {
                PobImportBridge.ImportAtPortAsync(config, new Progress<string>(), closedPort).GetAwaiter().GetResult();
                throw new Exception("Closed socket reported success.");
            }
            catch (PobBridgeConnectionException ex)
            {
                Require(ex.Phase == "connecting" && !ex.ImportMayHaveBeenSent
                    && ex.Message.Contains("ConnectionRefused"), "Connection refusal lost safe pre-import diagnosis.");
            }
            foreach (bool authentic in new[] { true, false })
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                try
                {
                    var server = Task.Run(() =>
                    {
                        using (var peer = listener.AcceptTcpClient())
                        using (var stream = peer.GetStream())
                        {
                            peer.ReceiveTimeout = 5000;
                            string[] fields = PobImportBridge.ReadLine(stream, 256).Split(' ');
                            Require(fields.Length == 4 && fields[1] == "HELLO", "Read-only probe sent a non-HELLO command.");
                            string challenge = PobImportBridge.NewNonce();
                            string signature = authentic ? PobImportBridge.Mac(token,
                                "HELLO|poe2|" + fields[3] + "|" + challenge) : new string('0', 64);
                            byte[] answer = Encoding.ASCII.GetBytes("PIC1 CHALLENGE " + fields[3] + " " + challenge + " " + signature + "\n");
                            stream.Write(answer, 0, answer.Length);
                            Require(stream.ReadByte() == -1, "Read-only probe sent more data after HELLO.");
                        }
                    });
                    var probe = PobImportBridge.ProbeAsync(GameId.Poe2, directory, ((IPEndPoint)listener.LocalEndpoint).Port);
                    if (authentic) probe.GetAwaiter().GetResult();
                    else
                    {
                        try { probe.GetAwaiter().GetResult(); throw new Exception("Read-only probe accepted forged challenge."); }
                        catch (PobBridgeConnectionException ex)
                        { Require(!ex.ImportMayHaveBeenSent && ex.Phase == "authenticating", "Probe incorrectly marked import as sent."); }
                    }
                    Require(server.Wait(5000), "Read-only mock peer did not finish.");
                }
                finally { listener.Stop(); }
            }
        }
    }
}
