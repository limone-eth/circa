using System.Text.Json;

namespace Circa;

/// <summary>
/// Location with graceful degradation: IP geolocation → cached last fix →
/// timezone estimate. (The macOS version also uses OS location services;
/// on Windows v1 the IP lookup is primary — city-level is plenty for
/// sunrise/sunset.)
/// </summary>
public sealed class LocationProvider
{
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string PlaceName { get; private set; }
    public string SourceName { get; private set; }

    public event Action? Updated;

    private readonly Settings _settings;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public LocationProvider(Settings settings)
    {
        _settings = settings;
        if (settings.CachedLatitude is double lat && settings.CachedLongitude is double lon)
        {
            Latitude = lat;
            Longitude = lon;
            PlaceName = settings.CachedPlace ?? $"{lat:F1}, {lon:F1}";
            SourceName = "last known";
        }
        else
        {
            // Longitude from the UTC offset puts solar noon near clock noon.
            Latitude = 40;
            Longitude = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalHours * 15.0;
            PlaceName = "estimating from clock";
            SourceName = "time zone";
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://ipapi.co/json/"));
            var root = doc.RootElement;
            if (root.TryGetProperty("latitude", out var latEl) && latEl.ValueKind == JsonValueKind.Number &&
                root.TryGetProperty("longitude", out var lonEl) && lonEl.ValueKind == JsonValueKind.Number)
            {
                Latitude = latEl.GetDouble();
                Longitude = lonEl.GetDouble();
                SourceName = "IP address";
                if (root.TryGetProperty("city", out var cityEl) && cityEl.ValueKind == JsonValueKind.String)
                    PlaceName = cityEl.GetString()!;
                _settings.CachedLatitude = Latitude;
                _settings.CachedLongitude = Longitude;
                _settings.CachedPlace = PlaceName;
                _settings.Save();
                Updated?.Invoke();
            }
        }
        catch { /* keep the current estimate */ }
    }
}
