using System.IO;
using System.Text.Json;

namespace DynamicIsland.Settings;

/// <summary>One add-on's state as the island sees it, e.g. "Set a city to show the weather".</summary>
/// <param name="Problem">True when the add-on can't do its job until the user does something.</param>
public sealed record AddonState(bool Running, string? Message, bool Problem);

/// <summary>
/// What the island reports back to the settings app (compiled into both): per add-on state keyed by the
/// add-on's settings name ("Weather", "Calendar"…), and notification access.
/// </summary>
public sealed class IslandStatus
{
    public static string FilePath => Path.Combine(AppSettings.Folder, "status.json");

    public Dictionary<string, AddonState> Addons { get; set; } = new();

    /// <summary>Windows notification access: "Allowed", "Denied", "Unspecified" or an error.</summary>
    public string? NotificationAccess { get; set; }

    public static IslandStatus? TryLoad()
    {
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<IslandStatus>(stream);
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            // Status is a nicety; never let it break the island.
        }
    }
}
