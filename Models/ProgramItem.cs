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
