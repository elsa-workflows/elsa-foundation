#!/usr/bin/env bash
# The publisher supplies an immutable, already verified index and its product source commit.
set -euo pipefail
[[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ && "$SOURCE_SHA" =~ ^[0-9a-f]{40}$ ]]
current=$(gh api "repos/$GITHUB_REPOSITORY/git/ref/heads/main" --jq '.object.sha')
if [[ "$current" != "$SOURCE_SHA" ]]; then
  echo "Versioned image $IMAGE@$DIGEST is verified. Main advanced before latest promotion." >> "$GITHUB_STEP_SUMMARY"
  exit 0
fi
# A newer preview may stage the same product commit while an older failed job waits for a retry.
staged=$(docker buildx imagetools inspect --format '{{json .Manifest}}' "$IMAGE:sha-${SOURCE_SHA:0:7}" | jq -er '.digest')
[[ "$staged" =~ ^sha256:[0-9a-f]{64}$ ]]
if [[ "$staged" != "$DIGEST" ]]; then
  echo "A different index now holds the source tag for $SOURCE_SHA. Skipped latest promotion for $DIGEST." >> "$GITHUB_STEP_SUMMARY"
  exit 0
fi
docker buildx imagetools create --tag "$IMAGE:latest" "$IMAGE@$DIGEST"
observed=$(docker buildx imagetools inspect --format '{{json .Manifest}}' "$IMAGE:latest" | jq -er '.digest')
[[ "$observed" == "$DIGEST" ]]
echo "Verified $IMAGE:latest at $DIGEST for $SOURCE_SHA." >> "$GITHUB_STEP_SUMMARY"
