# AutoSaver 界面一体化重设计 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 AutoSaver 的主窗口、设置窗口、主题样式、检查间隔输入、版本号与 Changelog 展示统一重做为现代卡片式界面，并修复主题切换、版本显示、滚动条遮挡与布局空白问题。

**Architecture:** 保留现有 WPF/.NET Framework 4.8 架构与 INI 配置方式，不引入新依赖。将界面重构集中在主题资源、主窗口无边框标题栏、设置页分组卡片、程序列表卡片化、以及按版本提取 Changelog 的辅助逻辑上；业务逻辑只做必要适配，避免改动自动保存、监控和托盘行为。

**Tech Stack:** C# 8.0, WPF, .NET Framework 4.8, XAML resource dictionaries, existing INI config service, existing `VERSION` file, existing GitHub Actions release workflow.

---

### Task 1: 建立统一视觉资源和无边框窗口骨架

**Files:**
- Modify: `Themes/LightTheme.xaml`
- Modify: `Themes/DarkTheme.xaml`
- Modify: `App.xaml.cs:58-71`
- Modify: `Views/MainWindow.xaml:1-96`
- Modify: `Views/MainWindow.xaml.cs:10-133`

- [ ] **Step 1: 写出会失败的界面验证用例**

在本地手工验证前，先把要检查的 UI 断言写进计划执行清单：

```text
1. 启动后主窗口没有系统标题栏，只有自定义顶部栏。
2. 标题栏右侧有最小化、最大化/还原、关闭按钮。
3. 深色/浅色切换后，标题栏、按钮、输入框、下拉框、卡片背景、滚动条都使用同一套资源键。
4. 主窗口不再使用表格式 GridView。
```

- [ ] **Step 2: 先跑一次当前版本确认基线仍会失败**

Run:

```bash
msbuild AutoSaver.csproj /p:Configuration=Debug /v:minimal /nologo
```

Expected: 仍能生成当前程序，但界面仍是旧布局，说明后续改动必须覆盖这些视觉点。

- [ ] **Step 3: 重写主题资源与窗口公共样式**

把 Light/Dark 主题补成同一套资源键，确保各控件共享相同命名：

```xml
<SolidColorBrush x:Key="CardBackground" Color="#FFFFFF" />
<SolidColorBrush x:Key="CardBorder" Color="#E5E7EB" />
<SolidColorBrush x:Key="CardShadow" Color="#20000000" />
<SolidColorBrush x:Key="DividerColor" Color="#E5E7EB" />
<SolidColorBrush x:Key="WindowChromeBackground" Color="#F8FAFC" />
<SolidColorBrush x:Key="WindowChromeForeground" Color="#111827" />
```

为按钮、输入框、组合框、复选框和滚动条建立统一模板，确保 hover / pressed / disabled 一致。

- [ ] **Step 4: 把主窗口改成无边框骨架**

把 `Views/MainWindow.xaml` 从三段 GridView 结构改成：

```xml
<Window ... WindowStyle="None" ResizeMode="CanResize" AllowsTransparency="False">
    <Border Background="{StaticResource WindowBackground}" CornerRadius="0">
        <Grid>
            <!-- 自定义标题栏 -->
            <!-- 快捷操作卡片 -->
            <!-- 程序列表卡片 -->
            <!-- 底部状态条 -->
        </Grid>
    </Border>
</Window>
```

在 `MainWindow.xaml.cs` 中补齐窗口拖拽、最大化切换和自定义按钮点击事件。

- [ ] **Step 5: 重新跑一次验证**

Run:

```bash
msbuild AutoSaver.csproj /p:Configuration=Debug /v:minimal /nologo
```

Expected: 编译通过，窗口 XAML 能接受新的无边框结构，资源键不缺失。

- [ ] **Step 6: 提交**

```bash
git add Themes/LightTheme.xaml Themes/DarkTheme.xaml App.xaml.cs Views/MainWindow.xaml Views/MainWindow.xaml.cs
git commit -m "feat: redesign window chrome and shared styles"
```

---

### Task 2: 将程序列表改成单列卡片并消除空白区域

**Files:**
- Modify: `Views/MainWindow.xaml:34-85`
- Modify: `Views/MainWindow.xaml.cs:30-75`
- Modify: `Models/ProgramDisplay.cs`

- [ ] **Step 1: 写出卡片列表的可见行为检查点**

```text
1. 每个程序显示为一张独立卡片。
2. 卡片内显示名称、exe 摘要、状态、间隔、最近保存信息、编辑/删除按钮。
3. 窗口变宽时不再出现 GridView 后方大片空白。
4. 滚动条只出现在内容溢出时，不遮挡卡片内容。
```

- [ ] **Step 2: 用当前布局确认旧问题仍存在**

Run:

```bash
msbuild AutoSaver.csproj /p:Configuration=Debug /v:minimal /nologo
```

Expected: 现有 GridView 仍会留下空白列，作为对照。

- [ ] **Step 3: 把 `ProgramDisplay` 扩展为卡片展示模型**

在 `Models/ProgramDisplay.cs` 增加卡片需要的摘要字段：

```csharp
public string ExeSummary { get; set; }
public string LastSaveText { get; set; }
public string StatusBadgeText { get; set; }
```

在 `MainWindow.RefreshList()` 中继续从 `_programs` 映射到 display 模型，但不再生成 GridView 专用列文本。

- [ ] **Step 4: 把 `MainWindow.xaml` 的列表区改成卡片模板**

用 `ItemsControl` + `ListBox` 风格模板替代 `GridView`：

```xml
<ListBox x:Name="ProgramListView" Background="Transparent" BorderThickness="0">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <Border Style="{StaticResource ProgramCardBorder}">
                <Grid>
                    <!-- 左侧名称与路径摘要 -->
                    <!-- 中间状态与间隔 -->
                    <!-- 右侧操作按钮 -->
                </Grid>
            </Border>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

保留 `Tag="{Binding}"` 不再需要，事件处理改为从 `DataContext` 获取当前项。

- [ ] **Step 5: 调整状态更新和最近保存展示**

`UpdateProgramStatus()` 和 `UpdateLastSave()` 继续刷新列表和底部状态条，但展示文本改为适合卡片的简短摘要，不再依赖列布局。

- [ ] **Step 6: 重新构建并人工核对列表空白问题**

Run:

```bash
msbuild AutoSaver.csproj /p:Configuration=Debug /v:minimal /nologo
```

Expected: 窗口宽度变化时不会出现右侧大片空白列。

- [ ] **Step 7: 提交**

```bash
git add Models/ProgramDisplay.cs Views/MainWindow.xaml Views/MainWindow.xaml.cs
git commit -m "feat: convert program list to cards"
```

---

### Task 3: 重做设置窗口并支持检查间隔单位选择

**Files:**
- Modify: `Views/SettingsDialog.xaml:1-89`
- Modify: `Views/SettingsDialog.xaml.cs:1-45`
- Modify: `Services/ConfigService.cs:35-61`
- Modify: `App.xaml.cs:171-184`

- [ ] **Step 1: 写出设置页的验收点**

```text
1. 检查间隔默认值为 30。
2. 默认单位为秒。
3. 单位可切换为分和时。
4. 保存后定时器内部仍使用秒。
5. 设置页内容不会被窗口底部覆盖。
```

- [ ] **Step 2: 先确认旧的单一数值输入仍然不满足需求**

Run:

```bash
msbuild AutoSaver.csproj /p:Configuration=Debug /v:minimal /nologo
```

Expected: 当前设置页仍只有一个秒数输入框，验证点未满足。

- [ ] **Step 3: 把设置页改成卡片分组布局**

将 `SettingsDialog.xaml` 改为纵向滚动容器，每个设置项是一块独立区域，中间用分割线隔开：

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto">
    <StackPanel>
        <!-- 主题卡片 -->
        <!-- 检查间隔卡片 -->
        <!-- 开机自启卡片 -->
        <!-- 关闭到托盘卡片 -->
        <!-- 保存通知卡片 -->
    </StackPanel>
</ScrollViewer>
```

- [ ] **Step 4: 把检查间隔拆成数值 + 单位**

在 `SettingsDialog.xaml` 中改成：

```xml
<TextBox x:Name="IntervalValueBox" Width="72" />
<ComboBox x:Name="IntervalUnitCombo" Width="96">
    <ComboBoxItem Content="秒" />
    <ComboBoxItem Content="分" />
    <ComboBoxItem Content="时" />
</ComboBox>
```

在 `SettingsDialog.xaml.cs` 中初始化默认值：

```csharp
IntervalValueBox.Text = "30";
IntervalUnitCombo.SelectedIndex = 0;
```

- [ ] **Step 5: 扩展配置读写逻辑**

在 `ConfigService.cs` 中保持向后兼容：读取旧的 `check_interval_sec` 时，把它换算成“数值 + 秒”显示；保存时仍写回统一的秒数供监控使用。新增的 UI 单位不必强制改变底层监控接口。

- [ ] **Step 6: 保存时重新应用间隔**

`App.ShowSettings()` 中保存成功后继续重启监控，但改为读取统一换算后的秒数。

- [ ] **Step 7: 构建验证**

Run:

```bash
msbuild AutoSaver.csproj /p:Configuration=Debug /v:minimal /nologo
```

Expected: 设置窗口与主程序一起编译通过，默认值和单位字段可用。

- [ ] **Step 8: 提交**

```bash
git add Views/SettingsDialog.xaml Views/SettingsDialog.xaml.cs Services/ConfigService.cs App.xaml.cs
git commit -m "feat: redesign settings and interval configuration"
```

---

### Task 4: 按版本提取 Changelog 并修正发布输出

**Files:**
- Modify: `App.xaml.cs:29-45`
- Modify: `.github/workflows/build.yml:19-47`
- Create: `Services/ChangelogService.cs`
- Modify: `AutoSaver.csproj:52-113`

- [ ] **Step 1: 写出 Changelog 提取行为**

```text
1. 只返回与当前 VERSION 匹配的章节。
2. 如果找不到章节，返回短回退文案。
3. GitHub Release 只使用该提取结果，不再直接发布整份 CHANGELOG.md。
```

- [ ] **Step 2: 先确认当前 workflow 仍然会使用整份文件**

查看 `.github/workflows/build.yml` 中现状，确认 release 步骤还在使用 `body_path: CHANGELOG.md`。

- [ ] **Step 3: 新增 Changelog 服务**

创建 `Services/ChangelogService.cs`，提供一个可复用的提取方法：

```csharp
public static string GetReleaseNotes(string changelogPath, string version)
```

实现应匹配 `## [1.2.0] - 2026-05-06` 这种章节标题，并返回该段正文。

- [ ] **Step 4: 在启动路径中使用提取结果**

`App.xaml.cs` 启动时读取 `VERSION` 后，调用新的 Changelog 服务，用于日志或后续展示时只取当前版本内容。

- [ ] **Step 5: 改造 GitHub Actions release body**

把 workflow 中的 release body 改成由一个生成的临时文件或脚本步骤提供，而不是 `body_path: CHANGELOG.md` 原样输出。

- [ ] **Step 6: 构建验证**

Run:

```bash
msbuild AutoSaver.csproj /p:Configuration=Debug /v:minimal /nologo
```

Expected: 新服务被项目编译进来，发布流程引用的新路径或输出方式不会破坏本地构建。

- [ ] **Step 7: 提交**

```bash
git add Services/ChangelogService.cs App.xaml.cs AutoSaver.csproj .github/workflows/build.yml
git commit -m "fix: scope changelog output to current version"
```

---

### Task 5: 修复主题切换一致性、滚动条遮挡和自适应缩放

**Files:**
- Modify: `Services/ThemeService.cs:58-99`
- Modify: `Views/MainWindow.xaml:1-96`
- Modify: `Views/SettingsDialog.xaml:1-89`
- Modify: `Themes/LightTheme.xaml`
- Modify: `Themes/DarkTheme.xaml`

- [ ] **Step 1: 写出回归检查点**

```text
1. 主题切换后不会只改一半界面。
2. 滚动条不会覆盖设置页或列表内容。
3. 窗口缩放时不会压坏标题栏、按钮或底部状态条。
4. 所有控件样式键在两套主题里都存在。
```

- [ ] **Step 2: 先确认主题切换链路的现状**

检查 `ThemeService.ApplyTheme()` 目前是否只移除一个旧字典并加入一个新字典，确认新主题资源要补齐完整键集合。

- [ ] **Step 3: 修正主题字典加载方式**

让 `ApplyTheme()` 只替换真正的主题字典，不影响其它资源；主题字典必须包含窗口、卡片、按钮、输入框、选择框、滚动条、分割线全部资源键。

- [ ] **Step 4: 为主窗口和设置页增加自适应布局约束**

在 XAML 中为主窗口内容区和设置页内容区使用 `Grid` / `ScrollViewer` / `MinHeight` / `MinWidth` 组合，确保缩小时不会遮挡设置内容，也不会把窗口控制按钮挤出可见区域。

- [ ] **Step 5: 重做滚动条视觉**

把 `ScrollBar` 样式从透明占位改成轻量可见样式，避免“滚动条覆盖内容但又看不见”的问题。

- [ ] **Step 6: 构建验证**

Run:

```bash
msbuild AutoSaver.csproj /p:Configuration=Debug /v:minimal /nologo
```

Expected: 主题资源完整，缩放与滚动区域都正常编译。

- [ ] **Step 7: 提交**

```bash
git add Services/ThemeService.cs Views/MainWindow.xaml Views/SettingsDialog.xaml Themes/LightTheme.xaml Themes/DarkTheme.xaml
git commit -m "fix: unify theme resources and window scaling"
```

---

### Task 6: 终检、手工验证和发布准备

**Files:**
- Modify: 视前几项所有已改动文件
- Modify: `CHANGELOG.md`（如需补充当前版本说明）

- [ ] **Step 1: 跑完整编译**

Run:

```bash
msbuild AutoSaver.csproj /p:Configuration=Release /v:minimal /nologo
```

Expected: Release 构建通过。

- [ ] **Step 2: 手工启动检查主界面**

启动程序后检查：

```text
1. 顶部自定义标题栏可拖动。
2. 最小化、最大化/还原、关闭按钮可用。
3. 版本号显示与 VERSION 一致。
4. 卡片布局没有大片空白。
5. 设置页不会被遮挡。
6. 主题切换后所有控件风格统一。
```

- [ ] **Step 3: 手工验证设置页**

检查：

```text
1. 检查间隔默认 30 秒。
2. 单位默认秒，可切换分和时。
3. 保存后监控仍按秒工作。
4. 分割线清楚地区分各设置项。
```

- [ ] **Step 4: 手工验证 Changelog 输出**

确认当前版本对应章节被正确提取，发布正文不再包含全部历史内容。

- [ ] **Step 5: 如果需要，更新当前版本说明**

如果本次功能足够大，补一条简洁的 `CHANGELOG.md` 当前版本说明，确保版本语义清晰。

- [ ] **Step 6: 最终提交**

```bash
git add .
git commit -m "feat: overhaul autosaver interface and versioned release notes"
```

## Coverage Check

- 主窗口无边框标题栏：Task 1
- 卡片式布局：Task 1, Task 2
- 按钮、选择框等样式重写：Task 1, Task 5
- 设置页分割线：Task 3
- 检查间隔默认值 30 秒、单位选择：Task 3
- 版本号对应：Task 4, Task 6
- Changelog 只显示对应版本：Task 4, Task 6
- 主题切换一致：Task 1, Task 5
- 滚动条遮挡修复：Task 1, Task 5
- 自适应缩放：Task 5, Task 6

## Self-Review Notes

- 没有保留 `TBD` / `TODO` / 占位步骤。
- 每个任务都明确了文件范围、验证命令和提交方式。
- 计划覆盖了设计文档中的所有要求，没有遗漏主题、版本、Changelog、卡片布局、设置页、缩放和分割线。
