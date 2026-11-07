#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
sdk_dir="$project_root/.tools/dotnet"
mkdir -p "$project_root/.tools/cli-home" "$project_root/.tools/nuget" "$project_root/.tools/runtime"
export DOTNET_CLI_HOME="$project_root/.tools/cli-home"
export NUGET_PACKAGES="$project_root/.tools/nuget"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-$project_root/.tools/runtime}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
if [[ ! -x "$sdk_dir/dotnet" ]]; then
  installer="$(mktemp)"
  trap 'rm -f "$installer"' EXIT
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
  bash "$installer" --channel 10.0 --install-dir "$sdk_dir"
fi

"$sdk_dir/dotnet" restore "$project_root/src/ScribeLocate/ScribeLocate.csproj"
echo "Ready. Run: bash run.sh --url https://app.cloudqa.io/home/AutomationPracticeForm"