using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace AutoSaver.Services
{
    public static class ExecutableMetadataService
    {
        private static readonly object IconLock = new object();
        private static readonly Dictionary<string, ImageSource> IconCache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public static string GetProcessPath(Process proc)
        {
            try
            {
                return proc.MainModule?.FileName;
            }
            catch
            {
                try
                {
                    var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, proc.Id);
                    if (hProcess == IntPtr.Zero) return null;

                    var sb = new StringBuilder(1024);
                    var size = sb.Capacity;
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        CloseHandle(hProcess);
                        return sb.ToString();
                    }

                    CloseHandle(hProcess);
                }
                catch { }

                return null;
            }
        }

        public static string GetExePath(string exeName)
        {
            var name = exeName;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);

            foreach (var proc in Process.GetProcessesByName(name))
            {
                var path = GetProcessPath(proc);
                if (!string.IsNullOrEmpty(path)) return path;
            }

            return null;
        }

        public static string GetFriendlyName(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return null;

            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription;
                if (!string.IsNullOrWhiteSpace(info.ProductName)) return info.ProductName;
            }
            catch { }

            return null;
        }

        public static ImageSource GetIcon(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return null;

            lock (IconLock)
            {
                if (IconCache.TryGetValue(exePath, out var cached))
                    return cached;
            }

            ImageSource result = null;
            try
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon == null) return null;
                result = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            catch { return null; }

            lock (IconLock)
            {
                IconCache[exePath] = result;
            }

            return result;
        }
    }
}
