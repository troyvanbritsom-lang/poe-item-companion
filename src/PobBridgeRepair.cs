using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Poe2ItemCompanion
{
    internal static class PobBridgeRepair
    {
        internal static string TargetFolder(string configuredPath)
        {
            string path = (configuredPath ?? "").Trim();
            if (path.Length < 3 || !((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
                || path[1] != ':' || (path[2] != '\\' && path[2] != '/')
                || path.IndexOf('"') >= 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new InvalidOperationException("Set an absolute local PoB installation folder in Settings first.");
            path = Path.GetFullPath(path).TrimEnd('\\');
            if (path.Length <= 2 || !Directory.Exists(path))
                throw new DirectoryNotFoundException("PoB installation folder not found: " + configuredPath);
            return path;
        }

        internal static Task RunAsync(GameId game, string pobPath)
        {
            return Task.Run(async () =>
            {
                string path = TargetFolder(pobPath);
                string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", "install-pob-bridge.ps1");
                if (!File.Exists(script) || !File.Exists(Path.Combine(Path.GetDirectoryName(script), "pob_import_bridge.lua")))
                    throw new FileNotFoundException("The bundled repair files are missing from the app's scripts folder. Rebuild the companion.");
                string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                string gameArg;
                string pathArg;
                switch (game)
                {
                    case GameId.Poe1: gameArg = "poe1"; pathArg = "-Poe1Path"; break;
                    case GameId.Poe2: gameArg = "poe2"; pathArg = "-Poe2Path"; break;
                    default: throw new ArgumentOutOfRangeException("game");
                }

                // No shell interpolation; the normalized target cannot contain quotes or a trailing slash.
                var start = new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""
                    + script + "\" -Game " + gameArg + " " + pathArg + " \"" + path + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(script)
                };
                using (var process = Process.Start(start))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    await Task.WhenAll(output, error).ConfigureAwait(false);
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        string detail = error.Result.Trim();
                        throw new IOException(detail.Length == 0 ? "The repair script failed with exit code " + process.ExitCode + "." : detail);
                    }
                }
            });
        }
    }
}
