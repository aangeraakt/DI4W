using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using DynamicIsland.Updates;
using Windows.Devices.Geolocation;

namespace DynamicIsland.Addons.Info;

/// <summary>
/// (23) Current temperature and an icon in the empty pill, plus "Rain in ~15 min" warnings.
/// Uses Open-Meteo (free, no account or key). Location: the city from the settings, else Windows location.
/// </summary>
public sealed class WeatherAddon : Addon
{
    private static readonly FontFamily Symbols = new("Segoe UI Symbol");

    private readonly TextBlock _icon = new() { FontFamily = Symbols, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _temp = Ui.Text("", 14, FontWeights.SemiBold, Ui.Primary, tabular: true);
    private readonly StackPanel _view;
    private DispatcherTimer? _refresh;
    private (string Query, double Lat, double Lon, string Place)? _location;
    private DateTime _lastRainAlert;
    private string _optionsKey = "";

    public WeatherAddon(AddonContext context) : base(context)
    {
        _temp.Margin = new Thickness(5, 0, 0, 0);
        _view = new StackPanel { Orientation = Orientation.Horizontal, Children = { _icon, _temp } };
    }

    public override string Key => nameof(AddonSettings.Weather);
    public override string Name => "Weather";
    protected override bool IsEnabled(AddonSettings o) => o.Weather;

    protected override void Start()
    {
        _optionsKey = $"{Options.WeatherLocation.Trim()}|{Options.WeatherFahrenheit}";
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _refresh.Tick += async (_, _) => await Refresh();
        _refresh.Start();
        _ = Refresh();
    }

    protected override void Stop()
    {
        _refresh?.Stop();
        _refresh = null;
        Context.SetIdleTrailing(null);
    }

    // A new city or unit should show now, not at the next 15-minute refresh.
    protected override void OnOptionsChanged()
    {
        var key = $"{Options.WeatherLocation.Trim()}|{Options.WeatherFahrenheit}";
        if (key == _optionsKey) return;
        _optionsKey = key;
        _ = Refresh();
    }

    private async Task Refresh()
    {
        try
        {
            var where = await Locate();
            if (where is null)
            {
                Context.SetIdleTrailing(null);
                return;
            }

            var unit = Options.WeatherFahrenheit ? "fahrenheit" : "celsius";
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={where.Value.Lat}&longitude={where.Value.Lon}" +
                $"&current=temperature_2m,weather_code,is_day&minutely_15=precipitation&forecast_minutely_15=4&temperature_unit={unit}&timezone=auto");
            using var json = JsonDocument.Parse(await UpdateChecker.Http.GetStringAsync(url));
            if (!Running) return;

            var current = json.RootElement.GetProperty("current");
            var temp = current.GetProperty("temperature_2m").GetDouble();
            var code = current.GetProperty("weather_code").GetInt32();
            var day = current.GetProperty("is_day").GetInt32() == 1;

            var (symbol, color, description) = Describe(code, day);
            _icon.Text = symbol;
            _icon.Foreground = color;
            _temp.Text = $"{Math.Round(temp)}°";
            _view.ToolTip = L.F("{0}, {1}° in {2}", description, Math.Round(temp), where.Value.Place);
            Context.SetIdleTrailing(_view);
            SetStatus(L.F("{0}, {1}° in {2}", description, Math.Round(temp), where.Value.Place), problem: false);

            if (Options.RainAlerts && !IsWet(code)) CheckRain(json.RootElement, where.Value.Place);
        }
        catch (HttpRequestException ex)
        {
            Log.Info($"Weather update failed: {ex.Message}");
            SetStatus(L.T("Couldn't reach the weather service. Trying again in 15 minutes."));
        }
        catch (Exception ex)
        {
            Log.Info($"Weather update failed: {ex.Message}");
            SetStatus(L.F("Weather update failed: {0}", ex.Message));
        }
    }

    /// <summary>Rain in the next 15–45 minutes while it's dry now: warn, at most every two hours.</summary>
    private void CheckRain(JsonElement root, string place)
    {
        if (!root.TryGetProperty("minutely_15", out var m) || DateTime.UtcNow - _lastRainAlert < TimeSpan.FromHours(2)) return;
        var rain = m.GetProperty("precipitation").EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0).ToList();
        if (rain.Count < 3 || rain[0] > 0.05) return;
        var soon = rain.Skip(1).Take(3).ToList();
        var first = soon.FindIndex(r => r >= 0.1);
        if (first < 0) return;

        _lastRainAlert = DateTime.UtcNow;
        RainAlert((first + 1) * 15, place);
    }

    public override void Preview() =>
        RainAlert(15, Options.WeatherLocation.Trim() is { Length: > 0 } city ? city : _location?.Place ?? "Amsterdam");

    private void RainAlert(int minutes, string place) => Show(new IslandAlert
    {
        Content = Ui.AlertView(new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = Ui.Blue,
                                            Child = new TextBlock { Text = "☂", FontFamily = Symbols, FontSize = 22, Foreground = Ui.OnColor,
                                                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
                               L.F("Rain in about {0} min", minutes), place),
        Size = new Size(360, 76),
        Duration = TimeSpan.FromSeconds(6),
    });

    private async Task<(double Lat, double Lon, string Place)?> Locate()
    {
        var query = Options.WeatherLocation.Trim();
        if (_location is { } cached && cached.Query == query) return (cached.Lat, cached.Lon, cached.Place);

        if (query.Length > 0)
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?count=1&name={Uri.EscapeDataString(query)}";
            using var json = JsonDocument.Parse(await UpdateChecker.Http.GetStringAsync(url));
            if (!json.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                Log.Info($"Weather: place '{query}' not found");
                SetStatus(L.F("Couldn't find \"{0}\". Check the spelling, or try a bigger city nearby.", query));
                return null;
            }
            var r = results[0];
            _location = (query, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), r.GetProperty("name").GetString() ?? query);
        }
        else
        {
            // No city set: ask Windows (works when location is on for desktop apps).
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
            {
                Log.Info("Weather: no city set and Windows location is off");
                SetStatus(L.T("Set your city below. (Windows location is off for desktop apps.)"));
                return null;
            }
            var pos = await new Geolocator { DesiredAccuracy = PositionAccuracy.Default }
                .GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(10));
            _location = ("", pos.Coordinate.Point.Position.Latitude, pos.Coordinate.Point.Position.Longitude, L.T("your area"));
        }
        return (_location.Value.Lat, _location.Value.Lon, _location.Value.Place);
    }

    private static bool IsWet(int code) => code is (>= 51 and <= 67) or (>= 80 and <= 82) or >= 95;

    /// <summary>WMO weather code -> symbol, color and words.</summary>
    private static (string Symbol, Brush Color, string Words) Describe(int code, bool day) => code switch
    {
        0 => day ? ("☀", Ui.Amber, L.T("Clear sky")) : ("☾", Ui.Teal, L.T("Clear sky")),
        1 or 2 => day ? ("⛅", Ui.Amber, L.T("Partly cloudy")) : ("☁", Ui.Grey, L.T("Partly cloudy")),
        3 => ("☁", Ui.Grey, L.T("Cloudy")),
        45 or 48 => ("☁", Ui.Grey, L.T("Fog")),
        >= 51 and <= 67 or >= 80 and <= 82 => ("☂", Ui.Blue, L.T("Rain")),
        >= 71 and <= 77 or 85 or 86 => ("❄", Ui.Primary, L.T("Snow")),
        >= 95 => ("⛈", Ui.Amber, L.T("Thunderstorm")),
        _ => ("☁", Ui.Grey, L.T("Cloudy")),
    };
}
