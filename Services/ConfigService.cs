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
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(
            string lpAppName, string lpKeyName, string lpDefault,
            StringBuilder lpReturnedString, int nSize, string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool WritePrivateProfileString(
            string lpAppName, string lpKeyName, string lpValue, string lpFileName);

        public static string IniPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autosaver.ini");

        internal static string Read(string section, string key, string defaultValue = "")
        {
            var sb = new StringBuilder(512);
            GetPrivateProfileString(section, key, defaultValue, sb, sb.Capacity, IniPath);
            return sb.ToString();
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
            get => Read("global", "check_updates_on_startup", "true") == "true";
            set => Write("global", "check_updates_on_startup", value ? "true" : "false");
        }

        public static string AppVersion
        {
            get => Read("meta", "version", "");
            set => Write("meta", "version", value);
        }

        public static void EnsureDefaults()
        {
            var fileExists = File.Exists(IniPath);
            if (!fileExists)
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
                    var verStr = asmVer == null ? "1.3.6" : $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}";
                    Write("meta", "version", verStr);
                    Write("global", "theme",                    "dark");
                    Write("global", "check_interval_sec",       "30");
                    Write("global", "start_with_windows",       "false");
                    Write("global", "minimize_to_tray_on_close","true");
                    Write("global", "show_notifications",       "true");
                    Write("global", "check_updates_on_startup", "true");
                }
            }

            if (string.IsNullOrWhiteSpace(Read("global", "check_updates_on_startup", "")))
                Write("global", "check_updates_on_startup", "true");
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
            return programs;
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
