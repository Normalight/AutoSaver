# AutoSaver UI 改进实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 对 AutoSaver 主界面进行 4 项 UI/UX 改进：配置文件写入版本号、整体圆角、列表选中高亮改为边框样式、标题栏倒计时胶囊。

**Architecture:** 纯 UI 层改动为主，涉及 XAML 主题文件（DarkTheme/LightTheme）、主窗口 XAML 和 code-behind，以及 SaveScheduler 新增一个 tick 事件供倒计时重置。各任务相互独立，可按顺序执行。

**Tech Stack:** WPF (.NET Framework)，XAML ResourceDictionary，DispatcherTimer，INI 配置文件

---

## 文件变更清单

| 文件 | 操作 |
|------|------|
| `Resources/autosaver.default.ini` | 修改：填写 `version=1.3.6` |
| `Services/ConfigService.cs` | 修改：EnsureDefaults fallback 写入实际版本号 |
| `Services/SaveScheduler.cs` | 修改：新增 `IntervalTicked` 事件 |
| `Themes/DarkTheme.xaml` | 修改：ListBoxItem 选中样式 + 新增 CountdownCapsule 样式 |
| `Themes/LightTheme.xaml` | 修改：同步以上两项 |
| `Views/MainWindow.xaml` | 修改：窗口圆角 + 标题栏胶囊元素 + DataTemplate 选中触发器 |
| `Views/MainWindow.xaml.cs` | 修改：倒计时 Timer 逻辑 + SetNextSaveTime 方法 |
| `App.xaml.cs` | 修改：订阅 IntervalTicked，调用 SetNextSaveTime |

---

## Task 1: 配置文件写入版本号

**Files:**
- Modify: `Resources/autosaver.default.ini`
- Modify: `Services/ConfigService.cs:91`

- [ ] **Step 1: 修改默认 INI 文件**

将 `Resources/autosaver.default.ini` 中：
```ini
[meta]
version=
```
改为：
```ini
[meta]
version=1.3.6
```

- [ ] **Step 2: 修改 ConfigService.EnsureDefaults fallback**

`Services/ConfigService.cs` 第 91 行，将：
```csharp
Write("meta",   "version",                  "");
```
改为：
```csharp
var asmVer = Assembly.GetExecutingAssembly().GetName().Version;
var verStr = asmVer == null ? "1.3.6" : $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}";
Write("meta", "version", verStr);
```

- [ ] **Step 3: 提交**

```bash
git add Resources/autosaver.default.ini Services/ConfigService.cs
git commit -m "fix: write version number into default ini config"
```

---

## Task 2: 整体界面圆角

**Files:**
- Modify: `Views/MainWindow.xaml`

- [ ] **Step 1: 启用透明度并设置窗口圆角**

在 `Views/MainWindow.xaml` 中，将 `Window` 声明的 `AllowsTransparency="False"` 改为 `True`，并将 `WindowChrome` 的 `CornerRadius` 从 `0` 改为 `12`：

```xml
<Window ...
        AllowsTransparency="True">

    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="0" CornerRadius="12" GlassFrameThickness="0" ResizeBorderThickness="6" UseAeroCaptionButtons="False"/>
    </shell:WindowChrome.WindowChrome>

    <Border Background="{DynamicResource WindowBackground}" CornerRadius="12">
```

注意：外层 `Border` 加 `CornerRadius="12"`，这是实际裁剪圆角的关键。

- [ ] **Step 2: 提交**

```bash
git add Views/MainWindow.xaml
git commit -m "ui: add rounded corners to main window"
```

---

## Task 3: 列表项选中高亮改为边框样式

**Files:**
- Modify: `Themes/DarkTheme.xaml:357-383`
- Modify: `Themes/LightTheme.xaml:357-383`
- Modify: `Views/MainWindow.xaml`（DataTemplate 中的卡片 Border）

**背景知识：** WPF 的 `DataTemplate` 内部控件无法直接感知父级 `ListBoxItem.IsSelected`。解决方案：在 `DataTemplate` 的卡片 `Border` 上用 `Style` + `DataTrigger`，通过 `RelativeSource` 向上查找 `ListBoxItem` 的 `IsSelected` 属性。

- [ ] **Step 1: 修改 DarkTheme.xaml 的 ListBoxItem 模板**

将 `Themes/DarkTheme.xaml` 中 `ListBoxItem` 的 `IsSelected` 触发器从整片背景改为透明（选中效果移到 DataTemplate 层）：

```xml
<Style TargetType="ListBoxItem">
    <Setter Property="Padding" Value="0"/>
    <Setter Property="Margin" Value="0"/>
    <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="ListBoxItem">
                <Border x:Name="itemBorder"
                        Background="{TemplateBinding Background}"
                        Padding="{TemplateBinding Padding}"
                        Margin="{TemplateBinding Margin}">
                    <ContentPresenter/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="itemBorder" Property="Background" Value="Transparent"/>
                    </Trigger>
                    <Trigger Property="IsSelected" Value="True">
                        <Setter TargetName="itemBorder" Property="Background" Value="Transparent"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

- [ ] **Step 2: 同步修改 LightTheme.xaml**

对 `Themes/LightTheme.xaml` 做完全相同的修改（ListBoxItem 模板触发器改为 Transparent）。

- [ ] **Step 3: 修改 MainWindow.xaml DataTemplate 中的卡片 Border**

在 `Views/MainWindow.xaml` 的 `ListBox.ItemTemplate` 的 `DataTemplate` 中，给卡片 `Border` 加 `Style`，通过 `DataTrigger` + `RelativeSource` 实现选中时边框变色：

```xml
<Border Background="{DynamicResource CardBackground}"
        BorderBrush="{DynamicResource CardBorderBrush}"
        BorderThickness="1" CornerRadius="8"
        Padding="10,8" Margin="0,0,0,6">
    <Border.Style>
        <Style TargetType="Border">
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}}" Value="True">
                    <Setter Property="BorderBrush" Value="{DynamicResource AccentColor}"/>
                    <Setter Property="Background" Value="{DynamicResource BgTertiary}"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <!-- 原有内容不变 -->
```

注意：`Border` 上已有内联的 `Background` 和 `BorderBrush` 属性，需要将它们移入 `Style` 的 `Setter` 中作为默认值，否则内联属性优先级高于触发器，触发器不会生效。完整写法：

```xml
<Border BorderThickness="1" CornerRadius="8"
        Padding="10,8" Margin="0,0,0,6">
    <Border.Style>
        <Style TargetType="Border">
            <Setter Property="Background" Value="{DynamicResource CardBackground}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource CardBorderBrush}"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}}" Value="True">
                    <Setter Property="BorderBrush" Value="{DynamicResource AccentColor}"/>
                    <Setter Property="Background" Value="{DynamicResource BgTertiary}"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <!-- 原有 Grid 内容 -->
```

- [ ] **Step 4: 提交**

```bash
git add Themes/DarkTheme.xaml Themes/LightTheme.xaml Views/MainWindow.xaml
git commit -m "ui: replace list selection solid fill with accent border highlight"
```

---

## Task 4: SaveScheduler 新增 IntervalTicked 事件

**Files:**
- Modify: `Services/SaveScheduler.cs:29,120`

倒计时需要知道"每次 timer tick 时的 interval 秒数"，以便重置倒计时。在 `OnTimerTick` 开始时触发此事件。

- [ ] **Step 1: 新增事件声明**

在 `Services/SaveScheduler.cs` 的事件声明区（第 29 行附近）添加：

```csharp
public event Action<int> IntervalTicked;
```

- [ ] **Step 2: 在 OnTimerTick 开始时触发**

在 `OnTimerTick` 方法开头（`List<ProgramItem> toSave;` 之前）添加：

```csharp
IntervalTicked?.Invoke(_intervalSec);
```

- [ ] **Step 3: 提交**

```bash
git add Services/SaveScheduler.cs
git commit -m "feat: add IntervalTicked event to SaveScheduler for countdown reset"
```

---

## Task 5: 主题文件新增 CountdownCapsule 样式

**Files:**
- Modify: `Themes/DarkTheme.xaml`（在 Separator 样式之前插入）
- Modify: `Themes/LightTheme.xaml`（同位置）

- [ ] **Step 1: 在 DarkTheme.xaml 新增样式**

在 `Themes/DarkTheme.xaml` 的 `<!-- Separator -->` 注释之前插入：

```xml
<!-- Countdown Capsule -->
<Style x:Key="CountdownCapsule" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource BgTertiary}"/>
    <Setter Property="CornerRadius" Value="999"/>
    <Setter Property="Padding" Value="8,3"/>
    <Setter Property="VerticalAlignment" Value="Center"/>
</Style>
```

- [ ] **Step 2: 在 LightTheme.xaml 新增相同样式**

在 `Themes/LightTheme.xaml` 的 `<!-- Separator -->` 注释之前插入完全相同的样式块。

- [ ] **Step 3: 提交**

```bash
git add Themes/DarkTheme.xaml Themes/LightTheme.xaml
git commit -m "ui: add CountdownCapsule style to both themes"
```

---

## Task 6: 标题栏加倒计时胶囊元素

**Files:**
- Modify: `Views/MainWindow.xaml`（标题栏 StackPanel）

- [ ] **Step 1: 在标题栏右侧按钮组左边插入胶囊**

在 `Views/MainWindow.xaml` 标题栏的右侧 `StackPanel`（`Grid.Column="1"`）中，在最小化按钮之前插入胶囊：

```xml
<StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
    <!-- Countdown capsule -->
    <Border Style="{StaticResource CountdownCapsule}" Margin="0,0,6,0">
        <StackPanel Orientation="Horizontal">
            <Path Data="M12 22C17.5228 22 22 17.5228 22 12C22 6.47715 17.5228 2 12 2C6.47715 2 2 6.47715 2 12C2 17.5228 6.47715 22 12 22Z M12 6V12L16 14"
                  Stroke="{DynamicResource TextMuted}" StrokeThickness="1.5"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round"
                  Width="11" Height="11" Stretch="Uniform"
                  VerticalAlignment="Center"/>
            <TextBlock x:Name="CountdownLabel"
                       Text="--:--"
                       FontSize="12" FontWeight="SemiBold"
                       Foreground="{DynamicResource TextMuted}"
                       VerticalAlignment="Center"
                       Margin="4,0,0,0"/>
        </StackPanel>
    </Border>
    <!-- Minimize -->
    <Button Style="{StaticResource GhostButton}" ...
```

- [ ] **Step 2: 提交**

```bash
git add Views/MainWindow.xaml
git commit -m "ui: add countdown capsule to title bar"
```

---

## Task 7: MainWindow code-behind 倒计时逻辑

**Files:**
- Modify: `Views/MainWindow.xaml.cs`

- [ ] **Step 1: 添加字段和 SetNextSaveTime 方法**

在 `MainWindow` 类的字段区（`_programs` 等字段附近）添加：

```csharp
private System.Windows.Threading.DispatcherTimer _countdownTimer;
private DateTime _nextSaveAt = DateTime.MaxValue;
```

在构造函数 `RefreshList()` 调用之后添加 timer 初始化：

```csharp
_countdownTimer = new System.Windows.Threading.DispatcherTimer
{
    Interval = TimeSpan.FromSeconds(1)
};
_countdownTimer.Tick += OnCountdownTick;
_countdownTimer.Start();
```

添加公开方法（放在 `UpdateLastSave` 方法之后）：

```csharp
public void SetNextSaveTime(DateTime nextSaveAt)
{
    _nextSaveAt = nextSaveAt;
}
```

- [ ] **Step 2: 添加 OnCountdownTick 和窗口关闭清理**

添加 tick 处理方法：

```csharp
private void OnCountdownTick(object sender, EventArgs e)
{
    var remaining = _nextSaveAt - DateTime.Now;
    if (remaining <= TimeSpan.Zero)
    {
        CountdownLabel.Text = "00:00";
        CountdownLabel.Foreground = (System.Windows.Media.Brush)FindResource("AccentColor");
        return;
    }
    var totalSec = (int)remaining.TotalSeconds;
    CountdownLabel.Text = $"{totalSec / 60:D2}:{totalSec % 60:D2}";
    CountdownLabel.Foreground = totalSec <= 5
        ? (System.Windows.Media.Brush)FindResource("AccentColor")
        : (System.Windows.Media.Brush)FindResource("TextMuted");
}
```

在 `OnCloseClick` 方法中添加 timer 停止：

```csharp
private void OnCloseClick(object sender, RoutedEventArgs e)
{
    _countdownTimer?.Stop();
    Close();
}
```

- [ ] **Step 3: 提交**

```bash
git add Views/MainWindow.xaml.cs
git commit -m "feat: add countdown timer logic to MainWindow"
```

---

## Task 8: App.xaml.cs 订阅 IntervalTicked 并重置倒计时

**Files:**
- Modify: `App.xaml.cs`

- [ ] **Step 1: 订阅 IntervalTicked 事件**

在 `App.xaml.cs` 的 `OnStartup` 方法中，`_scheduler.Start()` 调用之前添加：

```csharp
_scheduler.IntervalTicked += OnSchedulerIntervalTicked;
```

- [ ] **Step 2: 添加处理方法**

在 `OnSaveDone` 方法之后添加：

```csharp
private void OnSchedulerIntervalTicked(int intervalSec)
{
    Dispatcher.Invoke(() =>
    {
        _mainWindow?.SetNextSaveTime(DateTime.Now.AddSeconds(intervalSec));
    });
}
```

- [ ] **Step 3: 提交**

```bash
git add App.xaml.cs
git commit -m "feat: wire up countdown reset on scheduler tick"
```

---

## Task 9: 验证整体效果

- [ ] **Step 1: 构建项目**

```bash
cd /path/to/AutoSaver
dotnet build AutoSaver.csproj
```
预期：Build succeeded，0 errors。

- [ ] **Step 2: 检查 ini 文件**

确认 `Resources/autosaver.default.ini` 中 `version=1.3.6`。

- [ ] **Step 3: 手动验证清单**

启动应用后逐项确认：
1. 窗口四角有圆角（约 12px）
2. 标题栏右侧有胶囊倒计时，格式 `MM:SS`，每秒递减
3. 点击列表中的程序条目，卡片边框变为 accent 紫色，背景轻微提亮，无整片色块
4. 倒计时归零时文字变为 accent 色
5. 切换主题（深色/浅色），胶囊和选中样式均正常

- [ ] **Step 4: 最终提交（如有遗漏调整）**

```bash
git add -A
git commit -m "fix: ui polish adjustments after manual verification"
```
