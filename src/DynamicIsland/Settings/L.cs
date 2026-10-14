using System.Globalization;

namespace DynamicIsland.Settings;

/// <summary>
/// Translations for both apps. Text is written in English in the code and looked up here:
///   L.T("Low battery")                -> "Batterij bijna leeg" in Dutch
///   L.F("In {0} min", minutes)         -> "Over 5 min"
/// The translations live in Strings.nl.cs, Strings.de.cs and Strings.fr.cs; unit tests check every L.T/L.F text
/// in the code has one in each language, and that no translation is left over after its text was removed.
/// </summary>
public static partial class L
{
    /// <summary>The languages besides English, with their name in that language (for the settings).</summary>
    public static readonly (string Code, string Name)[] Languages = { ("nl", "Nederlands"), ("de", "Deutsch"), ("fr", "Français") };

    private static Dictionary<string, string>? _table;

    /// <summary>"" follows Windows' display language; "en", "nl", "de" or "fr" force one.</summary>
    public static void Use(string setting)
    {
        var language = (string.IsNullOrEmpty(setting) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : setting).ToLowerInvariant();
        _table = Table(language);
        Language = _table is null ? "en" : language;
    }

    /// <summary>"en", "nl", "de" or "fr".</summary>
    public static string Language { get; private set; } = "en";

    public static bool IsDutch => Language == "nl";

    /// <summary>True when the text is shown in another language than English.</summary>
    public static bool IsTranslated => _table is not null;

    /// <summary>The text in the current language.</summary>
    public static string T(string english) => _table is not null && _table.TryGetValue(english, out var text) ? text : english;

    /// <summary>A text with placeholders ({0}, {1}…) in the current language.</summary>
    public static string F(string english, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(english), args);

    /// <summary>For the settings app's page translator: the translation, or null to leave the text alone.</summary>
    public static string? Find(string english) => _table is not null && _table.TryGetValue(english, out var text) ? text : null;

    /// <summary>"1st", "1e", "1.", "1re": for "every 4th break".</summary>
    public static string Ordinal(int n) => Language switch
    {
        "nl" => $"{n}e",
        "de" => $"{n}.",
        "fr" => n == 1 ? "1re" : $"{n}e",
        _ => n % 100 is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" },
    };

    /// <summary>Every English text that has a Dutch translation (used by the coverage tests).</summary>
    public static IReadOnlyCollection<string> Known => Dutch.Keys;

    /// <summary>The English texts translated into one language (used by the coverage tests).</summary>
    public static IReadOnlyCollection<string> KnownIn(string language) => (IReadOnlyCollection<string>?)Table(language)?.Keys ?? Array.Empty<string>();

    // A method, not a field: the tables are fields of the other partial files, whose initialization order
    // relative to this file isn't defined.
    private static Dictionary<string, string>? Table(string language) => language switch
    {
        "nl" => Dutch,
        "de" => German,
        "fr" => French,
        _ => null,
    };
}
