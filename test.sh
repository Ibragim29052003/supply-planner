#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_CLI_HOME="$PWD/.runtime/dotnet-home"
export NUGET_PACKAGES="$PWD/.runtime/packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
exec dotnet test SupplyPlanner.slnx -m:1 -p:UseSharedCompilation=false "$@"
