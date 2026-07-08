namespace Circa;

/// <summary>
/// Compact solar-position math (accuracy well under 1 degree, plenty for
/// lighting). Same NOAA low-precision ephemeris as the macOS version.
/// </summary>
public static class Solar
{
    /// <summary>Sun elevation above the horizon in degrees.</summary>
    public static double Elevation(DateTime utcNow, double latitude, double longitude)
    {
        double jd = (utcNow - DateTime.UnixEpoch).TotalDays + 2440587.5;
        double d = jd - 2451545.0; // days since J2000

        double g = Deg2Rad((357.529 + 0.98560028 * d) % 360.0); // mean anomaly
        double q = 280.459 + 0.98564736 * d;                    // mean longitude (deg)
        double L = Deg2Rad((q + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) % 360.0);
        double e = Deg2Rad(23.439 - 0.00000036 * d);            // obliquity

        double rightAscension = Math.Atan2(Math.Cos(e) * Math.Sin(L), Math.Cos(L));
        double declination = Math.Asin(Math.Sin(e) * Math.Sin(L));

        double gmstHours = 18.697374558 + 24.06570982441908 * d;
        double localSidereal = Deg2Rad((gmstHours % 24.0) * 15.0 + longitude);

        double hourAngle = localSidereal - rightAscension;
        while (hourAngle > Math.PI) hourAngle -= 2 * Math.PI;
        while (hourAngle < -Math.PI) hourAngle += 2 * Math.PI;

        double lat = Deg2Rad(latitude);
        double sinElev = Math.Sin(lat) * Math.Sin(declination)
                       + Math.Cos(lat) * Math.Cos(declination) * Math.Cos(hourAngle);
        return Rad2Deg(Math.Asin(Math.Clamp(sinElev, -1.0, 1.0)));
    }

    /// <summary>Next moment the sun crosses above the horizon (10-minute scan, 36 h).</summary>
    public static DateTime NextSunriseUtc(DateTime utcStart, double latitude, double longitude)
    {
        double previous = Elevation(utcStart, latitude, longitude);
        DateTime t = utcStart;
        for (int i = 0; i < 36 * 6; i++)
        {
            DateTime next = t.AddMinutes(10);
            double e = Elevation(next, latitude, longitude);
            if (previous < 0 && e >= 0) return next;
            previous = e;
            t = next;
        }
        return utcStart.AddHours(8); // polar fallback
    }

    private static double Deg2Rad(double v) => v * Math.PI / 180.0;
    private static double Rad2Deg(double v) => v * 180.0 / Math.PI;
}
