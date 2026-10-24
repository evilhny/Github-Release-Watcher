
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using GitHubReleaseWatcher.Models;
using GitHubReleaseWatcher.Services;

namespace GitHubReleaseWatcher;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly GitHubService _github = new();
    private readonly DispatcherTimer _timer = new();

    private AppSettings _settings;
    private bool _isChecking;
    private bool _allowExit;
    private bool _isPaused;
    private bool _loadingUi;

    public ObservableCollection<WatchedRepository> Repositories { get; } = [];
    public ObservableCollection<ReleaseRecord> History { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _settings = _settingsService.Load();

        foreach (var repo in _settings.Repositories)
            Repositories.Add(repo);

        foreach (var item in _settings.ReleaseHistory
                     .OrderByDescending(x => x.PublishedAt)
                     .Take(500))
        {
            History.Add(item);
        }

        _loadingUi = true;

        IntervalInput.Text = _settings.CheckIntervalMinutes.ToString();
        StartupCheckBox.IsChecked =
            _settings.RunAtStartup || StartupService.IsEnabled();

        TrayCheckBox.IsChecked = _settings.MinimizeToTrayOnClose;
        NotificationsCheckBox.IsChecked = _settings.NotificationsEnabled;
        PrereleaseCheckBox.IsChecked = _settings.IncludePrereleases;

        _settings.RunAtStartup = StartupCheckBox.IsChecked == true;
        _loadingUi = false;

        HistoryGrid.ItemsSource = History;

        _timer.Tick += async (_, _) => await CheckAllAsync();
        ResetTimer();

        Loaded += async (_, _) =>
        {
            if (Repositories.Count > 0)
                await CheckAllAsync();
        };

        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                HideToTray();
        };

        Closing += MainWindow_Closing;
    }

    private void ResetTimer()
    {
        _timer.Stop();

        _timer.Interval = TimeSpan.FromMinutes(
            Math.Clamp(_settings.CheckIntervalMinutes, 1, 1440));

        if (!_isPaused)
            _timer.Start();
    }

    private void SaveSettings()
    {
        _settings.Repositories = Repositories.ToList();
        _settings.ReleaseHistory = History.Take(500).ToList();

        _settingsService.Save(_settings);
    }

    private async Task CheckAllAsync()
    {
        if (_isChecking || _isPaused)
            return;

        _isChecking = true;
        _timer.Stop();

        var addedCount = 0;
        var checkedCount = 0;
        var errors = new List<string>();

        try
        {
            StatusText.Text = "Checking GitHub releases…";
            FooterText.Text = "Scanning enabled repositories…";

            foreach (var repo in Repositories.Where(r => r.IsEnabled).ToList())
            {
                try
                {
                    var fetchedReleases =
                        await _github.GetRecentReleasesAsync(repo.FullName);

                    // Respect the Include prereleases setting.
                    var releases = _settings.IncludePrereleases
                        ? fetchedReleases
                        : fetchedReleases
                            .Where(r => !r.IsPrerelease)
                            .ToList();

                    // Find the newest release among the filtered results.
                    var latestRelease = releases
                        .OrderByDescending(r => r.PublishedAt)
                        .FirstOrDefault();

                    // Update both the displayed version and its download-page URL.
                    repo.LatestVersion =
                        latestRelease?.TagName ?? "No releases";

                    repo.LatestReleaseUrl =
                        latestRelease?.HtmlUrl ?? "";

                    RepositoriesGrid.Items.Refresh();

                    checkedCount++;

                    var currentIds = releases
                        .Select(r => r.ReleaseId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .ToHashSet(StringComparer.Ordinal);

                    if (!repo.HasBaseline)
                    {
                        repo.KnownReleaseIds = currentIds.ToList();
                        repo.HasBaseline = true;

                        foreach (var release in releases)
                            AddHistoryIfMissing(release);
                    }
                    else
                    {
                        var newReleases = releases
                            .Where(r => !repo.KnownReleaseIds.Contains(
                                r.ReleaseId,
                                StringComparer.Ordinal))
                            .OrderBy(r => r.PublishedAt)
                            .ToList();

                        foreach (var release in newReleases)
                        {
                            AddHistoryIfMissing(release);
                            addedCount++;

                            if (_settings.NotificationsEnabled)
                                ShowReleaseNotification(release);
                        }

                        repo.KnownReleaseIds = currentIds.ToList();
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{repo.FullName}: {ex.Message}");
                }
            }

            SortHistory();
            SaveSettings();

            var now = DateTime.Now;

            LastCheckText.Text = $"Last check: {now:yyyy-MM-dd HH:mm:ss}";

            StatusText.Text = errors.Count == 0
                ? $"Checked {checkedCount} repository/repositories. {addedCount} new release(s) found."
                : $"Checked {checkedCount} repository/repositories with {errors.Count} error(s).";

            FooterText.Text = errors.Count > 0
                ? string.Join(" | ", errors.Take(2))
                : addedCount > 0
                    ? $"Found {addedCount} new release(s)."
                    : "Everything is up to date.";
        }
        finally
        {
            _isChecking = false;

            if (!_isPaused)
                _timer.Start();
        }
    }

    private void AddHistoryIfMissing(ReleaseRecord release)
    {
        if (History.Any(h =>
                h.Repository.Equals(
                    release.Repository,
                    StringComparison.OrdinalIgnoreCase)
                && h.ReleaseId == release.ReleaseId))
        {
            return;
        }

        History.Insert(0, release);
    }

    private void SortHistory()
    {
        var items = History
            .OrderByDescending(r => r.PublishedAt)
            .Take(500)
            .ToList();

        History.Clear();

        foreach (var item in items)
            History.Add(item);
    }

    private void ShowReleaseNotification(ReleaseRecord release)
    {
        try
        {
            var title = $"New GitHub release: {release.Repository}";

            var details = string.IsNullOrWhiteSpace(release.Name)
                ? release.TagName
                : $"{release.TagName} — {release.Name}";

            TrayIcon.ShowBalloonTip(
                title,
                details,
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }
        catch
        {
            // Keep the release in history if Windows suppresses the notification.
        }
    }

    private void AddRepository_Click(object sender, RoutedEventArgs e)
        => AddRepository();

    private void RepositoryInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddRepository();
    }

    private void AddRepository()
    {
        if (!GitHubService.TryNormalizeRepository(
                RepositoryInput.Text,
                out var fullName,
                out var error))
        {
            MessageBox.Show(
                error,
                "Invalid repository",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (Repositories.Any(r =>
                r.FullName.Equals(
                    fullName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(
                "That repository is already in your list.",
                "Duplicate repository",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        Repositories.Add(new WatchedRepository
        {
            FullName = fullName
        });

        RepositoryInput.Clear();

        SaveSettings();
        _ = CheckAllAsync();
    }

    private void RemoveRepository_Click(object sender, RoutedEventArgs e)
    {
        if (RepositoriesGrid.SelectedItem is not WatchedRepository selected)
            return;

        if (MessageBox.Show(
                $"Remove {selected.FullName} from monitoring? Its history entries will remain.",
                "Remove repository",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Repositories.Remove(selected);
        SaveSettings();
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        if (_isPaused)
        {
            _isPaused = false;
            SetPauseMenuHeader("Pause monitoring");
            ResetTimer();
        }

        await CheckAllAsync();
    }

    private void ApplyInterval_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalInput.Text, out var minutes)
            || minutes < 1
            || minutes > 1440)
        {
            MessageBox.Show(
                "Enter a whole number from 1 to 1440 minutes.",
                "Invalid interval",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        _settings.CheckIntervalMinutes = minutes;

        SaveSettings();
        ResetTimer();

        StatusText.Text = $"Check interval set to {minutes} minute(s).";
    }

    private void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi || StartupCheckBox is null)
            return;

        try
        {
            var enabled = StartupCheckBox.IsChecked == true;

            StartupService.SetEnabled(enabled);
            _settings.RunAtStartup = enabled;

            SaveSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Couldn't update Windows startup settings.\n{ex.Message}",
                "Startup setting",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi || !IsInitialized || _settings is null)
            return;

        if (TrayCheckBox is null
            || NotificationsCheckBox is null
            || PrereleaseCheckBox is null)
        {
            return;
        }

        _settings.MinimizeToTrayOnClose = TrayCheckBox.IsChecked == true;
        _settings.NotificationsEnabled = NotificationsCheckBox.IsChecked == true;
        _settings.IncludePrereleases = PrereleaseCheckBox.IsChecked == true;

        SaveSettings();
    }

    // Open the selected release from the release history.
    private void HistoryGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
        => OpenSelectedRelease();

    private void OpenRelease_Click(object sender, RoutedEventArgs e)
        => OpenSelectedRelease();

    private void OpenSelectedRelease()
    {
        if (HistoryGrid.SelectedItem is not ReleaseRecord release
            || string.IsNullOrWhiteSpace(release.HtmlUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(release.HtmlUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Couldn't open the release page.\n{ex.Message}",
                "Open release",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // Open the latest release when its version hyperlink is clicked.
    private void LatestVersion_RequestNavigate(
        object sender,
        RequestNavigateEventArgs e)
    {
        try
        {
            if (Uri.TryCreate(
                    e.Uri?.ToString(),
                    UriKind.Absolute,
                    out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && uri.Host.Equals(
                    "github.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Couldn't open the GitHub release page.\n{ex.Message}",
                "Open release",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            e.Handled = true;
        }
    }

    private void TrayOpen_Click(object sender, RoutedEventArgs e)
        => ShowFromTray();

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void HideToTray()
        => Hide();

    private void MainWindow_Closing(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowExit && _settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            HideToTray();

            TrayIcon.ShowBalloonTip(
                "GitHub Release Watcher",
                "Still monitoring in the system tray.",
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }
        else
        {
            _timer.Stop();
            TrayIcon.Dispose();
            SaveSettings();
        }
    }

    private void SetPauseMenuHeader(string header)
    {
        if (TrayIcon.ContextMenu is ContextMenu menu
            && menu.Items.OfType<MenuItem>()
                .FirstOrDefault(item =>
                    (item.Header?.ToString() ?? "")
                    .Contains(
                        "monitoring",
                        StringComparison.OrdinalIgnoreCase)) is MenuItem pauseItem)
        {
            pauseItem.Header = header;
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;

        SetPauseMenuHeader(
            _isPaused ? "Resume monitoring" : "Pause monitoring");

        if (_isPaused)
            _timer.Stop();
        else
            ResetTimer();

        StatusText.Text = _isPaused
            ? "Monitoring paused."
            : "Monitoring resumed.";
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _allowExit = true;
        Close();
    }
}
