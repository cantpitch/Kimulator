#!/usr/bin/env sh
# Installs Kimulator for the current user: the program in ~/.local/lib/kimulator (with the OpenAL
# library it needs beside it), a "Kimulator" command in ~/.local/bin, and a menu entry with its icon.
set -e
here="$(cd "$(dirname "$0")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
lib="$HOME/.local/lib/kimulator"
bin="$HOME/.local/bin"
apps="$data/applications"
icons="$data/icons/hicolor/256x256/apps"

mkdir -p "$lib" "$bin" "$apps" "$icons"
install -m 755 "$here/Kimulator" "$lib/Kimulator"
install -m 644 "$here/libopenal.so" "$lib/libopenal.so"
ln -sf "$lib/Kimulator" "$bin/Kimulator"
install -m 644 "$here/kimulator.png" "$icons/kimulator.png"
sed "s|^Exec=.*|Exec=$lib/Kimulator|" "$here/kimulator.desktop" > "$apps/kimulator.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$apps" || true
echo "Installed Kimulator in $lib, added the 'Kimulator' command to $bin and a menu entry."
