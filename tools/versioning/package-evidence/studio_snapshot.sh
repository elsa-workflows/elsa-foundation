#!/usr/bin/env bash
# Find a snapshot within this run. Keep a deleted or expired publication snapshot
# distinguishable from an attempt that failed before signing.
set -euo pipefail
id=
if [[ "$GITHUB_RUN_ATTEMPT" != 1 ]]; then
  run="repos/$GITHUB_REPOSITORY/actions/runs/$GITHUB_RUN_ID"
  response=$(gh api "$run/artifacts?name=studio-signed-release")
  id=$(jq -er 'if .total_count == 0 then "" elif .total_count == 1 and (.artifacts[0].expired | not) then .artifacts[0].id else error("Signed snapshot unavailable") end' <<< "$response")
  if [[ -z "$id" ]]; then
    signed=$(gh api --paginate "$run/jobs?filter=all&per_page=100" --jq '.jobs[] | select(.name == "Select and attest the Studio release snapshot" and .conclusion == "success") | .id')
    if [[ -n "$signed" ]]; then
      echo "The prior signing job succeeded, but this run's original signed snapshot is unavailable. Publication recovery requires the original bytes." >&2
      exit 1
    fi
  fi
fi
echo "id=$id" >> "$GITHUB_OUTPUT"
