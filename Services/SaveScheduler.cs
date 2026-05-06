using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using AutoSaver.Models;
using Timer = System.Timers.Timer;

namespace AutoSaver.Services
{
    public enum SaveStatus { Success, NeedsConfirm, Failed }

    public class SaveResult
    {
        public ProgramItem Program { get; set; }
        public SaveStatus Status { get; set; }
        public string Message { get; set; }
        public int WindowCount { get; set; }
        public Action JumpAction { get; set; }
    }

    public class SaveScheduler
    {
        private readonly Dictionary<string, TimerInfo> _timers = new Dictionary<string, TimerInfo>();
        private readonly object _lock = new object();

        public event Action<string, string, int> SaveDone;
        public event Action<SaveResult> SaveCompleted;

        private class TimerInfo
        {
            public ProgramItem Program;
            public Timer Timer;
        }

        public void AddProgram(ProgramItem prog)
        {
            lock (_lock)
            {
                if (_timers.ContainsKey(prog.Id)) return;
                var timer = new Timer(prog.SaveIntervalSec * 1000);
                timer.AutoReset = true;
                timer.Elapsed += (s, e) => DoSave(prog);
                _timers[prog.Id] = new TimerInfo { Program = prog, Timer = timer };
            }
        }

        public void RemoveProgram(string programId)
        {
            lock (_lock)
            {
                if (_timers.TryGetValue(programId, out var info))
                {
                    info.Timer.Stop();
                    info.Timer.Dispose();
                    _timers.Remove(programId);
                }
            }
        }

        public void UpdateProgram(ProgramItem prog)
        {
            lock (_lock)
            {
                if (_timers.TryGetValue(prog.Id, out var info))
                {
                    info.Program = prog;
                    info.Timer.Interval = prog.SaveIntervalSec * 1000;
                }
                else
                {
                    AddProgram(prog);
                }
            }
        }

        public void SetRunning(string programId, bool running)
        {
            lock (_lock)
            {
                if (!_timers.TryGetValue(programId, out var info)) return;
                if (running)
                    info.Timer.Start();
                else
                    info.Timer.Stop();
            }
        }

        public void StopAll()
        {
            lock (_lock)
            {
                foreach (var info in _timers.Values)
                {
                    info.Timer.Stop();
                    info.Timer.Dispose();
                }
                _timers.Clear();
            }
        }

        private void DoSave(ProgramItem prog)
        {
            try
            {
                var hwnds = WindowService.GetWindowsByExe(prog.Exe);
                var timestamp = DateTime.Now.ToString("HH:mm:ss");

                if (hwnds.Count == 0)
                {
                    // Program may be running but no visible window — needs attention
                    SaveDone?.Invoke(prog.Id, timestamp, 0);
                    SaveCompleted?.Invoke(new SaveResult
                    {
                        Program = prog,
                        Status = SaveStatus.NeedsConfirm,
                        Message = "未找到可见窗口，请手动确认",
                        WindowCount = 0,
                        JumpAction = () =>
                        {
                            try { Process.Start(prog.Exe); } catch { }
                        }
                    });
                    return;
                }

                foreach (var hwnd in hwnds)
                    WindowService.SendCtrlS(hwnd);

                SaveDone?.Invoke(prog.Id, timestamp, hwnds.Count);
                SaveCompleted?.Invoke(new SaveResult
                {
                    Program = prog,
                    Status = SaveStatus.Success,
                    Message = $"已保存 {hwnds.Count} 个窗口",
                    WindowCount = hwnds.Count
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveScheduler.DoSave failed for {prog.Name}: {ex.Message}");
                SaveCompleted?.Invoke(new SaveResult
                {
                    Program = prog,
                    Status = SaveStatus.Failed,
                    Message = ex.Message,
                    WindowCount = 0
                });
            }
        }
    }
}
