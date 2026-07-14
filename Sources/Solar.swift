import Foundation

/// Compact solar-position math (accuracy well under 1°, plenty for lighting).
/// Based on the standard low-precision solar ephemeris used by NOAA.
enum Solar {
    /// Sun elevation above the horizon in degrees at `date` for lat/lon.
    static func elevation(date: Date, latitude: Double, longitude: Double) -> Double {
        let jd = date.timeIntervalSince1970 / 86400.0 + 2440587.5
        let d = jd - 2451545.0 // days since J2000

        let g = deg2rad((357.529 + 0.98560028 * d).truncatingRemainder(dividingBy: 360)) // mean anomaly
        let q = 280.459 + 0.98564736 * d // mean longitude (deg)
        let L = deg2rad((q + 1.915 * sin(g) + 0.020 * sin(2 * g)).truncatingRemainder(dividingBy: 360)) // ecliptic longitude
        let e = deg2rad(23.439 - 0.00000036 * d) // obliquity

        let rightAscension = atan2(cos(e) * sin(L), cos(L))
        let declination = asin(sin(e) * sin(L))

        let gmstHours = 18.697374558 + 24.06570982441908 * d
        let localSidereal = deg2rad(gmstHours.truncatingRemainder(dividingBy: 24) * 15 + longitude)

        var hourAngle = localSidereal - rightAscension
        while hourAngle > .pi { hourAngle -= 2 * .pi }
        while hourAngle < -.pi { hourAngle += 2 * .pi }

        let lat = deg2rad(latitude)
        let sinElev = sin(lat) * sin(declination) + cos(lat) * cos(declination) * cos(hourAngle)
        return rad2deg(asin(max(-1, min(1, sinElev))))
    }

    /// Next moment the sun crosses above the horizon, scanning in 10-minute
    /// steps up to 36 h ahead (handles any latitude that has a sunrise at all).
    static func nextSunrise(after start: Date, latitude: Double, longitude: Double) -> Date {
        var previous = elevation(date: start, latitude: latitude, longitude: longitude)
        var t = start
        for _ in 0..<(36 * 6) {
            let next = t.addingTimeInterval(600)
            let e = elevation(date: next, latitude: latitude, longitude: longitude)
            if previous < 0 && e >= 0 { return next }
            previous = e
            t = next
        }
        return start.addingTimeInterval(8 * 3600) // polar fallback: 8 h
    }

    /// First moment the engine's day phase (±6° twilight thresholds) changes:
    /// 10-minute scan up to 26 h ahead, bisected to the minute. Nil when the
    /// sun never crosses a threshold (polar day/night).
    static func nextPhaseChange(after start: Date, latitude: Double, longitude: Double) -> Date? {
        func band(_ date: Date) -> Int {
            let e = elevation(date: date, latitude: latitude, longitude: longitude)
            return e > 6 ? 2 : (e < -6 ? 0 : 1)
        }
        let startBand = band(start)
        var lo = start
        for _ in 0..<(26 * 6) {
            var hi = lo.addingTimeInterval(600)
            if band(hi) != startBand {
                while hi.timeIntervalSince(lo) > 60 {
                    let mid = lo.addingTimeInterval(hi.timeIntervalSince(lo) / 2)
                    if band(mid) != startBand { hi = mid } else { lo = mid }
                }
                return hi
            }
            lo = hi
        }
        return nil
    }

    private static func deg2rad(_ v: Double) -> Double { v * .pi / 180 }
    private static func rad2deg(_ v: Double) -> Double { v * 180 / .pi }
}
