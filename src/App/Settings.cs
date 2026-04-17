using System.IO;
using System.Text.Json;

namespace DiscordEchoFix.App;

internal sealed class Settings
{
    public bool AutoMuteEnabled { get; set; } = true;
    public bool AutoStartWithWindows { get; set; } = false;
    public uint HotkeyModifiers { get; set; } = (uint)(HotkeyModifier.Control | HotkeyModifier.Shift);
    public uint HotkeyVirtualKey { get; set; } = 0x77; // F8

    /// <summary>
    /// Per-endpoint override. Key = endpoint friendly name. Value = true means
    /// "mute Discord here", false means "leave Discord alone here". When an
    /// endpoint isn't in this dictionary, the default rule is used
    /// (DiscordSessionController.IsHearingPathEndpoint).
    /// </summary>
    public Dictionary<string, bool> EndpointOverrides { get; set; } = new();

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "DiscordEchoFix", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

[Flags]
internal enum HotkeyModifier : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
    NoRepeat = 0x4000
}
