using System;
using System.IO;

namespace AutoSaver.Models
{
    public class ProgramItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Exe { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int SaveIntervalSec { get; set; } = 300;

        /// <summary>
        /// 用于判断是否为同一程序：仅比较文件名、忽略大小写，无后缀时视为 .exe。
        /// </summary>
        public static string NormalizeExeKey(string exe)
        {
            if (string.IsNullOrWhiteSpace(exe)) return "";
            var f = Path.GetFileName(exe.Trim());
            if (string.IsNullOrEmpty(f)) return "";
            if (!f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                f += ".exe";
            return f.ToLowerInvariant();
        }

        public static bool ExeKeysEqual(string a, string b) =>
            NormalizeExeKey(a) == NormalizeExeKey(b);
    }
}
