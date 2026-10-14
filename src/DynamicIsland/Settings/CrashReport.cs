using System.IO;

namespace DynamicIsland.Settings;

/// <summary>
/// The last crash of the island, kept in crash.txt until it's reported or dismissed (shared by both apps).
/// The island writes it when it goes down and offers to report it on the next start; nothing is ever sent
/// without the user opening the report and pressing the button themselves.
/// </summary>
public static class CrashReport
{
    public static string FilePath => Path.Combine(AppSettings.Folder, "crash.txt");

    public static void Write(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.WriteAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} (version {Updates.UpdateChecker.CurrentVersion})\n{ex}");
        }
        catch
        {
            // the process is going down; nothing more to do
        }
    }

    public static string? Read()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch
        {
            // already gone
        }
    }
}
