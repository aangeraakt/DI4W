using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using DynamicIsland.Settings;
using Xunit;

// Some tests switch the language (L.Use), which is global; run the tests one after another so others see English.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DynamicIsland.Tests;

/// <summary>
/// Every text the apps translate must have a version in each language: L.T/L.F texts in the code, and the
/// visible texts in the settings app's XAML. Fails with the list of missing texts, so a new feature can't ship
/// half in English. And the other way round: a translation whose English text is gone from the code fails too,
/// so the tables don't fill up with leftovers.
/// </summary>
public class TranslationTests
{
    private static readonly string Src = FindSrc();

    public static TheoryData<string> Languages => new() { "nl", "de", "fr" };

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_text_has_a_translation(string language)
    {
        var known = L.KnownIn(language).ToHashSet();
        var texts = TranslatedTexts().ToList();
        Assert.True(texts.Count > 250, $"Only found {texts.Count} texts; is the scan still looking in the right place?");
        var missing = texts.Where(t => !known.Contains(t)).OrderBy(t => t).ToList();
        Assert.True(missing.Count == 0, $"Missing {language} translations:\n" + string.Join("\n", missing));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void No_translation_is_left_over(string language)
    {
        // Used: anywhere in the code as a string, or in the XAML (labels passed around in variables count too).
        var used = AllStrings().Concat(TranslatedTexts()).ToHashSet();
        var unused = L.KnownIn(language).Where(k => !used.Contains(k)).OrderBy(k => k).ToList();
        Assert.True(unused.Count == 0, $"{language} translations whose English text no longer appears in the code:\n" + string.Join("\n", unused));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Translations_keep_the_same_placeholders(string language)
    {
        L.Use(language);
        try
        {
            var broken = L.KnownIn(language).Where(k => !Placeholders(k).SetEquals(Placeholders(L.T(k)))).ToList();
            Assert.True(broken.Count == 0, "Placeholders differ:\n" + string.Join("\n", broken));
        }
        finally
        {
            L.Use("en");
        }
    }

    [Fact]
    public void Formats_in_each_language()
    {
        try
        {
            L.Use("nl");
            Assert.Equal("Over 5 min", L.F("In {0} min", 5));
            Assert.Equal("Batterij raakt leeg", L.T("Low battery"));
            Assert.Equal("A text without translation", L.T("A text without translation"));
            Assert.Equal("4e", L.Ordinal(4));
            L.Use("de");
            Assert.Equal("4.", L.Ordinal(4));
            L.Use("fr");
            Assert.Equal("1re", L.Ordinal(1));
            L.Use("xx");
            Assert.False(L.IsTranslated);
        }
        finally
        {
            L.Use("en");
        }
        Assert.Equal("In 5 min", L.F("In {0} min", 5));
        Assert.Equal("22nd", L.Ordinal(22));
    }

    private static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+(:[^}]*)?\}").Select(m => m.Value).ToHashSet();

    private static IEnumerable<string> CodeFiles() =>
        Directory.EnumerateFiles(Src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !Path.GetFileName(f).StartsWith("Strings."));

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(Path.Combine(Src, "DynamicIsland.Settings"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.EndsWith("App.xaml"));

    /// <summary>The texts that must be translated: L.T/L.F arguments and the XAML's visible texts.</summary>
    private static IEnumerable<string> TranslatedTexts()
    {
        var code = CodeFiles()
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"L\.[TF]\(\s*""((?:[^""\\]|\\.)*)""").Select(m => Regex.Unescape(m.Groups[1].Value)));

        var xaml = XamlFiles()
            .SelectMany(f => Regex.Matches(File.ReadAllText(f),
                    @"\s(?:Text|Header|Description|Content|Title|Message|PlaceholderText|ToolTipService\.ToolTip)=""([^""{&][^""]*)""")
                .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)))
            .Where(t => t.Any(char.IsLetter));

        return code.Concat(xaml).Distinct();
    }

    /// <summary>Every string literal in the code and every attribute value in the XAML.</summary>
    private static IEnumerable<string> AllStrings()
    {
        var code = CodeFiles()
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"""((?:[^""\\\r\n]|\\.)*)""").Select(m => Unescape(m.Groups[1].Value)));
        var xaml = XamlFiles()
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"=""([^""]*)""").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)));
        return code.Concat(xaml);
    }

    private static string Unescape(string literal)
    {
        try
        {
            return Regex.Unescape(literal);
        }
        catch (ArgumentException)
        {
            return literal; // something Regex doesn't read the C# way (e.g. "\d" in a pattern); not a UI text anyway
        }
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
