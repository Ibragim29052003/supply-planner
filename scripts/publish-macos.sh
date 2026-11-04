#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "Сборка .app выполняется на macOS." >&2
  exit 2
fi
rid="${1:-osx-arm64}"
case "$rid" in osx-arm64|osx-x64) ;; *) echo "Архитектура: osx-arm64 или osx-x64." >&2; exit 2 ;; esac
export DOTNET_CLI_HOME="$PWD/.runtime/dotnet-home"
export NUGET_PACKAGES="$PWD/.runtime/packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
version="1.0.1"
output="$PWD/output/release/$rid"
mkdir -p "$output"
# Собираем в отдельной папке: сбой publish не затронет предыдущий архив релиза.
staging=$(mktemp -d "$output/.build.XXXXXX")
trap 'rm -rf "$staging"' EXIT
app="$staging/SupplyPlanner.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish src/SupplyPlanner.Desktop -c Release -r "$rid" --self-contained true \
  -p:UseAppHost=true -p:PublishTrimmed=false -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:Version="$version" \
  -p:UseSharedCompilation=false -o "$app/Contents/MacOS"
cp packaging/macos/Info.plist "$app/Contents/Info.plist"
# Отладочные символы являются ресурсами, а не исполняемым кодом macOS.
find "$app/Contents/MacOS" -name '*.pdb' -exec mv {} "$app/Contents/Resources/" \;
# Ad-hoc подпись годится для локальной демонстрации; это не Apple notarization.
find "$app/Contents/MacOS" -type f \( -name '*.dylib' -o -name '*.so' \) -exec codesign --force --sign - {} \;
codesign --force --sign - "$app/Contents/MacOS/SupplyPlanner.Desktop"
codesign --force --sign - "$app"
codesign --verify --deep --strict "$app"
mkdir -p "$staging/SupplyPlanner-demo/examples"
cp -R "$app" "$staging/SupplyPlanner-demo/"
cp examples/*.json "$staging/SupplyPlanner-demo/examples/"
cp docs/QUICKSTART.md "$staging/SupplyPlanner-demo/READ-ME.md"
cp docs/DATA_FORMAT.md "$staging/SupplyPlanner-demo/DATA_FORMAT.md"
# В архиве инструкция лежит рядом с examples, а в исходниках — в docs.
sed 's#(\.\./examples/#(examples/#g' docs/SELLERSTATUS_AND_SETUP.md > "$staging/SupplyPlanner-demo/SELLERSTATUS_AND_SETUP.md"
archive="SupplyPlanner-$version-$rid.zip"
ditto -c -k --keepParent "$staging/SupplyPlanner-demo" "$staging/$archive"
# Архив содержит только приложение и вымышленные примеры, без локальных состояний.
mv -f "$staging/$archive" "$output/$archive"
(cd "$output" && shasum -a 256 "$archive" > "$archive.sha256")
echo "Готово: $output/$archive"
