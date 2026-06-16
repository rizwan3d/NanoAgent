#!/usr/bin/env bash
set -euo pipefail

workspace_root="${NANOAGENT_CONTAINER_WORKSPACE:-/workspace}"
container_home="${NANOAGENT_CONTAINER_HOME:-/var/nanoagent-home}"
nuget_packages="${NUGET_PACKAGES:-${NANOAGENT_NUGET_PACKAGES:-$container_home/.nuget/packages}}"
nuget_http_cache="${NUGET_HTTP_CACHE_PATH:-${NANOAGENT_NUGET_HTTP_CACHE:-$container_home/.local/share/NuGet/http-cache}}"

if [[ -d "$workspace_root" ]]; then
  cd "$workspace_root"
fi

mkdir -p "$container_home" "$nuget_packages" "$nuget_http_cache"

export HOME="$container_home"
export DOTNET_CLI_HOME="$container_home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export NUGET_PACKAGES="$nuget_packages"
export NUGET_HTTP_CACHE_PATH="$nuget_http_cache"

exec dbus-run-session -- bash -lc '
  set -euo pipefail
  eval "$(gnome-keyring-daemon --start --components=secrets)"
  export SSH_AUTH_SOCK GNOME_KEYRING_CONTROL
  dotnet restore NanoAgent.CLI/NanoAgent.CLI.csproj \
    -p:RestorePackagesPath="$NUGET_PACKAGES" \
    -p:RestoreFallbackFolders= \
    -p:RestoreAdditionalProjectFallbackFolders=

  exec dotnet run \
    --no-restore \
    --no-launch-profile \
    --project NanoAgent.CLI/NanoAgent.CLI.csproj \
    -- "$@"
' bash "$@"
