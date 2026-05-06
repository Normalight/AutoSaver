using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AutoSaver.Models;

namespace AutoSaver.Services
{
    public static class UpdateService
    {
        private const string ApiUrl = "https://api.github.com/repos/Normalight/AutoSaver/releases/latest";
        private const string InstallerAssetPrefix = "AutoSaver-Setup-v";
        private const string InstallerAssetSuffix = ".exe";

        private static string UserAgent => $"AutoSaver/{App.Version} (update-check)";

        public static Task<UpdateCheckResult> CheckAsync()
        {
            return Task.Run(() => Check());
        }

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

        public static Task<string> DownloadInstallerAsync(string url, string destinationPath, Action<long, long> onProgress = null)
        {
            return Task.Run(() => DownloadInstaller(url, destinationPath, onProgress));
        }

        public static string DownloadInstaller(string url, string destinationPath, Action<long, long> onProgress = null)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("缺少安装器下载地址。");

            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new InvalidOperationException("缺少安装器保存路径。");

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.UserAgent = UserAgent;
            req.Timeout = 30_000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (var resp = (HttpWebResponse)req.GetResponse())
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

            return destinationPath;
        }

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

        private static void ParseRelease(string json, UpdateCheckResult result)
        {
            var tag = ExtractStringValue(json, "tag_name");
            if (!string.IsNullOrEmpty(tag))
                result.LatestVersion = tag.TrimStart('v');

            result.ReleaseUrl = ExtractStringValue(json, "html_url");
            result.ReleaseNotes = ExtractStringValue(json, "body");
            result.InstallerUrl = FindInstallerUrl(json, result.LatestVersion);
        }

        private static string ExtractStringValue(string json, string key)
        {
            var needle = "\"" + key + "\"";
            var keyIdx = json.IndexOf(needle, StringComparison.Ordinal);
            if (keyIdx < 0) return "";

            var afterKey = keyIdx + needle.Length;
            while (afterKey < json.Length && (json[afterKey] == ' ' || json[afterKey] == '\t' || json[afterKey] == '\r' || json[afterKey] == '\n'))
                afterKey++;

            if (afterKey >= json.Length || json[afterKey] != ':') return "";
            afterKey++;

            while (afterKey < json.Length && (json[afterKey] == ' ' || json[afterKey] == '\t' || json[afterKey] == '\r' || json[afterKey] == '\n'))
                afterKey++;

            if (afterKey >= json.Length) return "";
            if (json.Length >= afterKey + 4 && json.Substring(afterKey, 4) == "null") return "";
            if (json[afterKey] != '"') return "";

            return ReadJsonString(json, afterKey);
        }

        private static string ReadJsonString(string json, int openQuoteIdx)
        {
            var sb = new StringBuilder();
            var i = openQuoteIdx + 1;
            while (i < json.Length)
            {
                var c = json[i];
                if (c == '"') break;
                if (c == '\\' && i + 1 < json.Length)
                {
                    i++;
                    switch (json[i])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 < json.Length)
                            {
                                var hex = json.Substring(i + 1, 4);
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
                                    sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default:
                            sb.Append(json[i]);
                            break;
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

        private static string FindInstallerUrl(string json, string latestVersion)
        {
            if (string.IsNullOrEmpty(latestVersion)) return "";

            var expectedName = InstallerAssetPrefix + latestVersion + InstallerAssetSuffix;
            var assetsIdx = json.IndexOf("\"assets\"", StringComparison.Ordinal);
            if (assetsIdx < 0) return "";

            var searchFrom = assetsIdx;
            while (true)
            {
                var nameIdx = json.IndexOf("\"name\"", searchFrom, StringComparison.Ordinal);
                if (nameIdx < 0) break;

                var nameValue = ExtractStringValueAt(json, nameIdx);
                if (string.Equals(nameValue, expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    var urlIdx = json.IndexOf("\"browser_download_url\"", nameIdx, StringComparison.Ordinal);
                    if (urlIdx >= 0 && urlIdx - nameIdx < 2048)
                        return ExtractStringValueAt(json, urlIdx);
                }

                searchFrom = nameIdx + 6;
            }

            return "";
        }

        private static string ExtractStringValueAt(string json, int keyIdx)
        {
            var afterKey = json.IndexOf(':', keyIdx);
            if (afterKey < 0) return "";
            afterKey++;

            while (afterKey < json.Length && (json[afterKey] == ' ' || json[afterKey] == '\t' || json[afterKey] == '\r' || json[afterKey] == '\n'))
                afterKey++;

            if (afterKey >= json.Length || json[afterKey] != '"') return "";
            return ReadJsonString(json, afterKey);
        }

        private static bool IsNewer(string candidate, string current)
        {
            if (TryParseVersion(candidate, out var cand) && TryParseVersion(current, out var cur))
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
