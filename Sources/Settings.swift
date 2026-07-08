import Foundation

/// UserDefaults-backed persistence. All reads have sensible defaults so first
/// launch needs no setup.
enum Settings {
    private static let d = UserDefaults.standard

    static var dayTemp: Double {
        get { d.object(forKey: "dayTemp") as? Double ?? 6500 }
        set { d.set(newValue, forKey: "dayTemp") }
    }
    static var nightTemp: Double {
        get { d.object(forKey: "nightTemp") as? Double ?? 2300 }
        set { d.set(newValue, forKey: "nightTemp") }
    }
    /// Extra software dimming at night, 0...70 (%). Scaled by the same solar
    /// blend as color temperature: no effect in daylight, full value at night.
    static var dimPercent: Double {
        get { d.object(forKey: "dimPercent") as? Double ?? 25 }
        set { d.set(newValue, forKey: "dimPercent") }
    }
    static var enabled: Bool {
        get { d.object(forKey: "enabled") as? Bool ?? true }
        set { d.set(newValue, forKey: "enabled") }
    }
    static var flickerFree: Bool {
        get { d.object(forKey: "flickerFree") as? Bool ?? false }
        set { d.set(newValue, forKey: "flickerFree") }
    }
    /// Software brightness multiplier that compensates for the backlight being
    /// pinned at 100% in flicker-free mode. 1.0 when the mode is off.
    static var flickerComp: Double {
        get { d.object(forKey: "flickerComp") as? Double ?? 1.0 }
        set { d.set(newValue, forKey: "flickerComp") }
    }

    static var cachedLatitude: Double? {
        get { d.object(forKey: "cachedLatitude") as? Double }
        set { d.set(newValue, forKey: "cachedLatitude") }
    }
    static var cachedLongitude: Double? {
        get { d.object(forKey: "cachedLongitude") as? Double }
        set { d.set(newValue, forKey: "cachedLongitude") }
    }
    static var cachedPlace: String? {
        get { d.string(forKey: "cachedPlace") }
        set { d.set(newValue, forKey: "cachedPlace") }
    }
}
