namespace AutoSaver.Models
{
    /// <summary>
    /// Carries the result of a GitHub-release update check.
    /// All fields are safe to read without null checks — strings default to "".
    /// </summary>
    public class UpdateCheckResult
    {
        /// <summary>Running assembly version, e.g. "1.3.7".</summary>
        public string CurrentVersion { get; set; } = "";

        /// <summary>Latest tag on GitHub (v-prefix stripped), e.g. "1.3.8".</summary>
        public string LatestVersion { get; set; } = "";

        /// <summary>True only when LatestVersion is strictly newer than CurrentVersion.</summary>
        public bool HasUpdate { get; set; }

        /// <summary>Release body / changelog excerpt from the GitHub release.</summary>
        public string ReleaseNotes { get; set; } = "";

        /// <summary>HTML URL of the GitHub release page.</summary>
        public string ReleaseUrl { get; set; } = "";

        /// <summary>
        /// Direct download URL for the installer asset
        /// (AutoSaver-Setup-vX.Y.Z.exe).  Empty when no installer asset was found.
        /// </summary>
        public string InstallerUrl { get; set; } = "";

        /// <summary>
        /// Non-empty when the check failed (network error, parse error, etc.).
        /// When ErrorMessage is set, HasUpdate is always false.
        /// </summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>Convenience: check succeeded (no error).</summary>
        public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
    }
}
