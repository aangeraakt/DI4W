using System.Globalization;

namespace DynamicIsland.Settings;

/// <summary>
/// Translations for both apps. Text is written in English in the code and looked up here:
///   L.T("Low battery")                -> "Batterij bijna leeg" in Dutch
///   L.F("In {0} min", minutes)         -> "Over 5 min"
/// The Dutch text lives in Strings.nl.cs; a unit test checks every L.T/L.F text in the code has a translation.
/// </summary>
public static partial class L
{
    private static bool _dutch;

    /// <summary>"" follows Windows' display language; "en" or "nl" force one.</summary>
    public static void Use(string setting)
    {
        var language = string.IsNullOrEmpty(setting) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : setting;
        _dutch = language.Equals("nl", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDutch => _dutch;

    /// <summary>The text in the current language.</summary>
    public static string T(string english) => _dutch && Dutch.TryGetValue(english, out var dutch) ? dutch : english;

    /// <summary>A text with placeholders ({0}, {1}…) in the current language.</summary>
    public static string F(string english, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(english), args);

    /// <summary>For the settings app's page translator: the translation, or null to leave the text alone.</summary>
    public static string? Find(string english) => _dutch && Dutch.TryGetValue(english, out var dutch) ? dutch : null;

    /// <summary>Every English text that has a Dutch translation (used by the coverage test).</summary>
    public static IReadOnlyCollection<string> Known => Dutch.Keys;
}
