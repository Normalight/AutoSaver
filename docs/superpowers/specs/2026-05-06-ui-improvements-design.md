# UI Improvements Design — AutoSaver v1.3.6

Date: 2026-05-06

## 1. 内置配置文件版本号

`Resources/autosaver.default.ini` 中 `version=` 当前为空，导致首次运行时版本来源依赖程序集反射。

**变更：** 将 `version=` 改为 `version=1.3.6`，与 `VERSION` 文件保持一致。后续每次发版时同步更新此值。

`ConfigService.EnsureDefaults()` 的 fallback 写入路径（`Write("meta", "version", "")`）也改为写入当前版本号，使用 `Assembly.GetExecutingAssembly().GetName().Version` 格式化。

## 2. 整体界面圆角

当前主窗口 `WindowChrome.CornerRadius="0"`，外层 `Border` 无圆角，视觉生硬。

**变更：**
- `MainWindow.xaml`：`AllowsTransparency="True"`，外层 `Border` 加 `CornerRadius="12"`
- `WindowChrome.CornerRadius` 改为 `12`
- `LightTheme.xaml` 同步检查是否有需要对齐的样式

注意：启用 `AllowsTransparency` 后窗口阴影由 WPF 自身渲染，需确认 `Background` 不为透明（保持 `WindowBackground`）。

## 3. 列表项选中高亮改为边框高亮

当前 `ListBoxItem` 选中时用 `ControlPressedBackground` 整片填充，视觉上过于"实"。

**变更（DarkTheme.xaml + LightTheme.xaml）：**
- `ListBoxItem` 的 `IsSelected` 触发器：移除整片背景，改为背景设为 `BgTertiary`（轻微提亮）
- 卡片内层 `Border`（`DataTemplate` 中）：选中时 `BorderBrush` 变为 `AccentColor`
- 实现方式：在 `MainWindow.xaml` 的 `DataTemplate` 中，给卡片 `Border` 绑定 `IsSelected`，通过 `DataTrigger` 切换 `BorderBrush`

由于 `DataTemplate` 内的 `Border` 无法直接感知 `ListBoxItem.IsSelected`，需通过 `RelativeSource` 向上查找 `ListBoxItem` 的 `IsSelected` 属性触发。

## 4. 标题栏倒计时胶囊

在标题栏右侧（最小化按钮左边）加一个胶囊样式的倒计时标签，显示距下次自动保存的剩余时间。

**样式（定义在主题文件中，key: `CountdownCapsule`）：**
- 外层 `Border`：`CornerRadius="999"`，背景 `BgTertiary`，`Padding="8,3"`
- 内容：`StackPanel Horizontal`，左侧时钟图标（Path，12×12），右侧 `TextBlock`（`Caption` 样式，`FontWeight="SemiBold"`）
- 格式：`MM:SS`（如 `00:23`）
- 颜色：正常为 `TextMuted`；当剩余 ≤ 5 秒时切换为 `AccentColor` 提示即将触发

**逻辑（MainWindow.xaml.cs）：**
- 新增 `_countdownTimer`（`DispatcherTimer`，Interval=1s）
- 新增 `_nextSaveAt`（`DateTime`）由外部通过 `SetNextSaveTime(DateTime)` 方法设置
- `App.xaml.cs` 在 `SaveScheduler` 每次 tick 时，通过 `_mainWindow.SetNextSaveTime(DateTime.Now.AddSeconds(intervalSec))` 更新
- 每秒计算 `remaining = _nextSaveAt - DateTime.Now`，格式化为 `MM:SS` 更新标签
- 窗口关闭时停止 timer

**SaveScheduler 改动：**
- 新增 `public event Action<int> IntervalTicked`，在每次 timer tick 开始时触发，传入当前 interval 秒数，供 App 层重置倒计时
