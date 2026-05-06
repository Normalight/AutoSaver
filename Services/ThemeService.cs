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

            // Remove existing theme if any
            var oldTheme = FindThemeDictionary(app);
            if (oldTheme != null)
                app.Resources.MergedDictionaries.Remove(oldTheme);

            // Load new theme
            var newTheme = new ResourceDictionary { Source = uri };
            app.Resources.MergedDictionaries.Add(newTheme);
        }

        public static void InitTheme(Application app)
        {
            // Ensure at least the dark theme is loaded on startup
            // ApplyTheme will replace it if needed
            ApplyTheme(app);

            // Watch for system theme changes
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General)
                {
                    app.Dispatcher.Invoke(() => ApplyTheme(app));
                }
            };
        }

        private static ResourceDictionary FindThemeDictionary(Application app)
        {
            foreach (var dict in app.Resources.MergedDictionaries)
            {
                var source = dict.Source?.ToString() ?? "";
                if (source.Contains("DarkTheme") || source.Contains("LightTheme"))
                    return dict;
            }
            return null;
        }
    }
}
