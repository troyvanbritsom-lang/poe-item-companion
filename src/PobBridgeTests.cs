using System;
using System.IO;
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

        public static int Run(string poe1Build = null, string poe2Build = null)
        {
            string directory = Path.Combine(Path.GetTempPath(), "CompanionBridgeTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string originalDirectory = Directory.GetCurrentDirectory();
            try
            {
                string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
                TestProtocol(directory);
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
                TestNativeTransport(root, pob1, directory);
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
                foreach (string folder in Directory.GetDirectories(directory)) Directory.Delete(folder);
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

        private static void TestNativeTransport(string root, string pob, string directory)
        {
            foreach (GameId game in new[] { GameId.Poe1, GameId.Poe2 })
            {
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
                        + "io.open = function(path, mode) if path:match('CompanionBridge.key$') then return {"
                        + "read=function() return " + LuaString(token) + " end, close=function() end} end return realOpen(path,mode) end;"
                        + "GetScriptPath=function() return " + LuaString(pob) + " end; ConPrintf=function() end;"
                        + "package.loaded.socket={gettime=socket.gettime,tcp4=function() testListener=assert(socket.tcp4()); return {"
                        + "settimeout=function(_,n) return testListener:settimeout(n) end,"
                        + "bind=function(_,host) return testListener:bind(host,0) end,"
                        + "listen=function(_,n) return testListener:listen(n) end,"
                        + "accept=function() return testListener:accept() end,"
                        + "close=function() return testListener:close() end} end};"
                        + "testMain={mode='LIST',modes={},popups={}};"
                        + "testBridge=assert(loadfile(" + LuaString(Path.Combine(root, "scripts", "pob_import_bridge.lua"))
                        + "))('" + (game == GameId.Poe1 ? "poe1" : "poe2") + "');"
                        + "testBridge:Tick(testMain); local _,port=testListener:getsockname(); return tostring(port)";
                    int port = int.Parse(RunLua(state, setup));
                    var config = new AppConfig { Game = game, PobPath = directory, CurrentBuildFilePath = file };
                    var import = PobImportBridge.ImportAtPortAsync(config, new Progress<string>(), port);
                    DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!import.IsCompleted && DateTime.UtcNow < deadline)
                    {
                        RunLua(state, "testBridge:Tick(testMain); return 'ok'");
                        System.Threading.Thread.Sleep(5);
                    }
                    Require(import.IsCompleted, "Native transport handshake did not finish.");
                    try { import.GetAwaiter().GetResult(); throw new Exception("Build-less native fixture accepted import."); }
                    catch (InvalidOperationException ex)
                    {
                        Require(ex.Message == PobImportBridge.ErrorText("BUILD"),
                            "C# to native LuaSocket authenticated exchange failed: " + ex.Message);
                    }
                    RunLua(state, "testListener:close(); return 'closed'");
                }
                finally { lua_close(state); }
            }
            Console.WriteLine("PASSED: actual C# to LuaSocket authenticated exchange for both games on ephemeral ports, with build-less fixture refusal and no character API calls.");
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
        }
    }
}
