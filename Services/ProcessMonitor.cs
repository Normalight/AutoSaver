using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using AutoSaver.Models;

namespace AutoSaver.Services
{
    public class ProcessMonitor
    {
        private readonly DispatcherTimer _timer;
        private List<ProgramItem> _programs = new List<ProgramItem>();
        private readonly Dictionary<string, bool> _prevState = new Dictionary<string, bool>();

        public event Action<ProgramItem, bool> StatusChanged;

        public ProcessMonitor()
        {
            _timer = new DispatcherTimer();
            _timer.Tick += (s, e) => Poll();
        }

        public void Start(int checkIntervalSec)
        {
            _timer.Interval = TimeSpan.FromSeconds(checkIntervalSec);
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public void RefreshPrograms(List<ProgramItem> programs)
        {
            _programs = new List<ProgramItem>(programs);
            _prevState.Clear();
        }

        public bool GetStatus(string programId)
        {
            return _prevState.TryGetValue(programId, out var running) && running;
        }

        private void Poll()
        {
            HashSet<string> runningExes;
            try
            {
                runningExes = new HashSet<string>(
                    Process.GetProcesses()
                        .Select(p =>
                        {
                            try { return p.ProcessName.ToLowerInvariant(); }
                            catch { return null; }
                        })
                        .Where(n => n != null),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch { return; }

            foreach (var prog in _programs)
            {
                if (!prog.Enabled) continue;
                var exeName = prog.Exe.ToLowerInvariant();
                if (exeName.EndsWith(".exe"))
                    exeName = exeName.Substring(0, exeName.Length - 4);

                var isRunning = runningExes.Contains(exeName);
                _prevState.TryGetValue(prog.Id, out var prev);
                if (prev != isRunning)
                {
                    _prevState[prog.Id] = isRunning;
                    StatusChanged?.Invoke(prog, isRunning);
                }
            }
        }
    }
}
