#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
image_tag="${NANOAGENT_DOCKER_IMAGE:-nanoagent-cli-debug:test}"
force_rebuild="${NANOAGENT_DOCKER_FORCE_REBUILD:-0}"

for env_file in "$script_dir/nanoai-ubuntu-debug.env" "$script_dir/.env"; do
  if [[ -f "$env_file" ]]; then
    # Load local Docker-only provider settings without committing secrets.
    # shellcheck disable=SC1090
    source "$env_file"
  fi
done

: "${NANOAGENT_API_KEY:?Set NANOAGENT_API_KEY before running this script.}"

if [[ "$force_rebuild" == "1" ]]; then
  docker build -f "$repo_root/Dockerfile.ubuntu-cli-debug" -t "$image_tag" "$repo_root"
else
  docker image inspect "$image_tag" > /dev/null 2>&1 || \
    docker build -f "$repo_root/Dockerfile.ubuntu-cli-debug" -t "$image_tag" "$repo_root"
fi

if [[ $# -eq 0 ]]; then
  set -- --interactive
fi

docker_tty_args=()
if [[ -t 0 && -t 1 ]]; then
  docker_tty_args=(-it)
fi

exec docker run --rm \
  "${docker_tty_args[@]}" \
  -e NANOAGENT_PROVIDER="${NANOAGENT_PROVIDER:-openrouter}" \
  -e NANOAGENT_MODEL="${NANOAGENT_MODEL:-poolside/laguna-m.1:free}" \
  -e NANOAGENT_THINKING="${NANOAGENT_THINKING:-on}" \
  -e NANOAGENT_REASONING="${NANOAGENT_REASONING:-high}" \
  -e NANOAGENT_API_KEY \
  -v "$repo_root:/workspace" \
  "$image_tag" \
  "$@"
