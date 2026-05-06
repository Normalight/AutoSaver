using System;
using System.Windows;
using Microsoft.Win32;

namespace AutoSaver.Services
{
    public enum AppTheme { Dark, Light, System }

    public static class ThemeService
    {
        private const string ThemeKey = "theme";

        public static AppTheme CurrentTheme
        {
            get
            {
                var v = ConfigService.Read("global", ThemeKey, "system");
                return v switch
                {
                    "dark" => AppTheme.Dark,
                    "light" => AppTheme.Light,
                    _ => AppTheme.System
                };
            }
            set
            {
                var v = value switch
                {
                    AppTheme.Dark => "dark",
                    AppTheme.Light => "light",
                    _ => "system"
                };
                ConfigService.Write("global", ThemeKey, v);
            }
        }

        public static bool IsDarkMode
        {
            get
            {
                if (CurrentTheme == AppTheme.Dark) return true;
                if (CurrentTheme == AppTheme.Light) return false;

                // System: check Windows registry for app mode
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        var value = key?.GetValue("AppsUseLightTheme");
                        if (value is int intVal) return intVal == 0;
                    }
                }
                catch { }
                return false;
            }
        }

        public static void ApplyTheme(Application app)
        {
            var dict = IsDarkMode ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";
            var uri = new Uri(dict, UriKind.Relative);

            for (var i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var source = app.Resources.MergedDictionaries[i].Source?.ToString() ?? "";
                if (IsThemeDictionary(source))
                    app.Resources.MergedDictionaries.RemoveAt(i);
            }

            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
        }

        public static void InitTheme(Application app)
        {
            ApplyTheme(app);

            // Watch for system theme changes
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && CurrentTheme == AppTheme.System)
                {
                    app.Dispatcher.Invoke(() => ApplyTheme(app));
                }
            };
        }

        private static bool IsThemeDictionary(string source)
        {
            return source.IndexOf("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
