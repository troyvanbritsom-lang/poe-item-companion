using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Poe2ItemCompanion
{
    internal static class ReleaseUpdates
    {
        private const string Repository = "troyvanbritsom-lang/poe-item-companion";
        private const string Releases = "https://github.com/" + Repository + "/releases";
        private static readonly Regex StableTag = new Regex(@"\Av(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\z");
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 1024 * 1024
        };

        public static string CurrentVersion
        {
            get
            {
                return ((AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    typeof(ReleaseUpdates).Assembly, typeof(AssemblyInformationalVersionAttribute))).InformationalVersion;
            }
        }

        public static async Task<Version> CheckAsync()
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repository + "/releases/latest"))
            {
                request.Headers.UserAgent.ParseAdd("PoeItemCompanion/" + CurrentVersion);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (HttpResponseMessage response = await Client.SendAsync(request).ConfigureAwait(false))
                    return await ReadReleaseAsync(response, CurrentVersion).ConfigureAwait(false);
            }
        }

        private static async Task<Version> ReadReleaseAsync(HttpResponseMessage response, string current)
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return NewerStableVersion(await response.Content.ReadAsStringAsync().ConfigureAwait(false), current);
        }

        internal static Version NewerStableVersion(string json, string current)
        {
            var release = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object draft, prerelease, tag;
            if (release == null || !release.TryGetValue("draft", out draft) || !(draft is bool)
                || !release.TryGetValue("prerelease", out prerelease) || !(prerelease is bool)
                || !release.TryGetValue("tag_name", out tag) || !(tag is string))
                throw new FormatException("GitHub returned an invalid release response.");
            if ((bool)draft || (bool)prerelease) return null;
            var version = ParseTag((string)tag);
            if (version == null) throw new FormatException("The latest GitHub release does not have a stable major.minor.patch tag.");
            return version > Version.Parse(current) ? version : null;
        }

        internal static Version ParseTag(string tag)
        {
            if (!StableTag.IsMatch(tag)) return null;
            return Version.Parse(tag.TrimStart('v'));
        }

        public static string ReleasePage(Version release)
        {
            // Use the canonical repository, never a URL supplied in a release response.
            return Releases + "/tag/v" + release.ToString(3);
        }

        internal static int Test()
        {
            if (NewerStableVersion("{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v1.10.0\"}", "1.9.9") != new Version(1, 10, 0)
                || NewerStableVersion("{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v1.2.3\"}", "1.2.3") != null
                || NewerStableVersion("{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v1.2.2\"}", "1.2.3") != null
                || NewerStableVersion("{\"draft\":false,\"prerelease\":true,\"tag_name\":\"v3.0.0-rc.1\"}", "1.0.0") != null
                || NewerStableVersion("{\"draft\":true,\"prerelease\":false,\"tag_name\":\"v3.0.0\"}", "1.0.0") != null
                || ParseTag("v1.2.3-rc.1") != null || ParseTag("v1.02.3") != null)
                return 1;
            try { NewerStableVersion("{}", CurrentVersion); return 1; }
            catch (FormatException) { }
            using (var missing = new HttpResponseMessage(HttpStatusCode.NotFound))
                if (ReadReleaseAsync(missing, CurrentVersion).GetAwaiter().GetResult() != null) return 1;
            using (var limited = new HttpResponseMessage(HttpStatusCode.Forbidden))
            {
                try { ReadReleaseAsync(limited, CurrentVersion).GetAwaiter().GetResult(); return 1; }
                catch (HttpRequestException) { }
            }
            return 0;
        }
    }
}
