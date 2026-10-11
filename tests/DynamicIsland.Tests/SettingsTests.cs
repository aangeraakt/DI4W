using System.IO;
using DynamicIsland.Settings;
using Xunit;

namespace DynamicIsland.Tests;

/// <summary>Uses a temporary settings folder; never touches the real %AppData%\DynamicIsland.</summary>
[Collection("Settings files")]
public sealed class SettingsTests : IDisposable
{
    private readonly string _original = AppSettings.Folder;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "DI4W-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsTests() => AppSettings.Folder = _folder;

    public void Dispose()
    {
        AppSettings.Folder = _original;
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Update_keeps_what_the_other_app_wrote()
    {
        // The settings app opened earlier and holds an old copy...
        AppSettings.Update(s => s.Sounds = true);
        var staleCopy = AppSettings.Load();

        // ...meanwhile the island records its path.
        AppSettings.Update(s => s.IslandPath = @"C:\Apps\DynamicIsland.exe");

        // The settings app changes one thing; the island's path must survive.
        staleCopy.Sounds = false;
        AppSettings.Update(s => s.Sounds = staleCopy.Sounds);

        var saved = AppSettings.Load();
        Assert.False(saved.Sounds);
        Assert.Equal(@"C:\Apps\DynamicIsland.exe", saved.IslandPath);
    }

    [Fact]
    public void Broken_file_is_kept_aside_and_defaults_are_used()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(AppSettings.FilePath, "{ \"Scale\": 1.2, oops");

        var settings = AppSettings.Load();

        Assert.Equal(1.0, settings.Scale);
        Assert.True(File.Exists(AppSettings.FilePath + ".bad"));
        Assert.Contains("oops", File.ReadAllText(AppSettings.FilePath + ".bad"));
    }

    [Fact]
    public void TryLoad_returns_null_for_missing_or_broken_files()
    {
        Assert.Null(AppSettings.TryLoad());
        Directory.CreateDirectory(_folder);
        File.WriteAllText(AppSettings.FilePath, "not json");
        Assert.Null(AppSettings.TryLoad());
    }

    [Fact]
    public void Old_files_without_add_on_settings_get_defaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(AppSettings.FilePath, """{ "Scale": 1.2, "Sounds": false }""");

        var settings = AppSettings.Load();

        Assert.Equal(1.2, settings.Scale);
        Assert.True(settings.Addons.Volume);
        Assert.Equal("Ctrl+Alt+I", settings.HideHotkey);
    }

    [Fact]
    public void CopyFrom_copies_every_setting()
    {
        var source = new AppSettings { Scale = 1.4, HideHotkey = "Ctrl+Shift+H" };
        source.Addons.Weather = false;
        var target = new AppSettings();

        target.CopyFrom(source);

        Assert.Equal(1.4, target.Scale);
        Assert.Equal("Ctrl+Shift+H", target.HideHotkey);
        Assert.False(target.Addons.Weather);
    }

    [Fact]
    public void Shortcut_text_is_saved_readably()
    {
        AppSettings.Update(s => s.HideHotkey = "Ctrl+Alt+I");
        Assert.Contains("\"Ctrl+Alt+I\"", File.ReadAllText(AppSettings.FilePath));
    }
}
