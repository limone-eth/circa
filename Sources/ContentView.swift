import SwiftUI

// MARK: - Root popover content

struct ContentView: View {
    @EnvironmentObject var model: CircaModel

    var body: some View {
        VStack(spacing: 12) {
            header
            slidersCard
            togglesCard
            pauseSection
            footer
        }
        .padding(14)
        .frame(width: 312)
    }

    // MARK: Header

    private var header: some View {
        HStack(spacing: 12) {
            PhaseOrb(phase: model.phase, active: model.isActive)
            VStack(alignment: .leading, spacing: 2) {
                Text(model.phaseTitle)
                    .font(.system(.headline, design: .rounded))
                    .contentTransition(.interpolate)
                Text(model.statusLine)
                    .font(.caption)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
                    .contentTransition(.numericText())
            }
            .animation(.spring(duration: 0.35, bounce: 0), value: model.statusLine)
            Spacer(minLength: 0)
            Toggle("", isOn: $model.enabled)
                .labelsHidden()
                .toggleStyle(.switch)
                .controlSize(.small)
        }
        .padding(.horizontal, 2)
    }

    // MARK: Sliders

    private var slidersCard: some View {
        Card {
            SliderRow(title: "Day", systemImage: "sun.max",
                      value: $model.dayTemp, range: 4800...6500, step: 50,
                      tint: .cyan) { "\(Int($0)) K" }
            SliderRow(title: "Night", systemImage: "moon",
                      value: $model.nightTemp, range: 1900...4500, step: 50,
                      tint: .orange) { "\(Int($0)) K" }
            SliderRow(title: "Night dim", systemImage: "circle.lefthalf.filled",
                      value: $model.dimPercent, range: 0...70, step: 1,
                      tint: Color.indigo) { $0 < 1 ? "Off" : "\(Int($0)) %" }

            if model.isCustomized {
                HStack {
                    Spacer()
                    Button {
                        withAnimation(.spring(duration: 0.3, bounce: 0)) {
                            model.resetToRecommended()
                        }
                    } label: {
                        Label("Reset to ideal", systemImage: "arrow.counterclockwise")
                            .font(.caption2)
                    }
                    .buttonStyle(.plain)
                    .foregroundStyle(.secondary)
                    .help("Back to the recommended day/night curve for this time of day")
                }
                .transition(.opacity)
            }
        }
        .animation(.spring(duration: 0.25, bounce: 0), value: model.isCustomized)
    }

    // MARK: Toggles

    private var togglesCard: some View {
        Card {
            Toggle(isOn: $model.flickerFree) {
                VStack(alignment: .leading, spacing: 1) {
                    Text("Flicker-free dimming")
                    Text(model.flickerFreeAvailable
                         ? "Pins the backlight at 100% and dims in software, so the LED panel never strobes (PWM). Use the slider below for brightness; the keys are overridden."
                         : "Not available for this display.")
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .toggleStyle(.switch)
            .controlSize(.small)
            .disabled(!model.flickerFreeAvailable)

            if model.flickerFree && model.flickerFreeAvailable {
                SliderRow(title: "Brightness", systemImage: "sun.min",
                          value: Binding(
                              get: { model.flickerBrightness * 100 },
                              set: { model.flickerBrightness = $0 / 100 }),
                          range: 20...100, step: 1,
                          tint: Color.yellow) { "\(Int($0)) %" }
                    .transition(.opacity)
            }

            Divider()

            Toggle(isOn: $model.launchAtLogin) {
                Text("Launch at login")
            }
            .toggleStyle(.switch)
            .controlSize(.small)
        }
        .animation(.spring(duration: 0.25, bounce: 0), value: model.flickerFree)
    }

    // MARK: Pause

    @ViewBuilder
    private var pauseSection: some View {
        if let until = model.pausedUntil {
            Card {
                HStack {
                    VStack(alignment: .leading, spacing: 1) {
                        Text("Paused")
                        Text("Resumes \(until, style: .relative)")
                            .font(.caption2)
                            .monospacedDigit()
                            .foregroundStyle(.tertiary)
                    }
                    Spacer()
                    Button("Resume") {
                        withAnimation(.spring(duration: 0.3, bounce: 0)) { model.resume() }
                    }
                    .controlSize(.small)
                }
            }
        } else {
            HStack(spacing: 8) {
                Button {
                    withAnimation(.spring(duration: 0.3, bounce: 0)) { model.pause(for: 3600) }
                } label: {
                    Text("Pause 1 h").frame(maxWidth: .infinity)
                }
                Button {
                    withAnimation(.spring(duration: 0.3, bounce: 0)) { model.pauseUntilSunrise() }
                } label: {
                    Text("Until sunrise").frame(maxWidth: .infinity)
                }
            }
            .controlSize(.small)
            .disabled(!model.enabled)
        }
    }

    // MARK: Footer

    private var footer: some View {
        HStack {
            Image(systemName: "location.fill")
                .font(.system(size: 8))
                .foregroundStyle(.quaternary)
            Text(model.locationLine)
                .font(.caption2)
                .foregroundStyle(.tertiary)
                .lineLimit(1)
            Spacer()
            Button("Quit") { NSApp.terminate(nil) }
                .buttonStyle(.plain)
                .font(.caption2)
                .foregroundStyle(.tertiary)
                .keyboardShortcut("q")
        }
        .padding(.horizontal, 2)
    }
}

// MARK: - Phase orb (the little sky)

struct PhaseOrb: View {
    let phase: DayPhase
    let active: Bool

    var body: some View {
        ZStack {
            Circle()
                .fill(LinearGradient(colors: colors, startPoint: .top, endPoint: .bottom))
            Image(systemName: symbol)
                .font(.system(size: 16, weight: .medium))
                .foregroundStyle(.white)
                .shadow(color: .black.opacity(0.15), radius: 1, y: 1)
                .contentTransition(.symbolEffect(.replace))
        }
        .frame(width: 40, height: 40)
        .glassIfAvailable()
        .shadow(color: glow.opacity(active ? 0.5 : 0.15), radius: 9, y: 2)
        .saturation(active ? 1 : 0.25)
        .animation(.spring(duration: 0.45, bounce: 0), value: phase)
        .animation(.spring(duration: 0.45, bounce: 0), value: active)
    }

    private var symbol: String {
        switch phase {
        case .day: return "sun.max.fill"
        case .twilight: return "sun.horizon.fill"
        case .night: return "moon.stars.fill"
        }
    }

    private var colors: [Color] {
        switch phase {
        case .day: return [Color(red: 0.35, green: 0.68, blue: 0.98), Color(red: 0.55, green: 0.82, blue: 1.0)]
        case .twilight: return [Color(red: 0.93, green: 0.52, blue: 0.32), Color(red: 0.72, green: 0.40, blue: 0.62)]
        case .night: return [Color(red: 0.18, green: 0.22, blue: 0.45), Color(red: 0.10, green: 0.11, blue: 0.25)]
        }
    }

    private var glow: Color {
        switch phase {
        case .day: return .blue
        case .twilight: return .orange
        case .night: return .indigo
        }
    }
}

// MARK: - Building blocks

/// Quiet grouped surface. The popover chrome already supplies the big glass
/// panel on macOS 26; cards stay subtle so the whole thing reads as one calm
/// sheet rather than glass-on-glass noise.
struct Card<Content: View>: View {
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) { content }
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .strokeBorder(.quaternary.opacity(0.6), lineWidth: 0.5)
            )
    }
}

struct SliderRow: View {
    let title: String
    let systemImage: String
    @Binding var value: Double
    let range: ClosedRange<Double>
    let step: Double
    let tint: Color
    let format: (Double) -> String

    var body: some View {
        VStack(spacing: 3) {
            HStack(spacing: 5) {
                Image(systemName: systemImage)
                    .font(.system(size: 9))
                    .foregroundStyle(.tertiary)
                    .frame(width: 12)
                Text(title)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Text(format(value))
                    .font(.caption)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
                    .contentTransition(.numericText())
                    .animation(.spring(duration: 0.25, bounce: 0), value: format(value))
            }
            Slider(value: $value, in: range, step: step)
                .controlSize(.small)
                .tint(tint)
        }
    }
}

// MARK: - Helpers

private extension View {
    /// Liquid Glass on macOS 26+, plain circle elsewhere.
    @ViewBuilder
    func glassIfAvailable() -> some View {
        if #available(macOS 26.0, *) {
            self.glassEffect(.regular, in: .circle)
        } else {
            self
        }
    }
}

// MARK: - Presentation helpers on the model

extension CircaModel {
    var isActive: Bool { enabled && pausedUntil == nil }

    var phaseTitle: String {
        if !enabled { return "Off" }
        if pausedUntil != nil { return "Paused" }
        switch phase {
        case .day: return "Daylight"
        case .twilight: return "Golden hour"
        case .night: return "Night"
        }
    }

    var statusLine: String {
        guard isActive else { return "Screen at system default" }
        var parts = ["\(Int((appliedKelvin / 50).rounded() * 50)) K"]
        let nightDim = Int((dimPercent * (1 - nightBlend)).rounded())
        if nightDim >= 1 { parts.append("dimmed \(nightDim) %") }
        if flickerFree { parts.append("flicker-free") }
        return parts.joined(separator: " · ")
    }

    var locationLine: String {
        placeName.isEmpty ? "Locating…" : "\(placeName) · \(locationSource)"
    }
}
