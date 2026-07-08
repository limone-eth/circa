import CoreGraphics
import Foundation
import IOKit.ps

/// AC vs battery, via IOKit power sources.
enum Power {
    static var onBattery: Bool {
        guard let snapshot = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
              let type = IOPSGetProvidingPowerSourceType(snapshot)?.takeRetainedValue() as String?
        else { return false }
        return type == kIOPSBatteryPowerValue
    }
}

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

    // MARK: - macOS auto-brightness (ambient light compensation)

    private typealias ALCHasFn = @convention(c) (CGDirectDisplayID) -> Bool
    private typealias ALCGetFn = @convention(c) (CGDirectDisplayID, UnsafeMutablePointer<Bool>) -> Int32
    private typealias ALCSetFn = @convention(c) (CGDirectDisplayID, Bool) -> Int32

    private static let alcFns: (has: ALCHasFn, get: ALCGetFn, set: ALCSetFn)? = {
        guard let handle = dlopen(
            "/System/Library/PrivateFrameworks/DisplayServices.framework/DisplayServices", RTLD_NOW),
            let hasSym = dlsym(handle, "DisplayServicesHasAmbientLightCompensation"),
            let getSym = dlsym(handle, "DisplayServicesAmbientLightCompensationEnabled"),
            let setSym = dlsym(handle, "DisplayServicesEnableAmbientLightCompensation")
        else { return nil }
        return (unsafeBitCast(hasSym, to: ALCHasFn.self),
                unsafeBitCast(getSym, to: ALCGetFn.self),
                unsafeBitCast(setSym, to: ALCSetFn.self))
    }()

    static var autoBrightnessSupported: Bool {
        guard let alcFns, let id = builtinDisplay() else { return false }
        return alcFns.has(id)
    }

    static func autoBrightnessEnabled() -> Bool {
        guard let alcFns, let id = builtinDisplay() else { return false }
        var value = false
        return alcFns.get(id, &value) == 0 ? value : false
    }

    @discardableResult
    static func setAutoBrightness(_ enabled: Bool) -> Bool {
        guard let alcFns, let id = builtinDisplay() else { return false }
        return alcFns.set(id, enabled) == 0
    }
}
