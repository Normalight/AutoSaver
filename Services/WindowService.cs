using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Forms;

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
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public static IntPtr GetForegroundWindowHandle() => GetForegroundWindow();

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        private static readonly object PidSnapshotLock = new object();
        private static DateTime _pidSnapshotUtc;
        private static Dictionary<string, HashSet<int>> _pidIndex;
        private static readonly TimeSpan PidSnapshotTtl = TimeSpan.FromSeconds(2);

        /// <summary>HWND 是否仍为有效窗口句柄（用于剔除已关闭窗口）。</summary>
        public static bool IsWindowAlive(IntPtr hWnd)
        {
            return hWnd != IntPtr.Zero && IsWindow(hWnd);
        }

        /// <summary>使进程 PID 快照失效（下次查询会重建）。</summary>
        public static void InvalidatePidSnapshot()
        {
            lock (PidSnapshotLock)
            {
                _pidIndex = null;
            }
        }

        private static void EnsurePidSnapshotFresh()
        {
            lock (PidSnapshotLock)
            {
                if (_pidIndex != null && DateTime.UtcNow - _pidSnapshotUtc < PidSnapshotTtl)
                    return;
                RebuildPidIndexUnlocked();
                _pidSnapshotUtc = DateTime.UtcNow;
            }
        }

        private static void RebuildPidIndexUnlocked()
        {
            var index = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

            void AddPid(string key, int pid)
            {
                if (string.IsNullOrEmpty(key)) return;
                if (!index.TryGetValue(key, out var set))
                {
                    set = new HashSet<int>();
                    index[key] = set;
                }

                set.Add(pid);
            }

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var pn = proc.ProcessName;
                    if (string.IsNullOrEmpty(pn)) continue;
                    var lower = pn.ToLowerInvariant();
                    AddPid(lower, proc.Id);
                    AddPid(lower + ".exe", proc.Id);
                }
                catch { }
            }

            _pidIndex = index;
        }

        private static HashSet<int> ResolvePidsForExe(string exeName)
        {
            EnsurePidSnapshotFresh();
            lock (PidSnapshotLock)
            {
                if (_pidIndex == null) return new HashSet<int>();

                var file = Path.GetFileName((exeName ?? "").Trim());
                if (string.IsNullOrEmpty(file)) return new HashSet<int>();

                var exeLower = file.ToLowerInvariant();
                if (_pidIndex.TryGetValue(exeLower, out var set) && set.Count > 0)
                    return new HashSet<int>(set);

                var baseName = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(baseName)
                    && _pidIndex.TryGetValue(baseName.ToLowerInvariant(), out var set2)
                    && set2.Count > 0)
                    return new HashSet<int>(set2);

                return new HashSet<int>();
            }
        }

        private static List<IntPtr> EnumVisibleEnabledTopLevelForPids(HashSet<int> pids)
        {
            var hwnds = new List<IntPtr>();
            if (pids == null || pids.Count == 0) return hwnds;

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

        public static List<IntPtr> GetWindowsByExe(string exeName)
        {
            var pids = ResolvePidsForExe(exeName);
            return EnumVisibleEnabledTopLevelForPids(pids);
        }

        /// <summary>
        /// Activate each window and send Ctrl+S via SendKeys on the UI thread (STA).
        /// </summary>
        public static void SendCtrlSToWindows(IReadOnlyList<IntPtr> hwnds)
        {
            if (hwnds == null || hwnds.Count == 0) return;

            void Run()
            {
                foreach (var hwnd in hwnds)
                {
                    if (hwnd == IntPtr.Zero) continue;
                    TryActivateAndSendCtrlS(hwnd);
                    Thread.Sleep(100);
                }
            }

            var app = System.Windows.Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.Invoke(Run);
            else
                Run();
        }

        private static void TryActivateAndSendCtrlS(IntPtr hwnd)
        {
            try
            {
                uint selfThreadId = GetCurrentThreadId();
                IntPtr fgHwnd = GetForegroundWindow();
                uint fgThreadId = fgHwnd != IntPtr.Zero
                    ? GetWindowThreadProcessId(fgHwnd, out _)
                    : selfThreadId;
                uint targetThreadId = GetWindowThreadProcessId(hwnd, out uint targetPid);

                bool linkedFg = false;
                bool linkedTarget = false;
                try
                {
                    AllowSetForegroundWindow(-1);

                    if (fgThreadId != selfThreadId && fgThreadId != targetThreadId)
                        linkedFg = AttachThreadInput(selfThreadId, fgThreadId, true);
                    if (targetThreadId != selfThreadId)
                        linkedTarget = AttachThreadInput(selfThreadId, targetThreadId, true);

                    ShowWindow(hwnd, SW_RESTORE);
                    AllowSetForegroundWindow((int)targetPid);
                    SetForegroundWindow(hwnd);
                    Thread.Sleep(120);
                    SendKeys.SendWait("^s");
                }
                finally
                {
                    if (linkedTarget)
                        AttachThreadInput(selfThreadId, targetThreadId, false);
                    if (linkedFg)
                        AttachThreadInput(selfThreadId, fgThreadId, false);
                }
            }
            catch
            {
                try
                {
                    BringToFront(hwnd);
                    Thread.Sleep(120);
                    SendKeys.SendWait("^s");
                }
                catch { }
            }
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

        public static List<IntPtr> GetAllWindowsByExe(string exeName)
        {
            var pids = ResolvePidsForExe(exeName);
            if (pids.Count == 0) return new List<IntPtr>();

            var hwnds = new List<IntPtr>();
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out var pid);
                if (pids.Contains((int)pid))
                    hwnds.Add(hWnd);
                return true;
            }, IntPtr.Zero);

            return hwnds;
        }

        public static void BringToFront(IntPtr hWnd)
        {
            ShowWindow(hWnd, SW_RESTORE);
            // Windows blocks SetForegroundWindow unless the calling thread already owns
            // the foreground. AllowSetForegroundWindow grants the target process permission
            // to steal the foreground, which is required when called from a background thread
            // or after the notification overlay has started hiding.
            GetWindowThreadProcessId(hWnd, out var pid);
            AllowSetForegroundWindow((int)pid);
            SetForegroundWindow(hWnd);
        }

        public static int GetWindowCountByExe(string exeName)
        {
            var pids = ResolvePidsForExe(exeName);
            if (pids.Count == 0) return 0;

            int count = 0;
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out var pid);
                if (pids.Contains((int)pid))
                    count++;
                return true;
            }, IntPtr.Zero);

            return count;
        }

        public static bool TryGetForegroundProcess(out IntPtr hwnd, out int processId, out string exePath)
        {
            hwnd = GetForegroundWindow();
            processId = 0;
            exePath = null;
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            processId = (int)pid;
            exePath = ExecutableMetadataService.TryGetProcessPathById(processId);
            return !string.IsNullOrEmpty(exePath);
        }

        public static bool ExeNamesEqual(string configuredExe, string runningFileName)
        {
            if (string.IsNullOrWhiteSpace(configuredExe) || string.IsNullOrWhiteSpace(runningFileName))
                return false;
            var a = configuredExe.Trim();
            var b = runningFileName.Trim();
            if (!a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) a += ".exe";
            if (!b.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) b += ".exe";
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ForegroundExeMatches(string configuredExe, string foregroundExeFileName)
        {
            if (string.IsNullOrEmpty(foregroundExeFileName)) return false;
            return ExeNamesEqual(configuredExe, foregroundExeFileName);
        }

        /// <summary>
        /// True when foreground window belongs to <paramref name="configuredExe"/>; returns its HWND.
        /// </summary>
        public static bool TryMatchForegroundExe(string configuredExe, out IntPtr foregroundHwnd)
        {
            foregroundHwnd = IntPtr.Zero;
            if (!TryGetForegroundProcess(out var hwnd, out _, out var path)) return false;
            var file = Path.GetFileName(path);
            if (file.Equals("autosaver.exe", StringComparison.OrdinalIgnoreCase)) return false;
            if (!ExeNamesEqual(configuredExe, file)) return false;
            foregroundHwnd = hwnd;
            return foregroundHwnd != IntPtr.Zero;
        }
    }
}
