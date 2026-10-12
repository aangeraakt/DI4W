using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using DynamicIsland.Settings;
using Xunit;

namespace DynamicIsland.Tests;

/// <summary>
/// Every text the apps translate must have a Dutch version: L.T/L.F texts in the code, and the visible
/// texts in the settings app's XAML. Fails with the list of missing texts, so a new feature can't ship
/// half in English.
/// </summary>
public class TranslationTests
{
    private static readonly string Src = FindSrc();

    [Fact]
    public void Every_text_has_a_Dutch_translation()
    {
        var known = L.Known.ToHashSet();
        var texts = AllTexts().ToList();
        Assert.True(texts.Count > 250, $"Only found {texts.Count} texts; is the scan still looking in the right place?");
        var missing = texts.Where(t => !known.Contains(t)).OrderBy(t => t).ToList();
        Assert.True(missing.Count == 0, "Missing Dutch translations:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Translations_keep_the_same_placeholders()
    {
        L.Use("nl");
        try
        {
            var broken = L.Known.Where(k => !Placeholders(k).SetEquals(Placeholders(L.T(k)))).ToList();
            Assert.True(broken.Count == 0, "Placeholders differ:\n" + string.Join("\n", broken));
        }
        finally
        {
            L.Use("en");
        }
    }

    [Fact]
    public void Formats_in_both_languages()
    {
        L.Use("nl");
        try
        {
            Assert.Equal("Over 5 min", L.F("In {0} min", 5));
            Assert.Equal("Batterij raakt leeg", L.T("Low battery"));
            Assert.Equal("A text without translation", L.T("A text without translation"));
        }
        finally
        {
            L.Use("en");
        }
        Assert.Equal("In 5 min", L.F("In {0} min", 5));
    }

    private static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+(:[^}]*)?\}").Select(m => m.Value).ToHashSet();

    private static IEnumerable<string> AllTexts()
    {
        var code = Directory.EnumerateFiles(Src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.EndsWith("Strings.nl.cs"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"L\.[TF]\(\s*""((?:[^""\\]|\\.)*)""").Select(m => Regex.Unescape(m.Groups[1].Value)));

        var xaml = Directory.EnumerateFiles(Path.Combine(Src, "DynamicIsland.Settings"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.EndsWith("App.xaml"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f),
                    @"\s(?:Text|Header|Description|Content|Title|Message|PlaceholderText|ToolTipService\.ToolTip)=""([^""{&][^""]*)""")
                .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)))
            .Where(t => t.Any(char.IsLetter));

        return code.Concat(xaml).Distinct();
    }

    private static string FindSrc()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var src = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(src, "DynamicIsland.Settings"))) return src;
        }
        throw new DirectoryNotFoundException("Couldn't find the src folder from the test output.");
    }
}
