using System;
using System.Windows;
using System.Windows.Input;
using AutoSaver.Models;

namespace AutoSaver.Views
{
    public partial class AboutDialog : Window
    {
        private UpdateCheckResult _currentResult;
        private bool _isInstalling;

        public AboutDialog()
        {
            InitializeComponent();
            LoadInitialState();
        }

        private App CurrentApp => Application.Current as App;

        private void LoadInitialState()
        {
            CurrentVersionText.Text = "当前版本：v" + App.Version;
            UpdateCurrentVersionText.Text = "v" + App.Version;
            UpdateLatestVersionText.Text = "-";
            ReleaseNotesText.Text = App.CurrentReleaseNotes;
            RepositoryButton.Content = App.RepositoryUrl;

            var app = CurrentApp;
            if (app == null)
                return;

            ApplyUpdateResult(app.GetLastUpdateCheckResult(), app.IsCheckingUpdates());
        }

        private void ApplyUpdateResult(UpdateCheckResult result, bool isChecking)
        {
            _currentResult = result ?? new UpdateCheckResult { CurrentVersion = App.Version, ReleaseNotes = App.CurrentReleaseNotes };

            UpdateCurrentVersionText.Text = string.IsNullOrWhiteSpace(_currentResult.CurrentVersion)
                ? "v" + App.Version
                : "v" + _currentResult.CurrentVersion;
            UpdateLatestVersionText.Text = string.IsNullOrWhiteSpace(_currentResult.LatestVersion)
                ? "-"
                : "v" + _currentResult.LatestVersion;

            if (!string.IsNullOrWhiteSpace(_currentResult.ReleaseNotes))
                ReleaseNotesText.Text = _currentResult.ReleaseNotes;
            else if (_currentResult.HasUpdate)
                ReleaseNotesText.Text = App.GetFallbackReleaseNotes(_currentResult.LatestVersion);
            else
                ReleaseNotesText.Text = App.CurrentReleaseNotes;

            if (isChecking)
            {
                UpdateStatusText.Text = "检查中";
            }
            else if (!string.IsNullOrWhiteSpace(_currentResult.ErrorMessage))
            {
                UpdateStatusText.Text = "检查更新失败";
            }
            else if (_currentResult.HasUpdate)
            {
                UpdateStatusText.Text = "发现新版本";
            }
            else if (!string.IsNullOrWhiteSpace(_currentResult.LatestVersion))
            {
                UpdateStatusText.Text = "当前已是最新版本";
            }
            else
            {
                UpdateStatusText.Text = "尚未检查更新";
            }

            OpenReleaseButton.Visibility = string.IsNullOrWhiteSpace(_currentResult.ReleaseUrl)
                ? Visibility.Collapsed
                : Visibility.Visible;
            InstallUpdateButton.IsEnabled = !_isInstalling && !isChecking && _currentResult.HasUpdate;
            CheckUpdateButton.IsEnabled = !_isInstalling && !isChecking;
        }

        private void OnCheckUpdateClick(object sender, RoutedEventArgs e)
        {
            var app = CurrentApp;
            if (app == null)
                return;

            ApplyUpdateResult(_currentResult, true);
            app.BeginBackgroundUpdateCheck(result =>
            {
                ApplyUpdateResult(result, false);
            });
        }

        private void OnInstallUpdateClick(object sender, RoutedEventArgs e)
        {
            if (_isInstalling)
                return;

            var app = CurrentApp;
            if (app == null)
                return;

            _isInstalling = true;
            DownloadProgressBar.Visibility = Visibility.Visible;
            DownloadProgressText.Visibility = Visibility.Visible;
            DownloadProgressBar.IsIndeterminate = true;
            DownloadProgressBar.Value = 0;
            DownloadProgressText.Text = "正在下载更新";
            UpdateStatusText.Text = "下载中";
            InstallUpdateButton.IsEnabled = false;
            CheckUpdateButton.IsEnabled = false;

            app.DownloadAndInstallUpdate(_currentResult,
                (downloaded, total) =>
                {
                    DownloadProgressBar.IsIndeterminate = total <= 0;
                    if (total > 0)
                    {
                        var percent = Math.Min(100, downloaded * 100d / total);
                        DownloadProgressBar.Value = percent;
                        DownloadProgressText.Text = $"正在下载更新 {percent:0}%";
                    }
                    else
                    {
                        DownloadProgressText.Text = "正在下载更新";
                    }
                },
                () =>
                {
                    UpdateStatusText.Text = "正在启动安装器";
                    DownloadProgressText.Text = "正在启动安装器";
                },
                (message, releaseUrl) =>
                {
                    _isInstalling = false;
                    DownloadProgressBar.IsIndeterminate = false;
                    DownloadProgressBar.Visibility = Visibility.Collapsed;
                    DownloadProgressText.Visibility = Visibility.Collapsed;
                    MessageBox.Show(message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    if (!string.IsNullOrWhiteSpace(releaseUrl))
                        _currentResult.ReleaseUrl = releaseUrl;
                    ApplyUpdateResult(_currentResult, false);
                });
        }

        private void OnOpenRepositoryClick(object sender, RoutedEventArgs e)
        {
            CurrentApp?.OpenReleasePage(App.RepositoryUrl);
        }

        private void OnOpenReleaseClick(object sender, RoutedEventArgs e)
        {
            var url = string.IsNullOrWhiteSpace(_currentResult?.ReleaseUrl)
                ? App.RepositoryUrl + "/releases/latest"
                : _currentResult.ReleaseUrl;
            CurrentApp?.OpenReleasePage(url);
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2) return;
            DragMove();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
