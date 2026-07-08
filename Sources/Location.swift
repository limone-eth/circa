import CoreLocation
import Foundation

/// Location with graceful degradation: CoreLocation → IP geolocation →
/// cached last fix → timezone-based estimate. Sunrise/sunset only needs
/// city-level accuracy, so anything in that chain is good enough.
final class LocationProvider: NSObject, CLLocationManagerDelegate {
    private let manager = CLLocationManager()
    private let geocoder = CLGeocoder()

    /// Called on the main thread whenever coordinates or the place name change.
    var onUpdate: (() -> Void)?

    private(set) var latitude: Double
    private(set) var longitude: Double
    private(set) var placeName: String
    private(set) var sourceName: String
    private var usingIPFallback = false

    override init() {
        if let lat = Settings.cachedLatitude, let lon = Settings.cachedLongitude {
            latitude = lat
            longitude = lon
            placeName = Settings.cachedPlace ?? String(format: "%.1f°, %.1f°", lat, lon)
            sourceName = "last known"
        } else {
            // Longitude from the UTC offset puts solar noon near clock noon —
            // a usable stand-in until a real fix arrives.
            latitude = 40
            longitude = Double(TimeZone.current.secondsFromGMT()) / 3600.0 * 15.0
            placeName = "Estimating from clock…"
            sourceName = "time zone"
        }
        super.init()
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyReduced
    }

    func start() {
        switch manager.authorizationStatus {
        case .notDetermined:
            manager.requestWhenInUseAuthorization()
        case .authorizedAlways, .authorizedWhenInUse:
            manager.requestLocation()
        default:
            fetchFromIP()
        }
    }

    func refresh() {
        switch manager.authorizationStatus {
        case .authorizedAlways, .authorizedWhenInUse:
            manager.requestLocation()
        default:
            if usingIPFallback { fetchFromIP() }
        }
    }

    // MARK: - CLLocationManagerDelegate

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        switch manager.authorizationStatus {
        case .authorizedAlways, .authorizedWhenInUse:
            usingIPFallback = false
            manager.requestLocation()
        case .denied, .restricted:
            fetchFromIP()
        default:
            break
        }
    }

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let loc = locations.last else { return }
        store(latitude: loc.coordinate.latitude, longitude: loc.coordinate.longitude,
              source: "Location Services")
        geocoder.reverseGeocodeLocation(loc) { [weak self] placemarks, _ in
            guard let self, let city = placemarks?.first?.locality ?? placemarks?.first?.name else { return }
            DispatchQueue.main.async {
                self.placeName = city
                Settings.cachedPlace = city
                self.onUpdate?()
            }
        }
    }

    func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        fetchFromIP()
    }

    // MARK: - Fallbacks

    private func fetchFromIP() {
        usingIPFallback = true
        guard let url = URL(string: "https://ipapi.co/json/") else { return }
        URLSession.shared.dataTask(with: url) { [weak self] data, _, _ in
            guard let self, let data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let lat = json["latitude"] as? Double,
                  let lon = json["longitude"] as? Double else { return }
            let city = json["city"] as? String
            DispatchQueue.main.async {
                self.store(latitude: lat, longitude: lon, source: "IP address")
                if let city {
                    self.placeName = city
                    Settings.cachedPlace = city
                    self.onUpdate?()
                }
            }
        }.resume()
    }

    private func store(latitude lat: Double, longitude lon: Double, source: String) {
        let apply = { [self] in
            latitude = lat
            longitude = lon
            sourceName = source
            Settings.cachedLatitude = lat
            Settings.cachedLongitude = lon
            if Settings.cachedPlace == nil {
                placeName = String(format: "%.1f°, %.1f°", lat, lon)
            }
            onUpdate?()
        }
        if Thread.isMainThread { apply() } else { DispatchQueue.main.async(execute: apply) }
    }
}
