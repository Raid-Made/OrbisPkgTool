#!/usr/bin/env sh
# Installs OrbisPkgTool from an extracted release folder.
#
#   ./install.sh               install for the current user (~/.local)
#   sudo ./install.sh --system install for all users (/usr/local)
#   ./install.sh --uninstall   remove a user install (add --system for a system one)
#
# Adds two commands, `orbispkgtool` (CLI) and `orbispkgtool-gui` (GUI),
# plus an application-menu entry for the GUI.
set -eu

prefix="${HOME}/.local"
action=install
for arg in "$@"; do
    case "$arg" in
        --system) prefix=/usr/local ;;
        --uninstall) action=uninstall ;;
        -h|--help) sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "unknown option: $arg" >&2; exit 2 ;;
    esac
done

libdir="$prefix/lib/orbispkgtool"
bindir="$prefix/bin"
appdir="$prefix/share/applications"
icondir="$prefix/share/icons/hicolor/scalable/apps"

if [ "$action" = uninstall ]; then
    rm -rf "$libdir"
    rm -f "$bindir/orbispkgtool" "$bindir/orbispkgtool-gui" \
          "$appdir/orbispkgtool.desktop" "$icondir/orbispkgtool.svg"
    echo "OrbisPkgTool removed from $prefix"
    exit 0
fi

src="$(cd "$(dirname "$0")" && pwd)"
if [ ! -x "$src/OrbisPkgTool" ]; then
    echo "Run this script from the extracted release folder (OrbisPkgTool not found next to it)." >&2
    exit 1
fi

# Replace any previous install so stale files from older versions don't linger.
rm -rf "$libdir"
mkdir -p "$libdir" "$bindir" "$appdir" "$icondir"
cp -R "$src"/. "$libdir"/
rm -f "$libdir/install.sh"
chmod +x "$libdir/OrbisPkgTool" "$libdir/OrbisPkgTool.Gui.Avalonia"

ln -sf "$libdir/OrbisPkgTool" "$bindir/orbispkgtool"
ln -sf "$libdir/OrbisPkgTool.Gui.Avalonia" "$bindir/orbispkgtool-gui"
cp "$src/orbispkgtool.svg" "$icondir/orbispkgtool.svg"

cat > "$appdir/orbispkgtool.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=OrbisPkgTool
Comment=Build, inspect, extract and check PS4 .pkg files
Exec=$libdir/OrbisPkgTool.Gui.Avalonia
Icon=orbispkgtool
Terminal=false
Categories=Utility;Development;
EOF

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$appdir" >/dev/null 2>&1 || true

echo "OrbisPkgTool installed to $libdir"
echo "  CLI: orbispkgtool --help"
echo "  GUI: orbispkgtool-gui  (or 'OrbisPkgTool' in your application menu)"
case ":$PATH:" in
    *":$bindir:"*) ;;
    *) echo "Note: $bindir is not on your PATH. Add it, e.g.: echo 'export PATH=\"$bindir:\$PATH\"' >> ~/.bashrc" ;;
esac
