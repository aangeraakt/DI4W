# Dynamic Island for Windows

The iPhone Dynamic Island, rebuilt for the Windows desktop.

A small black pill sits at the top-center of your screen. It changes shape to show what is going on: music playing, Claude working, a timer counting down, or a new Discord message.

<p align="center">
  <img src="docs/claude-done.png" alt="Claude is done alert" height="90">
  &nbsp;&nbsp;
  <img src="docs/split-timer-claude.png" alt="Timer with Claude in the side bubble" height="48">
</p>

## Features

| | In the pill | Click to expand |
|---|---|---|
| 🎵 **Spotify** | Album art and live audio bars, tinted to match the album art | Art, title and artist, a seekable progress bar, ⏮ ⏯ ⏭ |
| ✳️ **Claude Code** | Spinning Claude logo and elapsed time while Claude works | Every running session with its project and time |
| ⏱️ **Timer** | Orange timer icon and countdown | Pause/resume, cancel and a large countdown |
| 💬 **Discord** | A new message pops out of the island | Click to jump to Discord |

It also includes:

- **iOS-style animation.** The pill springs between sizes with a small bounce, and content fades in with a blur.
- **Two at once.** When two things are active, the second shows as a small circle next to the pill, as on the iPhone. Click it to swap them.
- **Alerts.** "Claude is done", "Claude needs you", "Timer done" and Discord messages pop open and close again by themselves. They stay open while your mouse is over them.
- **Stays out of the way.** It never takes keyboard focus, you can click through everything around the pill, and it hides while a fullscreen game or video is playing.
- **Lightweight.** Native C# and WPF, with a separate WinUI 3 settings app. Spotify works without a Spotify account or API key.

## Requirements

- Windows 10 (version 2004 or later) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Getting started

```bash
git clone https://github.com/aangeraakt/DI4W.git
cd DI4W
dotnet run --project src/DynamicIsland -c Release
```

The island appears at the top of your screen, with a purple icon in the system tray.

To get a standalone `.exe` instead:

```bash
dotnet publish src/DynamicIsland -c Release -o publish
```

This publishes the island and the settings app (`publish\Settings\`) in one go. Then run `publish\DynamicIsland.exe`. To start it automatically, turn on **Start with Windows** in the settings app.

## How to use it

| Action | What happens |
|---|---|
| **Click the empty island** | Opens the timer picker. Choose a preset, or scroll over the minutes/seconds, then press **Start**. |
| **Click an activity** | Expands it (music player, timer controls, Claude sessions). |
| **Move the mouse away** | Collapses it again. |
| **Click the small circle** | Swaps the second activity into the main pill. |
| **Click an alert** | Opens the app it came from and closes the alert. |
| **Right-click the island or tray icon** | Quick timers, Pause, **Settings…** and Quit. |
| **Double-click the tray icon** | Opens the settings app. |

## Setting up the integrations

### 🎵 Spotify

No setup is needed. Open Spotify and play something. The island reads Windows' built-in media controls, the same ones the volume pop-up uses.

To show other players too (browser, Apple Music and so on), turn on **Show any media player** under **Music** in the settings app.

### ✳️ Claude Code

Run this once to connect Claude Code to the island:

```bash
powershell -ExecutionPolicy Bypass -File tools/install-claude-hooks.ps1
```

This adds four [hooks](https://docs.claude.com/en/docs/claude-code/hooks) to `~/.claude/settings.json`. Your other settings are kept, and a `.bak` backup is made first.

| Claude Code event | Island shows |
|---|---|
| You send a prompt | "Claude is working" with a timer |
| Claude finishes | **Claude is done** with the project name and how long it took |
| Claude needs permission or input | **Claude needs you** (amber, stays until you hover it) |
| Session closes | The working activity disappears |

The hooks only apply to Claude Code sessions started *after* you install them. To remove them again:

```bash
powershell -ExecutionPolicy Bypass -File tools/install-claude-hooks.ps1 -Uninstall
```

<details>
<summary>Test it without Claude</summary>

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:47823/claude/stop -Body '{"cwd":"C:\\my-project"}'
```
</details>

### 💬 Discord

1. Make sure desktop notifications are **on** in Discord (**User Settings → Notifications**).
2. The first time the island starts, Windows may ask whether it can read your notifications. Click **Allow**.
3. *(Optional)* To get only the island pop-up instead of both it and the normal Windows pop-up, go to **Windows Settings → System → Notifications → Discord** and turn off **Show notification banners**. The island keeps working.

You can add other apps, such as WhatsApp or Outlook, under **Notifications** in the settings app.

## Settings

Open **Settings…** from the tray or right-click menu. It is a separate WinUI 3 app in the Windows 11 Settings style. Changes save immediately and the island applies them right away. Turning music or Claude Code on or off, or changing the port, restarts the island by itself.

Everything is stored in `%AppData%\DynamicIsland\settings.json`. You can also edit that file by hand; the island picks up the changes.

| Setting | Default | Description |
|---|---|---|
| `TopOffset` | `8` | Distance from the top of the screen |
| `Scale` | `1.0` | Overall size (also under **Size** in the menu) |
| `HideInFullscreen` | `true` | Hide while fullscreen games or videos run |
| `MediaEnabled` | `true` | Show the music activity |
| `MediaAnyPlayer` | `false` | Show players other than Spotify |
| `MediaHideAfterPauseSeconds` | `10` | How long the music activity stays after you pause |
| `ClaudeEnabled` | `true` | Listen for Claude Code hooks |
| `ClaudePort` | `47823` | Local port for the hooks (also set `DYNAMIC_ISLAND_PORT` for the hook script if you change it) |
| `NotificationsEnabled` | `true` | Show notification pop-outs |
| `NotificationApps` | `["Discord"]` | Apps whose notifications pop out of the island |
| `Sounds` | `true` | Chimes for alerts and the timer alarm |

Logs are written to `%AppData%\DynamicIsland\logs`.

## Troubleshooting

- **Nothing happens with Spotify.** Make sure Spotify is the desktop app, not the web player, or turn on **Show any media player**.
- **The audio bars don't move.** The bars react to what your speakers play. If no audio device is available, they fall back to a simple animation.
- **No Claude alerts.** Start a *new* Claude Code session after installing the hooks, and check that the island is running.
- **No Discord pop-ups.** Check that notification access is allowed under **Windows Settings → Privacy & security → Notifications**, and that Discord's own notifications are on.
- **Something else.** Look in the log folder (settings app → **About** → **Logs**).

## Project structure

```
src/DynamicIsland/
├─ IslandWindow.xaml(.cs)      the pill: window, shapes, spring animation, input
├─ Animation/Spring.cs         damped spring used for all shape changes
├─ Core/                       Activity base class, ActivityManager (priorities, alerts), logging
├─ Activities/
│  ├─ Media/                   Windows media session + loopback audio bars
│  ├─ Claude/                  local hook server + Claude activity/alerts
│  ├─ Notifications/           Windows notification listener (Discord)
│  └─ Timer/                   timer activity + picker
├─ Settings/                   settings.json + Start with Windows
└─ UI/Ui.cs                    shared colors, fonts, icons and controls
src/DynamicIsland.Settings/     WinUI 3 settings app (shares Settings/AppSettings.cs with the island)
tools/
├─ claude-hook.ps1             called by Claude Code hooks, forwards events to the island
└─ install-claude-hooks.ps1    adds/removes the hooks in ~/.claude/settings.json
```

### Adding your own activity

Subclass `Core/Activity`. Provide `Leading`, `Trailing`, `Bubble` and `Expanded` views and a `Priority`, and set `IsActive`. Then register it in `App.xaml.cs` with `_manager.Register(...)`. For one-off pop-ups, call `_manager.ShowAlert(new IslandAlert { ... })`.

## License

[MIT](LICENSE)
