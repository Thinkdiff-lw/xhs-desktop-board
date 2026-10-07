#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=1.0.0
BUILD_DIR="dist/macos-$(date +%Y%m%d-%H%M%S)-$(uuidgen)"
APP="$BUILD_DIR/XhsBoard.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$BUILD_DIR/AppIcon.iconset"
cp macos/Info.plist "$APP/Contents/Info.plist"
SDK_PATH=$(xcrun --sdk macosx --show-sdk-path)
for ARCH in arm64 x86_64; do
  xcrun swiftc -swift-version 5 -O -sdk "$SDK_PATH" -target "$ARCH-apple-macos13.0" \
    -framework Cocoa -framework WebKit -framework ServiceManagement \
    macos/Core.swift macos/Collector.swift macos/Board.swift macos/main.swift \
    -o "$BUILD_DIR/XhsBoard-$ARCH"
done
lipo -create "$BUILD_DIR/XhsBoard-arm64" "$BUILD_DIR/XhsBoard-x86_64" -output "$APP/Contents/MacOS/XhsBoard"
lipo "$APP/Contents/MacOS/XhsBoard" -verify_arch arm64 x86_64
for SIZE in 16 32 128 256 512; do
  sips -z "$SIZE" "$SIZE" macos/Assets/icon.png --out "$BUILD_DIR/AppIcon.iconset/icon_${SIZE}x${SIZE}.png" >/dev/null
  DOUBLE=$((SIZE * 2))
  sips -z "$DOUBLE" "$DOUBLE" macos/Assets/icon.png --out "$BUILD_DIR/AppIcon.iconset/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$BUILD_DIR/AppIcon.iconset" -o "$APP/Contents/Resources/AppIcon.icns"
plutil -lint "$APP/Contents/Info.plist"
codesign --force --sign - "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
"$APP/Contents/MacOS/XhsBoard" --self-test
cp docs/安装与使用.md "$BUILD_DIR/安装与使用.md"
cp docs/macOS.md "$BUILD_DIR/macOS说明.md"
cp docs/隐私说明.md "$BUILD_DIR/隐私说明.md"
cp LICENSE "$BUILD_DIR/LICENSE.txt"
PACKAGE="$BUILD_DIR/XhsBoard-macOS-Universal-v$VERSION-Beta.zip"
# An explicit bundle list prevents accidental inclusion of WebKit or account data.
ditto -c -k --sequesterRsrc --keepParent "$APP" "$BUILD_DIR/app.zip"
python3 tools/package_macos.py "$BUILD_DIR" "$PACKAGE"
(cd "$BUILD_DIR"; shasum -a 256 "$(basename "$PACKAGE")") > "$PACKAGE.sha256"
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  printf 'package=%s\n' "$PACKAGE" >> "$GITHUB_OUTPUT"
fi
printf 'Built %s\n' "$PACKAGE"
