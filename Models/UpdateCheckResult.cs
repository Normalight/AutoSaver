namespace AutoSaver.Models
{
    public class UpdateCheckResult
    {
        public string CurrentVersion { get; set; } = "";
        public string LatestVersion { get; set; } = "";
        public bool HasUpdate { get; set; }
        public string ReleaseNotes { get; set; } = "";
        public string ReleaseUrl { get; set; } = "";
        public string InstallerUrl { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
    }
}
