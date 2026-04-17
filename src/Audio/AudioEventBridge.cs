using System.Runtime.InteropServices;
using System.Threading;
using static DiscordEchoFix.Audio.CoreAudio;

namespace DiscordEchoFix.Audio;

/// <summary>
/// Subscribes to two Windows audio COM events so we react instantly instead of polling:
/// IMMNotificationClient (device add/remove/default change) and IAudioSessionNotification
/// (new audio session created on a render endpoint). The moment Discord opens a session,
/// the latter fires and we re-apply mute. Callbacks arrive from arbitrary RPC threads,
/// so we marshal back to the UI thread via the captured SynchronizationContext.
/// </summary>
internal sealed class AudioEventBridge : IDisposable
{
    private readonly Action _onChange;
    private readonly SynchronizationContext _sync;
    private readonly DeviceNotifier _deviceNotifier;
    private readonly List<EndpointSubscription> _subs = new();
    private IMMDeviceEnumerator? _enumerator;
    private bool _disposed;

    public AudioEventBridge(Action onChange)
    {
        _onChange = onChange;
        _sync = SynchronizationContext.Current ?? new SynchronizationContext();
        _deviceNotifier = new DeviceNotifier(this);
    }

    public void Start()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        _enumerator.RegisterEndpointNotificationCallback(_deviceNotifier);
        SubscribeAllRenderEndpoints();
    }

    public void Stop()
    {
        if (_enumerator != null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(_deviceNotifier); } catch { }
        }
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();
        if (_enumerator != null) { Marshal.ReleaseComObject(_enumerator); _enumerator = null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private void SubscribeAllRenderEndpoints()
    {
        if (_enumerator == null) return;
        if (_enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, out var devices) != 0) return;
        if (devices.GetCount(out var count) != 0) { Marshal.ReleaseComObject(devices); return; }

        for (uint i = 0; i < count; i++)
        {
            if (devices.Item(i, out var device) != 0) continue;
            TrySubscribe(device);
        }
        Marshal.ReleaseComObject(devices);
    }

    private void TrySubscribe(IMMDevice device)
    {
        try
        {
            var iid = IID_IAudioSessionManager2;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var raw) != 0)
            {
                Marshal.ReleaseComObject(device);
                return;
            }
            var manager = (IAudioSessionManager2)raw;
            var notifier = new SessionNotifier(this);
            if (manager.RegisterSessionNotification(notifier) != 0)
            {
                Marshal.ReleaseComObject(manager);
                Marshal.ReleaseComObject(device);
                return;
            }
            // Touch the enumerator once - legacy quirk that "arms" notifications on older Windows.
            manager.GetSessionEnumerator(out var sessions);
            if (sessions != null) Marshal.ReleaseComObject(sessions);

            _subs.Add(new EndpointSubscription(device, manager, notifier));
        }
        catch { }
    }

    private void Notify() =>
        _sync.Post(_ => { try { _onChange(); } catch { } }, null);

    private void RebuildSubscriptions()
    {
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();
        SubscribeAllRenderEndpoints();
        Notify();
    }

    private sealed class EndpointSubscription : IDisposable
    {
        private readonly IMMDevice _device;
        private readonly IAudioSessionManager2 _manager;
        private readonly SessionNotifier _notifier;

        public EndpointSubscription(IMMDevice device, IAudioSessionManager2 manager, SessionNotifier notifier)
        {
            _device = device; _manager = manager; _notifier = notifier;
        }

        public void Dispose()
        {
            try { _manager.UnregisterSessionNotification(_notifier); } catch { }
            try { Marshal.ReleaseComObject(_manager); } catch { }
            try { Marshal.ReleaseComObject(_device); } catch { }
        }
    }

    [ComVisible(true)]
    private sealed class SessionNotifier : IAudioSessionNotification
    {
        private readonly AudioEventBridge _owner;
        public SessionNotifier(AudioEventBridge owner) { _owner = owner; }
        public int OnSessionCreated(IAudioSessionControl newSession) { _owner.Notify(); return 0; }
    }

    [ComVisible(true)]
    private sealed class DeviceNotifier : IMMNotificationClient
    {
        private readonly AudioEventBridge _owner;
        public DeviceNotifier(AudioEventBridge owner) { _owner = owner; }

        public int OnDeviceStateChanged(string deviceId, uint dwNewState)
        { _owner._sync.Post(_ => _owner.RebuildSubscriptions(), null); return 0; }
        public int OnDeviceAdded(string deviceId)
        { _owner._sync.Post(_ => _owner.RebuildSubscriptions(), null); return 0; }
        public int OnDeviceRemoved(string deviceId)
        { _owner._sync.Post(_ => _owner.RebuildSubscriptions(), null); return 0; }
        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
        { _owner.Notify(); return 0; }
        public int OnPropertyValueChanged(string deviceId, PROPERTYKEY key) => 0;
    }
}
