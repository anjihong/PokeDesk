#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
  echo "Usage: bash scripts/package-macos.sh <publish-directory> <output-directory> [version]" >&2
  exit 2
fi
if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "Run this script on macOS to validate, ad-hoc sign, and archive the app bundle." >&2
  exit 1
fi

package_version="${3:-1.0.0}"
if [[ ! "$package_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must have the form 1.2.3." >&2
  exit 2
fi
script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
publish_directory="$(cd -- "$1" && pwd)"
if [[ ! -f "$publish_directory/DeskPokemon" || ! -f "$publish_directory/DeskPokemon.runtimeconfig.json" ]]; then
  echo "Missing macOS publish output. Run dotnet publish with -r osx-arm64 or -r osx-x64 first." >&2
  exit 1
fi
if ! /usr/bin/file "$publish_directory/DeskPokemon" | /usr/bin/grep -q 'Mach-O'; then
  echo "The published DeskPokemon executable is not a macOS Mach-O binary." >&2
  exit 1
fi
mkdir -p -- "$2"
output_directory="$(cd -- "$2" && pwd)"
if [[ "$output_directory" == "$publish_directory" || "$output_directory" == "$publish_directory/"* ]]; then
  echo "Output directory must be outside the publish directory." >&2
  exit 2
fi
app_directory="$output_directory/DeskPokemon.app"
archive_path="$output_directory/DeskPokemon.zip"
if [[ -e "$app_directory" || -e "$archive_path" ]]; then
  echo "Output already exists. Choose a fresh output directory to avoid packaging stale files." >&2
  exit 1
fi

mkdir -p "$app_directory/Contents/MacOS" "$app_directory/Contents/Resources"
cp -R "$publish_directory/." "$app_directory/Contents/MacOS/"
sed "s/@APP_VERSION@/$package_version/g" "$script_directory/macos/Info.plist" > "$app_directory/Contents/Info.plist"
cp "$script_directory/macos/README.md" "$app_directory/Contents/Resources/README.md"
chmod +x "$app_directory/Contents/MacOS/DeskPokemon"
/usr/bin/plutil -lint "$app_directory/Contents/Info.plist"

# Local/CI builds use an ad-hoc signature. Public distribution still needs
# a Developer ID signature and notarization; no signing credentials are used here.
/usr/bin/codesign --force --deep --sign - "$app_directory"
/usr/bin/codesign --verify --deep --strict "$app_directory"
# Archive before artifact upload so the executable bit and bundle layout survive.
/usr/bin/ditto -c -k --sequesterRsrc --keepParent "$app_directory" "$archive_path"
echo "Created $app_directory"
echo "Created $archive_path"
