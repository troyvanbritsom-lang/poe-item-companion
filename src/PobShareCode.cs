using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Poe2ItemCompanion
{
    // Path of Building (PoE2) share codes: URL-safe Base64 of zlib-compressed build XML,
    // decoded exactly like PoB's ImportTab: Inflate(base64.decode(code:gsub("-","+"):gsub("_","/"))).
    public static class PobShareCode
    {
        private class SiteRule
        {
            public string Label;
            public Regex Match;
            public string RawUrlFormat;
        }

        // Same build sites and raw-download URLs PoB2 itself uses (Modules/BuildSiteTools.lua).
        private static readonly SiteRule[] Sites = new SiteRule[]
        {
            new SiteRule { Label = "pobb.in", Match = new Regex(@"^https?://pobb\.in/(?:pob/)?([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase), RawUrlFormat = "https://pobb.in/pob/{0}" },
            new SiteRule { Label = "poe.ninja", Match = new Regex(@"^https?://poe2?\.ninja/(?:poe2/)?pob/(?:raw/)?([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase), RawUrlFormat = "https://poe.ninja/poe2/pob/raw/{0}" },
            new SiteRule { Label = "poe2db.tw", Match = new Regex(@"^https?://poe2db\.tw/pob/([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase), RawUrlFormat = "https://poe2db.tw/pob/{0}/raw" },
            new SiteRule { Label = "pastebin.com", Match = new Regex(@"^https?://pastebin\.com/(?:raw/)?(\w+)", RegexOptions.IgnoreCase), RawUrlFormat = "https://pastebin.com/raw/{0}" },
        };

        public static bool IsUrl(string input)
        {
            if (input == null) return false;
            string t = input.Trim();
            return t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        // Resolves a share code or supported build-site link into build XML. Links are fetched only
        // when the user explicitly loads them. 'resolvedCode' is the plain share code (for caching).
        public static bool TryResolve(string input, out string xml, out string sourceDescription, out string error)
        {
            string code;
            return TryResolve(input, out xml, out code, out sourceDescription, out error);
        }

        public static bool TryResolve(string input, out string xml, out string resolvedCode, out string sourceDescription, out string error)
        {
            return TryResolve(input, GameId.Poe2, out xml, out resolvedCode, out sourceDescription, out error);
        }

        public static bool TryResolve(string input, GameId game, out string xml, out string resolvedCode, out string sourceDescription, out string error)
        {
            xml = null;
            resolvedCode = null;
            sourceDescription = null;
            error = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "No share code entered.";
                return false;
            }

            string text = input.Trim();
            if (IsUrl(text))
            {
                string fetched;
                string label;
                if (!TryFetchFromUrl(text, game, out fetched, out label, out error)) return false;
                if (!TryDecode(fetched, out xml, out error))
                {
                    error = "Downloaded data from " + label + " is not a valid PoB code: " + error;
                    return false;
                }
                resolvedCode = fetched.Trim();
                sourceDescription = "Share link (" + label + "): " + text;
                return GameIdentity.ValidateBuild(xml, game, out error);
            }

            if (!TryDecode(text, out xml, out error)) return false;
            resolvedCode = text;
            sourceDescription = "Share code (" + text.Length + " chars)";
            return GameIdentity.ValidateBuild(xml, game, out error);
        }

        public static bool TryDecode(string code, out string xml, out string error)
        {
            xml = null;
            error = null;
            try
            {
                string b64 = Regex.Replace(code ?? "", @"\s+", "").Replace('-', '+').Replace('_', '/');
                if (b64.Length == 0)
                {
                    error = "Share code is empty.";
                    return false;
                }
                int pad = b64.Length % 4;
                if (pad == 1)
                {
                    error = "Share code has an invalid length (truncated copy?).";
                    return false;
                }
                if (pad > 0) b64 = b64 + new string('=', 4 - pad);

                byte[] data;
                try
                {
                    data = Convert.FromBase64String(b64);
                }
                catch (FormatException)
                {
                    error = "Share code is not valid Base64.";
                    return false;
                }

                if (data.Length < 3)
                {
                    error = "Share code is too short.";
                    return false;
                }

                int offset = 0;
                bool zlib = false;
                // zlib header: CMF (deflate, method 8) + FLG with (CMF*256+FLG) % 31 == 0
                if ((data[0] & 0x0F) == 8 && ((data[0] << 8) | data[1]) % 31 == 0)
                {
                    zlib = true;
                    offset = 2;
                    if ((data[1] & 0x20) != 0) offset += 4; // preset dictionary id (not used by PoB)
                }

                byte[] raw;
                using (var input = new MemoryStream(data, offset, data.Length - offset))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    deflate.CopyTo(output);
                    raw = output.ToArray();
                }

                // .NET's DeflateStream silently returns partial output for a cut-off stream, so verify
                // the zlib Adler-32 trailer to detect truncated/corrupted copies.
                if (zlib && data.Length >= offset + 4)
                {
                    uint expected = ((uint)data[data.Length - 4] << 24) | ((uint)data[data.Length - 3] << 16)
                        | ((uint)data[data.Length - 2] << 8) | data[data.Length - 1];
                    if (Adler32(raw) != expected)
                    {
                        error = "Share code is incomplete or corrupted (checksum mismatch, " + code.Trim().Length
                            + " chars received). It was probably cut off while copying/pasting - copy the whole code again.";
                        return false;
                    }
                }
                xml = Encoding.UTF8.GetString(raw);
            }
            catch (Exception ex)
            {
                error = "Could not decompress share code: " + ex.Message;
                return false;
            }

            if (xml.Length > 0 && xml[0] == '\uFEFF') xml = xml.Substring(1);
            if (xml.IndexOf("<PathOfBuilding", StringComparison.Ordinal) < 0)
            {
                error = "Decoded data is not a Path of Building build.";
                xml = null;
                return false;
            }
            if (!GameIdentity.ValidateBuild(xml, null, out error))
            {
                xml = null;
                return false;
            }
            return true;
        }

        // Produces a PoB-compatible import code (zlib + URL-safe Base64), e.g. for exported builds.
        public static string Encode(string xml)
        {
            byte[] raw = Encoding.UTF8.GetBytes(xml);
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x9C);
                using (var deflate = new DeflateStream(output, CompressionMode.Compress, true))
                {
                    deflate.Write(raw, 0, raw.Length);
                }
                uint adler = Adler32(raw);
                output.WriteByte((byte)(adler >> 24));
                output.WriteByte((byte)(adler >> 16));
                output.WriteByte((byte)(adler >> 8));
                output.WriteByte((byte)adler);
                return Convert.ToBase64String(output.ToArray()).Replace('+', '-').Replace('/', '_');
            }
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte d in data)
            {
                a = (a + d) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static bool TryFetchFromUrl(string url, GameId game, out string code, out string label, out string error)
        {
            code = null;
            label = null;
            error = null;
            var sites = new System.Collections.Generic.List<SiteRule>(Sites);
            sites.RemoveAll(s => s.Label == "poe.ninja" || s.Label == "poe2db.tw");
            string segment = game == GameId.Poe1 ? "poe1" : "poe2";
            string database = game == GameId.Poe1 ? "poedb" : "poe2db";
            sites.Add(new SiteRule { Label = "poe.ninja", Match = new Regex(@"^https?://poe(?:2)?\.ninja/(?:" + segment + @"/)?pob/(?:raw/)?([A-Za-z0-9_\-]+)(?:[?#].*)?$", RegexOptions.IgnoreCase), RawUrlFormat = "https://poe.ninja/" + segment + "/pob/raw/{0}" });
            sites.Add(new SiteRule { Label = database, Match = new Regex(@"^https?://" + database + @"\.tw/pob/([A-Za-z0-9_\-]+)(?:/raw)?(?:[?#].*)?$", RegexOptions.IgnoreCase), RawUrlFormat = "https://" + database + ".tw/pob/{0}/raw" });
            string maxroll = game == GameId.Poe1 ? "poe" : "poe2";
            sites.Add(new SiteRule { Label = "Maxroll", Match = new Regex(@"^https?://maxroll\.gg/" + maxroll + @"/pob/([A-Za-z0-9_\-]+)(?:[?#].*)?$", RegexOptions.IgnoreCase), RawUrlFormat = "https://maxroll.gg/" + maxroll + "/api/pob/{0}" });
            foreach (var site in sites)
            {
                var m = site.Match.Match(url);
                if (!m.Success) continue;
                label = site.Label;
                string rawUrl = string.Format(site.RawUrlFormat, m.Groups[1].Value);
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                    using (var client = new WebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        client.Headers[HttpRequestHeader.UserAgent] = "Poe2ItemCompanion/1.0";
                        Logger.Info("Downloading build code from " + rawUrl);
                        code = client.DownloadString(rawUrl);
                    }
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        error = label + " returned an empty response.";
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    error = "Failed to download build from " + label + ": " + ex.Message;
                    return false;
                }
            }
            error = "Unsupported link for the selected game. Paste its PoB share code/XML or a supported raw-build link; interactive website planner data is not inferred.";
            return false;
        }
    }
}
