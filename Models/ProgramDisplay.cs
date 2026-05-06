using System.Windows.Media;

namespace AutoSaver.Models
{
    public class ProgramDisplay
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Exe { get; set; }
        public string StatusText { get; set; }
        public Brush StatusColor { get; set; }
        public string IntervalText { get; set; }
    }
}
