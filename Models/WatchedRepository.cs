using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace GitHubReleaseWatcher.Models;

public sealed class WatchedRepository
{
    public string FullName { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public bool HasBaseline { get; set; }
    public List<string> KnownReleaseIds { get; set; } = [];

    public string LatestVersion { get; set; } = "Not checked";

    public string LatestReleaseUrl { get; set; } = "";

    [JsonIgnore]
    public string DisplayName => FullName;
}

public sealed class ReleaseRecord
{
    public string Repository { get; set; } = "";
    public string TagName { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset? PublishedAt { get; set; }
    public string HtmlUrl { get; set; } = "";
    public string ReleaseId { get; set; } = "";
    public bool IsPrerelease { get; set; }

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Name) || Name == TagName ? TagName : $"{TagName} — {Name}";

    [JsonIgnore]
    public string PublishedDisplay => PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Unknown date";
}

public sealed class AppSettings
{
    public int CheckIntervalMinutes { get; set; } = 30;
    public bool RunAtStartup { get; set; }
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool IncludePrereleases { get; set; } = true;
    public List<WatchedRepository> Repositories { get; set; } = [];
    public List<ReleaseRecord> ReleaseHistory { get; set; } = [];
}
