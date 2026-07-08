import AppKit
import Combine
import CoreGraphics
import Foundation
import ServiceManagement

enum DayPhase {
    case day, twilight, night
}

/// The whole engine: solar clock → target temperature → gamma, plus the
/// published state the UI binds to. Everything runs on the main thread; the
/// work per tick is trivial (a few thousand float multiplies).
final class CircaModel: NSObject, ObservableObject {
    static let shared = CircaModel()

    let gamma = GammaController()
    let location = LocationProvider()

    // MARK: - Published settings (didSet persists and re-applies)

    @Published var enabled: Bool = Settings.enabled {
        didSet {
            Settings.enabled = enabled
            enabled ? applyNow() : suspendOutput()
        }
    }
    @Published var dayTemp: Double = Settings.dayTemp {
        didSet { Settings.dayTemp = dayTemp; applyNow() }
    }
    @Published var nightTemp: Double = Settings.nightTemp {
        didSet { Settings.nightTemp = nightTemp; applyNow() }
    }
    @Published var dimPercent: Double = Settings.dimPercent {
        didSet { Settings.dimPercent = dimPercent; applyNow() }
    }
    @Published var flickerFree: Bool = Settings.flickerFree {
        didSet {
            Settings.flickerFree = flickerFree
            if flickerFree {
                engageFlickerFree()
            } else {
                if !flickerSuspended { disengageFlickerFree() } else { Settings.flickerComp = 1.0; flickerBrightness = 1.0 }
                flickerSuspended = false
            }
            applyNow()
        }
    }
    /// Suspend flicker-free on battery, where the pinned backlight costs
    /// real runtime; hand brightness back to macOS until power returns.
    @Published var flickerOnlyOnPower: Bool = Settings.flickerOnlyOnPower {
        didSet { Settings.flickerOnlyOnPower = flickerOnlyOnPower; applyNow() }
    }
    /// True while flicker-free is configured on but paused because we're on
    /// battery with flickerOnlyOnPower set.
    @Published private(set) var flickerSuspended = false
    /// Mirror of the macOS "Automatically adjust brightness" setting.
    @Published var autoBrightness: Bool = Brightness.autoBrightnessEnabled() {
        didSet {
            guard !syncingAutoBrightness, autoBrightness != Brightness.autoBrightnessEnabled() else { return }
            Brightness.setAutoBrightness(autoBrightness)
        }
    }
    let autoBrightnessAvailable = Brightness.autoBrightnessSupported
    private var syncingAutoBrightness = false
    /// Perceived brightness in flicker-free mode (the backlight stays at
    /// 100%; this scales the gamma table instead). The popover slider is the
    /// only control for it: brightness keys are inert while the backlight is
    /// pinned, so there must be an explicit, recoverable control.
    @Published var flickerBrightness: Double = Settings.flickerComp {
        didSet {
            Settings.flickerComp = max(0.2, min(1.0, flickerBrightness))
            applyNow()
        }
    }
    @Published var launchAtLogin: Bool = (SMAppService.mainApp.status == .enabled) {
        didSet {
            do {
                if launchAtLogin { try SMAppService.mainApp.register() }
                else { try SMAppService.mainApp.unregister() }
            } catch {
                NSLog("Circa: launch-at-login change failed: \(error)")
            }
        }
    }

    // MARK: - Published live state (read-only for the UI)

    @Published private(set) var phase: DayPhase = .day
    @Published private(set) var appliedKelvin: Double = 6500
    /// 1 = full day, 0 = full night; drives both temperature and night dim.
    @Published private(set) var nightBlend: Double = 1
    @Published private(set) var placeName: String = ""
    @Published private(set) var locationSource: String = ""
    @Published var pausedUntil: Date?

    let flickerFreeAvailable = Brightness.available

    private var timer: Timer?
    private var lastLocationRefresh = Date()
    private var outputSuspended = false

    // MARK: - Lifecycle

    func start() {
        placeName = location.placeName
        locationSource = location.sourceName
        location.onUpdate = { [weak self] in
            guard let self else { return }
            placeName = location.placeName
            locationSource = location.sourceName
            tick(slew: false)
        }
        location.start()

        // Re-capture the backlight if flicker-free mode was on when we quit.
        if flickerFree { engageFlickerFree() }

        tick(slew: false)

        timer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { [weak self] _ in
            self?.tick(slew: true)
        }

        let workspace = NSWorkspace.shared.notificationCenter
        workspace.addObserver(self, selector: #selector(systemWoke),
                              name: NSWorkspace.didWakeNotification, object: nil)
        workspace.addObserver(self, selector: #selector(systemWoke),
                              name: NSWorkspace.screensDidWakeNotification, object: nil)

        CGDisplayRegisterReconfigurationCallback({ _, _, _ in
            DispatchQueue.main.async { CircaModel.shared.tick(slew: false) }
        }, nil)
    }

    func shutdown() {
        timer?.invalidate()
        if flickerFree, !flickerSuspended, let id = Brightness.builtinDisplay() {
            // Leave the backlight at the perceived brightness so quitting
            // doesn't blast the user with a full-brightness screen.
            let perceived = outputSuspended ? Settings.flickerComp : lastEffectiveDim
            Brightness.set(id, Float(perceived))
        }
        gamma.restore()
    }

    @objc private func systemWoke() {
        location.refresh()
        lastLocationRefresh = Date()
        // Give the displays a moment to come back before touching LUTs.
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) { [weak self] in
            self?.tick(slew: false)
        }
    }

    // MARK: - Recommended values

    static let recommendedDay = 6500.0
    static let recommendedNight = 2300.0
    static let recommendedDim = 25.0

    var isCustomized: Bool {
        dayTemp != Self.recommendedDay
            || nightTemp != Self.recommendedNight
            || dimPercent != Self.recommendedDim
    }

    /// Back to the recommended curve for this moment of the day.
    func resetToRecommended() {
        dayTemp = Self.recommendedDay
        nightTemp = Self.recommendedNight
        dimPercent = Self.recommendedDim
        if pausedUntil != nil { pausedUntil = nil }
        if !enabled { enabled = true } else { applyNow() }
    }

    // MARK: - Pause

    func pause(for seconds: TimeInterval) {
        pausedUntil = Date().addingTimeInterval(seconds)
        suspendOutput()
    }

    func pauseUntilSunrise() {
        pausedUntil = Solar.nextSunrise(after: Date(),
                                        latitude: location.latitude,
                                        longitude: location.longitude)
        suspendOutput()
    }

    func resume() {
        pausedUntil = nil
        applyNow()
    }

    // MARK: - Engine

    /// Immediate, non-animated apply (slider drags, toggles).
    func applyNow() { tick(slew: false) }

    private func tick(slew: Bool) {
        if Date().timeIntervalSince(lastLocationRefresh) > 3 * 3600 {
            location.refresh()
            lastLocationRefresh = Date()
        }

        if let until = pausedUntil, Date() >= until { pausedUntil = nil }

        let elevation = Solar.elevation(date: Date(),
                                        latitude: location.latitude,
                                        longitude: location.longitude)
        let newPhase: DayPhase = elevation > 6 ? .day : (elevation < -6 ? .night : .twilight)
        if phase != newPhase { phase = newPhase }

        guard enabled, pausedUntil == nil else {
            suspendOutput()
            return
        }
        outputSuspended = false

        let blend = Self.solarBlend(elevation)
        if nightBlend != blend { nightBlend = blend }
        let target = nightTemp + (dayTemp - nightTemp) * blend

        if slew {
            // ≤150 K per 5 s tick → sunset-like drift, never a visible jump.
            let delta = target - appliedKelvin
            appliedKelvin += max(-150, min(150, delta))
        } else {
            appliedKelvin = target
        }

        if flickerFree {
            if flickerOnlyOnPower && Power.onBattery {
                if !flickerSuspended {
                    flickerSuspended = true
                    // Hand the backlight back at the same perceived level.
                    if let id = Brightness.builtinDisplay() {
                        Brightness.set(id, Float(Settings.flickerComp))
                    }
                }
            } else {
                if flickerSuspended {
                    flickerSuspended = false
                    engageFlickerFree()
                }
                flickerWatchdog()
            }
        }

        // Keep the auto-brightness mirror honest if it's changed in System
        // Settings while we're running.
        let systemALC = Brightness.autoBrightnessEnabled()
        if systemALC != autoBrightness {
            syncingAutoBrightness = true
            autoBrightness = systemALC
            syncingAutoBrightness = false
        }

        lastEffectiveDim = effectiveDim()
        gamma.apply(kelvin: appliedKelvin, dim: lastEffectiveDim)
    }

    private var lastEffectiveDim: Double = 1

    /// The day's shape: civil twilight (+6°…−6°) carries the main transition,
    /// and above it a gentle daylight slope keeps the screen drifting with the
    /// sun from sunrise to peak to sunset — always moving, never a plateau.
    /// 1 = full day (sun ≥ ~60°), 0 = full night (sun ≤ −6°).
    static func solarBlend(_ elevation: Double) -> Double {
        func smooth(_ v: Double) -> Double {
            let x = max(0, min(1, v))
            return x * x * (3 - 2 * x)
        }
        if elevation <= 6 {
            return 0.85 * smooth((elevation + 6) / 12)
        }
        return 0.85 + 0.15 * smooth((elevation - 6) / 54)
    }

    /// Night dim fades in with the same solar blend as the temperature —
    /// full daylight is never dimmed.
    private func effectiveDim() -> Double {
        let comp = (flickerFree && !flickerSuspended) ? Settings.flickerComp : 1.0
        let nightDim = dimPercent * (1 - nightBlend)
        return max(0.12, (1 - nightDim / 100) * comp)
    }

    private func suspendOutput() {
        guard !outputSuspended else { return }
        outputSuspended = true
        gamma.restore()
    }

    // MARK: - Flicker-free (PWM-safe) mode

    /// Pin the backlight at 100% (no PWM strobing) and fold the current
    /// hardware brightness into the software brightness once, so perceived
    /// brightness is unchanged at the moment of enabling.
    private func engageFlickerFree() {
        guard let id = Brightness.builtinDisplay(), let hw = Brightness.get(id) else { return }
        // The ambient light sensor would fight the pinned backlight, so turn
        // it off by default and remember to hand it back on disengage.
        if Brightness.autoBrightnessEnabled() {
            Settings.restoreAutoBrightness = true
            Brightness.setAutoBrightness(false)
        }
        if hw < 0.995 {
            flickerBrightness = max(0.2, Double(hw))
            Brightness.set(id, 1.0)
        }
    }

    /// Give brightness back to the hardware at the same perceived level.
    private func disengageFlickerFree() {
        if let id = Brightness.builtinDisplay() {
            Brightness.set(id, Float(Settings.flickerComp))
        }
        Settings.flickerComp = 1.0
        flickerBrightness = 1.0
        if Settings.restoreAutoBrightness {
            Settings.restoreAutoBrightness = false
            Brightness.setAutoBrightness(true)
        }
    }

    /// Keep the backlight pinned at 100% without absorbing the change into
    /// software dim. The old absorb-and-repin approach ratcheted: the ambient
    /// light sensor lowered the backlight, we folded that into software dim,
    /// re-pinned, and the screen only ever got darker — brightness-up keys
    /// are inert at a pinned backlight, so there was no way back. Now sensor
    /// and key changes are simply overridden; the popover slider is the
    /// single source of truth for brightness in this mode.
    private func flickerWatchdog() {
        guard let id = Brightness.builtinDisplay(), let hw = Brightness.get(id), hw < 0.985 else { return }
        Brightness.set(id, 1.0)
    }
}
