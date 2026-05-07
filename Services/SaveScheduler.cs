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
    /// 全局单一倒计时：不因切换窗口或更换前台监控程序而重置；仅在前台不在白名单时暂停递减；
    /// 主窗口关闭仅留托盘时仍保持 Timer 运行（自动保存继续）；退出进程时 <see cref="StopAll"/> 清空。
    /// </summary>
    public class SaveScheduler
    {
        private const int TickMs = 1000;

        private readonly List<ProgramState> _programs = new List<ProgramState>();
        private readonly object _lock = new object();
        private Timer _timer;
        private int _globalIntervalSec = 30;
        private int _tickGate;

        private IntPtr _slotHwnd = IntPtr.Zero;
        private string _slotProgramId;
        private int _slotRemainingSec;
        private readonly Dictionary<string, int> _consecutiveFailures = new Dictionary<string, int>();

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
                if (!string.IsNullOrEmpty(_slotProgramId))
                {
                    var st = FindProgramUnlocked(_slotProgramId);
                    if (st != null)
                        _slotRemainingSec = EffectiveInterval();
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
                if (_slotProgramId == programId)
                    ClearSlotUnlocked();
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

                if (_slotProgramId == prog.Id)
                    _slotRemainingSec = EffectiveInterval();
            }

            WindowService.InvalidatePidSnapshot();
        }

        public void StopAll()
        {
            lock (_lock)
            {
                StopInternal();
                _programs.Clear();
                ClearSlotUnlocked();
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

        private void ClearSlotUnlocked()
        {
            _slotHwnd = IntPtr.Zero;
            _slotProgramId = null;
            _slotRemainingSec = 0;
        }

        /// <summary>保存/倒计时周期仅使用全局设置（<c>check_interval_sec</c>），不再按程序单独间隔。</summary>
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
                    _slotHwnd = fgHwnd;
                    _slotProgramId = match.Id;

                    if (_slotRemainingSec <= 0)
                        _slotRemainingSec = EffectiveInterval();
                    else
                        _slotRemainingSec--;

                    if (_slotRemainingSec == 0)
                    {
                        AddSave(toSave, match, fgHwnd);
                        _slotRemainingSec = EffectiveInterval();
                    }
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
            if (fgHwnd == IntPtr.Zero || foregroundMatch == null ||
                _slotHwnd != fgHwnd || _slotProgramId != foregroundMatch.Id)
                return new FocusCountdownSnapshot(false, "", 0, 0);

            var iv = EffectiveInterval();
            var titleStem = foregroundMatch.GetDisplayTitle();
            var title = WindowService.GetWindowTitle(fgHwnd);
            var label = string.IsNullOrWhiteSpace(title)
                ? titleStem
                : $"{titleStem} · {title}";
            return new FocusCountdownSnapshot(true, label, _slotRemainingSec, iv);
        }

        private List<ProgramListRow> BuildProgramListRowsUnlocked(IntPtr fgHwnd, ProgramItem foregroundMatch)
        {
            var list = new List<ProgramListRow>(_programs.Count);
            foreach (var s in _programs.OrderBy(x => x.Program.Id, StringComparer.Ordinal))
            {
                var prog = s.Program;
                var iv = EffectiveInterval();
                var isTarget = prog.Enabled && foregroundMatch != null &&
                               string.Equals(prog.Id, foregroundMatch.Id, StringComparison.Ordinal) &&
                               fgHwnd != IntPtr.Zero && _slotHwnd == fgHwnd &&
                               string.Equals(_slotProgramId, prog.Id, StringComparison.Ordinal);
                var rem = isTarget ? _slotRemainingSec : 0;
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
