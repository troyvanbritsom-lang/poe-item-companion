using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Poe2ItemCompanion
{
    internal sealed class PobBridgeConnectionException : IOException
    {
        internal string Phase { get; private set; }
        internal bool ImportMayHaveBeenSent { get; private set; }

        internal PobBridgeConnectionException(string phase, bool importMayHaveBeenSent, Exception inner)
            : base("PoB bridge connection failed while " + phase + ". "
                + (importMayHaveBeenSent
                    ? "An import command may have reached PoB. Inspect the open build before retrying; import/save completion is unconfirmed."
                    : "No import command was sent by this attempt. Check that PoB's window is responsive and the local bridge is reachable.")
                + SocketDetail(inner), inner)
        {
            Phase = phase;
            ImportMayHaveBeenSent = importMayHaveBeenSent;
        }

        internal static string SocketDetail(Exception error)
        {
            for (Exception current = error; current != null; current = current.InnerException)
            {
                var socket = current as SocketException;
                if (socket != null) return " Socket: " + socket.SocketErrorCode + " (" + socket.NativeErrorCode + ").";
            }
            return error is TimeoutException ? " Timeout." : "";
        }
    }

    internal static class PobImportBridge
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdentity
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileIdentity identity);

        private static void ProtectGuide(AppConfig config, string current)
        {
            string guide = config.GetGuideFilePath();
            if (string.IsNullOrWhiteSpace(guide)) return;
            guide = Path.GetFullPath(guide);
            bool same = string.Equals(current, guide, StringComparison.OrdinalIgnoreCase);
            if (!same && File.Exists(guide))
            {
                using (var currentFile = File.Open(current, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var guideFile = File.Open(guide, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    FileIdentity a, b;
                    if (!GetFileInformationByHandle(currentFile.SafeFileHandle, out a)
                        || !GetFileInformationByHandle(guideFile.SafeFileHandle, out b))
                        throw new IOException("Cannot verify that CURRENT and GUIDE are different files.",
                            new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                    same = a.Volume == b.Volume && a.IndexHigh == b.IndexHigh && a.IndexLow == b.IndexLow;
                }
            }
            if (same)
                throw new InvalidOperationException("CURRENT and GUIDE refer to the same file. Choose a separate CURRENT XML before importing; the guide must not be overwritten.");
        }

        internal static int Port(GameId game) { return game == GameId.Poe1 ? 49171 : 49172; }

        internal static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        internal static string NewNonce()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Hex(bytes);
        }

        internal static string Mac(string token, string message)
        {
            using (var hmac = new HMACSHA256(Encoding.ASCII.GetBytes(token)))
                return Hex(hmac.ComputeHash(Encoding.ASCII.GetBytes(message)));
        }

        internal static bool EqualMac(string actual, string expected)
        {
            if (actual.Length != expected.Length) return false;
            int difference = 0;
            for (int i = 0; i < actual.Length; i++) difference |= actual[i] ^ expected[i];
            return difference == 0;
        }

        private static string ReadCredential(string pobPath, GameId game)
        {
            string keyPath = Path.Combine(pobPath, "Modules", "CompanionBridge.key");
            if (GameIdentity.DetectInstallation(pobPath) != game)
                throw new InvalidOperationException("The selected PoB installation belongs to the other game.");
            if (!File.Exists(keyPath))
                throw new InvalidOperationException("Bridge credential not found. Use Settings > Repair Bridge with PoB closed, then restart PoB.");
            string token = File.ReadAllText(keyPath).Trim();
            if (token.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(token, "^[0-9a-f]{64}$"))
                throw new InvalidDataException("Invalid bridge credential. Reinstall the bridge.");
            return token;
        }

        private static string Authenticate(Stream stream, string game, string nonce, string token)
        {
            byte[] helloBytes = Encoding.ASCII.GetBytes("PIC1 HELLO " + game + " " + nonce + "\n");
            stream.Write(helloBytes, 0, helloBytes.Length);
            string[] hello = ReadLine(stream, 256, DateTime.UtcNow.AddSeconds(5)).Split(' ');
            if (hello.Length != 5 || hello[0] != "PIC1" || hello[1] != "CHALLENGE" || hello[2] != nonce
                || !System.Text.RegularExpressions.Regex.IsMatch(hello[3], "^[0-9a-f]{64}$")
                || !EqualMac(hello[4], Mac(token, "HELLO|" + game + "|" + nonce + "|" + hello[3])))
                throw new InvalidDataException("PoB bridge authentication failed. No import command was sent.");
            return hello[3];
        }

        internal static Task ProbeAsync(GameId game, string pobPath, int? testPort = null)
        {
            return Task.Run(async () =>
            {
                string token = ReadCredential(pobPath, game);
                string phase = "connecting";
                try
                {
                    using (var client = new TcpClient(AddressFamily.InterNetwork))
                    {
                        var connect = client.ConnectAsync(IPAddress.Loopback, testPort ?? Port(game));
                        if (await Task.WhenAny(connect, Task.Delay(3000)) != connect)
                            throw new TimeoutException("PoB bridge connection timed out.");
                        await connect;
                        phase = "authenticating";
                        client.SendTimeout = 3000;
                        using (var stream = client.GetStream())
                            Authenticate(stream, game == GameId.Poe1 ? "poe1" : "poe2", NewNonce(), token);
                    }
                }
                catch (Exception ex)
                {
                    if (!(ex is SocketException) && !(ex is IOException) && !(ex is TimeoutException)
                        && !(ex is InvalidDataException)) throw;
                    throw new PobBridgeConnectionException(phase, false, ex);
                }
            });
        }

        internal static string Request(AppConfig config, string token, string nonce, string challenge)
        {
            if (string.IsNullOrWhiteSpace(config.CurrentBuildFilePath) || !string.IsNullOrEmpty(config.CurrentCharacterCode))
                throw new InvalidOperationException("Choose a saved PoB XML file as CURRENT first (not a character code).");
            string path = Path.GetFullPath(config.CurrentBuildFilePath);
            if (!File.Exists(path) || !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The configured CURRENT build must be an existing PoB XML file.");
            ProtectGuide(config, path);
            string error;
            if (!GameIdentity.ValidateBuild(File.ReadAllText(path), config.Game, out error))
                throw new InvalidOperationException(error);
            string game = config.Game == GameId.Poe1 ? "poe1" : "poe2";
            string hex = Hex(Encoding.UTF8.GetBytes(path));
            if (hex.Length > 4096) throw new InvalidOperationException("The CURRENT filename is too long for the bridge.");
            return "PIC1 " + nonce + " IMPORT " + game + " " + challenge + " " + hex + " "
                + Mac(token, "IMPORT|" + game + "|" + nonce + "|" + challenge + "|" + hex) + "\n";
        }

        internal static string ErrorText(string code)
        {
            switch (code)
            {
                case "BUILD": return "Open the saved CURRENT build in the matching PoB. Unsaved and different builds are refused.";
                case "AUTH": return "Authenticate in PoB's Import/Export tab and select a character first.";
                case "BUSY": return "Finish or close PoB's current dialog/import before trying again.";
                case "CHANGED": return "PoB's build, character or import options changed. No bridge save was performed.";
                case "DOWNLOAD": return "PoB could not download the character (authentication, API or rate-limit error). Check PoB and retry later.";
                case "DATA": return "PoB received invalid character data. No bridge save was performed.";
                case "IMPORT": return "PoB could not apply the import. Inspect the open build; it was not saved by the bridge.";
                case "SAVE": return "PoB could not save the CURRENT file. Check write permissions and inspect the open build.";
                case "TIMEOUT": return "The PoB import timed out. No bridge save was performed; late responses will be ignored.";
                default: return "The PoB bridge rejected the request (" + code + ").";
            }
        }

        internal static Task ImportAsync(AppConfig config, IProgress<string> progress)
        {
            return ImportAtPortAsync(config, progress, Port(config.Game));
        }

        internal static Task ImportAtPortAsync(AppConfig config, IProgress<string> progress, int port)
        {
            // Capture the selected profile before leaving the UI thread.
            var snapshot = new AppConfig { Game = config.Game, PobPath = config.PobPath,
                CurrentBuildFilePath = config.CurrentBuildFilePath, CurrentCharacterCode = config.CurrentCharacterCode,
                BuildSource = config.BuildSource, BuildFilePath = config.BuildFilePath, GggBuildFilePath = config.GggBuildFilePath };
            return Task.Run(async () =>
            {
                string token = ReadCredential(snapshot.PobPath, snapshot.Game);
                string nonce = NewNonce();
                Request(snapshot, token, nonce, nonce); // Validate Current before any network activity.
                string game = snapshot.Game == GameId.Poe1 ? "poe1" : "poe2";
                progress.Report("Connecting to PoB...");
                string phase = "connecting";
                bool importMayHaveBeenSent = false;
                try
                {
                    using (var client = new TcpClient(AddressFamily.InterNetwork))
                    {
                        var connect = client.ConnectAsync(IPAddress.Loopback, port);
                        if (await Task.WhenAny(connect, Task.Delay(3000)) != connect)
                            throw new TimeoutException("PoB bridge is unavailable. Keep PoB's window responsive and check local bridge reachability.");
                        await connect;
                        phase = "authenticating";
                        progress.Report("Authenticating PoB bridge...");
                        client.ReceiveTimeout = 5000;
                        client.SendTimeout = 3000;
                        using (var stream = client.GetStream())
                        {
                            string challenge = Authenticate(stream, game, nonce, token);
                            byte[] bytes = Encoding.ASCII.GetBytes(Request(snapshot, token, nonce, challenge));
                            phase = "sending the import request";
                            importMayHaveBeenSent = true;
                            stream.Write(bytes, 0, bytes.Length);
                            phase = "waiting for import/save completion";
                            client.ReceiveTimeout = 120000;
                            DateTime deadline = DateTime.UtcNow.AddSeconds(120);
                            while (DateTime.UtcNow < deadline)
                            {
                                string line = ReadLine(stream, 256, deadline);
                                string[] fields = line.Split(' ');
                                if (fields.Length != 5 || fields[0] != "PIC1" || fields[1] != nonce
                                    || !EqualMac(fields[4], Mac(token, "STATUS|" + game + "|" + challenge + "|"
                                        + string.Join(" ", fields, 0, 4))))
                                    throw new InvalidDataException("Untrusted or incompatible PoB bridge response.");
                                if (fields[2] == "ERROR") throw new InvalidOperationException(ErrorText(fields[3]));
                                if (fields[2] != "STATUS") throw new InvalidDataException("Invalid PoB bridge status.");
                                switch (fields[3])
                                {
                                    case "TREE": progress.Report("PoB: importing passive tree and jewels..."); break;
                                    case "ITEMS": progress.Report("PoB: importing equipment and skills..."); break;
                                    case "SAVE": progress.Report("PoB: saving the matching CURRENT build..."); break;
                                    case "DONE": return;
                                    default: throw new InvalidDataException("Unknown PoB bridge status.");
                                }
                            }
                            throw new TimeoutException("No completed PoB import acknowledgement. Inspect PoB before retrying.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!(ex is SocketException) && !(ex is IOException) && !(ex is TimeoutException)
                        && !(ex is InvalidDataException)) throw;
                    throw new PobBridgeConnectionException(phase, importMayHaveBeenSent, ex);
                }
            });
        }

        internal static string ReadLine(Stream stream, int limit, DateTime? deadline = null)
        {
            var text = new StringBuilder();
            while (text.Length <= limit)
            {
                if (deadline.HasValue)
                {
                    int remaining = (int)(deadline.Value - DateTime.UtcNow).TotalMilliseconds;
                    if (remaining <= 0) throw new TimeoutException("PoB bridge response timed out. Inspect PoB before retrying.");
                    if (stream.CanTimeout) stream.ReadTimeout = remaining;
                }
                int value = stream.ReadByte();
                if (value < 0) throw new IOException("PoB disconnected before acknowledging completion. Inspect PoB before retrying.");
                if (value == '\n') return text.ToString();
                if (value < 32 || value > 126) throw new InvalidDataException("Invalid bridge response encoding.");
                text.Append((char)value);
            }
            throw new InvalidDataException("Oversized bridge response.");
        }
    }
}
