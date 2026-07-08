using System.Text.Json;
using System.Text.Json.Serialization;

namespace Circa;

/// <summary>
/// JSON-backed persistence in %APPDATA%\Circa\settings.json. Same defaults
/// as the macOS version: 6500 K days, 2300 K nights, 25% night dim,
/// flicker-free only-on-power on by default.
/// </summary>
public sealed class Settings
{
    public double DayTemp { get; set; } = 6500;
    public double NightTemp { get; set; } = 2300;
    public double DimPercent { get; set; } = 25;
    public bool Enabled { get; set; } = true;
    public bool FlickerFree { get; set; } = false;
    public bool FlickerOnlyOnPower { get; set; } = true;
    /// <summary>Software brightness multiplier while the backlight is pinned.</summary>
    public double FlickerComp { get; set; } = 1.0;
    public double? CachedLatitude { get; set; }
    public double? CachedLongitude { get; set; }
    public string? CachedPlace { get; set; }

    [JsonIgnore]
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Circa", "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path), Options) ?? new Settings();
        }
        catch { /* corrupted settings: fall through to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Options));
        }
        catch { /* best effort */ }
    }
}
