#!/usr/bin/env bash
# Release jobs run on Linux x64. Keep this verifier independent of mutable installer scripts.
set -euo pipefail
dest=$(mktemp -d "$RUNNER_TEMP/elsa-oras.XXXXXX")
archive="$dest/oras.tar.gz"
curl --fail --location --retry 3 --output "$archive" \
  https://github.com/oras-project/oras/releases/download/v1.4.0/oras_1.4.0_linux_amd64.tar.gz
echo "d8e1dce2fd8ce996f1d901845b688eb0cf505a6082897606f053825bb47935d1  $archive" | sha256sum --check --status
tar -xzf "$archive" -C "$dest" oras
echo "$dest" >> "$GITHUB_PATH"
