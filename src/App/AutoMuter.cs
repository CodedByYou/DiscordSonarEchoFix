using DiscordEchoFix.Audio;

namespace DiscordEchoFix.App;

/// <summary>
/// Keeps Discord muted on user-selected endpoints by polling every 3 seconds and
/// re-applying the configured mute state. (Originally tried event-driven via
/// IAudioSessionNotification but it was crashing in AUDIOSES.dll - polling is
/// boring and reliable.)
/// </summary>
internal sealed class AutoMuter : IDisposable
{
    private readonly DiscordSessionController _controller;
    private readonly Func<Settings> _getSettings;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 3000 };

    public event EventHandler? Changed;

    public AutoMuter(DiscordSessionController controller, Func<Settings> getSettings)
    {
        _controller = controller;
        _getSettings = getSettings;
        _poll.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        Tick();
        _poll.Start();
    }

    public void Stop() => _poll.Stop();

    public void Tick()
    {
        try
        {
            var s = _getSettings();
            if (!s.AutoMuteEnabled) return;
            var changed = _controller.EnforceMute(s.EndpointOverrides);
            if (changed > 0) Changed?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }

    public void Dispose() => _poll.Dispose();
}
