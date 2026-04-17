using DiscordEchoFix.Audio;

namespace DiscordEchoFix.App;

/// <summary>
/// Keeps Discord muted on user-selected endpoints. Reacts to Windows audio events
/// (new session created, device added/removed, default endpoint changed) for
/// instant response, plus a low-frequency 30s poll as belt-and-suspenders in case
/// a notification is missed during device flux.
/// </summary>
internal sealed class AutoMuter : IDisposable
{
    private readonly DiscordSessionController _controller;
    private readonly Func<Settings> _getSettings;
    private readonly System.Windows.Forms.Timer _safetyPoll = new() { Interval = 30_000 };
    private readonly AudioEventBridge _events;

    public event EventHandler? Changed;

    public AutoMuter(DiscordSessionController controller, Func<Settings> getSettings)
    {
        _controller = controller;
        _getSettings = getSettings;
        _events = new AudioEventBridge(Tick);
        _safetyPoll.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        _events.Start();
        Tick();
        _safetyPoll.Start();
    }

    public void Stop()
    {
        _safetyPoll.Stop();
        _events.Stop();
    }

    public void Tick()
    {
        var s = _getSettings();
        if (!s.AutoMuteEnabled) return;
        var changed = _controller.EnforceMute(s.EndpointOverrides);
        if (changed > 0) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _safetyPoll.Dispose();
        _events.Dispose();
    }
}
