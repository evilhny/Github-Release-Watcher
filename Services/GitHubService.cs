using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GitHubReleaseWatcher.Models;

namespace GitHubReleaseWatcher.Services;

public sealed class GitHubService
{
    private static readonly HttpClient Client = CreateClient();
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GitHubReleaseWatcher/1.0 (Windows desktop app)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    public async Task<IReadOnlyList<ReleaseRecord>> GetRecentReleasesAsync(string fullName, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(fullName.Split('/')[0])}/{Uri.EscapeDataString(fullName.Split('/')[1])}/releases?per_page=20";
        using var response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Repository not found, private, or has no accessible releases.");
        if (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) && values.FirstOrDefault() == "0")
            throw new InvalidOperationException("GitHub API rate limit reached. The app will try again at the next check.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var releases = new List<ReleaseRecord>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var id = item.TryGetProperty("id", out var idEl) ? idEl.ToString() : "";
            var tag = item.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            var urlValue = item.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() ?? "" : "";
            DateTimeOffset? published = null;
            if (item.TryGetProperty("published_at", out var publishedEl) && publishedEl.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(publishedEl.GetString(), out var parsed)) published = parsed;
            var prerelease = item.TryGetProperty("prerelease", out var preEl) && preEl.GetBoolean();
            releases.Add(new ReleaseRecord { Repository = fullName, ReleaseId = id, TagName = tag, Name = name, HtmlUrl = urlValue, PublishedAt = published, IsPrerelease = prerelease });
        }
        return releases;
    }

    public static bool TryNormalizeRepository(string input, out string fullName, out string error)
    {
        fullName = ""; error = "";
        var value = input.Trim();
        if (value.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) value = value[19..];
        value = value.Trim('/').Split('?', '#')[0];
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0].Equals("settings", StringComparison.OrdinalIgnoreCase) || parts[1].Equals("releases", StringComparison.OrdinalIgnoreCase))
        {
            error = "Enter a repository as owner/repository or paste its GitHub URL.";
            return false;
        }
        var owner = parts[0];
        var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        if (owner.Length == 0 || repo.Length == 0 || owner.Any(char.IsWhiteSpace) || repo.Any(char.IsWhiteSpace))
        {
            error = "That doesn't look like a valid GitHub repository.";
            return false;
        }
        fullName = $"{owner}/{repo}";
        return true;
    }
}
