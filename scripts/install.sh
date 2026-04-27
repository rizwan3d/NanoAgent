#!/usr/bin/env bash
set -euo pipefail

readonly OWNER="rizwan3d"
readonly REPO="NanoAgent"
readonly APP_NAME="NanoAgent.CLI"
readonly EXECUTABLE_NAME="NanoAgent.CLI"
readonly COMMAND_NAME="nanoai"
readonly DEFAULT_INSTALL_DIR="${HOME}/.local/bin"

log() {
  # Important: logs must go to stderr so command substitution captures only data.
  printf '[%s] %s\n' "$APP_NAME" "$1" >&2
}

fail() {
  printf '[%s] Error: %s\n' "$APP_NAME" "$1" >&2
  exit 1
}

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    fail "Required command '$1' is not available."
  fi
}

download_to_file() {
  local url="$1"
  local destination="$2"

  if command -v curl >/dev/null 2>&1; then
    curl -fL \
      -H "User-Agent: ${APP_NAME}-installer" \
      --retry 3 \
      --retry-delay 2 \
      --connect-timeout 15 \
      -o "$destination" \
      "$url"
    return
  fi

  if command -v wget >/dev/null 2>&1; then
    wget \
      --header="User-Agent: ${APP_NAME}-installer" \
      -O "$destination" \
      "$url"
    return
  fi

  fail "Neither curl nor wget is available. Install one of them and try again."
}

resolve_latest_tag() {
  local api_url="https://api.github.com/repos/${OWNER}/${REPO}/releases/latest"
  local metadata
  local tag

  log "Resolving the latest release tag..."
  metadata="$(mktemp)"

  if ! download_to_file "$api_url" "$metadata"; then
    rm -f "$metadata"
    fail "Unable to determine the latest release tag from GitHub. Set NANOAGENT_TAG and try again."
  fi

  tag="$(sed -n 's/.*"tag_name"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$metadata" | head -n 1)"
  rm -f "$metadata"

  if [[ -z "$tag" ]]; then
    fail "Unable to determine the latest release tag from GitHub. Set NANOAGENT_TAG and try again."
  fi

  printf '%s\n' "$tag"
}

detect_platform() {
  local os
  local arch

  os="$(uname -s)"
  arch="$(uname -m)"

  case "$os" in
    Linux)
      case "$arch" in
        x86_64|amd64)
          printf 'linux-x64\n'
          ;;
        aarch64|arm64)
          printf 'linux-arm64\n'
          ;;
        *)
          fail "Unsupported Linux architecture '$arch'."
          ;;
      esac
      ;;
    Darwin)
      case "$arch" in
        x86_64)
          printf 'osx-x64\n'
          ;;
        arm64)
          printf 'osx-arm64\n'
          ;;
        *)
          fail "Unsupported macOS architecture '$arch'."
          ;;
      esac
      ;;
    *)
      fail "Unsupported operating system '$os'. This installer supports Linux and macOS only."
      ;;
  esac
}

main() {
  require_command unzip
  require_command mktemp

  local install_dir="${NANOAGENT_INSTALL_DIR:-${NanoAgent_INSTALL_DIR:-$DEFAULT_INSTALL_DIR}}"
  local requested_tag="${NANOAGENT_TAG:-${NanoAgent_TAG:-${1:-}}}"
  local tag="$requested_tag"
  local platform
  local asset_name
  local download_url
  local temp_root
  local archive_path
  local extract_dir
  local source_binary
  local destination_binary

  if [[ -z "$tag" ]]; then
    tag="$(resolve_latest_tag)"
  fi

  platform="$(detect_platform)"
  asset_name="${APP_NAME}-${platform}.zip"
  download_url="https://github.com/${OWNER}/${REPO}/releases/download/${tag}/${asset_name}"

  log "Installing ${APP_NAME} ${tag} for ${platform} as '${COMMAND_NAME}'..."
  log "Install directory: ${install_dir}"

  temp_root="$(mktemp -d)"
  archive_path="${temp_root}/${asset_name}"
  extract_dir="${temp_root}/extract"

  cleanup() {
    rm -rf "$temp_root"
  }
  trap cleanup EXIT

  mkdir -p "$extract_dir" "$install_dir"

  log "Downloading ${asset_name}..."
  if ! download_to_file "$download_url" "$archive_path"; then
    fail "Download failed from ${download_url}."
  fi

  log "Extracting archive..."
  unzip -qo "$archive_path" -d "$extract_dir"

  source_binary="$(find "$extract_dir" -type f -name "$EXECUTABLE_NAME" | head -n 1)"

  if [[ -z "$source_binary" || ! -f "$source_binary" ]]; then
    fail "Expected executable '${EXECUTABLE_NAME}' was not found in ${asset_name}."
  fi

  destination_binary="${install_dir}/${COMMAND_NAME}"
  cp "$source_binary" "$destination_binary"
  chmod 0755 "$destination_binary"

  log "Installed '${COMMAND_NAME}' to ${destination_binary}"

  case ":${PATH}:" in
    *":${install_dir}:"*)
      log "The install directory is already on PATH."
      ;;
    *)
      log "Add '${install_dir}' to your PATH if it is not already there."
      ;;
  esac

  log "Done."
}

main "${1:-}"