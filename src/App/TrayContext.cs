using System.Drawing;
using DiscordEchoFix.Audio;

namespace DiscordEchoFix.App;

internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly DiscordSessionController _discord = new();
    private readonly HotkeyWindow _hotkey = new();
    private readonly Settings _settings;
    private readonly AutoMuter _muter;

    private ToolStripMenuItem _miStatus = null!;
    private ToolStripMenuItem _miAutoMute = null!;
    private ToolStripMenuItem _miAutoStart = null!;

    public TrayContext()
    {
        _settings = Settings.Load();
        _muter = new AutoMuter(_discord, () => _settings);
        _muter.Changed += (_, _) => RefreshUi();

        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "Discord Sonar Echo Fix",
            Icon = BuildIcon(false),
            ContextMenuStrip = BuildMenu()
        };
        _tray.DoubleClick += (_, _) => ToggleHotkeyAction();

        _hotkey.Pressed += (_, _) => ToggleHotkeyAction();
        if (!_hotkey.Register(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey))
        {
            ShowBalloon("Hotkey unavailable", "Default hotkey Ctrl+Shift+F8 is in use by another app.");
        }

        if (_settings.AutoMuteEnabled) _muter.Start();
        RefreshUi();

        ShowBalloon(
            _settings.AutoMuteEnabled ? "Discord Sonar Echo Fix - auto-mute ON" : "Discord Sonar Echo Fix - auto-mute OFF",
            "Right-click the tray icon to pick which devices to mute.");
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        _miStatus = new ToolStripMenuItem("Status: …") { Enabled = false };

        _miAutoMute = new ToolStripMenuItem("Auto-mute Discord (always on)", null, (_, _) => ToggleAutoMute())
        {
            Checked = _settings.AutoMuteEnabled,
            CheckOnClick = true
        };

        var miToggleNow = new ToolStripMenuItem("Toggle Discord mute now (Ctrl+Shift+F8)",
            null, (_, _) => ToggleHotkeyAction());

        var miSettings = new ToolStripMenuItem("Devices…", null, (_, _) => OpenSettings());
        var miDiagnose = new ToolStripMenuItem("Diagnose audio devices…", null, (_, _) => ShowDiagnostics());

        _miAutoStart = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutoStart())
        {
            Checked = AutoStart.IsEnabled(),
            CheckOnClick = true
        };

        var miAbout = new ToolStripMenuItem("About", null, (_, _) =>
        {
            MessageBox.Show(
                "Discord Sonar Echo Fix v1.0.1\n\n" +
                "Keeps Discord's per-app audio session muted on selected output\n" +
                "endpoints so its voice audio isn't re-broadcast through Discord\n" +
                "screenshare's system loopback. You still hear friends through\n" +
                "Sonar's virtual mix (Chat / Game / Media), which is left alone.\n\n" +
                "Hotkey: Ctrl+Shift+F8 — manually toggle mute on selected devices.\n" +
                "Double-click tray icon does the same.\n\n" +
                "Right-click → Devices… to pick which outputs are muted.",
                "Discord Sonar Echo Fix",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        });

        var miExit = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());

        menu.Items.AddRange(new ToolStripItem[]
        {
            _miStatus,
            new ToolStripSeparator(),
            _miAutoMute,
            miToggleNow,
            miSettings,
            new ToolStripSeparator(),
            _miAutoStart,
            miDiagnose,
            miAbout,
            new ToolStripSeparator(),
            miExit
        });
        return menu;
    }

    private void ToggleHotkeyAction()
    {
        var anyMuted = _discord.IsAnyMuted(_settings.EndpointOverrides);
        var changed = _discord.SetMuted(!anyMuted, _settings.EndpointOverrides);
        if (changed == 0)
        {
            ShowBalloon("No Discord sessions found",
                "Open Discord and play any audio so its session appears in Volume Mixer.");
            return;
        }
        RefreshUi();
    }

    private void ToggleAutoMute()
    {
        _settings.AutoMuteEnabled = _miAutoMute.Checked;
        _settings.Save();
        if (_settings.AutoMuteEnabled) _muter.Start();
        else _muter.Stop();
        RefreshUi();
    }

    private void ToggleAutoStart()
    {
        _settings.AutoStartWithWindows = _miAutoStart.Checked;
        AutoStart.Set(_settings.AutoStartWithWindows);
        _settings.Save();
    }

    private void OpenSettings()
    {
        using var f = new SettingsForm(_settings, _discord);
        f.ShowDialog();
        if (_settings.AutoMuteEnabled)
        {
            _muter.Stop();
            _muter.Start(); // immediate apply
        }
        else _muter.Stop();
        _miAutoMute.Checked = _settings.AutoMuteEnabled;
        RefreshUi();
    }

    private void RefreshUi()
    {
        var muted = _discord.IsAnyMuted(_settings.EndpointOverrides);
        var mode = _settings.AutoMuteEnabled ? "auto" : "manual";
        _miStatus.Text = $"Status: {(muted ? "MUTED" : "active")}  ({mode})";
        _tray.Icon?.Dispose();
        _tray.Icon = BuildIcon(muted);
        _tray.Text = $"Discord Sonar Echo Fix - {(muted ? "muted" : "active")} ({mode})";
    }

    private void ShowBalloon(string title, string body)
    {
        try
        {
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = body;
            _tray.ShowBalloonTip(2500);
        }
        catch { }
    }

    private void ShowDiagnostics()
    {
        var endpoints = _discord.Diagnose(_settings.EndpointOverrides);
        if (endpoints.Count == 0)
        {
            MessageBox.Show("No active render endpoints found.", "Diagnostics");
            return;
        }

        var lines = new List<string>
        {
            "Render endpoints — Discord audio sessions:",
            "",
            "[MUTE]  = Discord will be muted on this device",
            "[skip]  = Discord left alone here (your hearing path)",
            "(*)     = user override (otherwise default rule applied)",
            "(D)     = Discord session present here right now",
            "(M)     = currently muted",
            ""
        };
        foreach (var e in endpoints)
        {
            var flags = (e.ShouldMute ? "[MUTE] " : "[skip] ")
                      + (e.IsOverride ? "(*)" : "   ")
                      + (e.DiscordPresent ? "(D)" : "   ")
                      + (e.DiscordMuted ? "(M) " : "    ");
            var detail = e.SessionCount > 0 ? $" sessions={e.SessionCount} ({e.StateBreakdown})" : "";
            lines.Add($"{flags} {e.FriendlyName}{detail}");
        }
        lines.Add("");
        lines.Add("Use 'Devices…' to override the default rule per device.");

        MessageBox.Show(string.Join(Environment.NewLine, lines), "Discord Sonar Echo Fix - Diagnostics",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static Icon BuildIcon(bool muted)
    {
        const int size = 32;
        var bg = muted ? Color.FromArgb(220, 60, 60) : Color.FromArgb(60, 180, 90);
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using var bgBrush = new SolidBrush(bg);
            g.FillEllipse(bgBrush, 0, 0, size, size);

            using var fg = new SolidBrush(Color.White);
            using var font = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("D", font, fg, new RectangleF(0, 1, size, size), fmt);

            if (muted)
            {
                using var slashOuter = new Pen(bg, 5f);
                g.DrawLine(slashOuter, 4, 28, 28, 4);
                using var slash = new Pen(Color.White, 2.5f);
                g.DrawLine(slash, 4, 28, 28, 4);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void ExitApp()
    {
        _muter.Dispose();
        _hotkey.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }
}
