#!/usr/bin/env bash
set -euo pipefail

readonly OWNER="rizwan3d"
readonly REPO="NanoAgent"
readonly APP_NAME="NanoAgent.CLI"
readonly EXECUTABLE_NAME="NanoAgent.CLI"
readonly COMMAND_NAME="nanoai"
readonly CHECKSUMS_NAME="SHA256SUMS"
readonly DEFAULT_INSTALL_DIR="${HOME}/.local/bin"
readonly TOTAL_STEPS=7

TEMP_ROOT=""
CURRENT_STEP=0

cleanup() {
  if [[ -n "${TEMP_ROOT:-}" && -d "$TEMP_ROOT" ]]; then
    rm -rf "$TEMP_ROOT"
  fi
}

trap cleanup EXIT

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

progress_enabled() {
  local value="${NANOAGENT_NO_PROGRESS:-${NanoAgent_NO_PROGRESS:-}}"

  case "$value" in
    1|true|TRUE|True|yes|YES|Yes)
      return 1
      ;;
    *)
      [[ -t 2 ]]
      ;;
  esac
}

start_step() {
  CURRENT_STEP=$((CURRENT_STEP + 1))
  log "[$CURRENT_STEP/$TOTAL_STEPS] $1"
}

finish_step() {
  log "    $1"
}

format_bytes() {
  local bytes="$1"

  awk -v bytes="$bytes" '
    BEGIN {
      split("B KiB MiB GiB", units, " ")
      value = bytes + 0
      unit = 1

      while (value >= 1024 && unit < 4) {
        value = value / 1024
        unit++
      }

      if (unit == 1) {
        printf "%d %s", value, units[unit]
      } else {
        printf "%.1f %s", value, units[unit]
      }
    }
  '
}

file_size() {
  wc -c < "$1" | tr -d '[:space:]'
}

download_to_file() {
  local url="$1"
  local destination="$2"
  local show_progress="${3:-0}"

  if command -v curl >/dev/null 2>&1; then
    local curl_args=(
      -fL
      -H "User-Agent: ${APP_NAME}-installer"
      --retry 3
      --retry-delay 2
      --connect-timeout 15
      -o "$destination"
    )

    if [[ "$show_progress" == "1" ]] && progress_enabled; then
      curl_args+=(--progress-bar)
    else
      curl_args+=(-sS)
    fi

    curl "${curl_args[@]}" "$url"
    return
  fi

  if command -v wget >/dev/null 2>&1; then
    local wget_args=(
      --header="User-Agent: ${APP_NAME}-installer"
      -O "$destination"
    )

    if [[ "$show_progress" == "1" ]] &&
      progress_enabled &&
      wget --help 2>&1 | grep -q -- '--show-progress'; then
      wget_args+=(--show-progress --progress=bar:force)
    else
      wget_args+=(-q)
    fi

    wget "${wget_args[@]}" "$url"
    return
  fi

  fail "Neither curl nor wget is available. Install one of them and try again."
}

sha256_required() {
  local value="${NANOAGENT_REQUIRE_SHA256:-${NanoAgent_REQUIRE_SHA256:-}}"

  case "$value" in
    1|true|TRUE|True|yes|YES|Yes)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

compute_sha256() {
  local path="$1"

  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$path" | awk '{ print tolower($1) }'
    return
  fi

  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$path" | awk '{ print tolower($1) }'
    return
  fi

  if command -v openssl >/dev/null 2>&1; then
    openssl dgst -sha256 -r "$path" | awk '{ print tolower($1) }'
    return
  fi

  fail "No SHA256 checksum tool is available. Install sha256sum, shasum, or openssl and try again."
}

read_expected_sha256() {
  local asset_name="$1"
  local checksums_path="$2"

  awk -v name="$asset_name" '
    {
      hash = $1
      file = $2
      sub(/^\*/, "", file)
      sub(/^\.\//, "", file)

      if (file == name) {
        print hash
        exit
      }
    }
  ' "$checksums_path"
}

read_release_asset_sha256() {
  local asset_name="$1"
  local metadata_path="$2"
  local escaped_asset_name

  escaped_asset_name="$(printf '%s\n' "$asset_name" | sed 's/[.[\*^$\\]/\\&/g')"

  sed 's/},{/}\
{/g' "$metadata_path" |
    sed -n "/\"name\":\"${escaped_asset_name}\"/s/.*\"digest\":\"sha256:\([0-9A-Fa-f]\{64\}\)\".*/\1/p" |
    head -n 1
}

resolve_release_asset_sha256() {
  local tag="$1"
  local asset_name="$2"
  local metadata_url="https://api.github.com/repos/${OWNER}/${REPO}/releases/tags/${tag}"
  local metadata_path="${TEMP_ROOT}/release-metadata.json"
  local digest

  if ! download_to_file "$metadata_url" "$metadata_path"; then
    return 1
  fi

  digest="$(read_release_asset_sha256 "$asset_name" "$metadata_path" | tr '[:upper:]' '[:lower:]')"
  if [[ -z "$digest" ]]; then
    return 1
  fi

  printf '%s\n' "$digest"
}

verify_archive_sha256() {
  local tag="$1"
  local asset_name="$2"
  local archive_path="$3"
  local checksums_url="https://github.com/${OWNER}/${REPO}/releases/download/${tag}/${CHECKSUMS_NAME}"
  local checksums_path="${TEMP_ROOT}/${CHECKSUMS_NAME}"
  local expected_sha256
  local actual_sha256

  log "Downloading ${CHECKSUMS_NAME}..."
  if ! download_to_file "$checksums_url" "$checksums_path"; then
    expected_sha256="$(resolve_release_asset_sha256 "$tag" "$asset_name" || true)"

    if [[ -z "$expected_sha256" ]]; then
      if sha256_required; then
        fail "Unable to download ${CHECKSUMS_NAME} from ${checksums_url}, and no GitHub release metadata digest was found."
      fi

      log "${CHECKSUMS_NAME} was not found for ${tag}; continuing without checksum verification. Set NANOAGENT_REQUIRE_SHA256=1 to require it."
      return
    fi

    log "Using SHA256 digest from GitHub release metadata for ${asset_name}."
  else
    expected_sha256="$(read_expected_sha256 "$asset_name" "$checksums_path" | tr '[:upper:]' '[:lower:]')"
  fi

  if [[ -z "$expected_sha256" ]]; then
    fail "${CHECKSUMS_NAME} does not contain a checksum for ${asset_name}."
  fi

  if ! printf '%s\n' "$expected_sha256" | grep -Eq '^[0-9a-f]{64}$'; then
    fail "${CHECKSUMS_NAME} contains an invalid SHA256 checksum for ${asset_name}."
  fi

  actual_sha256="$(compute_sha256 "$archive_path")"
  if [[ "$actual_sha256" != "$expected_sha256" ]]; then
    fail "SHA256 verification failed for ${asset_name}. Expected ${expected_sha256}, got ${actual_sha256}."
  fi

  log "Verified SHA256 checksum for ${asset_name}."
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
  local install_dir="${NANOAGENT_INSTALL_DIR:-${NanoAgent_INSTALL_DIR:-$DEFAULT_INSTALL_DIR}}"
  local requested_tag="${NANOAGENT_TAG:-${NanoAgent_TAG:-${1:-}}}"
  local tag="$requested_tag"
  local platform
  local asset_name
  local download_url
  local archive_path
  local extract_dir
  local source_binary
  local destination_binary

  log "NanoAgent CLI Installer"
  start_step "Checking system requirements..."
  require_command unzip
  require_command mktemp
  require_command find

  platform="$(detect_platform)"
  finish_step "Detected ${platform}."

  start_step "Resolving release..."
  if [[ -z "$tag" ]]; then
    tag="$(resolve_latest_tag)"
  fi

  asset_name="${APP_NAME}-${platform}.zip"
  download_url="https://github.com/${OWNER}/${REPO}/releases/download/${tag}/${asset_name}"

  finish_step "Using ${APP_NAME} ${tag} for ${platform}."

  start_step "Preparing install directory..."
  log "Install directory: ${install_dir}"
  TEMP_ROOT="$(mktemp -d)"
  archive_path="${TEMP_ROOT}/${asset_name}"
  extract_dir="${TEMP_ROOT}/extract"

  mkdir -p "$extract_dir" "$install_dir"
  finish_step "Workspace ready."

  start_step "Downloading ${asset_name}..."
  if ! download_to_file "$download_url" "$archive_path" 1; then
    fail "Download failed from ${download_url}."
  fi
  finish_step "Downloaded $(format_bytes "$(file_size "$archive_path")")."

  start_step "Verifying download..."
  verify_archive_sha256 "$tag" "$asset_name" "$archive_path"
  finish_step "Checksum verification passed."

  start_step "Extracting archive..."
  unzip -qo "$archive_path" -d "$extract_dir"

  source_binary="$(find "$extract_dir" -type f -name "$EXECUTABLE_NAME" | head -n 1)"

  if [[ -z "$source_binary" || ! -f "$source_binary" ]]; then
    fail "Expected executable '${EXECUTABLE_NAME}' was not found in ${asset_name}."
  fi
  finish_step "Found ${EXECUTABLE_NAME}."

  start_step "Installing command..."
  destination_binary="${install_dir}/${COMMAND_NAME}"
  cp "$source_binary" "$destination_binary"
  chmod 0755 "$destination_binary"

  finish_step "Installed '${COMMAND_NAME}' to ${destination_binary}."

  case ":${PATH}:" in
    *":${install_dir}:"*)
      log "The install directory is already on PATH."
      log "Run '${COMMAND_NAME}' to start NanoAgent."
      ;;
    *)
      log "Add '${install_dir}' to your PATH if it is not already there."
      log "For this shell, run: export PATH=\"${install_dir}:\$PATH\""
      ;;
  esac

  log "Done. Thanks for installing NanoAgent."
}

main "${1:-}"
