using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using AutoSaver.Models;
using Timer = System.Timers.Timer;

namespace AutoSaver.Services
{
    public enum SaveStatus { Success, NeedsConfirm, Failed }

    public readonly struct FocusCountdownSnapshot
    {
        public FocusCountdownSnapshot(bool showCapsule, string programDisplayName, int remainingSec, int intervalSec)
        {
            ShowCapsule = showCapsule;
            ProgramDisplayName = programDisplayName ?? "";
            RemainingSec = remainingSec;
            IntervalSec = intervalSec;
        }

        public bool ShowCapsule { get; }
        public string ProgramDisplayName { get; }
        public int RemainingSec { get; }
        public int IntervalSec { get; }
    }

    /// <summary>One top-level window row for the main list.</summary>
    public readonly struct WindowCountdownRow
    {
        public WindowCountdownRow(string programId, long hwndValue, string windowTitle, int remainingSec, int intervalSec,
            bool active)
        {
            ProgramId = programId ?? "";
            HwndValue = hwndValue;
            WindowTitle = windowTitle ?? "";
            RemainingSec = remainingSec;
            IntervalSec = intervalSec;
            Active = active;
        }

        public string ProgramId { get; }
        public long HwndValue { get; }
        public string WindowTitle { get; }
        public int RemainingSec { get; }
        public int IntervalSec { get; }
        public bool Active { get; }
    }

    public class SaveResult
    {
        public ProgramItem Program { get; set; }
        public SaveStatus Status { get; set; }
        public string Message { get; set; }
        public int WindowCount { get; set; }
        public Action JumpAction { get; set; }
    }

    /// <summary>
    /// Each visible top-level window has its own countdown. Synced from enumeration each tick.
    /// </summary>
    public class SaveScheduler
    {
        private const int TickMs = 1000;

        private readonly List<ProgramState> _programs = new List<ProgramState>();
        private readonly Dictionary<IntPtr, WindowSlot> _windowTimers = new Dictionary<IntPtr, WindowSlot>();
        private readonly object _lock = new object();
        private Timer _timer;
        private int _globalIntervalSec = 30;
        private IntPtr? _lastForegroundHwnd;
        private int _tickGate;

        /// <summary>距上次全量枚举窗口的最小间隔（减轻 EnumWindows / 快照压力）。</summary>
        private static readonly TimeSpan WindowScanInterval = TimeSpan.FromSeconds(1.5);

        private DateTime _nextFullWindowScanUtc = DateTime.MinValue;

        public event Action<string, string, int> SaveDone;
        public event Action<SaveResult> SaveCompleted;
        public event Action<FocusCountdownSnapshot> FocusCountdown;
        public event Action<IReadOnlyList<WindowCountdownRow>> ProgramListTick;

        private class ProgramState
        {
            public ProgramItem Program;
            public bool Running;
        }

        private class WindowSlot
        {
            public string ProgramId;
            public int RemainingSec;
        }

        public void Start()
        {
            lock (_lock)
            {
                StopInternal();
                _nextFullWindowScanUtc = DateTime.MinValue;
                _timer = new Timer(TickMs);
                _timer.AutoReset = true;
                _timer.Elapsed += OnSecondTick;
                _timer.Start();
            }
        }

        public void SetInterval(int seconds)
        {
            _globalIntervalSec = Math.Max(1, seconds);
            lock (_lock)
            {
                foreach (var kv in _windowTimers.ToList())
                {
                    var prog = FindProgramUnlocked(kv.Value.ProgramId);
                    if (prog != null)
                        kv.Value.RemainingSec = EffectiveInterval(prog.Program);
                }
            }
        }

        public void AddProgram(ProgramItem prog)
        {
            lock (_lock)
            {
                if (_programs.Any(p => p.Program.Id == prog.Id)) return;
                _programs.Add(new ProgramState { Program = prog, Running = false });
            }

            WindowService.InvalidatePidSnapshot();
        }

        public void RemoveProgram(string programId)
        {
            lock (_lock)
            {
                _programs.RemoveAll(p => p.Program.Id == programId);
                foreach (var key in _windowTimers.Keys.Where(k => _windowTimers[k].ProgramId == programId).ToList())
                    _windowTimers.Remove(key);
            }

            WindowService.InvalidatePidSnapshot();
        }

        public void UpdateProgram(ProgramItem prog)
        {
            lock (_lock)
            {
                var idx = _programs.FindIndex(p => p.Program.Id == prog.Id);
                if (idx >= 0)
                    _programs[idx].Program = prog;
                else
                    _programs.Add(new ProgramState { Program = prog, Running = false });

                foreach (var kv in _windowTimers.Where(x => x.Value.ProgramId == prog.Id).ToList())
                    kv.Value.RemainingSec = EffectiveInterval(prog);
            }

            WindowService.InvalidatePidSnapshot();
        }

        public void SetRunning(string programId, bool running)
        {
            lock (_lock)
            {
                var state = _programs.FirstOrDefault(p => p.Program.Id == programId);
                if (state != null)
                    state.Running = running;
            }
        }

        public void StopAll()
        {
            lock (_lock)
            {
                StopInternal();
                _programs.Clear();
                _windowTimers.Clear();
            }

            WindowService.InvalidatePidSnapshot();
        }

        private void StopInternal()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }
        }

        private int EffectiveInterval(ProgramItem prog)
        {
            var sec = prog.SaveIntervalSec > 0 ? prog.SaveIntervalSec : _globalIntervalSec;
            return Math.Max(1, sec);
        }

        private ProgramState FindProgramUnlocked(string programId)
        {
            return _programs.FirstOrDefault(p => p.Program.Id == programId);
        }

        private void SyncWindowTimersUnlocked()
        {
            var live = new HashSet<IntPtr>();

            foreach (var s in _programs.OrderBy(x => x.Program.Id, StringComparer.Ordinal))
            {
                if (!s.Program.Enabled || !s.Running) continue;

                List<IntPtr> hwnds;
                try
                {
                    hwnds = WindowService.GetWindowsByExe(s.Program.Exe);
                }
                catch
                {
                    continue;
                }

                foreach (var hwnd in hwnds)
                {
                    live.Add(hwnd);
                    if (!_windowTimers.TryGetValue(hwnd, out _))
                    {
                        _windowTimers[hwnd] = new WindowSlot
                        {
                            ProgramId = s.Program.Id,
                            RemainingSec = EffectiveInterval(s.Program)
                        };
                    }
                }
            }

            foreach (var key in _windowTimers.Keys.Where(k => !live.Contains(k)).ToList())
                _windowTimers.Remove(key);
        }

        private void PruneDeadHwndsUnlocked()
        {
            foreach (var key in _windowTimers.Keys.ToList())
            {
                if (!WindowService.IsWindowAlive(key))
                    _windowTimers.Remove(key);
            }
        }

        /// <summary>在非全量枚举周期内也能及时去掉已禁用/未运行的槽位。</summary>
        private void RemoveSlotsForInactiveProgramsUnlocked()
        {
            foreach (var kv in _windowTimers.ToList())
            {
                var st = FindProgramUnlocked(kv.Value.ProgramId);
                if (st == null || !st.Program.Enabled || !st.Running)
                    _windowTimers.Remove(kv.Key);
            }
        }

        private static void AddSave(List<(ProgramItem Prog, IntPtr Hwnd)> list, ProgramItem p, IntPtr hwnd)
        {
            if (list.Any(x => x.Prog.Id == p.Id && x.Hwnd == hwnd)) return;
            list.Add((p, hwnd));
        }

        private void OnSecondTick(object sender, ElapsedEventArgs e)
        {
            if (Interlocked.CompareExchange(ref _tickGate, 1, 0) != 0)
                return;

            var toSave = new List<(ProgramItem Prog, IntPtr Hwnd)>();

            try
            {
                if (!WindowService.TryGetForegroundProcess(out var fgHwnd, out _, out var exePath))
                {
                    _lastForegroundHwnd = null;
                    RaiseUiTick(IntPtr.Zero);
                    return;
                }

                var exeFile = Path.GetFileName(exePath);

                if (exeFile.Equals("autosaver.exe", StringComparison.OrdinalIgnoreCase))
                {
                    _lastForegroundHwnd = fgHwnd;
                    RaiseUiTick(IntPtr.Zero);
                    return;
                }

                var transition = _lastForegroundHwnd.HasValue && _lastForegroundHwnd.Value != fgHwnd;
                _lastForegroundHwnd = fgHwnd;

                lock (_lock)
                {
                    var utc = DateTime.UtcNow;
                    if (utc >= _nextFullWindowScanUtc)
                    {
                        SyncWindowTimersUnlocked();
                        _nextFullWindowScanUtc = utc.Add(WindowScanInterval);
                    }
                    else
                    {
                        PruneDeadHwndsUnlocked();
                    }

                    RemoveSlotsForInactiveProgramsUnlocked();

                    if (transition
                        && _windowTimers.TryGetValue(fgHwnd, out var pend)
                        && pend.RemainingSec == 0)
                    {
                        var progState = FindProgramUnlocked(pend.ProgramId);
                        if (progState != null && progState.Program.Enabled && progState.Running)
                        {
                            AddSave(toSave, progState.Program, fgHwnd);
                            pend.RemainingSec = EffectiveInterval(progState.Program);
                        }
                    }

                    foreach (var kv in _windowTimers.ToList())
                    {
                        var progState = FindProgramUnlocked(kv.Value.ProgramId);
                        if (progState == null || !progState.Program.Enabled || !progState.Running)
                            continue;

                        if (kv.Value.RemainingSec <= 0)
                            continue;

                        kv.Value.RemainingSec--;
                        if (kv.Value.RemainingSec == 0 && fgHwnd == kv.Key)
                        {
                            AddSave(toSave, progState.Program, kv.Key);
                            kv.Value.RemainingSec = EffectiveInterval(progState.Program);
                        }
                    }
                }

                RaiseUiTick(fgHwnd);
            }
            finally
            {
                Interlocked.Exchange(ref _tickGate, 0);
            }

            foreach (var item in toSave)
                QueueSaveWindow(item.Prog, item.Hwnd);
        }

        private void RaiseUiTick(IntPtr foregroundHwndForCapsule)
        {
            FocusCountdownSnapshot snap;
            List<WindowCountdownRow> rows;
            lock (_lock)
            {
                snap = BuildCapsuleUnsafe(foregroundHwndForCapsule);
                rows = BuildWindowRowsUnlocked();
            }

            ProgramListTick?.Invoke(rows);
            FocusCountdown?.Invoke(snap);
        }

        private FocusCountdownSnapshot BuildCapsuleUnsafe(IntPtr fgHwnd)
        {
            if (fgHwnd == IntPtr.Zero || !_windowTimers.TryGetValue(fgHwnd, out var slot))
                return new FocusCountdownSnapshot(false, "", 0, 0);

            var st = FindProgramUnlocked(slot.ProgramId);
            if (st == null || !st.Program.Enabled || !st.Running)
                return new FocusCountdownSnapshot(false, "", 0, 0);

            var iv = EffectiveInterval(st.Program);
            var stem = ProgramItem.GetExeStemDisplay(st.Program.Exe);
            if (string.IsNullOrWhiteSpace(stem))
                stem = st.Program.Name;
            var title = WindowService.GetWindowTitle(fgHwnd);
            var label = string.IsNullOrWhiteSpace(title)
                ? stem
                : $"{stem} · {title}";
            return new FocusCountdownSnapshot(true, label, slot.RemainingSec, iv);
        }

        private List<WindowCountdownRow> BuildWindowRowsUnlocked()
        {
            var list = new List<WindowCountdownRow>(_windowTimers.Count);
            foreach (var kv in _windowTimers.OrderBy(x => x.Value.ProgramId).ThenBy(x => x.Key.ToInt64()))
            {
                var st = FindProgramUnlocked(kv.Value.ProgramId);
                var prog = st?.Program;
                if (prog == null) continue;

                var title = WindowService.GetWindowTitle(kv.Key);
                if (string.IsNullOrWhiteSpace(title))
                    title = "（无标题）";

                var active = prog.Enabled && st.Running;
                list.Add(new WindowCountdownRow(
                    prog.Id,
                    kv.Key.ToInt64(),
                    title,
                    kv.Value.RemainingSec,
                    EffectiveInterval(prog),
                    active));
            }

            return list;
        }

        private void QueueSaveWindow(ProgramItem prog, IntPtr expectedHwnd)
        {
            var progRef = prog;
            var hwndRef = expectedHwnd;
            Task.Run(async () =>
            {
                try
                {
                    await RunSaveWindowAsync(progRef, hwndRef).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SaveScheduler.RunSaveWindowAsync failed for {progRef.Name}: {ex.Message}");
                    SaveCompleted?.Invoke(new SaveResult
                    {
                        Program = progRef,
                        Status = SaveStatus.Failed,
                        Message = ex.Message,
                        WindowCount = 0
                    });
                }
            });
        }

        private async Task RunSaveWindowAsync(ProgramItem prog, IntPtr expectedHwnd)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");

            if (WindowService.GetForegroundWindowHandle() != expectedHwnd)
                return;

            if (!WindowService.TryMatchForegroundExe(prog.Exe, out var hwnd))
                return;

            if (hwnd != expectedHwnd)
                return;

            var before = WindowService.GetWindowCountByExe(prog.Exe);
            if (before == 0)
            {
                SaveDone?.Invoke(prog.Id, timestamp, 0);
                return;
            }

            WindowService.SendCtrlSToWindows(new List<IntPtr> { hwnd });

            await Task.Delay(350).ConfigureAwait(false);

            var after = WindowService.GetWindowCountByExe(prog.Exe);

            if (after > before)
            {
                SaveDone?.Invoke(prog.Id, timestamp, 1);
                SaveCompleted?.Invoke(new SaveResult
                {
                    Program = prog,
                    Status = SaveStatus.NeedsConfirm,
                    Message = "检测到保存对话框，请手动选择保存位置",
                    WindowCount = 1,
                    JumpAction = () =>
                    {
                        try
                        {
                            var all = WindowService.GetAllWindowsByExe(prog.Exe);
                            if (all.Count > 0)
                                WindowService.BringToFront(all[all.Count - 1]);
                        }
                        catch { }
                    }
                });
                return;
            }

            SaveDone?.Invoke(prog.Id, timestamp, 1);
            SaveCompleted?.Invoke(new SaveResult
            {
                Program = prog,
                Status = SaveStatus.Success,
                Message = "已向前台窗口发送 Ctrl+S（是否写入文件由目标程序决定）",
                WindowCount = 1
            });
        }
    }
}
