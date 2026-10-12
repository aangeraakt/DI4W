using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using Windows.Networking.Connectivity;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>(6) "No internet connection", "Back online", and "Connected to HomeWiFi" when you switch networks.</summary>
public sealed class NetworkAddon : Addon
{
    private const string Wifi = "", Ethernet = "";

    private sealed record State(bool Online, string? Name, bool Wireless);

    private DispatcherTimer? _settle;
    private State? _last;

    public NetworkAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.Network);
    public override string Name => "Network";
    protected override bool IsEnabled(AddonSettings o) => o.Network;

    protected override void Start()
    {
        _last = Read();
        // Network changes come in bursts (adapter down, up, DHCP, captive portal…); wait until it settles.
        _settle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Compare();
        };
        NetworkInformation.NetworkStatusChanged += OnChanged;
    }

    protected override void Stop()
    {
        NetworkInformation.NetworkStatusChanged -= OnChanged;
        _settle?.Stop();
        _settle = null;
    }

    private void OnChanged(object sender) => Context.Ui(() =>
    {
        _settle?.Stop();
        _settle?.Start();
    });

    private void Compare()
    {
        var now = Read();
        var before = _last;
        _last = now;
        if (before is null || now == before) return;

        if (!now.Online && before.Online)
        {
            Announce(L.T("No internet connection"), now.Name is null ? L.T("Check your Wi-Fi or cable") : L.F("Connected to {0}, but no internet", now.Name),
                     now.Wireless ? Wifi : Ethernet, Ui.Red);
        }
        else if (now.Online && !before.Online)
        {
            Announce(L.T("Back online"), now.Name ?? "", now.Wireless ? Wifi : Ethernet, Ui.Green);
        }
        else if (now.Online && now.Name != before.Name && now.Name is not null)
        {
            Announce(L.F("Connected to {0}", now.Name), now.Wireless ? "Wi-Fi" : "Ethernet", now.Wireless ? Wifi : Ethernet, Ui.Blue);
        }
    }

    private static State Read()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null) return new State(false, null, false);
            var online = profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
            var wireless = profile.IsWlanConnectionProfile;
            var name = wireless ? profile.WlanConnectionProfileDetails?.GetConnectedSsid() : profile.ProfileName;
            return new State(online, string.IsNullOrWhiteSpace(name) ? profile.ProfileName : name, wireless);
        }
        catch (Exception ex)
        {
            Log.Info($"Network state unavailable: {ex.Message}");
            return new State(true, null, false);
        }
    }

    private void Announce(string title, string subtitle, string glyph, System.Windows.Media.Brush color) => Show(new IslandAlert
    {
        Content = Ui.AlertView(Ui.GlyphCircle(glyph, color, 46, 20), title, subtitle),
        Size = new Size(380, 76),
        Duration = TimeSpan.FromSeconds(3.5),
    });
}
