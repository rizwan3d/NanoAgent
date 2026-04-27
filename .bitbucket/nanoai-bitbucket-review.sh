#!/usr/bin/env bash
set -euo pipefail

workspace="${BITBUCKET_CLONE_DIR:-$(pwd)}"
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
NanoAI review skipped because the NANOAGENT_API_KEY repository variable is not configured.
EOF
  exit 0
fi

create_pull_request_diff() {
  local pull_request_id="${BITBUCKET_PR_ID:?BITBUCKET_PR_ID is required for pull request reviews.}"
  local target_branch="${BITBUCKET_PR_DESTINATION_BRANCH:?BITBUCKET_PR_DESTINATION_BRANCH is required for pull request reviews.}"
  local head_sha="${BITBUCKET_COMMIT:?BITBUCKET_COMMIT is required for pull request reviews.}"
  local target_ref="refs/remotes/origin/${target_branch}"
  local base_sha="${BITBUCKET_PR_DESTINATION_COMMIT:-}"

  : "$pull_request_id"

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
    printf '\n\n[Diff truncated by NanoAI Bitbucket automation: %s of %s bytes included.]\n' \
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

post_pull_request_review() {
  local pull_request_id="${BITBUCKET_PR_ID:?BITBUCKET_PR_ID is required for pull request reviews.}"
  local workspace_slug="${BITBUCKET_WORKSPACE:?BITBUCKET_WORKSPACE is required for pull request reviews.}"
  local repo_slug="${BITBUCKET_REPO_SLUG:?BITBUCKET_REPO_SLUG is required for pull request reviews.}"
  local access_token="${NANOAI_BITBUCKET_ACCESS_TOKEN:-${BITBUCKET_ACCESS_TOKEN:-}}"
  local username="${BITBUCKET_USERNAME:-}"
  local app_password="${BITBUCKET_APP_PASSWORD:-}"
  local body_file="${artifacts_dir}/bitbucket-review-body.md"
  local body_json_file="${artifacts_dir}/bitbucket-review-body.json"
  local comments_url="https://api.bitbucket.org/2.0/repositories/${workspace_slug}/${repo_slug}/pullrequests/${pull_request_id}/comments"

  {
    echo "## NanoAI pull request review"
    echo
    cat "$output_file"
    echo
    printf '<!-- nanoai-review:%s:%s -->\n' "${BITBUCKET_BUILD_NUMBER:-local}" "${BITBUCKET_STEP_UUID:-0}"
  } > "$body_file"

  if [[ "${NANOAI_DRY_RUN:-}" == "1" ]]; then
    echo "NanoAI dry run enabled. Review body:"
    echo
    cat "$body_file"
    return 0
  fi

  python3 - "$body_file" > "$body_json_file" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as body_file:
    body = body_file.read()

json.dump({"content": {"raw": body}}, sys.stdout)
PY

  if [[ -n "$access_token" ]]; then
    curl --fail --request POST \
      --header "Authorization: Bearer ${access_token}" \
      --header "Accept: application/json" \
      --header "Content-Type: application/json" \
      --data "@${body_json_file}" \
      "$comments_url"
    return 0
  fi

  if [[ -n "$username" && -n "$app_password" ]]; then
    curl --fail --request POST \
      --user "${username}:${app_password}" \
      --header "Accept: application/json" \
      --header "Content-Type: application/json" \
      --data "@${body_json_file}" \
      "$comments_url"
    return 0
  fi

  echo "NanoAI review generated but not posted because BITBUCKET_ACCESS_TOKEN or BITBUCKET_USERNAME/BITBUCKET_APP_PASSWORD is not configured." >&2
}

if [[ -z "${BITBUCKET_PR_ID:-}" ]]; then
  echo "NanoAI review skipped because this is not a Bitbucket pull request pipeline." > "$output_file"
  exit 0
fi

create_pull_request_diff

if [[ ! -s "$raw_diff_file" ]]; then
  echo "NanoAI review skipped because the diff is empty." > "$output_file"
  exit 0
fi

prepare_review_diff
run_nanoai_review
post_pull_request_review
