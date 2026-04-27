#!/usr/bin/env bash
set -euo pipefail

workspace="${CI_PROJECT_DIR:-$(pwd)}"
artifacts_dir="${NANOAI_ARTIFACTS_DIR:-artifacts/nanoai-review}"
nanoai_command="${NANOAI_COMMAND:-nanoai}"
review_profile="${NANOAI_REVIEW_PROFILE:-pr-reviewer}"
output_file="${NANOAI_REVIEW_OUTPUT:-${artifacts_dir}/review.md}"
raw_diff_file="${artifacts_dir}/changes.diff"
review_diff_file="${artifacts_dir}/changes.review.diff"

export NANOAGENT_PROVIDER="${NANOAGENT_PROVIDER:-openai}"
export NANOAGENT_MODEL="${NANOAGENT_MODEL:-gpt-5.4}"
export NANOAGENT_THINKING="${NANOAGENT_THINKING:-off}"

mkdir -p "$artifacts_dir"
cd "$workspace"

if [[ -z "${NANOAGENT_API_KEY:-}" ]]; then
  cat > "$output_file" <<'EOF'
NanoAI review skipped because the NANOAGENT_API_KEY CI/CD variable is not configured.
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

  git diff --find-renames --unified="${NANOAI_DIFF_CONTEXT_LINES:-80}" "$base_sha" "$head_sha" -- > "$raw_diff_file"
}

prepare_review_diff() {
  local max_bytes="${NANOAI_MAX_DIFF_BYTES:-240000}"
  local diff_bytes

  diff_bytes="$(wc -c < "$raw_diff_file" | tr -d '[:space:]')"
  if (( diff_bytes > max_bytes )); then
    head -c "$max_bytes" "$raw_diff_file" > "$review_diff_file"
    printf '\n\n[Diff truncated by NanoAI GitLab automation: %s of %s bytes included.]\n' \
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

post_merge_request_review() {
  local merge_request_iid="${CI_MERGE_REQUEST_IID:?CI_MERGE_REQUEST_IID is required for merge request reviews.}"
  local project_id="${CI_PROJECT_ID:?CI_PROJECT_ID is required for merge request reviews.}"
  local api_url="${CI_API_V4_URL:?CI_API_V4_URL is required for merge request reviews.}"
  local gitlab_token="${NANOAI_GITLAB_TOKEN:-${GITLAB_TOKEN:-}}"
  local body_file="${artifacts_dir}/gitlab-review-body.md"

  {
    echo "## NanoAI merge request review"
    echo
    cat "$output_file"
    echo
    printf '<!-- nanoai-review:%s:%s -->\n' "${CI_PIPELINE_ID:-local}" "${CI_JOB_ID:-0}"
  } > "$body_file"

  if [[ "${NANOAI_DRY_RUN:-}" == "1" ]]; then
    echo "NanoAI dry run enabled. Review body:"
    echo
    cat "$body_file"
    return 0
  fi

  if [[ -z "$gitlab_token" ]]; then
    echo "NanoAI review generated but not posted because GITLAB_TOKEN or NANOAI_GITLAB_TOKEN is not configured." >&2
    return 0
  fi

  curl --fail --request POST \
    --header "PRIVATE-TOKEN: ${gitlab_token}" \
    --data-urlencode "body@${body_file}" \
    "${api_url}/projects/${project_id}/merge_requests/${merge_request_iid}/notes"
}

if [[ "${CI_PIPELINE_SOURCE:-}" != "merge_request_event" ]]; then
  echo "NanoAI review skipped because this is not a GitLab merge request pipeline." > "$output_file"
  exit 0
fi

create_merge_request_diff

if [[ ! -s "$raw_diff_file" ]]; then
  echo "NanoAI review skipped because the diff is empty." > "$output_file"
  exit 0
fi

prepare_review_diff
run_nanoai_review
post_merge_request_review
