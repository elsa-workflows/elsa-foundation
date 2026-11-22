#!/usr/bin/env bash
# Gate 2, seventh review: NT-2 (R7-B1) and MX-1 (R7-M1) under every rule set.
# Run from anywhere; it works in prototypes/kernel-model. Each run prints its report and its exit code
# (0 = every assertion held, 1 = an assertion failed). The script exits with --all's result: 0 only when every run
# matches the expected matrix.
# Setup failures stop the script; a case that fails its assertions (exit 1) does not.
set -u
cd "$(dirname "$0")/../.." || { echo "cannot enter prototypes/kernel-model" >&2; exit 2; }
dotnet restore experiments/Review7.Blockers --locked-mode || { echo "locked restore failed" >&2; exit 2; }
dotnet build experiments/Review7.Blockers -c Release --no-restore || { echo "Release build failed" >&2; exit 2; }
BIN=experiments/Review7.Blockers/bin/Release/net10.0/Kernel.Review7.Blockers.dll

run() { dotnet "$BIN" --case "$1" --rules "$2" < /dev/null; echo "exit $? ($1, $2)"; echo; }

# Every case and rule set, read from the harness's own matrix (--list), so the individual pass and --all run the same
# rows. The README maps each run to its page case. A failing case does not stop the script.
list=$(dotnet "$BIN" --list) || { echo "--list failed" >&2; exit 2; }
while read -r id _ rules _ expected; do
  echo "# $id --rules $rules, expected $expected"
  run "$id" "$rules"
done <<< "$list"

# The whole matrix against its expected outcomes.
dotnet "$BIN" --all | sed -n '/^== summary ==/,$p'
matrix=${PIPESTATUS[0]}
echo "matrix exit $matrix"
exit "$matrix"
