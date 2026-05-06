using System;
using System.Diagnostics;
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
    /// <remarks>
    /// Proxy resolution order: <c>NO_PROXY</c> host match → direct;
    /// then <c>HTTPS_PROXY</c>/<c>HTTP_PROXY</c>/<c>ALL_PROXY</c> (typical for Clash when not using Windows system proxy);
    /// then <see cref="WebRequest.GetSystemWebProxy"/> (Clash「系统代理」、企业 PAC)。
    /// 仅支持 HTTP/HTTPS 代理；纯 SOCKS 端口请使用 Clash 的 mixed / HTTP 入站。
    /// </remarks>
    public static class UpdateService
    {
        private const string ApiUrl =
            "https://api.github.com/repos/Normalight/AutoSaver/releases/latest";

        /// <summary>
        /// Format string for the installer asset uploaded by CI/Inno (with extension).
        /// Must stay in sync with <c>installer/autosaver.iss</c>
        /// <c>OutputBaseFilename</c> (which becomes this name; Inno adds .exe).
        /// If you change one, change the other.
        /// </summary>
        public const string InstallerAssetFileNameFormat = "AutoSaver-{0}-Setup.exe";

        /// <summary>
        /// Expected GitHub release asset filename for a semantic version (e.g. 1.4.0).
        /// </summary>
        public static string GetExpectedInstallerAssetFileName(string semanticVersion)
        {
            if (string.IsNullOrEmpty(semanticVersion)) return "";
            return string.Format(InstallerAssetFileNameFormat, semanticVersion);
        }

        // GitHub API requires a User-Agent header.
        private static string UserAgent =>
            $"AutoSaver/{App.Version} (update-check)";

        static UpdateService()
        {
            try
            {
                // GitHub 要求 TLS 1.2+；部分环境默认仍启用 SSL3 / TLS1.0 会导致握手失败。
                // 不显式引用 Tls13：部分 CI 参考程序集未包含该枚举会导致 CS0117。
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch { }
        }

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
            Exception apiException = null;

            try
            {
                var json = FetchJson(ApiUrl);
                ParseRelease(json, result);
            }
            catch (Exception ex)
            {
                apiException = ex;
            }

            // API 失败或解析不到版本时：许多网络环境屏蔽 api.github.com 但不屏蔽 github.com，用 releases/latest 重定向解析版本。
            if (string.IsNullOrEmpty(result.LatestVersion))
            {
                if (!TryPopulateLatestFromGitHubReleasePage(result))
                {
                    result.ErrorMessage = apiException != null
                        ? FormatUserFacingError(apiException)
                        : "无法从 GitHub 获取最新版本（请检查网络或代理）。";
                    result.HasUpdate = false;
                    return result;
                }
            }

            if (!string.IsNullOrEmpty(result.LatestVersion))
                result.HasUpdate = IsNewer(result.LatestVersion, current);

            result.ErrorMessage = "";
            return result;
        }

        /// <summary>
        /// GET https://github.com/.../releases/latest ，跟随重定向，从最终 URL 的 /releases/tag/{tag} 解析版本并构造安装包直链。
        /// </summary>
        private static bool TryPopulateLatestFromGitHubReleasePage(UpdateCheckResult result)
        {
            const string pageUrl = "https://github.com/Normalight/AutoSaver/releases/latest";
            try
            {
                var uri = new Uri(pageUrl, UriKind.Absolute);
                var req = (HttpWebRequest)WebRequest.Create(uri);
                req.Method = "GET";
                req.UserAgent = UserAgent;
                req.AllowAutoRedirect = true;
                req.Timeout = 45_000;
                req.ReadWriteTimeout = 45_000;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                ConfigureProxy(req, uri);

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    using (resp.GetResponseStream()) { }

                    return TryFillResultFromReleaseTagUri(resp.ResponseUri, result);
                }
            }
            catch (WebException ex)
            {
                return TryPopulateFromRedirectException(ex, result);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryPopulateFromRedirectException(WebException ex, UpdateCheckResult result)
        {
            if (ex.Response is HttpWebResponse h)
            {
                var code = (int)h.StatusCode;
                if (code >= 300 && code < 400)
                {
                    var loc = h.GetResponseHeader("Location");
                    if (!string.IsNullOrEmpty(loc) &&
                        Uri.TryCreate(loc, UriKind.Absolute, out var locUri))
                        return TryFillResultFromReleaseTagUri(locUri, result);
                }
            }

            return false;
        }

        private static bool TryFillResultFromReleaseTagUri(Uri uri, UpdateCheckResult result)
        {
            var path = uri.AbsolutePath;
            const string marker = "/releases/tag/";
            var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return false;

            var tag = path.Substring(idx + marker.Length).Trim('/');
            if (string.IsNullOrEmpty(tag))
                return false;

            var semVer = tag.TrimStart('v');
            result.LatestVersion = semVer;
            result.ReleaseUrl = uri.ToString();
            var encTag = Uri.EscapeDataString(tag);
            result.InstallerUrl =
                "https://github.com/Normalight/AutoSaver/releases/download/" + encTag + "/" +
                GetExpectedInstallerAssetFileName(semVer);
            return true;
        }

        private static string FormatUserFacingError(Exception ex)
        {
            var core = ex.Message;
            if (ex.InnerException != null && !string.IsNullOrWhiteSpace(ex.InnerException.Message))
                core = ex.InnerException.Message;
            if (core.IndexOf("TLS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                core.IndexOf("SSL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                core.IndexOf("handshake", StringComparison.OrdinalIgnoreCase) >= 0)
                return core + "（可检查系统 TLS 或代理是否截断 HTTPS）";
            if (core.IndexOf("407", StringComparison.Ordinal) >= 0 ||
                core.IndexOf("proxy", StringComparison.OrdinalIgnoreCase) >= 0)
                return core + "（若使用代理，请确认已登录或设置 HTTPS_PROXY）";
            return core;
        }

        /// <summary>
        /// Downloads the installer from a GitHub asset URL to disk.
        /// </summary>
        public static Task<string> DownloadInstallerAsync(string url, string destinationPath, Action<long, long> onProgress = null)
        {
            return Task.Run(() => DownloadInstaller(url, destinationPath, onProgress));
        }

        /// <summary>
        /// Synchronous download — prefer calling from a background thread.
        /// </summary>
        public static string DownloadInstaller(string url, string destinationPath, Action<long, long> onProgress = null)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("缺少安装器下载地址。");

            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new InvalidOperationException("缺少安装器保存路径。");

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var uri = new Uri(url, UriKind.Absolute);
            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.Method = "GET";
            req.UserAgent = UserAgent;
            req.Timeout = 600_000;
            req.ReadWriteTimeout = 600_000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            ConfigureProxy(req, uri);

            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK)
                        throw new InvalidOperationException($"下载返回 HTTP {(int)resp.StatusCode} {resp.StatusDescription}");

                    using (var stream = resp.GetResponseStream())
                    using (var file = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var totalBytes = resp.ContentLength > 0 ? resp.ContentLength : -1;
                        var buffer = new byte[81920];
                        long downloadedBytes = 0;
                        int read;
                        while (stream != null && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            file.Write(buffer, 0, read);
                            downloadedBytes += read;
                            onProgress?.Invoke(downloadedBytes, totalBytes);
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                throw new InvalidOperationException(BuildWebExceptionMessage(ex), ex);
            }

            return destinationPath;
        }

        /// <summary>Starts the downloaded installer with the shell.</summary>
        public static void LaunchInstaller(string installerPath)
        {
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
                throw new FileNotFoundException("安装器文件不存在。", installerPath);

            Process.Start(new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? ""
            });
        }

        // ------------------------------------------------------------------ //
        //  Internals                                                           //
        // ------------------------------------------------------------------ //

        private static string FetchJson(string url)
        {
            var uri = new Uri(url, UriKind.Absolute);
            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.Method = "GET";
            req.UserAgent = UserAgent;
            req.Accept = "application/vnd.github+json";
            req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            req.Timeout = 45_000;
            req.ReadWriteTimeout = 45_000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            ConfigureProxy(req, uri);

            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK)
                    {
                        var errBody = "";
                        using (var es = resp.GetResponseStream())
                        {
                            if (es != null)
                            {
                                using (var sr = new StreamReader(es, Encoding.UTF8))
                                    errBody = sr.ReadToEnd();
                            }
                        }

                        throw new InvalidOperationException(
                            $"GitHub API HTTP {(int)resp.StatusCode}: {errBody}");
                    }

                    using (var stream = resp.GetResponseStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                throw new InvalidOperationException(BuildWebExceptionMessage(ex), ex);
            }
        }

        /// <summary>
        /// 环境变量代理优先（Clash 未开系统代理时常用），否则使用 Windows 系统代理（IE / WinHTTP）。
        /// </summary>
        private static void ConfigureProxy(HttpWebRequest req, Uri targetUri)
        {
            req.UseDefaultCredentials = true;

            if (ShouldBypassProxyForHost(targetUri))
            {
                req.Proxy = null;
                return;
            }

            var envProxy = TryCreateProxyFromEnvironment();
            if (envProxy != null)
            {
                req.Proxy = envProxy;
                return;
            }

            try
            {
                var sys = WebRequest.GetSystemWebProxy();
                if (sys != null && !sys.IsBypassed(targetUri))
                {
                    req.Proxy = sys;
                    return;
                }
            }
            catch { }

            try
            {
                var def = WebRequest.DefaultWebProxy;
                if (def != null && !def.IsBypassed(targetUri))
                    req.Proxy = def;
                else
                    req.Proxy = null;
            }
            catch
            {
                req.Proxy = null;
            }
        }

        private static bool ShouldBypassProxyForHost(Uri targetUri)
        {
            var raw = Environment.GetEnvironmentVariable("NO_PROXY")
                      ?? Environment.GetEnvironmentVariable("no_proxy");
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var host = targetUri.Host;
            foreach (var part in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Trim();
                if (p.Length == 0) continue;
                if (p == "*") return true;
                if (string.Equals(host, p, StringComparison.OrdinalIgnoreCase)) return true;
                if (p.StartsWith(".", StringComparison.Ordinal) &&
                    host.EndsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
                if (p.StartsWith("*.", StringComparison.Ordinal) &&
                    host.EndsWith(p.Substring(1), StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private static WebProxy TryCreateProxyFromEnvironment()
        {
            var raw = FirstNonEmpty(
                Environment.GetEnvironmentVariable("HTTPS_PROXY"),
                Environment.GetEnvironmentVariable("https_proxy"),
                Environment.GetEnvironmentVariable("HTTP_PROXY"),
                Environment.GetEnvironmentVariable("http_proxy"),
                Environment.GetEnvironmentVariable("ALL_PROXY"),
                Environment.GetEnvironmentVariable("all_proxy"));

            raw = TrimEnvQuotes(raw);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            if (raw.IndexOf("://", StringComparison.Ordinal) < 0)
                raw = "http://" + raw.Trim();

            try
            {
                var uri = new Uri(raw, UriKind.Absolute);
                return new WebProxy(uri);
            }
            catch
            {
                return null;
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return null;
            foreach (var v in values)
            {
                var t = TrimEnvQuotes(v);
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }

            return null;
        }

        private static string TrimEnvQuotes(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "";
            v = v.Trim();
            if (v.Length >= 2 &&
                ((v[0] == '"' && v[v.Length - 1] == '"') || (v[0] == '\'' && v[v.Length - 1] == '\'')))
                v = v.Substring(1, v.Length - 2).Trim();
            return v;
        }

        private static string BuildWebExceptionMessage(WebException ex)
        {
            if (ex.Status == WebExceptionStatus.Timeout)
                return "连接超时（请检查网络、代理或防火墙）。";

            if (ex.Response is HttpWebResponse h)
            {
                try
                {
                    using (var ts = h.GetResponseStream())
                    {
                        if (ts == null)
                            return $"HTTP {(int)h.StatusCode} {h.StatusDescription}";
                        using (var sr = new StreamReader(ts, Encoding.UTF8))
                        {
                            var body = sr.ReadToEnd();
                            if (body.Length > 600)
                                body = body.Substring(0, 600) + "...";
                            return $"HTTP {(int)h.StatusCode} {h.StatusDescription}: {body}";
                        }
                    }
                }
                catch
                {
                    return $"HTTP {(int)h.StatusCode} {h.StatusDescription}";
                }
            }

            return ex.Message;
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
            // GetExpectedInstallerAssetFileName(...). We scan the assets array by finding
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

            var expectedName = GetExpectedInstallerAssetFileName(latestVersion);

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
