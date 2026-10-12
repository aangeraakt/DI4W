using DynamicIsland.Settings;
using DynamicIsland.UI;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>(1) Volume bar when the output volume or mute changes, from any source (keys, mixer, apps).</summary>
public sealed class VolumeAddon : Addon, IMMNotificationClient
{
    private const string Speaker = "", Muted = "";

    private readonly HudView _hud = new(Speaker, Ui.White);
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private DateTime _quietUntil;

    public VolumeAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.Volume);
    public override string Name => "Volume";
    protected override bool IsEnabled(AddonSettings o) => o.Volume;

    protected override void Start()
    {
        _enumerator = new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(this);
        Attach();
    }

    protected override void Stop()
    {
        Detach();
        if (_enumerator is not null)
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
            _enumerator.Dispose();
            _enumerator = null;
        }
    }

    private void Attach()
    {
        Detach();
        if (_enumerator is null || !_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            SetStatus(L.T("No speakers or headphones found."), problem: false);
            return;
        }
        SetStatus(null);
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
        _quietUntil = DateTime.UtcNow.AddSeconds(1); // switching devices fires a notification; don't show it
    }

    private void Detach()
    {
        if (_device is null) return;
        _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
        _device.Dispose();
        _device = null;
    }

    // Called on a Core Audio thread.
    private void OnVolume(AudioVolumeNotificationData data)
    {
        if (DateTime.UtcNow < _quietUntil) return;
        var level = data.MasterVolume;
        var muted = data.Muted || level <= 0.001f;
        Context.Ui(() =>
        {
            _hud.Set(muted ? 0 : level, muted ? L.T("Muted") : $"{Math.Round(level * 100)}", muted ? Muted : Speaker);
            Show(_hud.Alert);
        });
    }

    // The default output (speakers -> headphones) changed: follow it.
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) Context.Ui(() => { if (_enumerator is not null) Attach(); });
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}
