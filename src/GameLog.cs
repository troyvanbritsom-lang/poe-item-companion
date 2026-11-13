using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Poe2ItemCompanion
{
    public enum GamePhase { Unknown, Act1, Act2, Act3, Act4, Interlude, Endgame, Act5, Act6, Act7, Act8, Act9, Act10 }

    public class GameLogState
    {
        public GameId Game { get; set; }
        public string CharacterName { get; set; }
        public string ClassName { get; set; }
        public int? CharacterLevel { get; set; }
        public string AreaCode { get; set; }
        public int? AreaLevel { get; set; }
        public GamePhase Phase { get; set; }
        public string PhaseAreaCode { get; set; }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(CharacterName == null ? "character not seen yet" : CharacterName + (ClassName == null ? "" : " (" + ClassName + ")"));
            sb.Append(CharacterLevel.HasValue ? ", level " + CharacterLevel.Value : ", level unknown");
            sb.Append("; zone " + (AreaCode ?? "unknown"));
            sb.Append("; " + GameLog.PhaseText(Phase));
            return sb.ToString();
        }
    }

    // Reads PoE2's logs\Client.txt (read-only, shared access) for zone changes and level-ups.
    // Chat and other lines are ignored and never stored.
    public class GameLogWatcher
    {
        private const long InitialTailBytes = 16L * 1024 * 1024;
        private readonly string _path;
        private readonly GameId _game;
        private long _position = -1;
        private string _partial = "";

        public GameLogState State { get; private set; }
        public string Path { get { return _path; } }

        public GameLogWatcher(string path) : this(path, GameId.Poe2) { }

        public GameLogWatcher(string path, GameId game)
        {
            _path = path;
            _game = game;
            State = new GameLogState { Game = game };
        }

        // Returns true when the state changed.
        public bool Poll(out string error)
        {
            error = null;
            try
            {
                using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long length = fs.Length;
                    if (_position < 0 || length < _position)
                    {
                        _position = Math.Max(0, length - InitialTailBytes);
                        _partial = "";
                        State = new GameLogState { Game = _game };
                        if (_position > 0)
                        {
                            // Skip the partial line at the tail start.
                            fs.Seek(_position, SeekOrigin.Begin);
                            int b;
                            while ((b = fs.ReadByte()) >= 0) { _position++; if (b == '\n') break; }
                        }
                    }
                    if (length == _position) return false;
                    fs.Seek(_position, SeekOrigin.Begin);
                    var bytes = new byte[length - _position];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = fs.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    _position += read;
                    string text = _partial + Encoding.UTF8.GetString(bytes, 0, read);
                    int lastNewline = text.LastIndexOf('\n');
                    if (lastNewline < 0) { _partial = text; return false; }
                    _partial = text.Substring(lastNewline + 1);
                    string before = State.Describe();
                    foreach (var line in text.Substring(0, lastNewline).Split('\n')) GameLog.ApplyLine(State, line);
                    return State.Describe() != before;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    public static class GameLog
    {
        private static readonly Regex AreaRegex = new Regex("Generating level (\\d+) area \"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex LevelRegex = new Regex(@"\] : (.+?) \(([A-Za-z ]+)\) is now level (\d+)\s*$", RegexOptions.Compiled);
        private static readonly Regex ActRegex = new Regex(@"^G([1-4])_", RegexOptions.Compiled);
        private static readonly Regex InterludeRegex = new Regex(@"^P\d+_", RegexOptions.Compiled);

        public static void ApplyLine(GameLogState state, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            var m = AreaRegex.Match(line);
            if (m.Success)
            {
                state.AreaCode = m.Groups[2].Value;
                state.AreaLevel = int.Parse(m.Groups[1].Value);
                var phase = ClassifyArea(state.AreaCode, state.Game);
                if (phase != GamePhase.Unknown) { state.Phase = phase; state.PhaseAreaCode = state.AreaCode; }
                return;
            }
            m = LevelRegex.Match(line.TrimEnd('\r'));
            if (m.Success)
            {
                state.CharacterName = m.Groups[1].Value;
                state.ClassName = m.Groups[2].Value;
                state.CharacterLevel = int.Parse(m.Groups[3].Value);
            }
        }

        // Zones that do not identify progress (hideouts, league areas) return Unknown and keep the last phase.
        public static GamePhase ClassifyArea(string code)
        {
            return ClassifyArea(code, GameId.Poe2);
        }

        public static GamePhase ClassifyArea(string code, GameId game)
        {
            if (string.IsNullOrEmpty(code)) return GamePhase.Unknown;
            if (game == GameId.Poe1)
            {
                var act = Regex.Match(code, @"^[12]_(10|[1-9])_");
                if (act.Success)
                {
                    GamePhase phase;
                    if (Enum.TryParse<GamePhase>("Act" + act.Groups[1].Value, out phase)) return phase;
                }
                return code.StartsWith("Map", StringComparison.Ordinal) || code.StartsWith("2_11_", StringComparison.Ordinal)
                    ? GamePhase.Endgame : GamePhase.Unknown;
            }
            var m = ActRegex.Match(code);
            if (m.Success) return (GamePhase)int.Parse(m.Groups[1].Value);
            if (InterludeRegex.IsMatch(code)) return GamePhase.Interlude;
            if (code.StartsWith("Map", StringComparison.Ordinal) || code.StartsWith("G_Endgame", StringComparison.Ordinal)) return GamePhase.Endgame;
            return GamePhase.Unknown;
        }

        public static string PhaseText(GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.Act1: return "Act 1";
                case GamePhase.Act2: return "Act 2";
                case GamePhase.Act3: return "Act 3";
                case GamePhase.Act4: return "Act 4";
                case GamePhase.Act5: return "Act 5";
                case GamePhase.Act6: return "Act 6";
                case GamePhase.Act7: return "Act 7";
                case GamePhase.Act8: return "Act 8";
                case GamePhase.Act9: return "Act 9";
                case GamePhase.Act10: return "Act 10";
                case GamePhase.Interlude: return "Interlude";
                case GamePhase.Endgame: return "Endgame (maps)";
                default: return "progress unknown";
            }
        }

        public static string FindLogPath(string configured)
        {
            return FindLogPath(configured, GameId.Poe2);
        }

        public static string FindLogPath(string configured, GameId game)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
            var candidates = new List<string>();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\GrindingGearGames\" + (game == GameId.Poe1 ? "Path of Exile" : "Path of Exile 2")))
                {
                    var install = key == null ? null : key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(install)) candidates.Add(System.IO.Path.Combine(install, @"logs\Client.txt"));
                }
            }
            catch (Exception) { }
            string gameFolder = game == GameId.Poe1 ? "Path of Exile" : "Path of Exile 2";
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                string steam = key == null ? null : key.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(steam))
                {
                    candidates.Add(Path.Combine(steam, "steamapps", "common", gameFolder, "logs", "Client.txt"));
                    string libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(libraries))
                        foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s+\"([^\"]+)\""))
                            candidates.Add(Path.Combine(match.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "common", gameFolder, "logs", "Client.txt"));
                }
            }
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                string root = drive.Name;
                candidates.Add(System.IO.Path.Combine(root, "SteamLibrary", "steamapps", "common", gameFolder, "logs", "Client.txt"));
                candidates.Add(System.IO.Path.Combine(root, "Program Files (x86)", "Steam", "steamapps", "common", gameFolder, "logs", "Client.txt"));
                candidates.Add(System.IO.Path.Combine(root, "Program Files (x86)", "Grinding Gear Games", gameFolder, "logs", "Client.txt"));
            }
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }

        // Matches the game's progress to a guide loadout by name. Never suggests a "min-max" style stage.
        public static string SuggestStage(IList<string> loadouts, GameLogState state, int midLevel, int lateLevel, out string reason)
        {
            reason = null;
            if (loadouts == null || loadouts.Count == 0) { reason = "the guide has no PoB loadouts"; return null; }
            // .build guides: stages are level ranges ("Level 1-11", "Level 12+", "All levels").
            var levelRange = new System.Text.RegularExpressions.Regex(@"^Level (\d+)(?:-(\d+)|(\+))?$");
            if (loadouts.All(n => n == "All levels" || levelRange.IsMatch(n)))
            {
                if (state == null || !state.CharacterLevel.HasValue) { reason = "no level-up seen in the log yet to pick a level range"; return null; }
                int level = state.CharacterLevel.Value;
                foreach (var n in loadouts)
                {
                    var m = levelRange.Match(n);
                    if (!m.Success) continue;
                    int from = int.Parse(m.Groups[1].Value);
                    int to = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : m.Groups[3].Success ? int.MaxValue : from;
                    if (level >= from && level <= to) { reason = "character level " + level + " (from the game log)"; return n; }
                }
                if (loadouts.Contains("All levels")) { reason = "the .build has a single stage"; return "All levels"; }
                reason = "no level range covers character level " + level; return null;
            }
            if (state == null || state.Phase == GamePhase.Unknown) { reason = "no campaign or map zone seen in the log yet"; return null; }
            var patterns = new List<string>();
            switch (state.Phase)
            {
                case GamePhase.Act1: patterns.Add(@"\bact\s*1\b"); break;
                case GamePhase.Act2: patterns.Add(@"\bact\s*2\b"); break;
                case GamePhase.Act3: patterns.Add(@"\bact\s*3\b"); break;
                case GamePhase.Act4: patterns.Add(@"\bact\s*4\b"); if (state.Game == GameId.Poe2) patterns.Add(@"interlude"); break;
                case GamePhase.Act5: patterns.Add(@"\bact\s*5\b"); break;
                case GamePhase.Act6: patterns.Add(@"\bact\s*6\b"); break;
                case GamePhase.Act7: patterns.Add(@"\bact\s*7\b"); break;
                case GamePhase.Act8: patterns.Add(@"\bact\s*8\b"); break;
                case GamePhase.Act9: patterns.Add(@"\bact\s*9\b"); break;
                case GamePhase.Act10: patterns.Add(@"\bact\s*10\b"); break;
                case GamePhase.Interlude: patterns.Add(@"interlude"); patterns.Add(@"\bact\s*4\b"); break;
                case GamePhase.Endgame:
                    if (!state.CharacterLevel.HasValue) { reason = "in maps, but no level-up seen in the log yet to pick Early/Mid/Endgame"; return null; }
                    int lvl = state.CharacterLevel.Value;
                    if (lvl >= lateLevel) { patterns.Add(@"\bend\s*-?game\b"); patterns.Add(@"\blate\b"); patterns.Add(@"\bmid\b"); }
                    else if (lvl >= midLevel) { patterns.Add(@"\bmid\b"); patterns.Add(@"\bearly\b"); }
                    else { patterns.Add(@"\bearly\b"); patterns.Add(@"\bmid\b"); }
                    break;
            }
            if (state.Phase != GamePhase.Endgame) patterns.Add(@"campaign|level?ling");
            foreach (var p in patterns)
                foreach (var name in loadouts)
                {
                    string clean = Regex.Replace(name, @"\{[^}]*\}", "");
                    if (Regex.IsMatch(clean, "min\\s*-?\\s*max", RegexOptions.IgnoreCase)) continue;
                    if (Regex.IsMatch(clean, p, RegexOptions.IgnoreCase))
                    {
                        reason = PhaseText(state.Phase) + (state.Phase == GamePhase.Endgame ? " at level " + state.CharacterLevel.Value
                            + " (Mid from " + midLevel + ", Endgame from " + lateLevel + ")" : "");
                        return name;
                    }
                }
            reason = "no guide loadout name matches " + PhaseText(state.Phase);
            return null;
        }
    }
}
