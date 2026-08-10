#!/usr/bin/env bash
set -euo pipefail

workspace="${CI_PROJECT_DIR:-$(pwd)}"
artifacts_dir="${stemcode_ARTIFACTS_DIR:-artifacts/stemcode-review}"
stemcode_command="${stemcode_COMMAND:-stemcode}"
review_profile="${stemcode_REVIEW_PROFILE:-pr-reviewer}"
output_file="${stemcode_REVIEW_OUTPUT:-${artifacts_dir}/review.md}"
raw_diff_file="${artifacts_dir}/changes.diff"
review_diff_file="${artifacts_dir}/changes.review.diff"

export STEMCODE_PROVIDER="${STEMCODE_PROVIDER:-openai}"
export STEMCODE_MODEL="${STEMCODE_MODEL:-gpt-5.4}"
export STEMCODE_THINKING="${STEMCODE_THINKING:-off}"

mkdir -p "$artifacts_dir"
cd "$workspace"

if [[ -z "${STEMCODE_API_KEY:-}" ]]; then
  cat > "$output_file" <<'EOF'
stemcode review skipped because the STEMCODE_API_KEY CI/CD variable is not configured.
EOF
  exit 0
fi

create_merge_request_diff() {
  local target_branch="${CI_MERGE_REQUEST_TARGET_BRANCH_NAME:?CI_MERGE_REQUEST_TARGET_BRANCH_NAME is required for merge request reviews.}"
  local head_sha="${CI_COMMIT_SHA:?CI_COMMIT_SHA is required for merge request reviews.}"
  local target_ref="refs/remotes/origin/${target_branch}"
  local base_sha="${CI_MERGE_REQUEST_DIFF_BASE_SHA:-}"

  git fetch --no-tags origin "+refs/heads/${target_branch}:${target_ref}"

  if [[ -z "$base_sha" ]] || ! git cat-file -e "${base_sha}^{commit}" 2>/dev/null; then
    base_sha="$(git merge-base "$target_ref" "$head_sha")"
  fi

  git diff --find-renames --unified="${stemcode_DIFF_CONTEXT_LINES:-80}" "$base_sha" "$head_sha" -- > "$raw_diff_file"
}

prepare_review_diff() {
  local max_bytes="${stemcode_MAX_DIFF_BYTES:-240000}"
  local diff_bytes

  diff_bytes="$(wc -c < "$raw_diff_file" | tr -d '[:space:]')"
  if (( diff_bytes > max_bytes )); then
    head -c "$max_bytes" "$raw_diff_file" > "$review_diff_file"
    printf '\n\n[Diff truncated by stemcode GitLab automation: %s of %s bytes included.]\n' \
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

post_merge_request_review() {
  local merge_request_iid="${CI_MERGE_REQUEST_IID:?CI_MERGE_REQUEST_IID is required for merge request reviews.}"
  local project_id="${CI_PROJECT_ID:?CI_PROJECT_ID is required for merge request reviews.}"
  local api_url="${CI_API_V4_URL:?CI_API_V4_URL is required for merge request reviews.}"
  local gitlab_token="${stemcode_GITLAB_TOKEN:-${GITLAB_TOKEN:-}}"
  local body_file="${artifacts_dir}/gitlab-review-body.md"

  {
    echo "## stemcode merge request review"
    echo
    cat "$output_file"
    echo
    printf '<!-- stemcode-review:%s:%s -->\n' "${CI_PIPELINE_ID:-local}" "${CI_JOB_ID:-0}"
  } > "$body_file"

  if [[ "${stemcode_DRY_RUN:-}" == "1" ]]; then
    echo "stemcode dry run enabled. Review body:"
    echo
    cat "$body_file"
    return 0
  fi

  if [[ -z "$gitlab_token" ]]; then
    echo "stemcode review generated but not posted because GITLAB_TOKEN or stemcode_GITLAB_TOKEN is not configured." >&2
    return 0
  fi

  curl --fail --request POST \
    --header "PRIVATE-TOKEN: ${gitlab_token}" \
    --data-urlencode "body@${body_file}" \
    "${api_url}/projects/${project_id}/merge_requests/${merge_request_iid}/notes"
}

if [[ "${CI_PIPELINE_SOURCE:-}" != "merge_request_event" ]]; then
  echo "stemcode review skipped because this is not a GitLab merge request pipeline." > "$output_file"
  exit 0
fi

create_merge_request_diff

if [[ ! -s "$raw_diff_file" ]]; then
  echo "stemcode review skipped because the diff is empty." > "$output_file"
  exit 0
fi

prepare_review_diff
run_stemcode_review
post_merge_request_review
