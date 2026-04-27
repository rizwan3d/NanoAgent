#!/usr/bin/env bash
set -euo pipefail

mode="${NANOAI_REVIEW_MODE:-}"
workspace="${GITHUB_WORKSPACE:-$(pwd)}"
artifacts_dir="${NANOAI_ARTIFACTS_DIR:-artifacts/nanoai-review}"
nanoai_command="${NANOAI_COMMAND:-nanoai}"
review_profile="${NANOAI_REVIEW_PROFILE:-pr-reviewer}"
output_file="${NANOAI_REVIEW_OUTPUT:-${artifacts_dir}/review.md}"
raw_diff_file="${artifacts_dir}/changes.diff"
review_diff_file="${artifacts_dir}/changes.review.diff"

mkdir -p "$artifacts_dir"
cd "$workspace"

if [[ -z "${NANOAGENT_API_KEY:-}" ]]; then
  cat > "$output_file" <<'EOF'
NanoAI review skipped because the NANOAGENT_API_KEY secret is not configured.
EOF
  exit 0
fi

create_pr_diff() {
  local base_sha="${NANOAI_BASE_SHA:?NANOAI_BASE_SHA is required for PR reviews.}"
  local head_sha="${NANOAI_HEAD_SHA:?NANOAI_HEAD_SHA is required for PR reviews.}"
  local head_repo_full_name="${NANOAI_HEAD_REPO_FULL_NAME:-}"
  local head_repo_url="${NANOAI_HEAD_REPO_URL:-}"
  local authenticated_head_repo_url=""

  git fetch --no-tags --depth=1 origin "$base_sha"

  if [[ -n "$head_repo_full_name" && -n "${GH_TOKEN:-}" ]]; then
    authenticated_head_repo_url="https://github.com/${head_repo_full_name}.git"
  fi

  if [[ -n "$authenticated_head_repo_url" ]]; then
    git -c "http.extraheader=AUTHORIZATION: bearer ${GH_TOKEN}" \
      fetch --no-tags --depth=1 "$authenticated_head_repo_url" "$head_sha"
  elif [[ -n "$head_repo_url" ]]; then
    git fetch --no-tags --depth=1 "$head_repo_url" "$head_sha"
  else
    git fetch --no-tags --depth=1 origin "$head_sha"
  fi

  git diff --find-renames --unified="${NANOAI_DIFF_CONTEXT_LINES:-80}" "$base_sha" "$head_sha" -- > "$raw_diff_file"
}

prepare_review_diff() {
  local max_bytes="${NANOAI_MAX_DIFF_BYTES:-240000}"
  local diff_bytes

  diff_bytes="$(wc -c < "$raw_diff_file" | tr -d '[:space:]')"
  if (( diff_bytes > max_bytes )); then
    head -c "$max_bytes" "$raw_diff_file" > "$review_diff_file"
    printf '\n\n[Diff truncated by NanoAI GitHub automation: %s of %s bytes included.]\n' \
      "$max_bytes" "$diff_bytes" >> "$review_diff_file"
    return
  fi

  cp "$raw_diff_file" "$review_diff_file"
}

run_nanoai_review() {
  "$nanoai_command" \
    --profile "$review_profile" \
    --stdin < "$review_diff_file" > "$output_file"
}

post_pr_review() {
  local pr_number="${NANOAI_PR_NUMBER:?NANOAI_PR_NUMBER is required for PR reviews.}"
  local body_file="${artifacts_dir}/pr-review-body.md"

  {
    echo "## NanoAI PR review"
    echo
    cat "$output_file"
    echo
    printf '<!-- nanoai-review:%s:%s -->\n' "${GITHUB_RUN_ID:-local}" "${GITHUB_RUN_ATTEMPT:-0}"
  } > "$body_file"

  if ! gh pr review "$pr_number" --comment --body-file "$body_file"; then
    gh pr comment "$pr_number" --body-file "$body_file"
  fi
}

case "$mode" in
  pr)
    create_pr_diff
    ;;
  *)
    echo "NANOAI_REVIEW_MODE must be 'pr'." >&2
    exit 1
    ;;
esac

if [[ ! -s "$raw_diff_file" ]]; then
  echo "NanoAI review skipped because the diff is empty." > "$output_file"
  exit 0
fi

prepare_review_diff
run_nanoai_review
post_pr_review
