using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Poe2ItemCompanion
{
    internal static class ReleaseUpdateTests
    {
        private sealed class FixtureServer : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;
            internal readonly List<Uri> Requests = new List<Uri>();
            internal FixtureServer(params HttpResponseMessage[] responses) { _responses = new Queue<HttpResponseMessage>(responses); }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Requests.Add(request.RequestUri);
                return Task.FromResult(_responses.Dequeue());
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Reject<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }

        private static string Digest(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static ReleaseUpdates.Release Read(string tag, bool preview, string assets)
        {
            return ReleaseUpdates.ParseRelease("{\"draft\":false,\"prerelease\":" + (preview ? "true" : "false") +
                ",\"tag_name\":\"" + tag + "\",\"assets\":[" + assets + "]}", new Version(1, 9, 0));
        }

        private static string Asset(byte[] bytes, string url)
        {
            return "{\"state\":\"uploaded\",\"name\":\"PoE-Item-Companion-Setup-v1.10.0-x64.exe\",\"size\":" + bytes.Length +
                ",\"digest\":\"sha256:" + Digest(bytes) + "\",\"browser_download_url\":\"" + url + "\"}";
        }

        private static HttpResponseMessage Bytes(byte[] data)
        { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }; }

        private static HttpResponseMessage Redirect(string url)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(url);
            return response;
        }

        internal static int Run(bool live)
        {
            string directory = Path.Combine(Path.GetTempPath(), "Companion update fixture " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var bytes = Encoding.ASCII.GetBytes("installer fixture");
                var assetUrl = Read("v1.10.0", false, "").AssetUri.AbsoluteUri;
                var asset = Asset(bytes, assetUrl);
                var release = Read("v1.10.0", false, asset);
                Require(release.CanInstall && Read("v1.9.0", false, asset) == null && Read("v1.8.9", false, asset) == null
                    && Read("v1.10.0-rc.1", true, asset) == null, "Wrong release selected.");
                Require(!Read("v1.10.0", false, asset + "," + asset).CanInstall
                    && !Read("v1.10.0", false, Asset(bytes, "https://example.test/setup.exe")).CanInstall
                    && !Read("v1.10.0", false, asset.Replace("sha256:", "sha1:")).CanInstall, "Unsafe metadata accepted.");
                Reject<FormatException>(() => Read("v01.10.0", false, asset));

                string destination = Path.Combine(directory, "fixture download.exe");
                var host = "https://release-assets.githubusercontent.com/fixture";
                var handler = new FixtureServer(Redirect(host), Bytes(bytes));
                using (var client = new HttpClient(handler))
                    ReleaseUpdates.DownloadAsync(client, release, destination, CancellationToken.None).GetAwaiter().GetResult();
                Require(handler.Requests.Select(u => u.AbsoluteUri).SequenceEqual(new[] { assetUrl, host })
                    && File.ReadAllBytes(destination).SequenceEqual(bytes), "Official GitHub download path was not used.");
                File.Delete(destination);
                foreach (var denied in new[] { "https://example.test/setup.exe", "http://release-assets.githubusercontent.com/fixture" })
                using (var client = new HttpClient(new FixtureServer(Redirect(denied))))
                    Reject<InvalidDataException>(() => ReleaseUpdates.DownloadAsync(client, release, destination, CancellationToken.None).GetAwaiter().GetResult());
                Require(!File.Exists(destination), "Untrusted redirect wrote an installer.");
                foreach (var bad in new[] { bytes.Take(bytes.Length - 1).ToArray(), new byte[bytes.Length], new byte[bytes.Length + 1] })
                {
                    using (var client = new HttpClient(new FixtureServer(Bytes(bad))))
                        Reject<InvalidDataException>(() => ReleaseUpdates.DownloadAsync(client, release, destination, CancellationToken.None).GetAwaiter().GetResult());
                    File.Delete(destination);
                }
                TestSetup(directory);
                if (live)
                {
                    var latest = ReleaseUpdates.CheckAsync().GetAwaiter().GetResult();
                    Console.WriteLine(latest == null ? "PASSED: live GitHub check found no newer release."
                        : "PASSED: live GitHub check found " + latest.Tag + ".");
                }
                Console.WriteLine("PASSED: stable update selection, GitHub asset restrictions, size and digest checks, and verified Setup handoff to an isolated build.");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("FAILED UPDATES: " + ex); return 1; }
            finally { Directory.Delete(directory, true); }
        }

        private static void TestSetup(string directory)
        {
            string install = Path.Combine(directory, "installed app");
            Directory.CreateDirectory(install);
            var config = Path.Combine(install, "config.json");
            File.WriteAllText(config, "settings fixture");
            var app = Path.Combine(install, "Poe2ItemCompanion.exe");
            var setup = Path.Combine(directory, "setup fixture.exe");
            var source = Path.Combine(directory, "setup.cs");
            var log = Path.Combine(directory, "setup log.txt");
            File.WriteAllText(source, @"
using System;
using System.IO;
using System.Linq;
[assembly: System.Reflection.AssemblyInformationalVersion(""1.10.0"")]
class Fixture {
    static int Main(string[] args) {
        if (args.Length != 7 || !args.Contains(""/COMPANIONUPDATE=1"") || !args.Contains(""/SILENT"") || !args.Contains(""/NORESTART"") ||
            !args.Contains(""/NOCLOSEAPPLICATIONS"") || !args.Contains(""/NORESTARTAPPLICATIONS"")) return 21;
        var target = args.Single(a => a.StartsWith(""/DIR="")).Substring(5);
        if (File.ReadAllText(Path.Combine(target, ""config.json"")) != ""settings fixture"") return 22;
        File.Copy(typeof(Fixture).Assembly.Location, Path.Combine(target, ""Poe2ItemCompanion.exe""), true);
        File.WriteAllText(args.Single(a => a.StartsWith(""/LOG="")).Substring(5), ""fixture setup ran"");
        return 0;
    }
}");
            var compiler = Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "csc.exe");
            using (var process = Process.Start(new ProcessStartInfo(compiler, "/nologo /target:winexe /out:\"" + setup + "\" \"" + source + "\"")
            { UseShellExecute = false, CreateNoWindow = true }))
            {
                process.WaitForExit();
                Require(process.ExitCode == 0, "Could not compile installer fixture.");
            }
            Reject<InvalidDataException>(() => ReleaseUpdates.RunVerifiedSetup(setup, new string('0', 64), app, log, "v1.10.0"));
            Require(!File.Exists(app) && !File.Exists(log), "An installer ran after digest validation failed.");
            var digest = Digest(File.ReadAllBytes(setup));
            ReleaseUpdates.RunVerifiedSetup(setup, digest, app, log, "v1.10.0");
            Require(File.ReadAllText(config) == "settings fixture" && File.ReadAllText(log) == "fixture setup ran",
                "Setup did not target the expected installation or keep its configuration.");
            File.Delete(log);
            Reject<InvalidOperationException>(() => ReleaseUpdates.RunVerifiedSetup(setup, digest, app, log, "v1.9.9"));
            Require(!File.Exists(log), "An installer ran for an older version.");
            Reject<InvalidOperationException>(() => ReleaseUpdates.RunVerifiedSetup(setup, digest, app, log, "v1.11.0"));
        }
    }
}
