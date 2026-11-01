#!/bin/zsh
set -euo pipefail

project_dir="$(cd "$(dirname "$0")" && pwd)"
if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    dotnet_path="$HOME/.dotnet/dotnet"
else
    dotnet_path="$(command -v dotnet)"
fi

publish_dir="$project_dir/bin/local-publish"
bundle_dir="$project_dir/bin/RepoG33k.app"
staging_dir="$project_dir/bin/RepoG33k-staging-$$.app"
trap 'rm -rf "$staging_dir"' EXIT
rm -rf "$publish_dir"
"$dotnet_path" publish "$project_dir/RepoG33k.csproj" --configuration Release --self-contained false --output "$publish_dir" --nologo
mkdir -p "$staging_dir/Contents/MacOS" "$staging_dir/Contents/Resources"
cp -R "$publish_dir/." "$staging_dir/Contents/MacOS/"
cp "$project_dir/../Assets/repog33k.icns" "$staging_dir/Contents/Resources/app.icns"
cat > "$staging_dir/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>RepoG33k-launcher</string>
<key>CFBundleIdentifier</key><string>com.deanthecoder.repog33k.local</string>
<key>CFBundleName</key><string>RepoG33k</string>
<key>CFBundleDisplayName</key><string>RepoG33k</string>
<key>CFBundleIconFile</key><string>app.icns</string>
<key>CFBundleVersion</key><string>0.1.0</string>
<key>CFBundleShortVersionString</key><string>0.1.0</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSDocumentsFolderUsageDescription</key><string>RepoG33k opens the Git repositories you select so you can review and edit your changes.</string>
</dict></plist>
PLIST
cat > "$staging_dir/Contents/MacOS/RepoG33k-launcher" <<'LAUNCHER'
#!/bin/zsh
set -euo pipefail
binary_dir="$(cd "$(dirname "$0")" && pwd)"
if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
cd "$HOME"
exec "$binary_dir/RepoG33k" "$@"
LAUNCHER
chmod +x "$staging_dir/Contents/MacOS/RepoG33k-launcher"
# Replace the bundle instead of overwriting binaries mapped by a running app.
previous_dir="$project_dir/bin/RepoG33k.previous.app"
rm -rf "$previous_dir"
if [[ -d "$bundle_dir" ]]; then
    mv "$bundle_dir" "$previous_dir"
fi
mv "$staging_dir" "$bundle_dir"
echo "Built $bundle_dir"
