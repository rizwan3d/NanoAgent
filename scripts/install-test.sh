#!/usr/bin/env bash
set -euo pipefail

readonly OWNER="rizwan3d"
readonly REPO="NanoAgent"
readonly WORKFLOW_FILE="ci.yml"
readonly APP_NAME="NanoAgent.CLI"
readonly EXECUTABLE_NAME="NanoAgent.CLI"
readonly COMMAND_NAME="nanoai"
readonly ARTIFACT_NAME="cli-linux-x64-test-build"
readonly DEFAULT_BRANCH="master"
readonly DEFAULT_INSTALL_DIR="${HOME}/.local/bin"
readonly GITHUB_API_VERSION="2026-03-10"
readonly TOTAL_STEPS=8

TEMP_ROOT=""
CURRENT_STEP=0
COMMAND_AVAILABLE_SCOPE="current"
PYTHON_COMMAND=""

cleanup() {
  if [[ -n "${TEMP_ROOT:-}" && -d "$TEMP_ROOT" ]]; then
    rm -rf "$TEMP_ROOT"
  fi
}

trap cleanup EXIT

log() {
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

resolve_python_command() {
  if command -v python3 >/dev/null 2>&1; then
    PYTHON_COMMAND="python3"
    return
  fi

  if command -v python >/dev/null 2>&1; then
    PYTHON_COMMAND="python"
    return
  fi

  fail "Python is required to parse GitHub API responses. Install python3 or python and try again."
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
  local token="${4:-}"

  if command -v curl >/dev/null 2>&1; then
    local curl_args=(
      -fL
      -H "User-Agent: ${APP_NAME}-test-installer"
      --retry 3
      --retry-delay 2
      --connect-timeout 15
      -o "$destination"
    )

    if [[ -n "$token" ]]; then
      curl_args+=(
        -H "Accept: application/vnd.github+json"
        -H "Authorization: Bearer ${token}"
        -H "X-GitHub-Api-Version: ${GITHUB_API_VERSION}"
      )
    fi

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
      --header="User-Agent: ${APP_NAME}-test-installer"
      -O "$destination"
    )

    if [[ -n "$token" ]]; then
      wget_args+=(
        --header="Accept: application/vnd.github+json"
        --header="Authorization: Bearer ${token}"
        --header="X-GitHub-Api-Version: ${GITHUB_API_VERSION}"
      )
    fi

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

resolve_github_token() {
  for value in \
    "${NANOAGENT_GITHUB_TOKEN:-}" \
    "${NanoAgent_GITHUB_TOKEN:-}" \
    "${GITHUB_TOKEN:-}" \
    "${GH_TOKEN:-}"; do
    if [[ -n "$value" ]]; then
      printf '%s\n' "$value"
      return
    fi
  done

  if command -v gh >/dev/null 2>&1; then
    local gh_token
    gh_token="$(gh auth token 2>/dev/null || true)"
    if [[ -n "$gh_token" ]]; then
      printf '%s\n' "$gh_token"
      return
    fi
  fi

  fail "A GitHub token with Actions read access is required to download test-build artifacts. Set NANOAGENT_GITHUB_TOKEN, GITHUB_TOKEN, or GH_TOKEN, or sign in with GitHub CLI."
}

json_value() {
  local json_path="$1"
  local mode="$2"
  local arg_one="${3:-}"
  local arg_two="${4:-}"

  "$PYTHON_COMMAND" - "$json_path" "$mode" "$arg_one" "$arg_two" <<'PY'
import json
import sys

json_path, mode, arg_one, arg_two = sys.argv[1:5]

with open(json_path, "r", encoding="utf-8") as handle:
    payload = json.load(handle)

if mode == "latest_run_id":
    runs = payload.get("workflow_runs") or []
    if not runs:
        raise SystemExit(1)
    print(runs[0]["id"])
elif mode == "artifact_field":
    name = arg_one
    field = arg_two
    for artifact in payload.get("artifacts") or []:
        if artifact.get("name") != name:
            continue
        value = artifact.get(field, "")
        if isinstance(value, bool):
            print("true" if value else "false")
        else:
            print(value)
        break
    else:
        raise SystemExit(2)
else:
    raise SystemExit(3)
PY
}

resolve_latest_run_id() {
  local token="$1"
  local branch="$2"
  local api_url="https://api.github.com/repos/${OWNER}/${REPO}/actions/workflows/${WORKFLOW_FILE}/runs?branch=${branch}&event=push&status=success&exclude_pull_requests=true&per_page=20"
  local metadata_path="${TEMP_ROOT}/workflow-runs.json"

  if ! download_to_file "$api_url" "$metadata_path" 0 "$token"; then
    fail "Unable to query the GitHub Actions workflow runs for ${WORKFLOW_FILE}."
  fi

  if ! json_value "$metadata_path" latest_run_id; then
    fail "No successful '${WORKFLOW_FILE}' push runs were found for branch '${branch}'."
  fi
}

resolve_artifact_field() {
  local token="$1"
  local run_id="$2"
  local field="$3"
  local api_url="https://api.github.com/repos/${OWNER}/${REPO}/actions/runs/${run_id}/artifacts?per_page=100"
  local metadata_path="${TEMP_ROOT}/artifacts.json"

  if ! download_to_file "$api_url" "$metadata_path" 0 "$token"; then
    fail "Unable to query the GitHub Actions artifacts for workflow run ${run_id}."
  fi

  if ! json_value "$metadata_path" artifact_field "$ARTIFACT_NAME" "$field"; then
    fail "Artifact '${ARTIFACT_NAME}' was not found for workflow run ${run_id}."
  fi
}

verify_archive_sha256() {
  local archive_path="$1"
  local digest="$2"
  local expected_sha256
  local actual_sha256

  case "$digest" in
    sha256:*)
      expected_sha256="${digest#sha256:}"
      ;;
    *)
      fail "GitHub did not return a valid SHA256 digest for '${ARTIFACT_NAME}'."
      ;;
  esac

  expected_sha256="$(printf '%s' "$expected_sha256" | tr '[:upper:]' '[:lower:]')"
  if ! printf '%s\n' "$expected_sha256" | grep -Eq '^[0-9a-f]{64}$'; then
    fail "GitHub returned an invalid SHA256 digest for '${ARTIFACT_NAME}'."
  fi

  actual_sha256="$(compute_sha256 "$archive_path")"
  if [[ "$actual_sha256" != "$expected_sha256" ]]; then
    fail "SHA256 verification failed for '${ARTIFACT_NAME}'. Expected ${expected_sha256}, got ${actual_sha256}."
  fi

  log "Verified SHA256 checksum for '${ARTIFACT_NAME}'."
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
        *)
          fail "Unsupported Linux architecture '${arch}'. This installer currently supports Linux x64 only."
          ;;
      esac
      ;;
    *)
      fail "Unsupported operating system '${os}'. This installer currently supports Linux only. Use install-test.ps1 on Windows."
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

  case "$shell_name" in
    zsh)
      printf '%s\n' "${HOME}/.zshrc"
      printf '%s\n' "${HOME}/.zprofile"
      ;;
    bash)
      printf '%s\n' "${HOME}/.bashrc"
      printf '%s\n' "${HOME}/.profile"
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
    printf '\n# Added by NanoAgent CLI test-build installer\n'
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
    printf '\n# Added by NanoAgent CLI test-build installer\n'
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
  local run_id="${NANOAGENT_RUN_ID:-${1:-}}"
  local branch="${NANOAGENT_BRANCH:-${DEFAULT_BRANCH}}"
  local token
  local platform
  local artifact_download_url
  local artifact_digest
  local artifact_expired
  local archive_path
  local extract_dir
  local source_binary
  local destination_binary

  log "NanoAgent CLI Test-Build Installer"
  start_step "Checking system requirements..."
  require_command unzip
  require_command mktemp
  require_command find
  resolve_python_command

  platform="$(detect_platform)"
  finish_step "Detected ${platform}."

  start_step "Resolving GitHub authentication..."
  token="$(resolve_github_token)"
  finish_step "GitHub token is available."

  TEMP_ROOT="$(mktemp -d)"

  start_step "Resolving workflow run..."
  if [[ -z "$run_id" ]]; then
    run_id="$(resolve_latest_run_id "$token" "$branch")"
  fi
  finish_step "Using workflow run ${run_id}."

  start_step "Locating test-build artifact..."
  artifact_download_url="$(resolve_artifact_field "$token" "$run_id" archive_download_url)"
  artifact_digest="$(resolve_artifact_field "$token" "$run_id" digest)"
  artifact_expired="$(resolve_artifact_field "$token" "$run_id" expired)"

  if [[ "$artifact_expired" == "true" ]]; then
    fail "Artifact '${ARTIFACT_NAME}' for workflow run ${run_id} has expired."
  fi

  finish_step "Found '${ARTIFACT_NAME}'."

  start_step "Preparing install directory..."
  log "Install directory: ${install_dir}"
  archive_path="${TEMP_ROOT}/${ARTIFACT_NAME}.zip"
  extract_dir="${TEMP_ROOT}/extract"
  mkdir -p "$extract_dir" "$install_dir"
  finish_step "Workspace ready."

  start_step "Downloading ${ARTIFACT_NAME}..."
  if ! download_to_file "$artifact_download_url" "$archive_path" 1 "$token"; then
    fail "Download failed for '${ARTIFACT_NAME}'."
  fi
  finish_step "Downloaded $(format_bytes "$(file_size "$archive_path")")."

  start_step "Verifying download..."
  verify_archive_sha256 "$archive_path" "$artifact_digest"
  finish_step "Checksum verification passed."

  start_step "Extracting and installing command..."
  unzip -qo "$archive_path" -d "$extract_dir"

  source_binary="$(find "$extract_dir" -type f -name "$EXECUTABLE_NAME" | head -n 1)"

  if [[ -z "$source_binary" || ! -f "$source_binary" ]]; then
    fail "Expected executable '${EXECUTABLE_NAME}' was not found in '${ARTIFACT_NAME}'."
  fi

  destination_binary="${install_dir}/${COMMAND_NAME}"
  cp "$source_binary" "$destination_binary"
  chmod 0755 "$destination_binary"
  finish_step "Installed '${COMMAND_NAME}' to ${destination_binary}."

  if make_command_available "$destination_binary" "$install_dir"; then
    log "Done. Run '${COMMAND_NAME}' to start the latest test build."
  elif [[ "$COMMAND_AVAILABLE_SCOPE" == "ci" ]]; then
    log "Done. '${COMMAND_NAME}' will be available in later GitHub Actions steps."
  else
    log "Done. '${COMMAND_NAME}' will be available in new terminals."
  fi
}

main "${1:-}"
