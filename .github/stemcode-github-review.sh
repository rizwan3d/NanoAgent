#!/usr/bin/env bash
set -euo pipefail

mode="${stemcode_REVIEW_MODE:-}"
workspace="${GITHUB_WORKSPACE:-$(pwd)}"
artifacts_dir="${stemcode_ARTIFACTS_DIR:-artifacts/stemcode-review}"
stemcode_command="${stemcode_COMMAND:-stemcode}"
review_profile="${stemcode_REVIEW_PROFILE:-pr-reviewer}"
output_file="${stemcode_REVIEW_OUTPUT:-${artifacts_dir}/review.md}"
raw_diff_file="${artifacts_dir}/changes.diff"
review_diff_file="${artifacts_dir}/changes.review.diff"

mkdir -p "$artifacts_dir"
cd "$workspace"

if [[ -z "${STEMCODE_API_KEY:-}" ]]; then
  cat > "$output_file" <<'EOF'
stemcode review skipped because the STEMCODE_API_KEY secret is not configured.
EOF
  exit 0
fi

create_pr_diff() {
  local base_sha="${stemcode_BASE_SHA:?stemcode_BASE_SHA is required for PR reviews.}"
  local head_sha="${stemcode_HEAD_SHA:?stemcode_HEAD_SHA is required for PR reviews.}"
  local head_repo_full_name="${stemcode_HEAD_REPO_FULL_NAME:-}"
  local head_repo_url="${stemcode_HEAD_REPO_URL:-}"
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

  git diff --find-renames --unified="${stemcode_DIFF_CONTEXT_LINES:-80}" "$base_sha" "$head_sha" -- > "$raw_diff_file"
}

prepare_review_diff() {
  local max_bytes="${stemcode_MAX_DIFF_BYTES:-240000}"
  local diff_bytes

  diff_bytes="$(wc -c < "$raw_diff_file" | tr -d '[:space:]')"
  if (( diff_bytes > max_bytes )); then
    head -c "$max_bytes" "$raw_diff_file" > "$review_diff_file"
    printf '\n\n[Diff truncated by stemcode GitHub automation: %s of %s bytes included.]\n' \
      "$max_bytes" "$diff_bytes" >> "$review_diff_file"
    return
  fi

  cp "$raw_diff_file" "$review_diff_file"
}

run_stemcode_review() {
  "$stemcode_command" \
    --profile "$review_profile" \
    --stdin < "$review_diff_file" > "$output_file"
}

post_pr_review() {
  local pr_number="${stemcode_PR_NUMBER:?stemcode_PR_NUMBER is required for PR reviews.}"
  local body_file="${artifacts_dir}/pr-review-body.md"

  {
    echo "## stemcode PR review"
    echo
    cat "$output_file"
    echo
    printf '<!-- stemcode-review:%s:%s -->\n' "${GITHUB_RUN_ID:-local}" "${GITHUB_RUN_ATTEMPT:-0}"
  } > "$body_file"

  if [[ "${stemcode_DRY_RUN:-}" == "1" ]]; then
    echo "stemcode dry run enabled. Review body:"
    echo
    cat "$body_file"
    return 0
  fi

  if ! gh pr review "$pr_number" --comment --body-file "$body_file"; then
    gh pr comment "$pr_number" --body-file "$body_file"
  fi
}

case "$mode" in
  pr)
    create_pr_diff
    ;;
  *)
    echo "stemcode_REVIEW_MODE must be 'pr'." >&2
    exit 1
    ;;
esac

if [[ ! -s "$raw_diff_file" ]]; then
  echo "stemcode review skipped because the diff is empty." > "$output_file"
  exit 0
fi

prepare_review_diff
run_stemcode_review
post_pr_review
