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
    public enum SaveStatus { Success, Failed }

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

    /// <summary>主界面每行：前台匹配项显示剩余时间，其余显示周期提示。</summary>
    public readonly struct ProgramListRow
    {
        public ProgramListRow(string programId, bool isForegroundTarget, int remainingSec, int intervalSec)
        {
            ProgramId = programId ?? "";
            IsForegroundTarget = isForegroundTarget;
            RemainingSec = remainingSec;
            IntervalSec = intervalSec;
        }

        public string ProgramId { get; }
        public bool IsForegroundTarget { get; }
        public int RemainingSec { get; }
        public int IntervalSec { get; }
    }

    public class SaveResult
    {
        public ProgramItem Program { get; set; }
        public SaveStatus Status { get; set; }
        public string Message { get; set; }
        public int WindowCount { get; set; }
        public bool IsPersistentAlert { get; set; }
        public Action JumpAction { get; set; }
    }

    /// <summary>
    /// 每秒一次的定时器驱动调度；保存周期仅使用全局检查间隔（<c>check_interval_sec</c>）。
    /// 每个前台匹配的顶层 HWND 各自在内存中维护剩余倒计时（不写磁盘；进程退出后清空）。
    /// 切换到其它窗口时该 HWND 的倒计时暂停在原值；回到该窗口后继续递减。
    /// 主窗口关闭仅留托盘时定时器仍运行；退出进程时 <see cref="StopAll"/> 清空。
    /// </summary>
    public class SaveScheduler
    {
        private const int TickMs = 1000;

        private readonly List<ProgramState> _programs = new List<ProgramState>();
        private readonly object _lock = new object();
        private Timer _timer;
        private int _globalIntervalSec = 30;
        private int _tickGate;

        /// <summary>每个顶层窗口 HWND 一条倒计时状态（仅内存，不落盘）。</summary>
        private readonly Dictionary<IntPtr, HwndSlot> _hwndSlots = new Dictionary<IntPtr, HwndSlot>();
        private readonly Dictionary<string, int> _consecutiveFailures = new Dictionary<string, int>();

        private sealed class HwndSlot
        {
            public string ProgramId;
            public int RemainingSec;
        }

        public event Action<string, string, int> SaveDone;
        public event Action<SaveResult> SaveCompleted;
        public event Action<FocusCountdownSnapshot> FocusCountdown;
        public event Action<IReadOnlyList<ProgramListRow>> ProgramListTick;

        private class ProgramState
        {
            public ProgramItem Program;
        }

        public void Start()
        {
            lock (_lock)
            {
                StopInternal();
                _timer = new Timer(TickMs);
                _timer.AutoReset = true;
                _timer.Elapsed += OnSecondTick;
                _timer.Start();
            }
        }

        /// <summary>
        /// 保留调用点兼容：托盘模式下不再停止定时器，自动保存与前台监控照常运行；
        /// UI 侧通过无主窗口引用跳过倒计时刷新。
        /// </summary>
        public void SetTrayPaused(bool paused)
        {
        }

        public void SetInterval(int seconds)
        {
            _globalIntervalSec = Math.Max(1, seconds);
            lock (_lock)
            {
                foreach (var kv in _hwndSlots.ToList())
                {
                    var st = FindProgramUnlocked(kv.Value.ProgramId);
                    if (st == null)
                    {
                        _hwndSlots.Remove(kv.Key);
                        continue;
                    }

                    kv.Value.RemainingSec = EffectiveInterval();
                }
            }
        }

        public void AddProgram(ProgramItem prog)
        {
            lock (_lock)
            {
                if (_programs.Any(p => p.Program.Id == prog.Id)) return;
                _programs.Add(new ProgramState { Program = prog });
            }

            WindowService.InvalidatePidSnapshot();
        }

        public void RemoveProgram(string programId)
        {
            lock (_lock)
            {
                _programs.RemoveAll(p => p.Program.Id == programId);
                RemoveSlotsForProgramUnlocked(programId);
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
                    _programs.Add(new ProgramState { Program = prog });

                RemoveSlotsForProgramUnlocked(prog.Id);
            }

            WindowService.InvalidatePidSnapshot();
        }

        public void StopAll()
        {
            lock (_lock)
            {
                StopInternal();
                _programs.Clear();
                _hwndSlots.Clear();
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

        private void RemoveSlotsForProgramUnlocked(string programId)
        {
            var dead = _hwndSlots.Where(kv => kv.Value.ProgramId == programId).Select(kv => kv.Key).ToList();
            foreach (var h in dead)
                _hwndSlots.Remove(h);
        }

        private void PruneStaleSlotsUnlocked()
        {
            var dead = _hwndSlots.Keys.Where(h => !WindowService.IsWindowAlive(h)).ToList();
            foreach (var h in dead)
                _hwndSlots.Remove(h);
        }

        /// <summary>与设置中的全局检查间隔一致。</summary>
        private int EffectiveInterval()
        {
            return Math.Max(1, _globalIntervalSec);
        }

        private ProgramState FindProgramUnlocked(string programId)
        {
            return _programs.FirstOrDefault(p => p.Program.Id == programId);
        }

        /// <summary>在启用项中按稳定顺序匹配前台 exe 文件名。</summary>
        private ProgramItem FindMatchingEnabledProgramUnlocked(string foregroundExeFileName)
        {
            foreach (var s in _programs.OrderBy(x => x.Program.Id, StringComparer.Ordinal))
            {
                if (!s.Program.Enabled) continue;
                if (WindowService.ForegroundExeMatches(s.Program.Exe, foregroundExeFileName))
                    return s.Program;
            }

            return null;
        }

        private void OnSecondTick(object sender, ElapsedEventArgs e)
        {
            if (Interlocked.CompareExchange(ref _tickGate, 1, 0) != 0)
                return;

            var toSave = new List<(ProgramItem Prog, IntPtr Hwnd)>();

            try
            {
                lock (_lock)
                    PruneStaleSlotsUnlocked();

                if (!WindowService.TryGetForegroundProcess(out var fgHwnd, out _, out var exePath))
                {
                    RaiseUiTick(IntPtr.Zero, null);
                    return;
                }

                if (Path.GetFileName(exePath).Equals("autosaver.exe", StringComparison.OrdinalIgnoreCase))
                {
                    RaiseUiTick(IntPtr.Zero, null);
                    return;
                }

                var exeFile = Path.GetFileName(exePath);

                ProgramItem match;
                lock (_lock)
                    match = FindMatchingEnabledProgramUnlocked(exeFile);

                if (match == null)
                {
                    RaiseUiTick(fgHwnd, null);
                    return;
                }

                lock (_lock)
                {
                    if (!_hwndSlots.TryGetValue(fgHwnd, out var slot) || slot.ProgramId != match.Id)
                        slot = new HwndSlot { ProgramId = match.Id, RemainingSec = 0 };

                    if (slot.RemainingSec <= 0)
                        slot.RemainingSec = EffectiveInterval();
                    else
                    {
                        slot.RemainingSec--;
                        if (slot.RemainingSec == 0)
                        {
                            AddSave(toSave, match, fgHwnd);
                            slot.RemainingSec = EffectiveInterval();
                        }
                    }

                    _hwndSlots[fgHwnd] = slot;
                }

                RaiseUiTick(fgHwnd, match);
            }
            finally
            {
                Interlocked.Exchange(ref _tickGate, 0);
            }

            foreach (var item in toSave)
                QueueSaveWindow(item.Prog, item.Hwnd);
        }

        private static void AddSave(List<(ProgramItem Prog, IntPtr Hwnd)> list, ProgramItem p, IntPtr hwnd)
        {
            if (list.Any(x => x.Prog.Id == p.Id && x.Hwnd == hwnd)) return;
            list.Add((p, hwnd));
        }

        private void RaiseUiTick(IntPtr foregroundHwndForCapsule, ProgramItem foregroundMatch)
        {
            FocusCountdownSnapshot snap;
            List<ProgramListRow> rows;
            lock (_lock)
            {
                snap = BuildCapsuleUnsafe(foregroundHwndForCapsule, foregroundMatch);
                rows = BuildProgramListRowsUnlocked(foregroundHwndForCapsule, foregroundMatch);
            }

            ProgramListTick?.Invoke(rows);
            FocusCountdown?.Invoke(snap);
        }

        private FocusCountdownSnapshot BuildCapsuleUnsafe(IntPtr fgHwnd, ProgramItem foregroundMatch)
        {
            if (fgHwnd == IntPtr.Zero || foregroundMatch == null)
                return new FocusCountdownSnapshot(false, "", 0, 0);

            if (!_hwndSlots.TryGetValue(fgHwnd, out var slot) ||
                !string.Equals(slot.ProgramId, foregroundMatch.Id, StringComparison.Ordinal))
                return new FocusCountdownSnapshot(false, "", 0, 0);

            var iv = EffectiveInterval();
            var titleStem = foregroundMatch.GetDisplayTitle();
            var title = WindowService.GetWindowTitle(fgHwnd);
            var label = string.IsNullOrWhiteSpace(title)
                ? titleStem
                : $"{titleStem} · {title}";
            return new FocusCountdownSnapshot(true, label, slot.RemainingSec, iv);
        }

        private List<ProgramListRow> BuildProgramListRowsUnlocked(IntPtr fgHwnd, ProgramItem foregroundMatch)
        {
            var list = new List<ProgramListRow>(_programs.Count);
            HwndSlot fgSlot = null;
            if (fgHwnd != IntPtr.Zero && foregroundMatch != null &&
                _hwndSlots.TryGetValue(fgHwnd, out var s) &&
                string.Equals(s.ProgramId, foregroundMatch.Id, StringComparison.Ordinal))
                fgSlot = s;

            foreach (var st in _programs.OrderBy(x => x.Program.Id, StringComparer.Ordinal))
            {
                var prog = st.Program;
                var iv = EffectiveInterval();
                var isTarget = prog.Enabled && foregroundMatch != null &&
                               string.Equals(prog.Id, foregroundMatch.Id, StringComparison.Ordinal) &&
                               fgSlot != null;
                var rem = isTarget ? fgSlot.RemainingSec : 0;
                list.Add(new ProgramListRow(prog.Id, isTarget, rem, iv));
            }

            return list;
        }

        private void ResetConsecutiveFailures(string programId)
        {
            lock (_lock)
            {
                _consecutiveFailures.Remove(programId);
            }
        }

        private bool IncrementFailureAndCheckThreshold(string programId)
        {
            lock (_lock)
            {
                if (!_consecutiveFailures.ContainsKey(programId))
                    _consecutiveFailures[programId] = 0;
                _consecutiveFailures[programId]++;
                return _consecutiveFailures[programId] >= ConfigService.ErrorThreshold;
            }
        }

        private void QueueSaveWindow(ProgramItem prog, IntPtr expectedHwnd)
        {
            var progRef = prog;
            var hwndRef = expectedHwnd;
            Task.Run(() =>
            {
                try
                {
                    RunSaveWindow(progRef, hwndRef);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SaveScheduler.RunSaveWindow failed for {progRef.Name}: {ex.Message}");
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

        private void RunSaveWindow(ProgramItem prog, IntPtr expectedHwnd)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");

            if (!IsForegroundStillMatch(prog, expectedHwnd))
                return;

            var sent = WindowService.SendCtrlS(expectedHwnd);

            SaveDone?.Invoke(prog.Id, timestamp, 1);

            if (sent)
            {
                ResetConsecutiveFailures(prog.Id);
                SaveCompleted?.Invoke(new SaveResult
                {
                    Program = prog,
                    Status = SaveStatus.Success,
                    Message = "已向前台窗口发送 Ctrl+S",
                    IsPersistentAlert = false
                });
            }
            else
            {
                var isThreshold = IncrementFailureAndCheckThreshold(prog.Id);
                SaveCompleted?.Invoke(new SaveResult
                {
                    Program = prog,
                    Status = SaveStatus.Failed,
                    Message = "发送 Ctrl+S 失败",
                    IsPersistentAlert = isThreshold
                });
            }
        }

        private static bool IsForegroundStillMatch(ProgramItem prog, IntPtr expectedHwnd)
        {
            if (WindowService.GetForegroundWindowHandle() != expectedHwnd)
                return false;
            if (!WindowService.TryMatchForegroundExe(prog.Exe, out var hwnd))
                return false;
            return hwnd == expectedHwnd;
        }
    }
}
