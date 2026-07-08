import CoreGraphics
import Foundation

/// Hardware backlight control via the private DisplayServices framework,
/// loaded at runtime so the app still builds and runs if Apple removes it —
/// flicker-free mode just becomes unavailable.
enum Brightness {
    private typealias GetFn = @convention(c) (CGDirectDisplayID, UnsafeMutablePointer<Float>) -> Int32
    private typealias SetFn = @convention(c) (CGDirectDisplayID, Float) -> Int32

    private static let fns: (get: GetFn, set: SetFn)? = {
        guard let handle = dlopen(
            "/System/Library/PrivateFrameworks/DisplayServices.framework/DisplayServices", RTLD_NOW),
            let getSym = dlsym(handle, "DisplayServicesGetBrightness"),
            let setSym = dlsym(handle, "DisplayServicesSetBrightness")
        else { return nil }
        return (unsafeBitCast(getSym, to: GetFn.self), unsafeBitCast(setSym, to: SetFn.self))
    }()

    static var available: Bool { fns != nil && builtinDisplay() != nil }

    static func get(_ id: CGDirectDisplayID) -> Float? {
        guard let fns else { return nil }
        var value: Float = 0
        return fns.get(id, &value) == 0 ? value : nil
    }

    @discardableResult
    static func set(_ id: CGDirectDisplayID, _ value: Float) -> Bool {
        guard let fns else { return false }
        return fns.set(id, max(0, min(1, value))) == 0
    }

    static func builtinDisplay() -> CGDirectDisplayID? {
        var count: UInt32 = 0
        var ids = [CGDirectDisplayID](repeating: 0, count: 16)
        guard CGGetOnlineDisplayList(16, &ids, &count) == .success else { return nil }
        return ids.prefix(Int(count)).first { CGDisplayIsBuiltin($0) != 0 }
    }
}
