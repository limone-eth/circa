#!/bin/zsh
# Build Lumen.app and (with "install") copy it to ~/Applications and launch it.
set -euo pipefail
cd "$(dirname "$0")"

APP=build/Lumen.app
rm -rf build
mkdir -p "$APP/Contents/MacOS"
cp Info.plist "$APP/Contents/Info.plist"

swiftc -O -swift-version 5 \
    Sources/*.swift \
    -o "$APP/Contents/MacOS/Lumen" \
    -framework AppKit \
    -framework SwiftUI \
    -framework CoreLocation \
    -framework CoreGraphics \
    -framework ServiceManagement

codesign --force --sign - "$APP"
echo "Built $APP"

if [[ "${1:-}" == "install" ]]; then
    mkdir -p ~/Applications
    pkill -x Lumen 2>/dev/null || true
    sleep 0.5
    rm -rf ~/Applications/Lumen.app
    ditto "$APP" ~/Applications/Lumen.app
    open ~/Applications/Lumen.app
    echo "Installed and launched ~/Applications/Lumen.app"
fi
