#!/usr/bin/env bash
# Gate 2, fifth review: RT-2 (R5-B1, R5-m3), RT-3 (R5-B2) and RT-4 (R5-M5) under every rule set.
# Run from anywhere; it works in prototypes/kernel-model. Each run prints its report and its exit code
# (0 = every assertion held, 1 = an assertion failed). The script exits with --all's result: 0 only when every run
# matches the expected matrix.
# Setup failures stop the script; a case that fails its assertions (exit 1) does not.
set -u
cd "$(dirname "$0")/../.." || { echo "cannot enter prototypes/kernel-model" >&2; exit 2; }
dotnet restore experiments/Review5.Termination --locked-mode || { echo "locked restore failed" >&2; exit 2; }
dotnet build experiments/Review5.Termination -c Release --no-restore || { echo "Release build failed" >&2; exit 2; }
BIN=experiments/Review5.Termination/bin/Release/net10.0/Kernel.Review5.Termination.dll

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
