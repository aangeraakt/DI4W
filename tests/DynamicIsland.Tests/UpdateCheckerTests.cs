using DynamicIsland.Updates;
using Xunit;

namespace DynamicIsland.Tests;

public class UpdateCheckerTests
{
    private static string Release(string tag, params (string Name, string? Digest)[] assets) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/aangeraakt/DI4W/releases/tag/{{tag}}",
          "body": "What's new",
          "assets": [{{string.Join(",", assets.Select(a => $$"""
            { "name": "{{a.Name}}", "size": 1234, "browser_download_url": "https://example.test/{{a.Name}}"{{(a.Digest is null ? "" : $", \"digest\": \"{a.Digest}\"")}} }
            """))}}]
        }
        """;

    [Fact]
    public void Finds_a_newer_release_with_both_packages()
    {
        var json = Release("v1.3.0", ("DI4W-v1.3.0-win-x64.zip", "sha256:abc123"), ("DI4W-v1.3.0-win-x64.msi", "sha256:def456"));
        var update = UpdateChecker.Parse(json, new Version(1, 2, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 3, 0), update.Version);
        Assert.Equal("abc123", update.Zip?.Sha256);
        Assert.Equal("def456", update.Msi?.Sha256);
        Assert.Equal(1234, update.Zip?.Size);
        Assert.Equal("What's new", update.Notes);
    }

    [Theory]
    [InlineData("v1.2.0")] // same version
    [InlineData("v1.1.9")] // older
    [InlineData("nightly")] // not a version
    public void Ignores_releases_that_are_not_newer(string tag) =>
        Assert.Null(UpdateChecker.Parse(Release(tag, ("DI4W-win-x64.zip", null)), new Version(1, 2, 0)));

    [Fact]
    public void Ignores_a_release_without_a_windows_package() =>
        Assert.Null(UpdateChecker.Parse(Release("v2.0.0", ("source.tar.gz", null)), new Version(1, 2, 0)));

    [Fact]
    public void Missing_digest_means_no_checksum()
    {
        var update = UpdateChecker.Parse(Release("v2.0.0", ("DI4W-v2.0.0-win-x64.zip", null)), new Version(1, 0, 0));
        Assert.Null(update?.Zip?.Sha256);
        Assert.Null(update?.Msi);
    }

    [Theory]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.0.0", "1.2.0")]
    [InlineData("1.2.3.4", "1.2.3")]
    public void Normalizes_versions(string input, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.Normalize(Version.Parse(input)));

    [Fact]
    public void Four_part_build_version_is_not_older_than_its_release()
    {
        // The app reports 1.2.0.0; the release is tagged v1.2.0. That's the same version, not an update.
        Assert.Null(UpdateChecker.Parse(Release("v1.2.0", ("DI4W-win-x64.zip", null)), new Version(1, 2, 0, 0)));
    }
}
