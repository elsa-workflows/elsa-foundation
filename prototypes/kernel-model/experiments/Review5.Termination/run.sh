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

run() { dotnet "$BIN" --case "$1" --rules "$2"; echo "exit $? ($1, $2)"; echo; }

# Every case and rule set (the README maps each run to its page case). A failing case does not stop the script.
run rt2-witness              reviewed                     # expected FAIL
run rt2-witness              repaired                     # expected PASS
run rt2-witness              negative                     # expected FAIL
run rt2-pending              reviewed                     # expected PASS
run rt2-pending              repaired                     # expected PASS
run rt2-pending              negative                     # expected PASS
run rt2-done-kept            reviewed                     # expected FAIL
run rt2-done-kept            repaired                     # expected PASS
run rt2-done-kept            negative                     # expected FAIL
run rt2-absent               reviewed                     # expected FAIL
run rt2-absent               repaired                     # expected PASS
run rt2-absent               negative                     # expected FAIL
run rt2-absent               presence-from-memory         # expected PASS
run rt2-mixed                reviewed                     # expected FAIL
run rt2-mixed                repaired                     # expected PASS
run rt2-mixed                negative                     # expected FAIL
run rt2-settle-after         reviewed                     # expected PASS
run rt2-settle-after         repaired                     # expected PASS
run rt2-settle-after         negative                     # expected PASS
run rt2-duplicate            reviewed                     # expected FAIL
run rt2-duplicate            repaired                     # expected PASS
run rt2-duplicate            negative                     # expected FAIL
run rt2-takeover             reviewed                     # expected FAIL
run rt2-takeover             repaired                     # expected PASS
run rt2-takeover             negative                     # expected FAIL
run rt2-takeover             presence-from-memory         # expected FAIL
run rt2-open-drain-mixed     reviewed                     # expected FAIL
run rt2-open-drain-mixed     repaired                     # expected PASS
run rt2-open-drain-mixed     negative                     # expected FAIL
run rt2-live-pending         reviewed                     # expected PASS
run rt2-live-pending         repaired                     # expected PASS
run rt2-live-pending         negative                     # expected PASS
run rt2-generations          reviewed                     # expected PASS
run rt2-generations          repaired                     # expected PASS
run rt2-generations          negative                     # expected PASS
run rt2-generations          foreign-derivation           # expected FAIL
run rt2-generations          generation-blind             # expected FAIL
run rt2-off-registry         reviewed                     # expected PASS
run rt2-off-registry         repaired                     # expected PASS
run rt2-off-registry         negative                     # expected PASS
run rt2-off-registry         accept-off-registry          # expected FAIL
run rt3-live-p               reviewed                     # expected PASS
run rt3-live-p               repaired                     # expected PASS
run rt3-live-p               negative                     # expected PASS
run rt3-live-p-plus-1        reviewed                     # expected PASS
run rt3-live-p-plus-1        repaired                     # expected PASS
run rt3-live-p-plus-1        negative                     # expected PASS
run rt3-live-10001           reviewed                     # expected FAIL
run rt3-live-10001           repaired                     # expected PASS
run rt3-live-10001           negative                     # expected PASS
run rt3-live-10001           page-above-cap               # expected FAIL
run rt3-marker-10001         reviewed                     # expected FAIL
run rt3-marker-10001         repaired                     # expected PASS
run rt3-marker-10001         negative                     # expected PASS
run rt3-marker-10001         page-above-cap               # expected FAIL
run rt3-open-drain-10001     reviewed                     # expected FAIL
run rt3-open-drain-10001     repaired                     # expected PASS
run rt3-open-drain-10001     negative                     # expected PASS
run rt3-jobs-marker          reviewed                     # expected FAIL
run rt3-jobs-marker          repaired                     # expected PASS
run rt3-jobs-marker          negative                     # expected PASS
run rt3-takeover             reviewed                     # expected FAIL
run rt3-takeover             repaired                     # expected PASS
run rt3-takeover             negative                     # expected FAIL
run rt3-takeover             no-next-control              # expected FAIL
run rt3-crash-landed         reviewed                     # expected FAIL
run rt3-crash-landed         repaired                     # expected PASS
run rt3-crash-landed         negative                     # expected FAIL
run rt3-crash-lost           reviewed                     # expected FAIL
run rt3-crash-lost           repaired                     # expected PASS
run rt3-crash-lost           negative                     # expected FAIL
run rt3-delete-discharging   reviewed                     # expected FAIL
run rt3-delete-discharging   repaired                     # expected PASS
run rt3-delete-discharging   negative                     # expected PASS
run rt3-delete-discharging   delete-drops-discharging     # expected FAIL
run rt4-running-unrelated    reviewed                     # expected FAIL
run rt4-running-unrelated    repaired                     # expected PASS
run rt4-running-unrelated    negative                     # expected FAIL
run rt4-running-unrelated    binding-running-only         # expected PASS
run rt4-ended-registry       reviewed                     # expected FAIL
run rt4-ended-registry       repaired                     # expected PASS
run rt4-ended-registry       negative                     # expected FAIL
run rt4-ended-registry       binding-running-only         # expected FAIL
run rt4-genuine-child        reviewed                     # expected PASS
run rt4-genuine-child        repaired                     # expected PASS
run rt4-genuine-child        negative                     # expected PASS
run rt4-operator             reviewed                     # expected PASS
run rt4-operator             repaired                     # expected PASS
run rt4-operator             negative                     # expected PASS
run rt4-model-send           reviewed                     # expected PASS
run rt4-model-send           repaired                     # expected PASS
run rt4-model-send           negative                     # expected PASS
run rt4-model-send           bind-every-issued-terminate  # expected FAIL
run rt4-forgery              reviewed                     # expected FAIL
run rt4-forgery              repaired                     # expected PASS
run rt4-forgery              negative                     # expected PASS
run rt4-forgery              commit-accepts-mark          # expected FAIL

# The whole matrix against its expected outcomes.
dotnet "$BIN" --all | sed -n '/^== summary ==/,$p'
matrix=${PIPESTATUS[0]}
echo "matrix exit $matrix"
exit "$matrix"
