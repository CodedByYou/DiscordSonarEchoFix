using System.Diagnostics;
using System.Runtime.InteropServices;
using static DiscordEchoFix.Audio.CoreAudio;

namespace DiscordEchoFix.Audio;

/// <summary>
/// Mutes Discord's per-app session on user-selected render endpoints. Defaults to
/// "every endpoint that is NOT a Sonar virtual mix" (Chat/Game/Gaming/Media/Aux/Stream),
/// which is the screenshare-loopback path. The Sonar mixes are the path you HEAR
/// friends through, so they're left alone. Per-endpoint user overrides win.
/// </summary>
internal sealed class DiscordSessionController
{
    private static readonly string[] DiscordProcessNames =
    {
        "discord", "discordcanary", "discordptb", "discorddevelopment"
    };

    /// <summary>Default rule: skip Sonar virtual mixes that route audio back to your headset.</summary>
    public static bool IsHearingPathEndpoint(string friendlyName)
    {
        var n = friendlyName.ToLowerInvariant();
        return n.Contains("sonar") && !n.Contains("microphone");
    }

    /// <summary>Effective decision for whether Discord should be muted on a given endpoint.</summary>
    public static bool ShouldMuteOn(string friendlyName, IReadOnlyDictionary<string, bool> overrides)
    {
        if (overrides.TryGetValue(friendlyName, out var ov)) return ov;
        return !IsHearingPathEndpoint(friendlyName);
    }

    /// <summary>Snapshot info per render endpoint - used by the Diagnose dialog.</summary>
    public sealed record EndpointInfo(
        string FriendlyName,
        bool ShouldMute,
        bool IsOverride,
        bool DiscordPresent,
        bool DiscordMuted,
        int SessionCount,
        int ActiveSessionCount,
        string StateBreakdown);

    /// <summary>
    /// Ensures every Discord session on enabled endpoints is muted. Call repeatedly to
    /// catch sessions Discord opens later (new voice call, restart, etc.). Returns the
    /// number of sessions whose state was changed.
    /// </summary>
    public int EnforceMute(IReadOnlyDictionary<string, bool> overrides)
    {
        var ctx = Guid.Empty;
        var changed = 0;
        ForEachSession(overrides, (vol, _, _, shouldMute) =>
        {
            if (vol.GetMute(out var current) != 0) return;
            if (current == shouldMute) return;
            if (vol.SetMute(shouldMute, ref ctx) == 0) changed++;
        });
        return changed;
    }

    /// <summary>Force-set the mute state on enabled endpoints (manual toggle).</summary>
    public int SetMuted(bool mute, IReadOnlyDictionary<string, bool> overrides)
    {
        var ctx = Guid.Empty;
        var changed = 0;
        ForEachSession(overrides, (vol, _, _, shouldHandle) =>
        {
            if (!shouldHandle) return;
            if (vol.SetMute(mute, ref ctx) == 0) changed++;
        });
        return changed;
    }

    /// <summary>Returns true if any Discord session on an enabled endpoint is muted.</summary>
    public bool IsAnyMuted(IReadOnlyDictionary<string, bool> overrides)
    {
        var any = false;
        ForEachSession(overrides, (vol, _, _, shouldHandle) =>
        {
            if (!shouldHandle) return;
            if (vol.GetMute(out var m) == 0 && m) any = true;
        });
        return any;
    }

    public List<EndpointInfo> Diagnose(IReadOnlyDictionary<string, bool> overrides)
    {
        var pids = GetDiscordPids();
        var list = new List<EndpointInfo>();

        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, out var devices) != 0) return list;
        if (devices.GetCount(out var deviceCount) != 0) return list;

        for (uint i = 0; i < deviceCount; i++)
        {
            if (devices.Item(i, out var device) != 0) continue;
            try
            {
                var name = GetFriendlyName(device) ?? "(unknown)";
                var shouldMute = ShouldMuteOn(name, overrides);
                var isOverride = overrides.ContainsKey(name);
                var present = false;
                var muted = false;
                var sessionCount = 0;
                var activeCount = 0;
                var stateCounts = new Dictionary<int, int>();

                EnumerateDiscordSessionsOn(device, pids, (vol, c2) =>
                {
                    present = true;
                    sessionCount++;
                    if (vol.GetMute(out var m) == 0 && m) muted = true;
                    if (c2.GetState(out var st) == 0)
                    {
                        stateCounts[st] = stateCounts.GetValueOrDefault(st) + 1;
                        if (st == 1) activeCount++;
                    }
                });

                var breakdown = string.Join(",",
                    stateCounts.OrderBy(kv => kv.Key)
                               .Select(kv => $"{StateName(kv.Key)}={kv.Value}"));
                list.Add(new EndpointInfo(name, shouldMute, isOverride, present, muted, sessionCount, activeCount, breakdown));
            }
            finally { Marshal.ReleaseComObject(device); }
        }

        Marshal.ReleaseComObject(devices);
        Marshal.ReleaseComObject(enumerator);
        return list;
    }

    /// <summary>Lightweight enumeration of all active render endpoints with friendly names.</summary>
    public List<string> ListEndpoints()
    {
        var list = new List<string>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, out var devices) != 0) return list;
        if (devices.GetCount(out var deviceCount) != 0) return list;

        for (uint i = 0; i < deviceCount; i++)
        {
            if (devices.Item(i, out var device) != 0) continue;
            try
            {
                var name = GetFriendlyName(device);
                if (!string.IsNullOrEmpty(name)) list.Add(name);
            }
            finally { Marshal.ReleaseComObject(device); }
        }

        Marshal.ReleaseComObject(devices);
        Marshal.ReleaseComObject(enumerator);
        return list;
    }

    private static string StateName(int s) => s switch { 0 => "Inactive", 1 => "Active", 2 => "Expired", _ => "?" };

    private static HashSet<uint> GetDiscordPids()
    {
        var pids = new HashSet<uint>();
        foreach (var name in DiscordProcessNames)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { pids.Add((uint)p.Id); } catch { }
                p.Dispose();
            }
        return pids;
    }

    private static string? GetFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(STGM_READ, out var store) != 0) return null;
        try
        {
            var key = PKEY_Device_FriendlyName;
            var pv = default(PROPVARIANT);
            if (store.GetValue(ref key, out pv) != 0) return null;
            try { return pv.pwszVal != IntPtr.Zero ? Marshal.PtrToStringUni(pv.pwszVal) : null; }
            finally { PropVariantClear(ref pv); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    private static void EnumerateDiscordSessionsOn(IMMDevice device, HashSet<uint> pids,
        Action<ISimpleAudioVolume, IAudioSessionControl2> action)
    {
        var iid = IID_IAudioSessionManager2;
        if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var raw) != 0) return;
        var manager = (IAudioSessionManager2)raw;

        if (manager.GetSessionEnumerator(out var sessions) != 0) return;
        if (sessions.GetCount(out var sessionCount) != 0) return;

        for (var s = 0; s < sessionCount; s++)
        {
            if (sessions.GetSession(s, out var control) != 0) continue;
            if (control is not IAudioSessionControl2 c2) continue;
            if (c2.GetProcessId(out var pid) != 0) continue;
            if (!pids.Contains(pid)) continue;
            if (control is not ISimpleAudioVolume vol) continue;
            action(vol, c2);
        }
    }

    private void ForEachSession(IReadOnlyDictionary<string, bool> overrides,
        Action<ISimpleAudioVolume, IAudioSessionControl2, string, bool> action)
    {
        var pids = GetDiscordPids();
        if (pids.Count == 0) return;

        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, out var devices) != 0) return;
        if (devices.GetCount(out var deviceCount) != 0) return;

        for (uint i = 0; i < deviceCount; i++)
        {
            if (devices.Item(i, out var device) != 0) continue;
            try
            {
                var name = GetFriendlyName(device) ?? "";
                var shouldMute = ShouldMuteOn(name, overrides);
                EnumerateDiscordSessionsOn(device, pids, (vol, c2) => action(vol, c2, name, shouldMute));
            }
            finally { Marshal.ReleaseComObject(device); }
        }

        Marshal.ReleaseComObject(devices);
        Marshal.ReleaseComObject(enumerator);
    }
}
