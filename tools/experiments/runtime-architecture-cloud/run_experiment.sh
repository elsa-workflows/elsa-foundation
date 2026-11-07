#!/usr/bin/env bash
# Invoke only on an allocated executor supported by runner.py. No local fallback.
set -euo pipefail

if [[ $# -ne 1 ]]; then
  printf 'Usage: %s ABSOLUTE_OWNED_EXECUTION_ROOT\n' "$0" >&2
  exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
runner=(python3 "$script_dir/runner.py" --root "$1")

stage() {
  printf 'Experiment stage:'
  printf ' %q' "$@"
  printf '\n'
  "${runner[@]}" "$@"
}

stage materialize
python3 -m unittest discover -s "$script_dir/helpers" -p 'test_*.py' -v 2>&1 | tee "$1/evidence/linux-ownership-controls.log"
stage prepare-postgres
stage build --variant baseline
stage sync-lock
stage focused --variant runview
stage focused --variant snapshot
stage provider-build
stage build --variant runview
stage build --variant snapshot
stage provider-qualify

for mode in Balanced Durable; do
  for variant in baseline runview snapshot; do
    for proof in sequence suspension crash-before-commit crash-after-commit crash-after-body; do
      stage proof --provider PostgreSql --variant "$variant" --mode "$mode" --leaves 100 --case "$proof"
    done
    for proof in suspension late-output waited-child protected compensation-control compensation-kill; do
      stage proof --provider Sqlite --variant "$variant" --mode "$mode" --leaves 10 --case "$proof"
    done
  done

  # Emit the small serial cell first, then expand. Counts and timings stay separate.
  for leaves in 10 1 100; do
    stage measure --profile count --mode "$mode" --leaves "$leaves" --concurrency 1
    for concurrency in 1 4; do
      stage measure --profile timing --mode "$mode" --leaves "$leaves" --concurrency "$concurrency" --samples 20
    done
  done
  stage measure --profile count --mode "$mode" --leaves 100 --action resume --concurrency 1
  stage measure --profile timing --mode "$mode" --leaves 100 --action resume --concurrency 1
done
