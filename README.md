# Discord Echo Fix

A small Windows tray app that stops Discord screenshare from echoing your friends back to themselves when you use SteelSeries Sonar (or any virtual-audio routing software).

## The problem

When you screenshare on Discord with "Share Sound" enabled, Discord captures system audio via Windows loopback. If your default playback device is the same physical output that Sonar mixes Discord voice into, the loopback picks up Discord's own audio and broadcasts it back into the call. Your friends hear themselves with a delay.

Disabling "Share Sound" works but loses game audio. Switching to Streamer mode in Sonar helps but doesn't eliminate the issue on every routing setup.

## What this does

Discord renders audio as separate per-endpoint sessions (one entry in Volume Mixer per device it touches). This app keeps Discord's session **muted on the loopback path** (your physical headset, the Sonar Microphone virtual device) while leaving it **unmuted on the Sonar mix path** (Sonar Chat / Game / Media / Aux), which is what feeds your headset.

Result: the screenshare loopback gets silence from Discord, so friends don't hear themselves. You still hear them through Sonar's mix.

The mute is reapplied automatically whenever Discord opens a new audio session (joining a voice call, restart, screenshare with sound, switching device), via Windows COM audio events.

## Install

1. Download `DiscordSonarEchoFix.exe` from the [latest release](https://github.com/codedbyyou/DiscordSonarEchoFix/releases).
2. Double-click it. SmartScreen may warn the first time; click **More info → Run anyway**.
3. The tray icon appears (green = Discord active, red = muted).
4. Right-click the tray icon for options.

There's nothing to install. Settings live in `%APPDATA%\DiscordEchoFix\settings.json`. To start with Windows, right-click the tray → **Start with Windows**.

## Use

By default it does the right thing for a typical Sonar setup. If you want to override which devices are muted, right-click the tray → **Devices...** and toggle individual outputs.

- **Tray double-click** or **Ctrl+Shift+F8**: manually toggle Discord mute on the selected devices.
- **Auto-mute (always on)**: keeps Discord muted on selected devices continuously.
- **Diagnose audio devices...**: shows every render endpoint and whether Discord is currently muted on it.

The default rule is: mute Discord on every output except devices whose name contains `Sonar` but not `Microphone`. This works for every Arctis variant (Nova 7, Nova Pro, 7+, etc.) and any non-SteelSeries headset used through Sonar.

## Build from source

Requires .NET 10 SDK.

```
dotnet publish src/DiscordSonarEchoFix.csproj -c Release -p:PublishMode=SelfContained -o dist
```

Output: `dist/DiscordSonarEchoFix.exe`, self-contained, no .NET install needed on the target machine.

## Signing

Releases on GitHub are signed via [SignPath](https://signpath.io/) (free for OSS). Locally, you can sign with a self-signed cert for personal use:

```
.\scripts\sign-local.ps1
```

This generates a code-signing cert in your user store, signs the exe in `dist/`, and installs the cert as a trusted publisher locally. Self-signed signatures don't help when sending to others - they'd see the same SmartScreen warning. For that, get the signed release from GitHub.

## Credits

The manual fix (muting Discord's Volume Mixer entry on the right device) is an old community workaround - I first saw it in [this YouTube video](https://www.youtube.com/watch?v=g891qYcBYBw).

[ayment/Fix-discord-echo](https://github.com/ayment/Fix-discord-echo) was the first tool that automated it. That repo is archived and was scoped tightly to the Arctis Nova Pro. This is a from-scratch rewrite that generalises to any SteelSeries headset and adds:

- Tray UI with status indicator and per-device picker
- Always-on mode that re-applies mute when Discord opens new sessions (voice call join, restart, screenshare with sound)
- COM event-driven (IMMNotificationClient + IAudioSessionNotification) instead of polling
- Hotkey toggle and auto-start

## License

MIT. See `LICENSE`.
