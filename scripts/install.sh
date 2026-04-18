#!/usr/bin/env bash
set -euo pipefail

readonly OWNER="rizwan3d"
readonly REPO="FinalAgent"
readonly APP_NAME="FinalAgent"
readonly DEFAULT_INSTALL_DIR="${HOME}/.local/bin"

log() {
  printf '[%s] %s\n' "$APP_NAME" "$1"
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
    curl -fL --retry 3 --retry-delay 2 --connect-timeout 15 -o "$destination" "$url"
    return
  fi

  if command -v wget >/dev/null 2>&1; then
    wget -O "$destination" "$url"
    return
  fi

  fail "Neither curl nor wget is available. Install one of them and try again."
}

resolve_latest_tag() {
  local api_url="https://api.github.com/repos/${OWNER}/${REPO}/releases/latest"
  local metadata

  log "Resolving the latest release tag..."
  metadata="$(mktemp)"

  download_to_file "$api_url" "$metadata"

  local tag
  tag="$(sed -n 's/.*"tag_name"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$metadata" | head -n 1)"
  rm -f "$metadata"

  if [[ -z "$tag" ]]; then
    fail "Unable to determine the latest release tag from GitHub. Set FINALAGENT_TAG and try again."
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

  local install_dir="${FINALAGENT_INSTALL_DIR:-$DEFAULT_INSTALL_DIR}"
  local requested_tag="${FINALAGENT_TAG:-${1:-}}"
  local tag="${requested_tag}"
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

  log "Installing ${APP_NAME} ${tag} for ${platform}..."
  log "Install directory: ${install_dir}"

  temp_root="$(mktemp -d)"
  archive_path="${temp_root}/${asset_name}"
  extract_dir="${temp_root}/extract"
  mkdir -p "$extract_dir" "$install_dir"

  cleanup() {
    rm -rf "$temp_root"
  }
  trap cleanup EXIT

  log "Downloading ${asset_name}..."
  if ! download_to_file "$download_url" "$archive_path"; then
    fail "Download failed from ${download_url}."
  fi

  log "Extracting archive..."
  unzip -qo "$archive_path" -d "$extract_dir"

  source_binary="${extract_dir}/${APP_NAME}"
  if [[ ! -f "$source_binary" ]]; then
    fail "Expected executable '${APP_NAME}' was not found in ${asset_name}."
  fi

  destination_binary="${install_dir}/${APP_NAME}"
  cp "$source_binary" "$destination_binary"
  chmod 0755 "$destination_binary"

  log "Installed '${APP_NAME}' to ${destination_binary}"

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
