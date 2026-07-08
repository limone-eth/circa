#!/bin/zsh
# Build Circa.app and (with "install") copy it to ~/Applications and launch it.
set -euo pipefail
cd "$(dirname "$0")"

APP=build.noindex/Circa.app
rm -rf build.noindex
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp Info.plist "$APP/Contents/Info.plist"
cp Resources/AppIcon.icns "$APP/Contents/Resources/"

swiftc -O -swift-version 5 \
    Sources/*.swift \
    -o "$APP/Contents/MacOS/Circa" \
    -framework AppKit \
    -framework SwiftUI \
    -framework CoreLocation \
    -framework CoreGraphics \
    -framework ServiceManagement \
    -framework IOKit

codesign --force --sign - "$APP"
echo "Built $APP"

if [[ "${1:-}" == "install" ]]; then
    mkdir -p ~/Applications
    pkill -x Circa 2>/dev/null || true
    sleep 0.5
    rm -rf ~/Applications/Circa.app
    ditto "$APP" ~/Applications/Circa.app
    open ~/Applications/Circa.app
    echo "Installed and launched ~/Applications/Circa.app"
fi
