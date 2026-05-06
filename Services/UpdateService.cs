using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoSaver.Models;

namespace AutoSaver.Services
{
    /// <summary>
    /// 通过 GitHub 网页「最新发行版」重定向解析版本号与安装包直链（不使用 api.github.com REST，避免 API 限流与访问限制）。
    /// 网络错误写入 <see cref="UpdateCheckResult.ErrorMessage"/>，不向调用方抛异常。
    /// </summary>
    /// <remarks>
    /// 代理：<c>NO_PROXY</c> → 直连；<c>HTTPS_PROXY</c>/<c>HTTP_PROXY</c>/<c>ALL_PROXY</c>；
    /// 再回退 <see cref="WebRequest.GetSystemWebProxy"/>。仅 HTTP/HTTPS 代理。
    /// 发行说明：从 <c>github.com/.../releases/tag/...</c> 页面 HTML 中解析 <c>markdown-body</c>，不依赖本地文件。
    /// </remarks>
    public static class UpdateService
    {
        /// <summary>浏览器访问「最新 release」时的入口 URL（会 302 到带 tag 的页面）。</summary>
        private const string LatestReleasePageUrl =
            "https://github.com/Normalight/AutoSaver/releases/latest";

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

        private static string UserAgent =>
            $"AutoSaver/{App.Version} (update-check)";

        static UpdateService()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch { }
        }

        /// <summary>
        /// Asynchronously checks GitHub for the latest release (via releases/latest redirect only).
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
                if (!TryPopulateLatestFromGitHubReleasePage(result))
                {
                    result.ErrorMessage = "无法打开 GitHub 发行页或解析版本（请检查网络、代理或防火墙）。";
                    result.HasUpdate = false;
                    return result;
                }

                if (!string.IsNullOrEmpty(result.LatestVersion))
                    result.HasUpdate = IsNewer(result.LatestVersion, current);

                if (!string.IsNullOrEmpty(result.ReleaseUrl))
                {
                    var notes = FetchReleaseNotesFromReleasePage(result.ReleaseUrl);
                    if (!string.IsNullOrEmpty(notes))
                        result.ReleaseNotes = notes;
                }

                result.ErrorMessage = "";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = FormatUserFacingError(ex);
                result.HasUpdate = false;
            }

            return result;
        }

        /// <summary>
        /// GET <see cref="LatestReleasePageUrl"/>，跟随重定向，从最终 URL 的 /releases/tag/{tag} 解析版本并构造安装包直链。
        /// </summary>
        private static bool TryPopulateLatestFromGitHubReleasePage(UpdateCheckResult result)
        {
            try
            {
                var uri = new Uri(LatestReleasePageUrl, UriKind.Absolute);
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

            tag = Uri.UnescapeDataString(tag);

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

        /// <summary>构建某版本的 GitHub 发行页 URL（tag 通常为 <c>v1.2.3</c>）。</summary>
        public static string BuildReleaseTagPageUrl(string semanticVersion)
        {
            if (string.IsNullOrWhiteSpace(semanticVersion))
                return "";

            var t = semanticVersion.Trim();
            if (!t.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                t = "v" + t;

            return "https://github.com/Normalight/AutoSaver/releases/tag/" + Uri.EscapeDataString(t);
        }

        /// <summary>GET 对应 tag 的发行页并解析正文（失败返回空串）。</summary>
        public static string FetchReleaseNotesForSemanticVersion(string semanticVersion)
        {
            var url = BuildReleaseTagPageUrl(semanticVersion);
            return string.IsNullOrEmpty(url) ? "" : FetchReleaseNotesFromReleasePage(url);
        }

        /// <summary>
        /// GET 发行页 HTML，将 <c>markdown-body</c> 区域转为纯文本（无 NuGet、不调用 REST API）。
        /// </summary>
        public static string FetchReleaseNotesFromReleasePage(string releasePageUrl)
        {
            if (string.IsNullOrWhiteSpace(releasePageUrl))
                return "";

            try
            {
                var uri = new Uri(releasePageUrl, UriKind.Absolute);
                var req = (HttpWebRequest)WebRequest.Create(uri);
                req.Method = "GET";
                req.UserAgent = UserAgent;
                req.Timeout = 45_000;
                req.ReadWriteTimeout = 45_000;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                ConfigureProxy(req, uri);

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK)
                        return "";

                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        var html = sr.ReadToEnd();
                        var fragment = ExtractMarkdownBodyInnerHtml(html);
                        if (string.IsNullOrWhiteSpace(fragment))
                            fragment = ExtractMarkdownBodyFallback(html);

                        return HtmlFragmentToPlainText(fragment);
                    }
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>定位 GitHub 渲染的正文容器（含嵌套 div）。</summary>
        private static string ExtractMarkdownBodyInnerHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return "";

            var key = "markdown-body";
            var mb = html.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (mb < 0)
                return "";

            var divOpen = html.LastIndexOf("<div", mb, StringComparison.OrdinalIgnoreCase);
            if (divOpen < 0 || mb - divOpen > 400)
                return "";

            var gt = html.IndexOf('>', divOpen);
            if (gt < 0)
                return "";

            var contentStart = gt + 1;
            var depth = 1;
            var pos = contentStart;

            while (pos < html.Length && depth > 0)
            {
                var nextOpen = IndexOfIgnoreCase(html, "<div", pos);
                var nextClose = IndexOfIgnoreCase(html, "</div>", pos);

                if (nextClose < 0)
                    break;

                if (nextOpen >= 0 && nextOpen < nextClose)
                {
                    depth++;
                    pos = nextOpen + 4;
                }
                else
                {
                    depth--;
                    if (depth == 0)
                        return html.Substring(contentStart, nextClose - contentStart);

                    pos = nextClose + 6;
                }
            }

            return "";
        }

        private static int IndexOfIgnoreCase(string s, string needle, int start)
        {
            return s.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>结构变更时的兜底：匹配第一个带 markdown-body 的 div 内层。</summary>
        private static string ExtractMarkdownBodyFallback(string html)
        {
            if (string.IsNullOrEmpty(html))
                return "";

            var m = Regex.Match(
                html,
                @"<div[^>]*markdown-body[^>]*>([\s\S]*?)</div>\s*</div>",
                RegexOptions.IgnoreCase);

            return m.Success ? m.Groups[1].Value : "";
        }

        private static string HtmlFragmentToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return "";

            var s = WebUtility.HtmlDecode(html);
            s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"</h[1-6]>", "\n\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"</p>", "\n\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"</li>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^>]+>", "");
            s = Regex.Replace(s, @"[ \t]+\r?\n", "\n");
            s = Regex.Replace(s, @"\r?\n{3,}", "\n\n");
            return s.Trim();
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

        private static bool IsNewer(string candidate, string current)
        {
            if (TryParseVersion(candidate, out var cand) &&
                TryParseVersion(current, out var cur))
            {
                if (cand[0] != cur[0]) return cand[0] > cur[0];
                if (cand[1] != cur[1]) return cand[1] > cur[1];
                return cand[2] > cur[2];
            }

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
