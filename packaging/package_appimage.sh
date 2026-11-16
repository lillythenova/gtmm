#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
EXECUTABLE="${1:-${ROOT_DIR}/dist/linux-x64/gtmm}"
OUTPUT="${2:-${ROOT_DIR}/dist/gtmm-linux-x86_64.AppImage}"
APPIMAGETOOL="${APPIMAGETOOL:-appimagetool}"

if [[ ! -f "$EXECUTABLE" ]]; then
    echo "Linux x64 executable not found: $EXECUTABLE" >&2
    exit 1
fi

if ! command -v "$APPIMAGETOOL" >/dev/null 2>&1 && [[ ! -x "$APPIMAGETOOL" ]]; then
    echo "appimagetool is required; set APPIMAGETOOL to its executable path." >&2
    exit 1
fi

APPDIR="$(mktemp -d)"
trap 'rm -rf "$APPDIR"' EXIT

mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" \
    "$APPDIR/usr/share/icons/hicolor/16x16/apps"
install -m 755 "$EXECUTABLE" "$APPDIR/usr/bin/gtmm"
install -m 644 "$SCRIPT_DIR/gtmm.desktop" "$APPDIR/usr/share/applications/gtmm.desktop"
install -m 644 "$SCRIPT_DIR/gtmm.desktop" "$APPDIR/gtmm.desktop"
install -m 644 "$SCRIPT_DIR/application-x-executable.xpm" "$APPDIR/application-x-executable.xpm"
install -m 644 "$SCRIPT_DIR/application-x-executable.xpm" \
    "$APPDIR/usr/share/icons/hicolor/16x16/apps/application-x-executable.xpm"

cat > "$APPDIR/AppRun" <<'EOF'
#!/usr/bin/env sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/gtmm" "$@"
EOF
chmod 755 "$APPDIR/AppRun"
chmod -R a+rX "$APPDIR"

mkdir -p "$(dirname "$OUTPUT")"
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" "$APPDIR" "$OUTPUT"
chmod 755 "$OUTPUT"
echo "Created $OUTPUT"
