#!/usr/bin/env bash
set -euo pipefail

readonly OWNER="rizwan3d"
readonly REPO="NanoAgent"
readonly APP_NAME="NanoAgent.CLI"
readonly EXECUTABLE_NAME="NanoAgent.CLI"
# Installed command name: '/update' sets NanoAgent_COMMAND_NAME so the running
# binary's filename is preserved when replacing it in place.
readonly COMMAND_NAME="${NANOAGENT_COMMAND_NAME:-${NanoAgent_COMMAND_NAME:-nanoai}}"
readonly CHECKSUMS_NAME="SHA256SUMS"
readonly DEFAULT_INSTALL_DIR="${HOME}/.local/bin"
readonly TOTAL_STEPS=7

TEMP_ROOT=""
CURRENT_STEP=0
COMMAND_AVAILABLE_SCOPE="current"

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
      fail "Unable to download ${CHECKSUMS_NAME} from ${checksums_url}, and no GitHub release metadata digest was found. Checksum verification is mandatory."
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

path_contains_directory() {
  local path_value="${1:-}"
  local directory="$2"
  local entry
  local IFS=:

  for entry in $path_value; do
    if [[ "$entry" == "$directory" ]]; then
      return 0
    fi
  done

  return 1
}

single_quote() {
  printf "'"
  printf '%s' "$1" | sed "s/'/'\\\\''/g"
  printf "'"
}

fish_quote() {
  printf "'"
  printf '%s' "$1" | sed "s/[\\']/\\&/g"
  printf "'"
}

profile_paths_for_shell() {
  local shell_name="${SHELL##*/}"
  local os

  os="$(uname -s)"

  case "$shell_name" in
    zsh)
      printf '%s\n' "${HOME}/.zshrc"
      printf '%s\n' "${HOME}/.zprofile"
      ;;
    bash)
      printf '%s\n' "${HOME}/.bashrc"
      if [[ "$os" == "Darwin" ]]; then
        printf '%s\n' "${HOME}/.bash_profile"
      else
        printf '%s\n' "${HOME}/.profile"
      fi
      ;;
    fish)
      printf '%s\n' "${HOME}/.config/fish/config.fish"
      ;;
    *)
      printf '%s\n' "${HOME}/.profile"
      ;;
  esac
}

append_posix_path_entry() {
  local profile_path="$1"
  local install_dir="$2"
  local quoted_install_dir
  local quoted_path_match

  quoted_install_dir="$(single_quote "$install_dir")"
  quoted_path_match="$(single_quote ":${install_dir}:")"

  {
    printf '\n# Added by NanoAgent CLI installer\n'
    printf "if [ -d %s ] && ! printf '%%s' \":\$PATH:\" | grep -qF -- %s; then\n" "$quoted_install_dir" "$quoted_path_match"
    printf '  export PATH=%s:$PATH\n' "$quoted_install_dir"
    printf 'fi\n'
  } >> "$profile_path"
}

append_fish_path_entry() {
  local profile_path="$1"
  local install_dir="$2"
  local quoted_install_dir

  quoted_install_dir="$(fish_quote "$install_dir")"

  {
    printf '\n# Added by NanoAgent CLI installer\n'
    printf 'if test -d %s; and not contains -- %s $PATH\n' "$quoted_install_dir" "$quoted_install_dir"
    printf '    set -gx PATH %s $PATH\n' "$quoted_install_dir"
    printf 'end\n'
  } >> "$profile_path"
}

add_install_dir_to_shell_profiles() {
  local install_dir="$1"
  local profile_path
  local profile_dir
  local updated_profiles=()

  while IFS= read -r profile_path; do
    if [[ -z "$profile_path" ]]; then
      continue
    fi

    if [[ -f "$profile_path" ]] && grep -F -- "$install_dir" "$profile_path" >/dev/null 2>&1; then
      continue
    fi

    profile_dir="$(dirname "$profile_path")"
    mkdir -p "$profile_dir"

    if [[ "$profile_path" == */config.fish ]]; then
      append_fish_path_entry "$profile_path" "$install_dir"
    else
      append_posix_path_entry "$profile_path" "$install_dir"
    fi

    updated_profiles+=("$profile_path")
  done < <(profile_paths_for_shell)

  printf '%s\n' "${updated_profiles[@]}"
}

link_command_into_existing_path() {
  local destination_binary="$1"
  local install_dir="$2"
  local path_entry
  local path_link
  local IFS=:

  for path_entry in ${PATH:-}; do
    if [[ -z "$path_entry" || "$path_entry" != /* || "$path_entry" == "$install_dir" || "$path_entry" == */sbin ]]; then
      continue
    fi

    if [[ ! -d "$path_entry" || ! -w "$path_entry" ]]; then
      continue
    fi

    path_link="${path_entry}/${COMMAND_NAME}"
    if [[ -e "$path_link" || -L "$path_link" ]]; then
      if [[ "$path_link" -ef "$destination_binary" ]]; then
        printf '%s\n' "$path_link"
        return 0
      fi

      continue
    fi

    if ln -s "$destination_binary" "$path_link" 2>/dev/null; then
      printf '%s\n' "$path_link"
      return 0
    fi
  done

  return 1
}

add_install_dir_to_github_path() {
  local install_dir="$1"
  local github_path_dir

  if [[ -z "${GITHUB_PATH:-}" ]]; then
    return 1
  fi

  github_path_dir="$(dirname "$GITHUB_PATH")"
  if [[ ! -d "$github_path_dir" || ! -w "$github_path_dir" ]]; then
    return 1
  fi

  printf '%s\n' "$install_dir" >> "$GITHUB_PATH"
  log "Added '${install_dir}' to GitHub Actions PATH for later steps."
  return 0
}

make_command_available() {
  local destination_binary="$1"
  local install_dir="$2"
  local linked_path
  local updated_profiles
  local profile_path

  if path_contains_directory "${PATH:-}" "$install_dir"; then
    log "The install directory is already on PATH."
    COMMAND_AVAILABLE_SCOPE="current"
    return 0
  fi

  if linked_path="$(link_command_into_existing_path "$destination_binary" "$install_dir")"; then
    log "Linked '${COMMAND_NAME}' into PATH at ${linked_path}."
    COMMAND_AVAILABLE_SCOPE="current"
    return 0
  fi

  if add_install_dir_to_github_path "$install_dir"; then
    COMMAND_AVAILABLE_SCOPE="ci"
    return 1
  fi

  updated_profiles="$(add_install_dir_to_shell_profiles "$install_dir")"

  if [[ -n "$updated_profiles" ]]; then
    while IFS= read -r profile_path; do
      if [[ -n "$profile_path" ]]; then
        log "Added '${install_dir}' to PATH in ${profile_path}."
      fi
    done <<< "$updated_profiles"
  else
    log "The install directory is already listed in your shell profile."
  fi

  log "Open a new terminal to use '${COMMAND_NAME}'."
  COMMAND_AVAILABLE_SCOPE="new_terminal"
  return 1
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

  if make_command_available "$destination_binary" "$install_dir"; then
    log "Done. Run '${COMMAND_NAME}' to start NanoAgent."
  elif [[ "$COMMAND_AVAILABLE_SCOPE" == "ci" ]]; then
    log "Done. '${COMMAND_NAME}' will be available in later GitHub Actions steps."
  else
    log "Done. '${COMMAND_NAME}' will be available in new terminals."
  fi
}

main "${1:-}"
