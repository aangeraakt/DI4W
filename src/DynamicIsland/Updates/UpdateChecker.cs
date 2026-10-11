using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace DynamicIsland.Updates;

/// <summary>A downloadable file of a release.</summary>
/// <param name="Sha256">Hex SHA-256 as reported by GitHub, when available.</param>
public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

/// <summary>A newer release found on GitHub: the zip (copy-over update) and/or the installer (.msi).</summary>
public sealed record UpdateInfo(Version Version, string Notes, string PageUrl, ReleaseAsset? Zip, ReleaseAsset? Msi);

/// <summary>
/// Looks for updates in the GitHub releases of the repository (compiled into both the island and the settings app).
/// A release counts when its tag is a version ("v1.2.0") and it has a "*-win-x64.zip" or "*-win-x64.msi" asset.
/// </summary>
public static class UpdateChecker
{
    public const string Repository = "aangeraakt/DI4W";

    /// <summary>Signalled by the settings app to make a running island quit cleanly before files are replaced.</summary>
    public const string IslandQuitEvent = "DynamicIsland4Windows.Quit";

    public static HttpClient Http { get; } = CreateClient();

    public static Version CurrentVersion { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>The latest release when it is newer than this build, otherwise null. Throws on network or API errors.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request, cancel);
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // no published releases yet
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancel), CurrentVersion);
    }

    /// <summary>Reads GitHub's "latest release" JSON; null when it isn't newer than <paramref name="current"/> or has no package.</summary>
    public static UpdateInfo? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var release = doc.RootElement;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        version = Normalize(version);
        if (version <= Normalize(current)) return null;

        ReleaseAsset? zip = null, msi = null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            // GitHub reports "sha256:<hex>" for uploaded assets; used to verify the download.
            var digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            var file = new ReleaseAsset(
                name,
                asset.GetProperty("browser_download_url").GetString()!,
                asset.GetProperty("size").GetInt64(),
                digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..] : null);

            if (name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)) zip = file;
            else if (name.EndsWith("-win-x64.msi", StringComparison.OrdinalIgnoreCase)) msi = file;
        }
        if (zip is null && msi is null) return null; // a release without a Windows package

        return new UpdateInfo(
            version,
            release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            release.GetProperty("html_url").GetString() ?? $"https://github.com/{Repository}/releases",
            zip,
            msi);
    }

    /// <summary>1.2 and 1.2.0.0 both become 1.2.0, so versions compare the way people read them.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DI4W-Updater", "1.0"));
        return client;
    }
}
