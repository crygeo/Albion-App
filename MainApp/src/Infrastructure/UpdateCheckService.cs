using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using MaterialDesignThemes.Wpf;

namespace Albion_App.Infrastructure;

/// <summary>
/// Checks GitHub Releases for a newer version and enqueues a snackbar notification
/// if one is found. Fire-and-forget — never throws.
/// </summary>
public sealed class UpdateCheckService
{
    private const string ApiUrl      = "https://api.github.com/repos/crygeo/Albion-App/releases/latest";
    private const string ReleasePage = "https://github.com/crygeo/Albion-App/releases/latest";

    private readonly ISnackbarMessageQueue _queue;

    public UpdateCheckService(ISnackbarMessageQueue queue)
        => _queue = queue;

    public async Task CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AlbionApp/1.0");

            var response = await http.GetFromJsonAsync<GitHubRelease>(ApiUrl, ct);
            if (response is null) return;

            var tagRaw = response.TagName?.TrimStart('v');
            if (!Version.TryParse(tagRaw, out var remoteVersion)) return;

            var localVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (localVersion is null || remoteVersion <= localVersion) return;

            var message = $"Nueva versión disponible: v{remoteVersion.ToString(3)}";
            _queue.Enqueue(
                message,
                "VER",
                (Action)(() => Process.Start(new ProcessStartInfo(ReleasePage) { UseShellExecute = true })));
        }
        catch
        {
            // No internet, GitHub unreachable, repo private — fail silently.
        }
    }

    // Minimal shape of the GitHub API response we care about.
    private sealed record GitHubRelease(
        [property: System.Text.Json.Serialization.JsonPropertyName("tag_name")] string? TagName,
        [property: System.Text.Json.Serialization.JsonPropertyName("html_url")] string? HtmlUrl);
}
