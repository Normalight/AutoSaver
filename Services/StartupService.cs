using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using Microsoft.Win32;

namespace AutoSaver.Services
{
    /// <summary>
    /// 将「开机自启」同步到当前用户 Run 注册表项（与配置中的 start_with_windows 一致）。
    /// </summary>
    public static class StartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "AutoSaver";

        private static readonly object LogLock = new object();

        /// <summary>
        /// 按 <see cref="ConfigService.StartWithWindows"/> 写入或删除 Run 项。
        /// </summary>
        public static void ApplyStartupPreference()
        {
            try
            {
                var want = ConfigService.StartWithWindows;

                if (!want)
                {
                    try
                    {
                        using (var key = Registry.CurrentUser.CreateSubKey(
                                   RunKeyPath,
                                   RegistryKeyPermissionCheck.ReadWriteSubTree))
                        {
                            if (key == null)
                            {
                                TryLog("ApplyStartupPreference: CreateSubKey returned null while clearing Run entry.");
                                return;
                            }

                            key.DeleteValue(RunValueName, throwOnMissingValue: false);
                        }
                    }
                    catch (Exception ex)
                    {
                        TryLog($"ApplyStartupPreference (disable): {ex.GetType().Name}: {ex.Message}");
                    }

                    return;
                }

                var exePath = GetExecutablePath();
                if (string.IsNullOrEmpty(exePath))
                {
                    TryLog("ApplyStartupPreference: start_with_windows=true but executable path could not be resolved.");
                    return;
                }

                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(
                               RunKeyPath,
                               RegistryKeyPermissionCheck.ReadWriteSubTree))
                    {
                        if (key == null)
                        {
                            TryLog("ApplyStartupPreference: CreateSubKey returned null for HKCU Run.");
                            return;
                        }

                        key.SetValue(RunValueName, QuoteIfNeeded(exePath), RegistryValueKind.String);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    TryLog($"ApplyStartupPreference (registry denied): {ex.Message}");
                }
                catch (System.Security.SecurityException ex)
                {
                    TryLog($"ApplyStartupPreference (security): {ex.Message}");
                }
                catch (Exception ex)
                {
                    TryLog($"ApplyStartupPreference: {ex.GetType().Name}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                TryLog($"ApplyStartupPreference (outer): {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 与 App 使用相同的日志路径规则（exe 目录下的 autosaver.log），便于排查开机自启写入失败。
        /// </summary>
        private static void TryLog(string message)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = ".";

                var path = Path.Combine(baseDir, "autosaver.log");
                var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [StartupService] {message}{Environment.NewLine}";
                lock (LogLock)
                {
                    File.AppendAllText(path, entry);
                }
            }
            catch
            {
                // 日志失败时不影响主流程
            }
        }

        private static string GetExecutablePath()
        {
            try
            {
                var loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
                    return loc;
            }
            catch { }

            try
            {
                var p = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    return p;
            }
            catch { }

            return null;
        }

        private static string QuoteIfNeeded(string path)
        {
            return path.IndexOf(' ') >= 0 ? "\"" + path + "\"" : path;
        }
    }
}
