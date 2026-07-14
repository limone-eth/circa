import AppKit

/// Self-update from GitHub releases. The latest version comes from the
/// /releases/latest redirect (no API calls, no rate limits). When a newer
/// release exists the popover shows a banner (click to update), or — with
/// "Update automatically" on — the app replaces its own bundle and relaunches
/// quietly. Local builds (version "dev") never self-update.
@MainActor
final class Updater: NSObject, ObservableObject {
    static let shared = Updater()

    /// Latest release tag ("v1.0.3") once a newer one is known.
    @Published private(set) var availableTag: String?
    @Published private(set) var updating = false
    @Published private(set) var updateFailed = false

    private static let repo = "https://github.com/limone-eth/circa"
    private var timer: Timer?

    func start() {
        guard Self.currentVersion() != nil else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 10) { self.check() }
        timer = Timer.scheduledTimer(withTimeInterval: 6 * 3600, repeats: true) { _ in
            Task { @MainActor in Updater.shared.check() }
        }
    }

    func applyAvailableUpdate() {
        guard let tag = availableTag, !updating else { return }
        Task { await downloadAndApply(tag: tag) }
    }

    // ------------------------------------------------------------- check

    private func check() {
        Task {
            guard let current = Self.currentVersion(),
                  let tag = await Self.latestTag(), tag.hasPrefix("v"),
                  let latest = Self.parse(String(tag.dropFirst())),
                  Self.isNewer(latest, than: current),
                  tag != Settings.skippedUpdateTag
            else { return }
            if Settings.autoUpdate {
                await downloadAndApply(tag: tag)
            } else {
                availableTag = tag
            }
        }
    }

    private static func currentVersion() -> [Int]? {
        parse(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "")
    }

    /// "1.0.3" → [1, 0, 3]. Nil for anything non-numeric (the "dev" version of
    /// local builds) and for 0.0.0, so those builds never try to update.
    private static func parse(_ s: String) -> [Int]? {
        let parts = s.split(separator: ".").map { Int($0) }
        guard !parts.isEmpty, !parts.contains(nil) else { return nil }
        var v = parts.compactMap { $0 }
        while v.count < 3 { v.append(0) }
        guard v.contains(where: { $0 > 0 }) else { return nil }
        return v
    }

    private static func isNewer(_ a: [Int], than b: [Int]) -> Bool {
        for (x, y) in zip(a, b) where x != y { return x > y }
        return a.count > b.count
    }

    /// GitHub redirects /releases/latest to /releases/tag/<tag>; the Location
    /// header names the version without touching the rate-limited API.
    private static func latestTag() async -> String? {
        guard let url = URL(string: "\(repo)/releases/latest") else { return nil }
        let session = URLSession(configuration: .ephemeral, delegate: NoRedirect(), delegateQueue: nil)
        defer { session.finishTasksAndInvalidate() }
        guard let (_, response) = try? await session.data(from: url),
              let http = response as? HTTPURLResponse,
              let location = http.value(forHTTPHeaderField: "Location"),
              let tag = location.split(separator: "/").last
        else { return nil }
        return String(tag)
    }

    private final class NoRedirect: NSObject, URLSessionTaskDelegate {
        func urlSession(_ session: URLSession, task: URLSessionTask,
                        willPerformHTTPRedirection response: HTTPURLResponse,
                        newRequest request: URLRequest) async -> URLRequest? { nil }
    }

    // ------------------------------------------------------------- apply

    private struct UpdateFailure: Error {}

    private func downloadAndApply(tag: String) async {
        updating = true
        updateFailed = false
        defer { updating = false }
        do {
            guard let current = Self.currentVersion(),
                  let url = URL(string: "\(Self.repo)/releases/download/\(tag)/Circa.zip")
            else { throw UpdateFailure() }

            let fm = FileManager.default
            let work = fm.temporaryDirectory.appendingPathComponent("circa-update-\(tag)")
            try? fm.removeItem(at: work)
            try fm.createDirectory(at: work, withIntermediateDirectories: true)

            let (downloaded, response) = try await URLSession.shared.download(from: url)
            guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw UpdateFailure() }
            let zip = work.appendingPathComponent("Circa.zip")
            try fm.moveItem(at: downloaded, to: zip)
            try Self.run("/usr/bin/ditto", "-xk", zip.path, work.path)

            let newApp = work.appendingPathComponent("Circa.app")

            // A stale asset must not cause an update loop: only swap when the
            // downloaded bundle really is newer than the running one.
            guard let plist = NSDictionary(contentsOf: newApp.appendingPathComponent("Contents/Info.plist")),
                  let newVersion = Self.parse(plist["CFBundleShortVersionString"] as? String ?? ""),
                  Self.isNewer(newVersion, than: current)
            else {
                Settings.skippedUpdateTag = tag
                availableTag = nil
                return
            }

            // We wrote these files ourselves, but ditto can preserve a
            // quarantine xattr from the zip; Gatekeeper must not block relaunch.
            _ = try? Self.run("/usr/bin/xattr", "-dr", "com.apple.quarantine", newApp.path)

            let dest = Bundle.main.bundleURL
            let backup = work.appendingPathComponent("Circa-previous.app")
            try fm.moveItem(at: dest, to: backup)
            do {
                try fm.moveItem(at: newApp, to: dest)
            } catch {
                try? fm.moveItem(at: backup, to: dest)
                throw error
            }

            let config = NSWorkspace.OpenConfiguration()
            config.createsNewApplicationInstance = true
            _ = try? await NSWorkspace.shared.openApplication(at: dest, configuration: config)
            NSApp.terminate(nil)
        } catch {
            NSLog("Circa: update to \(tag) failed: \(error)")
            updateFailed = true
        }
    }

    private static func run(_ tool: String, _ args: String...) throws {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: tool)
        p.arguments = args
        try p.run()
        p.waitUntilExit()
        guard p.terminationStatus == 0 else { throw UpdateFailure() }
    }
}
