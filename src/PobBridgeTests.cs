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
            Directory.CreateDirectory(Path.Combine(directory, "Modules"));
            File.WriteAllText(Path.Combine(directory, "Modules", "CompanionBridge.key"), token);
            foreach (GameId game in new[] { GameId.Poe1, GameId.Poe2 })
            {
                config.Game = game;
                File.WriteAllText(file, "<" + GameIdentity.Root(game) + "><Build/></" + GameIdentity.Root(game) + ">");
                File.WriteAllText(Path.Combine(directory, "Modules", "Build.lua"), "dbXML[1].elem ~= \"" + GameIdentity.Root(game) + "\"");
                foreach (string response in new[] { "STATUS DONE", "ERROR BUILD", "STATUS INVALID", "SPOOF", "BADSTATUS" })
                {
                    var listener = new TcpListener(IPAddress.Loopback, PobImportBridge.Port(game));
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
                                string text = "PIC1 " + nonce + " " + (response == "BADSTATUS" ? "STATUS DONE" : response);
                                answer = Encoding.ASCII.GetBytes(text + " " + (response == "BADSTATUS" ? new string('0', 64)
                                    : PobImportBridge.Mac(token, "STATUS|" + gameName + "|" + challenge + "|" + text)) + "\n");
                                stream.Write(answer, 0, answer.Length);
                            }
                        });
                        var import = PobImportBridge.ImportAsync(config, new Progress<string>());
                        if (response == "STATUS DONE") import.GetAwaiter().GetResult();
                        else Reject(() => import.GetAwaiter().GetResult(), "Failed/unrecognized PoB status reported success.");
                        Require(server.Wait(5000), "Mock bridge did not complete.");
                    }
                    finally { listener.Stop(); }
                }
            }
        }
    }
}
