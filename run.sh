#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export DOTNET_CLI_HOME="$project_root/.tools/cli-home"
export NUGET_PACKAGES="$project_root/.tools/nuget"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-$project_root/.tools/runtime}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
exec "$project_root/.tools/dotnet/dotnet" run --project "$project_root/src/ScribeLocate/ScribeLocate.csproj" -- "$@"