#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "$0")" && pwd)"
dotnet_cmd="${DOTNET_CMD:-dotnet}"
publish_dir="$project_dir/release/ProxySammler"

"$dotnet_cmd" restore "$project_dir/ProxySammler.Tests/ProxySammler.Tests.csproj"
"$dotnet_cmd" run --project "$project_dir/ProxySammler.Tests/ProxySammler.Tests.csproj" -c Release --no-restore
"$dotnet_cmd" restore "$project_dir/ProxySammler/ProxySammler.csproj" -r win-x64
"$dotnet_cmd" publish "$project_dir/ProxySammler/ProxySammler.csproj" \
  -c Release -r win-x64 --self-contained true --no-restore -m:1 /nr:false \
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "$publish_dir"

mv "$publish_dir/ProxySammler.exe" "$publish_dir/ProxySammler-1.8.0-Windows-x64.exe"
cp "$project_dir/README.txt" "$publish_dir/README.txt"
cp "$project_dir/ProxySammler/sources.txt" "$publish_dir/sources.txt"
mkdir -p "$project_dir/release"
(cd "$publish_dir" && zip -9 "$project_dir/release/ProxySammler-1.8.0-Windows-x64-portable.zip" ProxySammler-1.8.0-Windows-x64.exe README.txt sources.txt)
