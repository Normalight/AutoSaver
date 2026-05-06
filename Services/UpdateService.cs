using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AutoSaver.Models;

namespace AutoSaver.Services
{
    /// <summary>
    /// Checks GitHub Releases for a newer version of AutoSaver.
    /// All public methods are fire-and-forget safe: network errors are caught and
    /// returned as UpdateCheckResult.ErrorMessage rather than thrown to callers.
    /// </summary>
    public static class UpdateService
    {
        private const string ApiUrl =
            "https://api.github.com/repos/Normalight/AutoSaver/releases/latest";

        private const string InstallerAssetPrefix = "AutoSaver-Setup-v";
        private const string InstallerAssetSuffix = ".exe";

        // GitHub API requires a User-Agent header.
        private static string UserAgent =>
            $"AutoSaver/{App.Version} (update-check)";

        // ------------------------------------------------------------------ //
        //  Public API                                                          //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Asynchronously checks GitHub for the latest release.
        /// Never throws — all errors are captured in UpdateCheckResult.ErrorMessage.
        /// </summary>
        public static Task<UpdateCheckResult> CheckAsync()
        {
            return Task.Run(() => Check());
        }

        /// <summary>
        /// Synchronous version — call from a background thread only.
        /// </summary>
        public static UpdateCheckResult Check()
        {
            var current = App.Version;
            var result = new UpdateCheckResult { CurrentVersion = current };

            try
            {
                var json = FetchJson(ApiUrl);
                ParseRelease(json, result);

                if (!string.IsNullOrEmpty(result.LatestVersion))
                    result.HasUpdate = IsNewer(result.LatestVersion, current);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                result.HasUpdate = false;
            }

            return result;
        }

        // ------------------------------------------------------------------ //
        //  Internals                                                           //
        // ------------------------------------------------------------------ //

        private static string FetchJson(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.UserAgent = UserAgent;
            req.Accept = "application/vnd.github+json";
            req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            req.Timeout = 15_000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        /// <summary>
        /// Minimal hand-rolled JSON extraction — avoids any NuGet dependency.
        /// Extracts: tag_name, html_url, body, and the installer asset browser_download_url.
        /// </summary>
        private static void ParseRelease(string json, UpdateCheckResult result)
        {
            // tag_name → strip leading "v"
            var tag = ExtractStringValue(json, "tag_name");
            if (!string.IsNullOrEmpty(tag))
                result.LatestVersion = tag.TrimStart('v');

            result.ReleaseUrl = ExtractStringValue(json, "html_url");
            result.ReleaseNotes = ExtractStringValue(json, "body");

            // Installer asset: look for the browser_download_url whose name matches
            // "AutoSaver-Setup-vX.Y.Z.exe".  We scan the assets array by finding
            // the asset name first, then the download URL that follows it.
            result.InstallerUrl = FindInstallerUrl(json, result.LatestVersion);
        }

        /// <summary>
        /// Extracts the first string value for a given JSON key using simple
        /// substring search.  Handles escaped quotes and basic escape sequences.
        /// Returns empty string when the key is not found.
        /// </summary>
        private static string ExtractStringValue(string json, string key)
        {
            // Look for  "key":  (with optional whitespace)
            var needle = "\"" + key + "\"";
            var keyIdx = json.IndexOf(needle, StringComparison.Ordinal);
            if (keyIdx < 0) return "";

            var afterKey = keyIdx + needle.Length;

            // Skip whitespace and colon
            while (afterKey < json.Length && (json[afterKey] == ' ' || json[afterKey] == '\t' || json[afterKey] == '\r' || json[afterKey] == '\n'))
                afterKey++;

            if (afterKey >= json.Length || json[afterKey] != ':') return "";
            afterKey++; // skip ':'

            while (afterKey < json.Length && (json[afterKey] == ' ' || json[afterKey] == '\t' || json[afterKey] == '\r' || json[afterKey] == '\n'))
                afterKey++;

            if (afterKey >= json.Length) return "";

            // null literal
            if (json.Length >= afterKey + 4 && json.Substring(afterKey, 4) == "null")
                return "";

            if (json[afterKey] != '"') return "";

            // Read quoted string with escape handling
            return ReadJsonString(json, afterKey);
        }

        private static string ReadJsonString(string json, int openQuoteIdx)
        {
            var sb = new StringBuilder();
            var i = openQuoteIdx + 1; // skip opening quote
            while (i < json.Length)
            {
                var c = json[i];
                if (c == '"') break; // closing quote
                if (c == '\\' && i + 1 < json.Length)
                {
                    i++;
                    switch (json[i])
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'b':  sb.Append('\b'); break;
                        case 'f':  sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 < json.Length)
                            {
                                var hex = json.Substring(i + 1, 4);
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
                                    sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default:   sb.Append(json[i]); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
                i++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Scans the assets array for an entry whose "name" matches the expected
        /// installer filename, then returns the adjacent "browser_download_url".
        /// Returns empty string when not found — not an error.
        /// </summary>
        private static string FindInstallerUrl(string json, string latestVersion)
        {
            if (string.IsNullOrEmpty(latestVersion)) return "";

            var expectedName = InstallerAssetPrefix + latestVersion + InstallerAssetSuffix;

            // Find the assets array
            var assetsIdx = json.IndexOf("\"assets\"", StringComparison.Ordinal);
            if (assetsIdx < 0) return "";

            // Walk through occurrences of "name" inside the assets section
            var searchFrom = assetsIdx;
            while (true)
            {
                var nameIdx = json.IndexOf("\"name\"", searchFrom, StringComparison.Ordinal);
                if (nameIdx < 0) break;

                var nameValue = ExtractStringValueAt(json, nameIdx);
                if (string.Equals(nameValue, expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    // Found the right asset — now find browser_download_url in the
                    // same asset object.  It should appear within the next ~512 chars.
                    var urlIdx = json.IndexOf("\"browser_download_url\"", nameIdx, StringComparison.Ordinal);
                    if (urlIdx >= 0 && urlIdx - nameIdx < 2048)
                        return ExtractStringValueAt(json, urlIdx);
                }

                searchFrom = nameIdx + 6; // advance past "name"
            }

            return "";
        }

        /// <summary>
        /// Like ExtractStringValue but starts from a known key position rather
        /// than searching by key name.
        /// </summary>
        private static string ExtractStringValueAt(string json, int keyIdx)
        {
            // Skip past the key token to find the colon and value
            var afterKey = json.IndexOf(':', keyIdx);
            if (afterKey < 0) return "";
            afterKey++;

            while (afterKey < json.Length && (json[afterKey] == ' ' || json[afterKey] == '\t' || json[afterKey] == '\r' || json[afterKey] == '\n'))
                afterKey++;

            if (afterKey >= json.Length || json[afterKey] != '"') return "";
            return ReadJsonString(json, afterKey);
        }

        /// <summary>
        /// Returns true when <paramref name="candidate"/> is strictly greater than
        /// <paramref name="current"/> using three-part numeric comparison (Major.Minor.Patch).
        /// Falls back to string comparison when parsing fails.
        /// </summary>
        private static bool IsNewer(string candidate, string current)
        {
            if (TryParseVersion(candidate, out var cand) &&
                TryParseVersion(current,   out var cur))
            {
                if (cand[0] != cur[0]) return cand[0] > cur[0];
                if (cand[1] != cur[1]) return cand[1] > cur[1];
                return cand[2] > cur[2];
            }

            // Fallback: lexicographic — not ideal but safe
            return string.Compare(candidate, current, StringComparison.Ordinal) > 0;
        }

        private static bool TryParseVersion(string v, out int[] parts)
        {
            parts = new int[3];
            if (string.IsNullOrEmpty(v)) return false;

            var segments = v.Split('.');
            if (segments.Length < 3) return false;

            for (var i = 0; i < 3; i++)
            {
                if (!int.TryParse(segments[i], out parts[i]))
                    return false;
            }
            return true;
        }
    }
}
