#!/usr/bin/env bash
# Builds self-contained Kimulator packages (no .NET install needed to run them).
#
#   scripts/package.sh [RID ...]        default: the machine you run it on
#
#   win-x64, win-arm64       dist/Kimulator-<version>-<rid>.zip      Kimulator.exe (single file)
#   linux-x64, linux-arm64   dist/Kimulator-<version>-<rid>.tar.gz   Kimulator (single file), icon, .desktop, install.sh
#   osx-arm64, osx-x64       dist/Kimulator-<version>-<rid>.zip      Kimulator.app
#                            dist/Kimulator-<version>-<rid>.dmg      (only when built on macOS, which also signs ad hoc)
#
# The version comes from <Version> in the project, or from KIMULATOR_VERSION when it is set. The release
# workflow sets it from the tag, so tag v1.2.0 builds 1.2.0 (a leading "v" is dropped).
#
# Needs the .NET 10 SDK and python3 (for archives that keep execute permissions on any host).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/src/Kimulator.App/Kimulator.App.csproj"
version="${KIMULATOR_VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -1)}"
version="${version#v}"
dist="$root/dist"
work="$root/artifacts/package"
python="$(command -v python3 || command -v python)"

default_rid() {
    local arch
    case "$(uname -m)" in arm64|aarch64) arch=arm64 ;; *) arch=x64 ;; esac
    case "$(uname -s)" in
        Darwin) echo "osx-$arch" ;;
        Linux) echo "linux-$arch" ;;
        *) echo "win-$arch" ;;
    esac
}

publish() { # rid out single-file
    dotnet publish "$project" -c Release -r "$1" --self-contained true -p:Version="$version" \
        -p:PublishSingleFile="$3" -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile="$3" \
        -p:DebugType=none -p:DebugSymbols=false -o "$2" >/dev/null
    find "$2" -name '*.pdb' -delete # native packages ship debug symbols we don't need
}

package_windows() {
    local rid=$1 stage="$work/$1/Kimulator-$version"
    publish "$rid" "$stage" true
    cp "$root/README.md" "$stage/"
    "$python" "$root/scripts/archive.py" zip "$dist/Kimulator-$version-$rid.zip" "$stage"
}

package_linux() {
    local rid=$1 stage="$work/$1/Kimulator-$version"
    publish "$rid" "$stage" true
    cp "$root/packaging/linux/kimulator.png" "$root/packaging/linux/kimulator.desktop" "$root/packaging/linux/install.sh" "$root/README.md" "$stage/"
    "$python" "$root/scripts/archive.py" tar "$dist/Kimulator-$version-$rid.tar.gz" "$stage" \
        --exec "Kimulator-$version/Kimulator" "Kimulator-$version/install.sh"
}

package_macos() {
    local rid=$1 stage="$work/$1"
    local app="$stage/Kimulator.app"
    # A normal (multi-file) publish inside the bundle: nothing to extract at start-up, and it signs cleanly.
    publish "$rid" "$app/Contents/MacOS" false
    mkdir -p "$app/Contents/Resources"
    cp "$root/packaging/macos/Kimulator.icns" "$app/Contents/Resources/"
    sed "s/__VERSION__/$version/g" "$root/packaging/macos/Info.plist" > "$app/Contents/Info.plist"

    if [[ "$(uname -s)" == Darwin ]]; then
        chmod +x "$app/Contents/MacOS/Kimulator"
        codesign --force --deep --sign - "$app"
        (cd "$stage" && ditto -c -k --keepParent Kimulator.app "$dist/Kimulator-$version-$rid.zip")
        hdiutil create -volname "Kimulator" -srcfolder "$app" -ov -format UDZO "$dist/Kimulator-$version-$rid.dmg" >/dev/null
    else
        echo "  (not on macOS: the .app is unsigned; Apple Silicon Macs need it signed, e.g. 'codesign --force --deep -s - Kimulator.app')"
        "$python" "$root/scripts/archive.py" zip "$dist/Kimulator-$version-$rid.zip" "$app" \
            --exec "Kimulator.app/Contents/MacOS/Kimulator"
    fi
}

rids=("$@")
[[ ${#rids[@]} -eq 0 ]] && rids=("$(default_rid)")
mkdir -p "$dist"
for rid in "${rids[@]}"; do
    echo "Packaging Kimulator $version for $rid"
    rm -rf "${work:?}/$rid"
    case "$rid" in
        win-*) package_windows "$rid" ;;
        linux-*) package_linux "$rid" ;;
        osx-*) package_macos "$rid" ;;
        *) echo "Unknown runtime '$rid'"; exit 1 ;;
    esac
done
ls -l "$dist"
