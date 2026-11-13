using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Poe2ItemCompanion
{
    public enum GameId { Poe2 = 0, Poe1 = 1 }

    public static class GameIdentity
    {
        public static string Name(GameId game) { return game == GameId.Poe1 ? "Path of Exile 1" : "Path of Exile 2"; }
        public static string Root(GameId game) { return game == GameId.Poe1 ? "PathOfBuilding" : "PathOfBuilding2"; }
        public static string DefaultPobPath(GameId game)
        {
            return game == GameId.Poe1 ? @"D:\Path of Building Community" : @"D:\Path of Building Community (PoE2)";
        }

        public static GameId DetectInstallation(string path)
        {
            string module = Path.Combine(path, "Modules", "Build.lua");
            if (!File.Exists(module)) throw new InvalidDataException("Not a supported Path of Building installation: " + path);
            string text = File.ReadAllText(module);
            if (text.Contains("dbXML[1].elem ~= \"PathOfBuilding2\"")) return GameId.Poe2;
            if (text.Contains("dbXML[1].elem ~= \"PathOfBuilding\"")) return GameId.Poe1;
            throw new InvalidDataException("Cannot identify this Path of Building installation.");
        }

        public static bool ValidateBuild(string xml, GameId? expected, out string error)
        {
            error = null;
            try
            {
                var document = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(new StringReader(xml ?? ""),
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    document.Load(reader);
                string root = document.DocumentElement == null ? "" : document.DocumentElement.Name;
                if (root != Root(GameId.Poe1) && root != Root(GameId.Poe2))
                    throw new InvalidDataException("Not a supported Path of Building build.");
                GameId actual = root == Root(GameId.Poe1) ? GameId.Poe1 : GameId.Poe2;
                if (expected.HasValue && expected.Value != actual)
                    throw new InvalidDataException("This build is for " + Name(actual) + ". Switch games before importing it.");
                var import = document.SelectSingleNode("/*/Import") as XmlElement;
                string realm = import == null ? "" : import.GetAttribute("lastRealm");
                if ((actual == GameId.Poe1 && realm.Equals("PoE2", StringComparison.OrdinalIgnoreCase))
                    || (actual == GameId.Poe2 && (realm == "pc" || realm == "xbox" || realm == "sony")))
                    throw new InvalidDataException("Build root and imported-character realm disagree.");
                return true;
            }
            catch (Exception ex)
            {
                error = "Build rejected: " + ex.Message;
                Logger.Warn(error);
                return false;
            }
        }

        public static string FindBuildDirectory(string pobPath, GameId game)
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string user = Path.Combine(documents, game == GameId.Poe1 ? "Path of Building" : "Path of Building (PoE2)");
            foreach (string settings in new[] { Path.Combine(user, "Settings.xml"), Path.Combine(pobPath, "Settings.xml") })
            {
                if (!File.Exists(settings)) continue;
                try
                {
                    var doc = new XmlDocument { XmlResolver = null };
                    using (var reader = XmlReader.Create(settings, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                        doc.Load(reader);
                    var node = doc.SelectSingleNode("//*[@buildPath]") as XmlElement;
                    if (node != null && Directory.Exists(node.GetAttribute("buildPath"))) return node.GetAttribute("buildPath");
                }
                catch (Exception ex) { Logger.Warn("Cannot read PoB build-directory settings: " + ex.Message); }
            }
            foreach (string candidate in new[] { Path.Combine(user, "Builds"), Path.Combine(pobPath, "build"), Path.Combine(pobPath, "Builds") })
                if (Directory.Exists(candidate)) return candidate;
            return Path.Combine(user, "Builds");
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        public static GameId? IdentifyProcess(string name, string executablePath)
        {
            if (name.StartsWith("PathOfExile2", StringComparison.OrdinalIgnoreCase)) return GameId.Poe2;
            if (!IsSharedProcessName(name)) return null;
            foreach (string part in ((executablePath ?? "").Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Reverse())
            {
                string folder = part.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                if (folder == "pathofexile2" || folder == "poe2") return GameId.Poe2;
                if (folder == "pathofexile" || folder == "pathofexile1" || folder == "poe1") return GameId.Poe1;
            }
            return null;
        }

        private static bool IsSharedProcessName(string name)
        {
            return name.Equals("PathOfExile", StringComparison.OrdinalIgnoreCase)
                || name.Equals("PathOfExile_x64", StringComparison.OrdinalIgnoreCase)
                || name.Equals("PathOfExileSteam", StringComparison.OrdinalIgnoreCase)
                || name.Equals("PathOfExile_x64Steam", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsForeground(AppConfig config)
        {
            uint pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            if (pid == 0) return false;
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                {
                    string name = process.ProcessName;
                    if (!IsSharedProcessName(name) && !name.StartsWith("PathOfExile2", StringComparison.OrdinalIgnoreCase)) return false;
                    string executable = process.MainModule.FileName;
                    var game = IdentifyProcess(name, executable);
                    if (game.HasValue) return game.Value == config.Game;
                    string product = process.MainModule.FileVersionInfo.ProductName ?? "";
                    if (product.IndexOf("Path of Exile 2", StringComparison.OrdinalIgnoreCase) >= 0) return config.Game == GameId.Poe2;
                    string log = GameLog.FindLogPath(config.GameLogPath, config.Game);
                    return log != null && string.Equals(Path.GetDirectoryName(executable),
                        Path.GetDirectoryName(Path.GetDirectoryName(log)), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) { Logger.Debug("Cannot identify foreground game: " + ex.Message); return false; }
        }
    }

    public sealed class CompanionProfiles
    {
        public int SchemaVersion { get; set; }
        public GameId ActiveGame { get; set; }
        public Dictionary<string, AppConfig> Profiles { get; set; }
        public CompanionProfiles() { SchemaVersion = 1; Profiles = new Dictionary<string, AppConfig>(); }
    }
}
