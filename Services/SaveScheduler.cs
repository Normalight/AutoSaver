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
        private readonly List<ProgramState> _programs = new List<ProgramState>();
        private readonly object _lock = new object();
        private Timer _timer;
        private int _intervalSec = 30;

        public event Action<string, string, int> SaveDone;
        public event Action<SaveResult> SaveCompleted;

        private class ProgramState
        {
            public ProgramItem Program;
            public bool Running;
        }

        public void Start()
        {
            lock (_lock)
            {
                StopInternal();
                _timer = new Timer(_intervalSec * 1000);
                _timer.AutoReset = true;
                _timer.Elapsed += OnTimerTick;
                _timer.Start();
            }
        }

        public void SetInterval(int seconds)
        {
            _intervalSec = seconds;
            lock (_lock)
            {
                if (_timer != null)
                {
                    _timer.Interval = seconds * 1000;
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
        }

        public void RemoveProgram(string programId)
        {
            lock (_lock)
            {
                _programs.RemoveAll(p => p.Program.Id == programId);
            }
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
            }
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
            }
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

        private void OnTimerTick(object sender, ElapsedEventArgs e)
        {
            // Take a snapshot of programs that need saving (enabled + running)
            List<ProgramItem> toSave;
            lock (_lock)
            {
                toSave = _programs
                    .Where(p => p.Program.Enabled && p.Running)
                    .Select(p => p.Program)
                    .ToList();
            }

            // Save sequentially
            foreach (var prog in toSave)
            {
                DoSave(prog);
            }
        }

        private void DoSave(ProgramItem prog)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");

                var before = WindowService.GetWindowCountByExe(prog.Exe);
                if (before == 0)
                {
                    SaveDone?.Invoke(prog.Id, timestamp, 0);
                    return;
                }

                var hwnds = WindowService.GetWindowsByExe(prog.Exe);
                foreach (var hwnd in hwnds)
                    WindowService.SendCtrlS(hwnd);

                System.Threading.Thread.Sleep(200);

                var after = WindowService.GetWindowCountByExe(prog.Exe);

                if (after > before)
                {
                    SaveDone?.Invoke(prog.Id, timestamp, hwnds.Count);
                    SaveCompleted?.Invoke(new SaveResult
                    {
                        Program = prog,
                        Status = SaveStatus.NeedsConfirm,
                        Message = "检测到保存对话框，请手动选择保存位置",
                        WindowCount = hwnds.Count,
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
