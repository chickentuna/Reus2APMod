#!/usr/bin/env bash
# Regenerates decomp/Assembly-CSharp from the installed game.
# Usage:  tools/decompile.sh ["path/to/Reus 2_Data/Managed/Assembly-CSharp.dll"]
set -euo pipefail

dll="${1:-E:/SteamLibrary/steamapps/common/Reus2/Reus 2_Data/Managed/Assembly-CSharp.dll}"
out="$(cd "$(dirname "$0")/.." && pwd)/decomp/Assembly-CSharp"

command -v ilspycmd >/dev/null || dotnet tool install -g ilspycmd
rm -rf "$out"
mkdir -p "$out"
ilspycmd -p -o "$out" "$dll"
