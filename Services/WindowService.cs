using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoSaver.Services
{
    public static class WindowService
    {
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const int VK_CONTROL = 0x11;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        public static List<IntPtr> GetWindowsByExe(string exeName)
        {
            var pids = new HashSet<int>();
            var exeLower = exeName.ToLowerInvariant();

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.ProcessName.ToLowerInvariant() == exeLower
                        || (proc.ProcessName + ".exe").ToLowerInvariant() == exeLower)
                        pids.Add(proc.Id);
                }
                catch { }
            }

            if (pids.Count == 0) return new List<IntPtr>();

            var hwnds = new List<IntPtr>();
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                if (!IsWindowEnabled(hWnd)) return true;
                GetWindowThreadProcessId(hWnd, out var pid);
                if (pids.Contains((int)pid))
                    hwnds.Add(hWnd);
                return true;
            }, IntPtr.Zero);

            return hwnds;
        }

        public static bool SendCtrlS(IntPtr hWnd)
        {
            try
            {
                PostMessage(hWnd, WM_KEYDOWN, VK_CONTROL, 0);
                PostMessage(hWnd, WM_KEYDOWN, (int)'S', 0);
                PostMessage(hWnd, WM_KEYUP, (int)'S', 0);
                PostMessage(hWnd, WM_KEYUP, VK_CONTROL, 0);
                return true;
            }
            catch { return false; }
        }

        public static string GetWindowTitle(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
