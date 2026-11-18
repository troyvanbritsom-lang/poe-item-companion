using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    internal static class ReleaseUpdates
    {
        private const string Repository = "troyvanbritsom-lang/poe-item-companion";
        private const string Github = "https://github.com/" + Repository + "/releases";
        private const string Api = "https://api.github.com/repos/" + Repository + "/releases/latest";
        private const string Helper = "ApplyCompanionUpdate.exe";
        private const string AppExe = "Poe2ItemCompanion.exe";
        private const long MaxInstallerBytes = 256L * 1024 * 1024;
        private static readonly Regex TagPattern = new Regex(@"\Av(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\z");
        private static readonly Regex DigestPattern = new Regex(@"\A[0-9a-f]{64}\z");
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            // The legacy Framework build can default to TLS versions that GitHub rejects.
            SslProtocols = SslProtocols.Tls12
        }) { Timeout = Timeout.InfiniteTimeSpan };

        private sealed class ReleaseResponse
        {
            public bool? draft { get; set; }
            public bool? prerelease { get; set; }
            public string tag_name { get; set; }
            public AssetResponse[] assets { get; set; }
        }

        private sealed class AssetResponse
        {
            public string name { get; set; }
            public string state { get; set; }
            public long size { get; set; }
            public string digest { get; set; }
            public string browser_download_url { get; set; }
        }

        internal sealed class Release
        {
            internal string Tag { get; private set; }
            internal long Size { get; private set; }
            internal string Digest { get; private set; }
            internal string Page { get { return Github + "/tag/" + Tag; } }
            internal string AssetName { get { return InstallerName(Tag); } }
            internal Uri AssetUri { get { return new Uri(Github + "/download/" + Tag + "/" + AssetName); } }
            internal bool CanInstall { get { return Digest != null; } }

            internal Release(string tag, long size = 0, string digest = null)
            {
                Tag = tag;
                Size = size;
                Digest = digest;
            }
        }

        private static string InstallerName(string tag) { return "PoE-Item-Companion-Setup-" + tag + "-x64.exe"; }
        private static string UpdatesFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeItemCompanion", "Updates"); }
        }

        internal static Version PublishedBuildVersion
        {
            get
            {
                var attr = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    typeof(ReleaseUpdates).Assembly, typeof(AssemblyInformationalVersionAttribute));
                return attr != null && TagPattern.IsMatch("v" + attr.InformationalVersion) ? Version.Parse(attr.InformationalVersion) : null;
            }
        }

        internal static bool IsInstalledBuild
        {
            get { return PublishedBuildVersion != null && File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installed.mode")); }
        }

        internal static Release ParseRelease(string json, Version current)
        {
            var response = new JavaScriptSerializer().Deserialize<ReleaseResponse>(json);
            if (response == null || !response.draft.HasValue || !response.prerelease.HasValue)
                throw new FormatException("GitHub returned invalid release information.");
            if (response.draft.Value || response.prerelease.Value) return null;
            if (response.tag_name == null || !TagPattern.IsMatch(response.tag_name))
                throw new FormatException("The latest GitHub release does not have a stable vX.Y.Z tag.");
            if (Version.Parse(response.tag_name.Substring(1)) <= current) return null;
            var release = new Release(response.tag_name);
            var assets = (response.assets ?? new AssetResponse[0]).Where(a => a != null && a.name == release.AssetName).ToArray();
            if (assets.Length != 1) return release;
            var asset = assets[0];
            if (asset.state != "uploaded" || asset.size <= 0 || asset.size > MaxInstallerBytes ||
                asset.browser_download_url != release.AssetUri.AbsoluteUri || asset.digest == null ||
                !asset.digest.StartsWith("sha256:", StringComparison.Ordinal) || !DigestPattern.IsMatch(asset.digest.Substring(7)))
                return release;
            return new Release(release.Tag, asset.size, asset.digest.Substring(7));
        }

        private static HttpRequestMessage Request(Uri uri)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("PoeItemCompanion/" + (PublishedBuildVersion ?? new Version(0, 0, 0)).ToString(3));
            return request;
        }

        internal static async Task<Release> CheckAsync()
        {
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using (var request = Request(new Uri(Api)))
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound) return null;
                    response.EnsureSuccessStatusCode();
                    using (var data = new MemoryStream())
                    {
                        await CopyBoundedAsync(response.Content, data, 1024 * 1024, deadline.Token).ConfigureAwait(false);
                        return ParseRelease(System.Text.Encoding.UTF8.GetString(data.ToArray()), PublishedBuildVersion ?? new Version(0, 0, 0));
                    }
                }
            }
        }

        private static async Task<long> CopyBoundedAsync(HttpContent content, Stream target, long limit, CancellationToken token)
        {
            if (content.Headers.ContentLength > limit) throw new InvalidDataException("GitHub response exceeds the expected size.");
            long total = 0;
            using (var source = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (token.Register(source.Dispose))
            {
                var buffer = new byte[65536];
                int count;
                while ((count = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
                {
                    total += count;
                    if (total > limit) throw new InvalidDataException("GitHub response exceeds the expected size.");
                    await target.WriteAsync(buffer, 0, count, token).ConfigureAwait(false);
                }
            }
            return total;
        }

        internal static bool IsReleaseRedirect(Uri uri)
        {
            return uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
                (uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com");
        }

        internal static async Task DownloadAsync(HttpClient client, Release release, string destination, CancellationToken token)
        {
            if (!release.CanInstall) throw new InvalidDataException("No verifiable Windows installer is available.");
            Uri uri = release.AssetUri;
            for (int hop = 0; hop < 4; hop++)
            {
                using (var request = Request(uri))
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    int status = (int)response.StatusCode;
                    if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                    {
                        if (response.Headers.Location == null || !IsReleaseRedirect(response.Headers.Location))
                            throw new InvalidDataException("GitHub sent an unexpected installer download redirect.");
                        uri = response.Headers.Location;
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, true))
                    {
                        if (await CopyBoundedAsync(response.Content, file, release.Size, token).ConfigureAwait(false) != release.Size)
                            throw new InvalidDataException("The installer download was incomplete.");
                        Verify(file, release.Digest);
                    }
                    return;
                }
            }
            throw new InvalidDataException("Too many installer download redirects.");
        }

        internal static void Verify(Stream file, string expected)
        {
            file.Position = 0;
            using (var sha = SHA256.Create())
                if (!string.Equals(BitConverter.ToString(sha.ComputeHash(file)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The installer SHA-256 does not match the official GitHub release. Nothing was run.");
        }

        internal static async Task<string> StageAsync(Release release)
        {
            string directory = Path.Combine(UpdatesFolder, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using (var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
                    await DownloadAsync(Client, release, Path.Combine(directory, release.AssetName), deadline.Token).ConfigureAwait(false);
                return directory;
            }
            catch { Discard(directory, release); throw; }
        }

        internal static void Discard(string directory, Release release)
        {
            try
            {
                File.Delete(Path.Combine(directory, release.AssetName));
                File.Delete(Path.Combine(directory, Helper));
                Directory.Delete(directory);
            }
            catch (IOException ex) { Logger.Error("Could not clean the update staging folder: " + directory, ex); }
            catch (UnauthorizedAccessException ex) { Logger.Error("Could not clean the update staging folder: " + directory, ex); }
        }

        internal static void LaunchHelper(string directory, Release release)
        {
            if (!IsInstalledBuild) throw new InvalidOperationException("Only a packaged Companion installation can be updated in place.");
            string helper = Path.Combine(directory, Helper);
            File.Copy(Application.ExecutablePath, helper);
            using (var app = Process.GetCurrentProcess())
            using (var child = Process.Start(new ProcessStartInfo(helper, "--apply-release-update " + app.Id + " "
                + app.StartTime.ToUniversalTime().Ticks + " " + release.Tag + " " + release.Digest + " " + Quote(Application.ExecutablePath))
                { UseShellExecute = false, WorkingDirectory = directory }))
                if (child == null) throw new InvalidOperationException("Windows could not start the update helper.");
        }

        internal static string SetupArguments(string appPath, string logPath)
        {
            return "/SILENT /NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /COMPANIONUPDATE=1 /DIR="
                + Quote(Path.GetDirectoryName(appPath)) + " /LOG=" + Quote(logPath);
        }

        private static string Quote(string path)
        {
            if (path.IndexOf('"') >= 0 || path.EndsWith("\\", StringComparison.Ordinal))
                throw new ArgumentException("Cannot safely pass the update path.");
            return "\"" + path + "\"";
        }

        internal static int Apply(string[] args)
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string installer = null;
            string log = null;
            try
            {
                int pid;
                long started;
                Guid id;
                if (args.Length != 6 || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out pid) || pid <= 0 ||
                    !long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out started) || !TagPattern.IsMatch(args[3]) ||
                    !DigestPattern.IsMatch(args[4]) || !Guid.TryParseExact(Path.GetFileName(directory), "N", out id) ||
                    !string.Equals(Path.GetDirectoryName(directory), UpdatesFolder, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetFileName(Application.ExecutablePath), Helper, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Invalid update handoff.");

                string appPath = Path.GetFullPath(args[5]);
                string installDir = Path.GetDirectoryName(appPath);
                if (!string.Equals(Path.GetFileName(appPath), AppExe, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(Path.Combine(installDir, "installed.mode")))
                    throw new InvalidOperationException("The installed Companion could not be located.");

                WaitForExit(pid, started);
                installer = Path.Combine(directory, InstallerName(args[3]));
                log = Path.Combine(directory, "Setup.log");
                RunVerifiedSetup(installer, args[4], appPath, log, args[3]);
                MessageBox.Show("The update was installed. Open the Companion again from your usual shortcut.",
                    "Companion update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("Companion update did not finish.", ex);
                MessageBox.Show("The update did not finish: " + ex.Message
                    + (log != null && File.Exists(log) ? "\n\nSetup log: " + log : ""), "Companion update",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            finally
            {
                if (installer != null)
                {
                    try { File.Delete(installer); }
                    catch (IOException ex) { Logger.Error("Could not remove the downloaded installer.", ex); }
                    catch (UnauthorizedAccessException ex) { Logger.Error("Could not remove the downloaded installer.", ex); }
                }
            }
        }

        internal static void RunVerifiedSetup(string installer, string digest, string appPath, string log, string tag)
        {
            Version existing;
            if (File.Exists(appPath) && Version.TryParse(FileVersionInfo.GetVersionInfo(appPath).ProductVersion, out existing)
                && existing >= Version.Parse(tag.Substring(1)))
                throw new InvalidOperationException("The installed Companion is already at " + tag + " or newer. Setup was not started.");
            using (var file = File.Open(installer, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Verify(file, digest);
                using (var setup = Process.Start(new ProcessStartInfo(installer, SetupArguments(appPath, log))
                { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installer) }))
                {
                    if (setup == null) throw new InvalidOperationException("Windows could not start Setup.");
                    setup.WaitForExit();
                    if (setup.ExitCode != 0) throw new InvalidOperationException("Setup exited with code " + setup.ExitCode + ".");
                }
            }
            if (FileVersionInfo.GetVersionInfo(appPath).ProductVersion != tag.Substring(1))
                throw new InvalidOperationException("Setup exited but the installed Companion version did not match " + tag + ".");
        }

        internal static void WaitForExit(int pid, long started)
        {
            Process existing;
            try { existing = Process.GetProcessById(pid); }
            catch (ArgumentException) { return; }
            using (existing)
            {
                if (!existing.HasExited && existing.StartTime.ToUniversalTime().Ticks == started && !existing.WaitForExit(30000))
                    throw new TimeoutException("The Companion did not exit. Setup was not started.");
            }
        }
    }
}
