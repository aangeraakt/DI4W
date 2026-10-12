using System.Windows;
using Windows.System.Power;

namespace DynamicIsland.Core;

/// <summary>
/// Windows' battery saver (energy saver). While it's on the island does less: the music bars redraw 10 times
/// a second instead of 30, blur and shadow effects are skipped, and background checks run less often.
/// </summary>
public static class Power
{
    private static bool _started;

    public static bool Saving { get; private set; }

    /// <summary>Raised on the UI thread when battery saver turns on or off.</summary>
    public static event Action? Changed;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        Saving = Read();
        PowerManager.EnergySaverStatusChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var saving = Read();
            if (saving == Saving) return;
            Saving = saving;
            Log.Info($"Battery saver {(saving ? "on: reducing effects and checks" : "off")}");
            Changed?.Invoke();
        });
    }

    /// <summary>An interval that stretches while battery saver is on.</summary>
    public static TimeSpan Interval(TimeSpan normal, double savingFactor = 3) => Saving ? normal * savingFactor : normal;

    private static bool Read()
    {
        try
        {
            return PowerManager.EnergySaverStatus == EnergySaverStatus.On;
        }
        catch
        {
            return false;
        }
    }
}
