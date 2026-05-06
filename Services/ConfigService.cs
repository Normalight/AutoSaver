using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using AutoSaver.Models;

namespace AutoSaver.Services
{
    public static class ConfigService
    {
        /// <summary>惰性解析：优先 %AppData%\AutoSaver\（安装目录在 Program Files 时也可写）；若曾使用 exe 旁旧配置则迁移一次。</summary>
        private static string _resolvedIniPath;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(
            string lpAppName, string lpKeyName, string lpDefault,
            StringBuilder lpReturnedString, int nSize, string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool WritePrivateProfileString(
            string lpAppName, string lpKeyName, string lpValue, string lpFileName);

        /// <summary>用于判断键是否不存在（不可能出现在正常 ini 值中）。</summary>
        private static readonly string MissingIniKeySentinel =
            new string(new[] { '\uE000', '\uE001', '\uE002', '\uE003' });

        public static string IniPath
        {
            get
            {
                if (_resolvedIniPath != null)
                    return _resolvedIniPath;
                _resolvedIniPath = ResolveIniPath();
                return _resolvedIniPath;
            }
        }

        private static string ResolveIniPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            var legacyIni = Path.Combine(baseDir, "autosaver.ini");

            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoSaver");
            var appDataIni = Path.Combine(appDataDir, "autosaver.ini");

            if (File.Exists(appDataIni))
                return appDataIni;

            if (File.Exists(legacyIni))
            {
                try
                {
                    Directory.CreateDirectory(appDataDir);
                    File.Copy(legacyIni, appDataIni, overwrite: false);
                    return appDataIni;
                }
                catch
                {
                    return legacyIni;
                }
            }

            try
            {
                Directory.CreateDirectory(appDataDir);
            }
            catch { }

            return appDataIni;
        }

        internal static string Read(string section, string key, string defaultValue = "")
        {
            return ReadFromPath(IniPath, section, key, defaultValue);
        }

        private static string ReadFromPath(string iniPath, string section, string key, string defaultValue)
        {
            var sb = new StringBuilder(512);
            GetPrivateProfileString(section, key, defaultValue, sb, sb.Capacity, iniPath);
            return sb.ToString();
        }

        private static bool IniKeyExists(string iniPath, string section, string key)
        {
            return ReadFromPath(iniPath, section, key, MissingIniKeySentinel) != MissingIniKeySentinel;
        }

        internal static void Write(string section, string key, string value)
        {
            WritePrivateProfileString(section, key, value, IniPath);
        }

        public static int CheckIntervalSec
        {
            get
            {
                var v = Read("global", "check_interval_sec", "30");
                return int.TryParse(v, out var n) && n > 0 ? n : 30;
            }
            set => Write("global", "check_interval_sec", value.ToString());
        }

        public static bool StartWithWindows
        {
            get => Read("global", "start_with_windows", "false") == "true";
            set => Write("global", "start_with_windows", value ? "true" : "false");
        }

        public static bool MinimizeToTrayOnClose
        {
            get => Read("global", "minimize_to_tray_on_close", "true") != "false";
            set => Write("global", "minimize_to_tray_on_close", value ? "true" : "false");
        }

        public static bool ShowNotifications
        {
            get => Read("global", "show_notifications", "true") == "true";
            set => Write("global", "show_notifications", value ? "true" : "false");
        }

        public static bool CheckUpdatesOnStartup
        {
            get => Read("global", "check_updates_on_startup", "true") != "false";
            set => Write("global", "check_updates_on_startup", value ? "true" : "false");
        }

        public static string AppVersion
        {
            get => Read("meta", "version", "");
            set => Write("meta", "version", value);
        }

        public static void EnsureDefaults()
        {
            try
            {
                var dir = Path.GetDirectoryName(IniPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }

            if (!File.Exists(IniPath))
            {
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    using (var stream = asm.GetManifestResourceStream("AutoSaver.Resources.autosaver.default.ini"))
                    {
                        if (stream != null)
                        {
                            using (var fs = new FileStream(IniPath, FileMode.Create, FileAccess.Write))
                                stream.CopyTo(fs);
                        }
                    }
                }
                catch { }

                if (!File.Exists(IniPath))
                {
                    var asmVer = Assembly.GetExecutingAssembly().GetName().Version;
                    var verStr = asmVer == null || (asmVer.Major == 0 && asmVer.Minor == 0 && asmVer.Build == 0)
                        ? "1.0.0"
                        : $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}";
                    Write("meta", "version", verStr);
                    Write("global", "theme", "dark");
                    Write("global", "check_interval_sec", "30");
                    Write("global", "start_with_windows", "false");
                    Write("global", "minimize_to_tray_on_close", "true");
                    Write("global", "show_notifications", "true");
                    Write("global", "check_updates_on_startup", "true");
                }
            }

            MergeMissingKeysFromEmbeddedDefaults();

            if (string.IsNullOrWhiteSpace(Read("global", "check_updates_on_startup", "")))
                Write("global", "check_updates_on_startup", "true");
        }

        /// <summary>
        /// 升级或默认模板新增字段时，仅补齐缺失键，不覆盖用户已有值。
        /// </summary>
        private static void MergeMissingKeysFromEmbeddedDefaults()
        {
            try
            {
                var defaults = LoadEmbeddedDefaultIniSections();
                if (defaults == null || defaults.Count == 0)
                    return;

                foreach (var sectionName in defaults.Keys)
                {
                    foreach (var kv in defaults[sectionName])
                    {
                        if (IniKeyExists(IniPath, sectionName, kv.Key))
                            continue;

                        var value = kv.Value;
                        if (sectionName.Equals("meta", StringComparison.OrdinalIgnoreCase)
                            && kv.Key.Equals("version", StringComparison.OrdinalIgnoreCase))
                            value = GetAssemblyVersionForIniMeta();

                        Write(sectionName, kv.Key, value);
                    }
                }
            }
            catch { }
        }

        private static Dictionary<string, Dictionary<string, string>> LoadEmbeddedDefaultIniSections()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("AutoSaver.Resources.autosaver.default.ini"))
                {
                    if (stream == null)
                        return null;
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        var text = reader.ReadToEnd();
                        return ParseIniSections(text);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, Dictionary<string, string>> ParseIniSections(string content)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string section = null;

            foreach (var raw in content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                    continue;

                if (line.Length >= 2 && line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    if (!result.ContainsKey(section))
                        result[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq <= 0 || string.IsNullOrEmpty(section))
                    continue;

                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();
                result[section][key] = val;
            }

            return result;
        }

        private static string GetAssemblyVersionForIniMeta()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var iv = info?.InformationalVersion?.Trim();
                if (!string.IsNullOrEmpty(iv))
                    return iv;

                var ver = asm.GetName().Version;
                if (ver != null && !(ver.Major == 0 && ver.Minor == 0 && ver.Build == 0))
                    return $"{ver.Major}.{ver.Minor}.{ver.Build}";
            }
            catch { }

            return "1.0.0";
        }

        public static List<ProgramItem> LoadPrograms()
        {
            var programs = new List<ProgramItem>();
            for (int i = 1; ; i++)
            {
                var id = Read($"program.{i}", "id");
                if (string.IsNullOrEmpty(id)) break;

                programs.Add(new ProgramItem
                {
                    Id = id,
                    Name = Read($"program.{i}", "name"),
                    Exe = Read($"program.{i}", "exe"),
                    Enabled = Read($"program.{i}", "enabled", "true") == "true",
                    SaveIntervalSec = int.TryParse(Read($"program.{i}", "save_interval_sec", "300"), out var iv) ? iv : 300
                });
            }

            var deduped = DeduplicateProgramsByExe(programs);
            if (deduped.Count != programs.Count)
                SavePrograms(deduped);

            return deduped;
        }

        /// <summary>同一 exe 只保留一条（按配置顺序先出现的为准），用于合并历史重复项。</summary>
        private static List<ProgramItem> DeduplicateProgramsByExe(List<ProgramItem> programs)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<ProgramItem>();
            foreach (var p in programs)
            {
                var key = ProgramItem.NormalizeExeKey(p.Exe);
                if (string.IsNullOrEmpty(key))
                    continue;
                if (!seen.Add(key))
                    continue;
                list.Add(p);
            }

            return list;
        }

        public static void SavePrograms(List<ProgramItem> programs)
        {
            // Write current programs
            for (int i = 0; i < programs.Count; i++)
            {
                var section = $"program.{i + 1}";
                var p = programs[i];
                Write(section, "id", p.Id);
                Write(section, "name", p.Name);
                Write(section, "exe", p.Exe);
                Write(section, "enabled", p.Enabled ? "true" : "false");
                Write(section, "save_interval_sec", p.SaveIntervalSec.ToString());
            }

            // Clean up stale sections beyond the current count
            for (int i = programs.Count + 1; ; i++)
            {
                var id = Read($"program.{i}", "id");
                if (string.IsNullOrEmpty(id)) break;
                WritePrivateProfileString($"program.{i}", null, null, IniPath);
            }
        }
    }
}
