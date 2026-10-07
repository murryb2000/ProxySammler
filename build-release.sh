#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "$0")" && pwd)"
dotnet_cmd="${DOTNET_CMD:-dotnet}"
publish_dir="$project_dir/release/ProxySammler"

"$dotnet_cmd" build "$project_dir/ProxySammler.Tests/ProxySammler.Tests.csproj" -c Release --no-restore -m:1 /nr:false
"$dotnet_cmd" "$project_dir/ProxySammler.Tests/bin/Release/net8.0/ProxySammler.Tests.dll"
"$dotnet_cmd" publish "$project_dir/ProxySammler/ProxySammler.csproj" \
  -c Release -r win-x64 --self-contained true --no-restore -m:1 /nr:false \
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "$publish_dir"

cp "$project_dir/README.txt" "$publish_dir/README.txt"
mkdir -p "$project_dir/release"
(cd "$project_dir/release" && zip -9 -r ProxySammler-1.7-Windows-x64-portable.zip ProxySammler)
