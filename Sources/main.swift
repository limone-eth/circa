import AppKit
import Combine
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private let popover = NSPopover()
    private var cancellables = Set<AnyCancellable>()
    private let model = CircaModel.shared

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = statusItem.button {
            button.image = Self.icon(for: model.phase, active: model.isActive)
            button.target = self
            button.action = #selector(statusItemClicked)
            button.sendAction(on: [.leftMouseUp, .rightMouseUp])
        }

        popover.behavior = .transient
        popover.animates = true
        let host = NSHostingController(rootView: ContentView().environmentObject(model))
        // Let SwiftUI's ideal size drive the popover, otherwise NSPopover
        // keeps a stale default height and clips the content.
        host.sizingOptions = [.preferredContentSize]
        popover.contentViewController = host
        popover.contentSize = host.view.fittingSize

        // Keep the menu bar icon in sync with phase and on/off state.
        model.objectWillChange
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in
                guard let self else { return }
                statusItem.button?.image = Self.icon(for: model.phase, active: model.isActive)
            }
            .store(in: &cancellables)

        model.start()
    }

    func applicationWillTerminate(_ notification: Notification) {
        model.shutdown()
    }

    @objc private func statusItemClicked() {
        guard let event = NSApp.currentEvent else { return togglePopover() }
        if event.type == .rightMouseUp {
            let menu = NSMenu()
            menu.addItem(withTitle: "Quit Circa", action: #selector(quit), keyEquivalent: "q")
            menu.items.forEach { $0.target = self }
            statusItem.menu = menu
            statusItem.button?.performClick(nil)
            statusItem.menu = nil
        } else {
            togglePopover()
        }
    }

    private func togglePopover() {
        guard let button = statusItem.button else { return }
        if popover.isShown {
            popover.performClose(nil)
        } else {
            popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
            popover.contentViewController?.view.window?.makeKey()
            NSApp.activate(ignoringOtherApps: true)
        }
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }

    private static func icon(for phase: DayPhase, active: Bool) -> NSImage? {
        let name: String
        if !active {
            name = "circle.lefthalf.filled"
        } else {
            switch phase {
            case .day: name = "sun.max"
            case .twilight: name = "sun.haze"
            case .night: name = "moon.stars"
            }
        }
        let image = NSImage(systemSymbolName: name, accessibilityDescription: "Circa")
        image?.isTemplate = true
        return image
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
