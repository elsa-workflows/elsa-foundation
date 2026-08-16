#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
exec python3 "$repo_root/tools/groundwork/generate-e3-baseline.py" \
  --check \
  --artifact "$repo_root/docs/reports/groundwork-e3-baseline.json"
