# AutoSaver C# WPF Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rewrite AutoSaver as a C# WPF (.NET Framework 4.8) tray app — zero NuGet deps, INI config, dynamic tray menu with status dots, destroy-on-close main window, two-method program add.

**Architecture:** WPF entry point skips window creation, starts tray directly. NotifyIcon with ContextMenuStrip rebuilt on every Opening event. 4 services (Config, Window, Monitor, Scheduler) are long-lived; 3 views (MainWindow, AddEditDialog, ProcessPickerDialog) are created/destroyed per use.

**Tech Stack:** C# 7.3 / .NET Framework 4.8 / WPF / Windows Forms NotifyIcon / P/Invoke / kernel32 INI API

---

## Dependency Graph

```
Group A (parallel, no deps):
  Task 1: .csproj + ProgramItem.cs + App.xaml/.cs
  Task 2: ConfigService.cs
  Task 3: WindowService.cs

Group B (parallel, depends on A):
  Task 4: ProcessMonitor.cs
  Task 5: SaveScheduler.cs

Group C (parallel, depends on A):
  Task 6: ProcessPickerDialog.xaml/.cs
  Task 7: AddEditDialog.xaml/.cs
  Task 8: MainWindow.xaml/.cs
```

After all tasks, App.xaml.cs is updated to wire everything.

---

### Task 1: Project scaffolding + data model + entry point

**Files:**
- Create: `AutoSaver.csproj`
- Create: `Models/ProgramItem.cs`
- Create: `Models/ProgramDisplay.cs`
- Create: `App.xaml`
- Create: `App.xaml.cs`

- [ ] **Step 1: Write AutoSaver.csproj**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
    <ProjectGuid>{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}</ProjectGuid>
    <OutputType>WinExe</OutputType>
    <RootNamespace>AutoSaver</RootNamespace>
    <AssemblyName>autosaver</AssemblyName>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
    <FileAlignment>512</FileAlignment>
    <ProjectTypeGuids>{60dc8134-eba5-43b8-bcc9-bb4bc16c2548};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>
    <WarningLevel>4</WarningLevel>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
    <PlatformTarget>AnyCPU</PlatformTarget>
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <OutputPath>bin\Debug\</OutputPath>
    <DefineConstants>DEBUG;TRACE</DefineConstants>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
    <PlatformTarget>AnyCPU</PlatformTarget>
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>bin\Release\</OutputPath>
    <DefineConstants>TRACE</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="System" />
    <Reference Include="System.Drawing" />
    <Reference Include="System.Windows.Forms" />
    <Reference Include="WindowsBase" />
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="System.Xaml" />
  </ItemGroup>
  <ItemGroup>
    <ApplicationDefinition Include="App.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </ApplicationDefinition>
    <Compile Include="App.xaml.cs">
      <DependentUpon>App.xaml</DependentUpon>
      <SubType>Code</SubType>
    </Compile>
    <Compile Include="Models\ProgramItem.cs" />
    <Compile Include="Models\ProgramDisplay.cs" />
    <Compile Include="Services\ConfigService.cs" />
    <Compile Include="Services\WindowService.cs" />
    <Compile Include="Services\ProcessMonitor.cs" />
    <Compile Include="Services\SaveScheduler.cs" />
    <Page Include="Views\MainWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Compile Include="Views\MainWindow.xaml.cs">
      <DependentUpon>MainWindow.xaml</DependentUpon>
      <SubType>Code</SubType>
    </Compile>
    <Page Include="Views\AddEditDialog.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Compile Include="Views\AddEditDialog.xaml.cs">
      <DependentUpon>AddEditDialog.xaml</DependentUpon>
      <SubType>Code</SubType>
    </Compile>
    <Page Include="Views\ProcessPickerDialog.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Compile Include="Views\ProcessPickerDialog.xaml.cs">
      <DependentUpon>ProcessPickerDialog.xaml</DependentUpon>
      <SubType>Code</SubType>
    </Compile>
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

- [ ] **Step 2: Write Models/ProgramItem.cs**

```csharp
using System;

namespace AutoSaver.Models
{
    public class ProgramItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Exe { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int SaveIntervalSec { get; set; } = 300;
    }
}
```

- [ ] **Step 3: Write Models/ProgramDisplay.cs**

```csharp
namespace AutoSaver.Models
{
    public class ProgramDisplay
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Exe { get; set; }
        public string StatusText { get; set; }
        public string StatusColor { get; set; }
        public string IntervalText { get; set; }
    }
}
```

- [ ] **Step 4: Write App.xaml**

```xml
<Application x:Class="AutoSaver.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             Startup="OnStartup">
</Application>
```

- [ ] **Step 4: Write App.xaml.cs (scaffolding only)**

```csharp
using System;
using System.Windows;

namespace AutoSaver
{
    public partial class App : Application
    {
        private void OnStartup(object sender, StartupEventArgs e)
        {
            // Wiring done in Task 9 after all services/views exist
        }
    }
}
```

- [ ] **Step 5: Commit**

```
git add AutoSaver.csproj Models/ProgramItem.cs App.xaml App.xaml.cs
git commit -m "feat: add project scaffolding, data model, and app entry"
```

---

### Task 2: ConfigService.cs

**Files:**
- Create: `Services/ConfigService.cs`

- [ ] **Step 1: Write Services/ConfigService.cs**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AutoSaver.Models;

namespace AutoSaver.Services
{
    public static class ConfigService
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(
            string lpAppName, string lpKeyName, string lpDefault,
            StringBuilder lpReturnedString, int nSize, string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool WritePrivateProfileString(
            string lpAppName, string lpKeyName, string lpValue, string lpFileName);

        public static string IniPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autosaver.ini");

        private static string Read(string section, string key, string defaultValue = "")
        {
            var sb = new StringBuilder(512);
            GetPrivateProfileString(section, key, defaultValue, sb, sb.Capacity, IniPath);
            return sb.ToString();
        }

        private static void Write(string section, string key, string value)
        {
            WritePrivateProfileString(section, key, value, IniPath);
        }

        public static int CheckIntervalSec
        {
            get
            {
                var v = Read("global", "check_interval_sec", "3");
                return int.TryParse(v, out var n) && n > 0 ? n : 3;
            }
            set => Write("global", "check_interval_sec", value.ToString());
        }

        public static bool StartWithWindows
        {
            get => Read("global", "start_with_windows", "false") == "true";
            set => Write("global", "start_with_windows", value ? "true" : "false");
        }

        public static bool MinimizeToTrayOnClose
        {
            get => Read("global", "minimize_to_tray_on_close", "true") != "false";
            set => Write("global", "minimize_to_tray_on_close", value ? "true" : "false");
        }

        public static List<ProgramItem> LoadPrograms()
        {
            var programs = new List<ProgramItem>();
            var countStr = Read("programs", "count", "0");
            if (!int.TryParse(countStr, out var count)) return programs;

            for (int i = 1; i <= count; i++)
            {
                var section = $"program.{i}";
                var id = Read(section, "id");
                if (string.IsNullOrEmpty(id)) continue;

                programs.Add(new ProgramItem
                {
                    Id = id,
                    Name = Read(section, "name"),
                    Exe = Read(section, "exe"),
                    Enabled = Read(section, "enabled", "true") == "true",
                    SaveIntervalSec = int.TryParse(Read(section, "save_interval_sec", "300"), out var iv) ? iv : 300
                });
            }
            return programs;
        }

        public static void SavePrograms(List<ProgramItem> programs)
        {
            Write("programs", "count", programs.Count.ToString());
            for (int i = 0; i < programs.Count; i++)
            {
                var section = $"program.{i + 1}";
                var p = programs[i];
                Write(section, "id", p.Id);
                Write(section, "name", p.Name);
                Write(section, "exe", p.Exe);
                Write(section, "enabled", p.Enabled ? "true" : "false");
                Write(section, "save_interval_sec", p.SaveIntervalSec.ToString());
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```
git add Services/ConfigService.cs
git commit -m "feat: add INI config service via kernel32 API"
```

---

### Task 3: WindowService.cs

**Files:**
- Create: `Services/WindowService.cs`

- [ ] **Step 1: Write Services/WindowService.cs**

```csharp
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
```

- [ ] **Step 2: Commit**

```
git add Services/WindowService.cs
git commit -m "feat: add P/Invoke window service for enumeration and Ctrl+S"
```

---

### Task 4: ProcessMonitor.cs

**Files:**
- Create: `Services/ProcessMonitor.cs`

- [ ] **Step 1: Write Services/ProcessMonitor.cs**

```csharp
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
            _programs = programs;
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
                // strip .exe extension for matching
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
```

- [ ] **Step 2: Commit**

```
git add Services/ProcessMonitor.cs
git commit -m "feat: add process monitor with DispatcherTimer polling"
```

---

### Task 5: SaveScheduler.cs

**Files:**
- Create: `Services/SaveScheduler.cs`

- [ ] **Step 1: Write Services/SaveScheduler.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using AutoSaver.Models;
using Timer = System.Timers.Timer;

namespace AutoSaver.Services
{
    public class SaveScheduler
    {
        private readonly Dictionary<string, TimerInfo> _timers = new Dictionary<string, TimerInfo>();
        private readonly object _lock = new object();

        public event Action<string, string, int> SaveDone; // programId, timestamp, windowCount

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
                foreach (var hwnd in hwnds)
                    WindowService.SendCtrlS(hwnd);

                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                SaveDone?.Invoke(prog.Id, timestamp, hwnds.Count);
            }
            catch { }
        }
    }
}
```

- [ ] **Step 2: Commit**

```
git add Services/SaveScheduler.cs
git commit -m "feat: add save scheduler with per-program System.Timers.Timer"
```

---

### Task 6: ProcessPickerDialog

**Files:**
- Create: `Views/ProcessPickerDialog.xaml`
- Create: `Views/ProcessPickerDialog.xaml.cs`

- [ ] **Step 1: Write ProcessPickerDialog.xaml**

```xml
<Window x:Class="AutoSaver.Views.ProcessPickerDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        Title="选择正在运行的程序" Height="350" Width="300"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <DockPanel Margin="8">
        <TextBlock DockPanel.Dock="Top" Text="双击选择进程：" Margin="0,0,0,6"/>
        <ListBox x:Name="ProcessList" DisplayMemberPath="DisplayName"
                 MouseDoubleClick="OnItemDoubleClick"/>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Write ProcessPickerDialog.xaml.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace AutoSaver.Views
{
    public partial class ProcessPickerDialog : Window
    {
        private static readonly HashSet<string> Blacklist = new HashSet<string>(
            new[] { "system", "system idle process", "svchost", "csrss", "smss",
                    "wininit", "services", "lsass", "winlogon", "explorer",
                    "taskmgr", "autosaver" },
            StringComparer.OrdinalIgnoreCase);

        public string SelectedProcessName { get; private set; }

        public ProcessPickerDialog()
        {
            InitializeComponent();
            LoadProcesses();
        }

        private void LoadProcesses()
        {
            var exes = new SortedSet<string>();
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var name = proc.ProcessName;
                    if (!string.IsNullOrEmpty(name) && !Blacklist.Contains(name))
                        exes.Add(name + ".exe");
                }
                catch { }
            }

            ProcessList.ItemsSource = exes.Select(e => new { DisplayName = e }).ToList();
        }

        private void OnItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ProcessList.SelectedItem != null)
            {
                dynamic item = ProcessList.SelectedItem;
                SelectedProcessName = item.DisplayName;
                DialogResult = true;
                Close();
            }
        }
    }
}
```

- [ ] **Step 3: Commit**

```
git add Views/ProcessPickerDialog.xaml Views/ProcessPickerDialog.xaml.cs
git commit -m "feat: add process picker dialog for running process selection"
```

---

### Task 7: AddEditDialog

**Files:**
- Create: `Views/AddEditDialog.xaml`
- Create: `Views/AddEditDialog.xaml.cs`

- [ ] **Step 1: Write AddEditDialog.xaml**

```xml
<Window x:Class="AutoSaver.Views.AddEditDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        Title="添加程序" Height="280" Width="400"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <StackPanel Margin="12">
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,0,0,10">
            <Button x:Name="BrowseButton" Content="&#x1F4C1; 选择本地程序"
                    Width="150" Height="36" Margin="0,0,8,0" Click="OnBrowseClick"/>
            <Button x:Name="PickRunningButton" Content="&#x1F4CB; 正在运行中选取"
                    Width="150" Height="36" Click="OnPickRunningClick"/>
        </StackPanel>

        <Grid Margin="0,6">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="70"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="32"/>
                <RowDefinition Height="32"/>
                <RowDefinition Height="32"/>
                <RowDefinition Height="32"/>
            </Grid.RowDefinitions>

            <TextBlock Text="显示名称:" VerticalAlignment="Center" Grid.Row="0" Grid.Column="0"/>
            <TextBox x:Name="NameBox" Grid.Row="0" Grid.Column="1" Margin="4,2" Height="24"/>

            <TextBlock Text="进程名:" VerticalAlignment="Center" Grid.Row="1" Grid.Column="0"/>
            <TextBox x:Name="ExeBox" Grid.Row="1" Grid.Column="1" Margin="4,2" Height="24"/>

            <TextBlock Text="保存间隔:" VerticalAlignment="Center" Grid.Row="2" Grid.Column="0"/>
            <StackPanel Orientation="Horizontal" Grid.Row="2" Grid.Column="1" Margin="4,2">
                <TextBox x:Name="IntervalBox" Width="60" Height="24" Text="5"/>
                <TextBlock Text=" 分钟" VerticalAlignment="Center"/>
            </StackPanel>

            <CheckBox x:Name="EnabledCheck" Content="启用" Grid.Row="3" Grid.Column="1"
                      IsChecked="True" Margin="4,2"/>
        </Grid>

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button Content="取消" Width="70" Height="28" IsCancel="True" Margin="0,0,8,0"/>
            <Button Content="确定" Width="70" Height="28" IsDefault="True" Click="OnOkClick"/>
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Write AddEditDialog.xaml.cs**

```csharp
using System;
using System.Diagnostics;
using System.Windows;
using AutoSaver.Models;
using Microsoft.Win32;

namespace AutoSaver.Views
{
    public partial class AddEditDialog : Window
    {
        private readonly ProgramItem _existing;

        public ProgramItem Result { get; private set; }

        public AddEditDialog(ProgramItem program = null)
        {
            InitializeComponent();
            _existing = program;

            if (program != null)
            {
                Title = "编辑程序";
                NameBox.Text = program.Name;
                ExeBox.Text = program.Exe;
                IntervalBox.Text = (program.SaveIntervalSec / 60).ToString();
                EnabledCheck.IsChecked = program.Enabled;
            }
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "可执行文件 (*.exe)|*.exe",
                Title = "选择目标程序"
            };

            try { dlg.InitialDirectory = @"C:\Program Files"; }
            catch { dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop); }

            if (dlg.ShowDialog() == true)
            {
                var exeName = System.IO.Path.GetFileName(dlg.FileName);
                ExeBox.Text = exeName;
                NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }

        private void OnPickRunningClick(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerDialog();
            picker.Owner = this;
            if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedProcessName))
            {
                ExeBox.Text = picker.SelectedProcessName;
                NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(picker.SelectedProcessName);
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            var exe = ExeBox.Text.Trim();
            if (string.IsNullOrEmpty(exe))
            {
                MessageBox.Show("请输入进程名。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(IntervalBox.Text, out var minutes) || minutes < 1)
            {
                MessageBox.Show("保存间隔必须为大于 0 的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = _existing ?? new ProgramItem();
            Result.Name = name.Length > 0 ? name : System.IO.Path.GetFileNameWithoutExtension(exe);
            Result.Exe = exe;
            Result.SaveIntervalSec = minutes * 60;
            Result.Enabled = EnabledCheck.IsChecked == true;

            DialogResult = true;
            Close();
        }
    }
}
```

- [ ] **Step 3: Commit**

```
git add Views/AddEditDialog.xaml Views/AddEditDialog.xaml.cs
git commit -m "feat: add program add/edit dialog with browse and pick buttons"
```

---

### Task 8: MainWindow

**Files:**
- Create: `Views/MainWindow.xaml`
- Create: `Views/MainWindow.xaml.cs`

- [ ] **Step 1: Write MainWindow.xaml**

```xml
<Window x:Class="AutoSaver.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        Title="AutoSaver" Height="350" Width="520"
        MinHeight="300" ResizeMode="CanResizeWithGrip"
        WindowStartupLocation="CenterScreen">
    <DockPanel Margin="8">
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" Margin="0,8,0,0">
            <CheckBox x:Name="StartupCheck" Content="开机自启" VerticalAlignment="Center"
                      Checked="OnStartupChanged" Unchecked="OnStartupChanged" Margin="0,0,16,0"/>
            <CheckBox x:Name="TrayCloseCheck" Content="关闭到托盘(不退出)" VerticalAlignment="Center"
                      Checked="OnTrayCloseChanged" Unchecked="OnTrayCloseChanged" Margin="0,0,16,0"/>
            <TextBlock x:Name="StatusLabel" Text="" VerticalAlignment="Center"
                       TextWrapping="NoWrap" TextTrimming="CharacterEllipsis"/>
        </StackPanel>

        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="*"/>
                <RowDefinition Height="36"/>
            </Grid.RowDefinitions>

            <ListView x:Name="ProgramListView" Grid.Row="0">
                <ListView.View>
                    <GridView>
                        <GridViewColumn Header="程序名" Width="140" DisplayMemberBinding="{Binding Name}"/>
                        <GridViewColumn Header="状态" Width="80">
                            <GridViewColumn.CellTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding StatusText}" Foreground="{Binding StatusColor}"/>
                                </DataTemplate>
                            </GridViewColumn.CellTemplate>
                        </GridViewColumn>
                        <GridViewColumn Header="间隔" Width="80" DisplayMemberBinding="{Binding IntervalText}"/>
                        <GridViewColumn Header="操作" Width="160">
                            <GridViewColumn.CellTemplate>
                                <DataTemplate>
                                    <StackPanel Orientation="Horizontal">
                                        <Button Content="编辑" Width="50" Height="22" Margin="2,0"
                                                Click="OnEditClick" Tag="{Binding}"/>
                                        <Button Content="删除" Width="50" Height="22" Margin="2,0"
                                                Click="OnDeleteClick" Tag="{Binding}"/>
                                    </StackPanel>
                                </DataTemplate>
                            </GridViewColumn.CellTemplate>
                        </GridViewColumn>
                    </GridView>
                </ListView.View>
            </ListView>

            <Button x:Name="AddButton" Content="+ 添加程序" Grid.Row="1"
                    Width="120" Height="28" HorizontalAlignment="Left"
                    Click="OnAddClick" Margin="0,8,0,0"/>
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Write MainWindow.xaml.cs**

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using AutoSaver.Models;
using AutoSaver.Services;
using Microsoft.Win32;

namespace AutoSaver.Views
{
    public partial class MainWindow : Window
    {
        private List<ProgramItem> _programs;
        private readonly Dictionary<string, string> _statuses = new Dictionary<string, string>();
        private readonly Dictionary<string, Tuple<string, int>> _lastSaves = new Dictionary<string, Tuple<string, int>>();

        public event Action<ProgramItem> ProgramAdded;
        public event Action<ProgramItem> ProgramEdited;
        public event Action<string> ProgramDeleted;

        public MainWindow(List<ProgramItem> programs)
        {
            InitializeComponent();
            _programs = programs;
            StartupCheck.IsChecked = ConfigService.StartWithWindows;
            TrayCloseCheck.IsChecked = ConfigService.MinimizeToTrayOnClose;
            RefreshList();
        }

        public void RefreshList()
        {
            var displayItems = _programs.Select(p => new ProgramDisplay
            {
                Id = p.Id,
                Name = p.Name,
                Exe = p.Exe,
                IntervalText = (p.SaveIntervalSec / 60) + " 分钟",
                StatusText = _statuses.TryGetValue(p.Id, out var running) && running == "running"
                    ? "● 运行中" : "○ 未检测到",
                StatusColor = _statuses.TryGetValue(p.Id, out var s) && s == "running"
                    ? "Green" : "Gray"
            }).ToList();

            ProgramListView.ItemsSource = displayItems;
            UpdateStatusBar();
        }

        public void UpdateProgramStatus(string programId, bool running)
        {
            _statuses[programId] = running ? "running" : "stopped";
            RefreshList();
        }

        public void UpdateLastSave(string programId, string timestamp, int windowCount)
        {
            var prog = _programs.FirstOrDefault(p => p.Id == programId);
            if (prog != null)
            {
                _lastSaves[prog.Name] = Tuple.Create(timestamp, windowCount);
                UpdateStatusBar();
            }
        }

        private void UpdateStatusBar()
        {
            if (_lastSaves.Count > 0)
            {
                var parts = _lastSaves.Select(kv => $"{kv.Key} {kv.Value.Item1} ({kv.Value.Item2}窗口)");
                StatusLabel.Text = "上次保存: " + string.Join(", ", parts);
            }
            else
            {
                StatusLabel.Text = "";
            }
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dlg = new AddEditDialog();
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                _programs.Add(dlg.Result);
                ConfigService.SavePrograms(_programs);
                RefreshList();
                ProgramAdded?.Invoke(dlg.Result);
            }
        }

        private void OnEditClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement el && el.Tag is ProgramDisplay display)) return;
            var prog = _programs.FirstOrDefault(p => p.Id == display.Id);
            if (prog == null) return;

            var dlg = new AddEditDialog(prog);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                var idx = _programs.FindIndex(p => p.Id == prog.Id);
                if (idx >= 0) _programs[idx] = dlg.Result;
                ConfigService.SavePrograms(_programs);
                RefreshList();
                ProgramEdited?.Invoke(dlg.Result);
            }
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement el && el.Tag is ProgramDisplay display)) return;
            _programs.RemoveAll(p => p.Id == display.Id);
            ConfigService.SavePrograms(_programs);
            RefreshList();
            ProgramDeleted?.Invoke(display.Id);
        }

        private void OnStartupChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                var enabled = StartupCheck.IsChecked == true;
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (enabled)
                        key?.SetValue("AutoSaver", System.Reflection.Assembly.GetEntryAssembly().Location);
                    else
                        key?.DeleteValue("AutoSaver", false);
                }
                ConfigService.StartWithWindows = enabled;
            }
            catch
            {
                StartupCheck.IsChecked = !StartupCheck.IsChecked;
                MessageBox.Show("注册表写入失败，请以管理员身份运行。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnTrayCloseChanged(object sender, RoutedEventArgs e)
        {
            ConfigService.MinimizeToTrayOnClose = TrayCloseCheck.IsChecked == true;
        }
    }
}
```

- [ ] **Step 3: Commit**

```
git add Views/MainWindow.xaml Views/MainWindow.xaml.cs
git commit -m "feat: add main window with program list, status dots, and tray close option"
```

---

### Task 9: Final wiring in App.xaml.cs

**Files:**
- Modify: `App.xaml.cs`

- [ ] **Step 1: Rewrite App.xaml.cs with full wiring**

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using AutoSaver.Models;
using AutoSaver.Services;
using AutoSaver.Views;
using MenuItem = System.Windows.Forms.MenuItem;

namespace AutoSaver
{
    public partial class App : Application
    {
        private NotifyIcon _tray;
        private ProcessMonitor _monitor;
        private SaveScheduler _scheduler;
        private List<ProgramItem> _programs;
        private MainWindow _mainWindow;

        private void OnStartup(object sender, StartupEventArgs e)
        {
            _programs = ConfigService.LoadPrograms();

            _monitor = new ProcessMonitor();
            _monitor.StatusChanged += OnStatusChanged;

            _scheduler = new SaveScheduler();
            _scheduler.SaveDone += OnSaveDone;

            foreach (var prog in _programs)
            {
                _scheduler.AddProgram(prog);
            }

            _monitor.RefreshPrograms(_programs);
            _monitor.Start(ConfigService.CheckIntervalSec);

            SetupTray();
        }

        private void SetupTray()
        {
            _tray = new NotifyIcon
            {
                Text = "AutoSaver",
                Visible = true
            };

            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "generated-image-2.png");
            if (File.Exists(iconPath))
                _tray.Icon = Icon.FromHandle(new Bitmap(iconPath).GetHicon());
            else
                _tray.Icon = SystemIcons.Application;

            _tray.DoubleClick += (s, e) => ShowMainWindow();
            _tray.ContextMenuStrip = new ContextMenuStrip();
            _tray.ContextMenuStrip.Opening += (s, e) => RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            var menu = _tray.ContextMenuStrip;
            menu.Items.Clear();

            foreach (var prog in _programs)
            {
                var running = _monitor.GetStatus(prog.Id);
                var text = (running ? "● " : "○ ") + prog.Name;
                var item = new ToolStripMenuItem(text) { Enabled = false };
                menu.Items.Add(item);
            }

            if (_programs.Count > 0)
                menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripMenuItem("显示主窗口", null, (s, e) => ShowMainWindow()));
            menu.Items.Add(new ToolStripMenuItem("退出", null, (s, e) => QuitApp()));
        }

        private void ShowMainWindow()
        {
            if (_mainWindow != null) return;

            _mainWindow = new MainWindow(_programs);

            // Restore statuses from monitor
            foreach (var prog in _programs)
                _mainWindow.UpdateProgramStatus(prog.Id, _monitor.GetStatus(prog.Id));

            _mainWindow.ProgramAdded += OnProgramAdded;
            _mainWindow.ProgramEdited += OnProgramEdited;
            _mainWindow.ProgramDeleted += OnProgramDeleted;

            _mainWindow.Closed += (s, e) =>
            {
                _mainWindow = null;
            };

            _mainWindow.Show();
        }

        private void OnStatusChanged(ProgramItem prog, bool running)
        {
            _mainWindow?.UpdateProgramStatus(prog.Id, running);
            _scheduler.SetRunning(prog.Id, running);
        }

        private void OnSaveDone(string programId, string timestamp, int windowCount)
        {
            _mainWindow?.UpdateLastSave(programId, timestamp, windowCount);
        }

        private void OnProgramAdded(ProgramItem prog)
        {
            _programs.Add(prog);
            _scheduler.AddProgram(prog);
            _monitor.RefreshPrograms(_programs);
            ConfigService.SavePrograms(_programs);
        }

        private void OnProgramEdited(ProgramItem prog)
        {
            var idx = _programs.FindIndex(p => p.Id == prog.Id);
            if (idx >= 0) _programs[idx] = prog;
            _scheduler.UpdateProgram(prog);
            ConfigService.SavePrograms(_programs);
        }

        private void OnProgramDeleted(string programId)
        {
            _programs.RemoveAll(p => p.Id == programId);
            _scheduler.RemoveProgram(programId);
            _monitor.RefreshPrograms(_programs);
            ConfigService.SavePrograms(_programs);
        }

        private void QuitApp()
        {
            _scheduler.StopAll();
            _monitor.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _scheduler?.StopAll();
            _monitor?.Stop();
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}
```

- [ ] **Step 2: Commit**

```
git add App.xaml.cs
git commit -m "feat: wire all services and views with tray lifecycle"
```

---

## Post-Implementation Checklist

- [ ] Place `generated-image-2.png` next to `autosaver.exe` (or in project root for Debug)
- [ ] Build with Visual Studio 2022 or MSBuild: `msbuild AutoSaver.csproj /p:Configuration=Release`
- [ ] Test: tray icon appears, right-click shows programs with status dots
- [ ] Test: add program via both buttons (browse local + pick running)
- [ ] Test: Ctrl+S sends to target window
- [ ] Test: close window → destroyed, reopen → rebuilt with fresh state
- [ ] Test: config persists in `autosaver.ini`
- [ ] Test: close-to-tray checkbox toggles behavior
