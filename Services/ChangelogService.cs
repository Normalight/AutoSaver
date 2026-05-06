using System;
using System.IO;

namespace AutoSaver.Services
{
    public static class ChangelogService
    {
        private const string FallbackTemplate = "Release notes for version {0} are not available.";

        public static string GetReleaseNotes(string changelogPath, string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return string.Format(FallbackTemplate, "unknown");

            if (string.IsNullOrWhiteSpace(changelogPath) || !File.Exists(changelogPath))
                return string.Format(FallbackTemplate, version.Trim());

            try
            {
                var lines = File.ReadAllLines(changelogPath);
                var targetHeading = "## [" + version.Trim() + "]";
                var capture = false;
                var writer = new StringWriter();

                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (!capture)
                    {
                        if (line.StartsWith(targetHeading, StringComparison.OrdinalIgnoreCase))
                        {
                            capture = true;
                        }
                        continue;
                    }

                    if (line.StartsWith("## [", StringComparison.OrdinalIgnoreCase))
                        break;

                    if (writer.GetStringBuilder().Length > 0)
                        writer.WriteLine();
                    writer.Write(line);
                }

                var body = writer.ToString().Trim();
                return string.IsNullOrWhiteSpace(body)
                    ? string.Format(FallbackTemplate, version.Trim())
                    : body;
            }
            catch
            {
                return string.Format(FallbackTemplate, version.Trim());
            }
        }
    }
}
