using System.Collections.Generic;
using System.Windows.Media;

namespace AutoSaver.Models
{
    public class WindowSubRow
    {
        /// <summary>子卡片主文案：产品全称 · 窗口标题（无元数据时仅有窗口标题）。</summary>
        public string Headline { get; set; }
        public string TimerStatus { get; set; }
    }

    public class ProgramDisplay
    {
        /// <summary>列表行唯一键（与 ProgramId 一致，每项配置一行）。</summary>
        public string RowId { get; set; }
        /// <summary>配置中的程序 ID（删除、启用开关）。</summary>
        public string ProgramId { get; set; }
        public string Name { get; set; }
        public string Exe { get; set; }
        public string ExeSummary { get; set; }
        public Brush StatusColor { get; set; }
        public bool Enabled { get; set; }
        public ImageSource Icon { get; set; }
        /// <summary>主区域第三行：单窗口/汇总时的倒计时说明；多窗口时为分组摘要。</summary>
        public string TimerStatus { get; set; }

        /// <summary>是否存在多个顶层窗口（为 true 时使用展开面板列出子窗口）。</summary>
        public bool HasMultipleWindows { get; set; }

        /// <summary>子窗口详情（仅 <see cref="HasMultipleWindows"/> 时有效）。</summary>
        public List<WindowSubRow> SubWindows { get; set; }

        /// <summary>展开状态（由主窗口在刷新时根据记忆字典写入）。</summary>
        public bool IsExpanded { get; set; }
    }
}
