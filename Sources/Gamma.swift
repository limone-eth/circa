import CoreGraphics
import Foundation

/// Applies color temperature + software dimming through the displays' gamma
/// LUTs (the same mechanism f.lux uses). The original per-display tables are
/// cached on first touch so ColorSync calibration is preserved — we only
/// scale them, never replace them.
final class GammaController {
    private struct Table {
        var r: [CGGammaValue]
        var g: [CGGammaValue]
        var b: [CGGammaValue]
    }

    private var originals: [CGDirectDisplayID: Table] = [:]

    func onlineDisplays() -> [CGDirectDisplayID] {
        var count: UInt32 = 0
        var ids = [CGDirectDisplayID](repeating: 0, count: 16)
        guard CGGetOnlineDisplayList(16, &ids, &count) == .success else { return [] }
        return Array(ids.prefix(Int(count)))
    }

    private func original(for id: CGDirectDisplayID) -> Table? {
        if let cached = originals[id] { return cached }
        let capacity = CGDisplayGammaTableCapacity(id)
        guard capacity > 0 else { return nil }
        var r = [CGGammaValue](repeating: 0, count: Int(capacity))
        var g = r
        var b = r
        var sampleCount: UInt32 = 0
        guard CGGetDisplayTransferByTable(id, capacity, &r, &g, &b, &sampleCount) == .success,
              sampleCount > 0 else { return nil }
        let table = Table(r: Array(r.prefix(Int(sampleCount))),
                          g: Array(g.prefix(Int(sampleCount))),
                          b: Array(b.prefix(Int(sampleCount))))
        originals[id] = table
        return table
    }

    /// dim is a linear brightness multiplier 0...1 (floored to keep the
    /// screen readable — a fully black lockout would be unrecoverable).
    func apply(kelvin: Double, dim: Double) {
        let (mr, mg, mb) = Self.multipliers(kelvin: kelvin)
        let scale = max(0.10, min(1.0, dim))
        for id in onlineDisplays() {
            guard let t = original(for: id) else { continue }
            var r = t.r.map { CGGammaValue(Double($0) * mr * scale) }
            var g = t.g.map { CGGammaValue(Double($0) * mg * scale) }
            var b = t.b.map { CGGammaValue(Double($0) * mb * scale) }
            CGSetDisplayTransferByTable(id, UInt32(r.count), &r, &g, &b)
        }
    }

    /// Hand the LUTs back to ColorSync. Also happens automatically if the
    /// process exits.
    func restore() {
        CGDisplayRestoreColorSyncSettings()
    }

    /// RGB channel multipliers for a color temperature, normalized so 6500 K
    /// is exactly (1, 1, 1).
    static func multipliers(kelvin: Double) -> (Double, Double, Double) {
        let c = raw(kelvin: kelvin)
        let base = raw(kelvin: 6500)
        return (min(1, c.0 / base.0), min(1, c.1 / base.1), min(1, c.2 / base.2))
    }

    /// Tanner Helland's blackbody → RGB approximation, 0...1 per channel.
    private static func raw(kelvin: Double) -> (Double, Double, Double) {
        let t = max(1000, min(12000, kelvin)) / 100
        let r: Double = t <= 66 ? 255 : 329.698727446 * pow(t - 60, -0.1332047592)
        let g: Double = t <= 66
            ? 99.4708025861 * log(t) - 161.1195681661
            : 288.1221695283 * pow(t - 60, -0.0755148492)
        let b: Double
        if t >= 66 { b = 255 } else if t <= 19 { b = 0 } else { b = 138.5177312231 * log(t - 10) - 305.0447927307 }
        func unit(_ v: Double) -> Double { max(0, min(255, v)) / 255 }
        return (unit(r), unit(g), unit(b))
    }
}
