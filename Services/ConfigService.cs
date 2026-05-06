using System;
using System.Collections.Generic;
using System.IO;
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

        private static string Read(string section, string key, string defaultValue = "")
        {
            var sb = new StringBuilder(512);
            GetPrivateProfileString(section, key, defaultValue, sb, sb.Capacity, IniPath);
            return sb.ToString();
        }

        private static void Write(string section, string key, string value)
        {
            WritePrivateProfileString(section, key, value, IniPath);
        }

        public static int CheckIntervalSec
        {
            get
            {
                var v = Read("global", "check_interval_sec", "3");
                return int.TryParse(v, out var n) && n > 0 ? n : 3;
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

        public static List<ProgramItem> LoadPrograms()
        {
            var programs = new List<ProgramItem>();
            var countStr = Read("programs", "count", "0");
            if (!int.TryParse(countStr, out var count)) return programs;

            for (int i = 1; i <= count; i++)
            {
                var section = $"program.{i}";
                var id = Read(section, "id");
                if (string.IsNullOrEmpty(id)) continue;

                programs.Add(new ProgramItem
                {
                    Id = id,
                    Name = Read(section, "name"),
                    Exe = Read(section, "exe"),
                    Enabled = Read(section, "enabled", "true") == "true",
                    SaveIntervalSec = int.TryParse(Read(section, "save_interval_sec", "300"), out var iv) ? iv : 300
                });
            }
            return programs;
        }

        public static void SavePrograms(List<ProgramItem> programs)
        {
            Write("programs", "count", programs.Count.ToString());
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
        }
    }
}
