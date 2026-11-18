#!/usr/bin/env bash
# Gate 2, fifth review: EV-1 (R5-M2), FR-2 (R5-M3), JR-1 (R5-M4) and PO-1 (R5-M6) under every rule set.
# Run from anywhere; it works in prototypes/kernel-model. Each run prints its report and its exit code
# (0 = every assertion held, 1 = an assertion failed, 3 = the run stopped on an unstated rule). The script exits with
# --all's result: 0 only when every run matches the expected matrix.
# Setup failures stop the script; a case that fails its assertions or stops does not.
set -u
cd "$(dirname "$0")/../.." || { echo "cannot enter prototypes/kernel-model" >&2; exit 2; }
dotnet restore experiments/Review5.Delivery --locked-mode || { echo "locked restore failed" >&2; exit 2; }
dotnet build experiments/Review5.Delivery -c Release --no-restore || { echo "Release build failed" >&2; exit 2; }
BIN=experiments/Review5.Delivery/bin/Release/net10.0/Kernel.Review5.Delivery.dll

run() { dotnet "$BIN" --case "$1" --rules "$2"; echo "exit $? ($1, $2)"; echo; }

# Every case and rule set (the README maps each run to its page case). A failing or stopped case does not stop the script.
run ev1-a                       reviewed                    # expected FAIL
run ev1-a                       repaired                    # expected PASS
run ev1-a                       negative                    # expected FAIL
run ev1-a-band                  reviewed                    # expected PASS
run ev1-a-band                  repaired                    # expected PASS
run ev1-a-band                  negative                    # expected PASS
run ev1-a-after                 reviewed                    # expected PASS
run ev1-a-after                 repaired                    # expected PASS
run ev1-a-after                 negative                    # expected PASS
run ev1-b                       reviewed                    # expected FAIL
run ev1-b                       repaired                    # expected PASS
run ev1-b                       negative                    # expected FAIL
run ev1-b                       match-at-now                # expected FAIL
run ev1-b-paged                 reviewed                    # expected FAIL
run ev1-b-paged                 repaired                    # expected PASS
run ev1-b-paged                 negative                    # expected FAIL
run ev1-b-paged                 match-at-now                # expected FAIL
run ev1-b-paged                 owed-from-at-commit         # expected FAIL
run ev1-b-band                  reviewed                    # expected FAIL
run ev1-b-band                  repaired                    # expected STOPPED
run ev1-b-band                  negative                    # expected FAIL
run ev1-replay                  reviewed                    # expected FAIL
run ev1-replay                  repaired                    # expected PASS
run ev1-replay                  negative                    # expected FAIL
run ev1-replay                  fresh-event-id              # expected FAIL
run ev1-replay-consumed         reviewed                    # expected FAIL
run ev1-replay-consumed         repaired                    # expected STOPPED
run ev1-replay-consumed         negative                    # expected FAIL
run fr2-base                    reviewed                    # expected FAIL
run fr2-base                    repaired                    # expected PASS
run fr2-base                    negative                    # expected FAIL
run fr2-hold                    reviewed                    # expected FAIL
run fr2-hold                    repaired                    # expected PASS
run fr2-hold                    negative                    # expected FAIL
run fr2-suspend                 reviewed                    # expected FAIL
run fr2-suspend                 repaired                    # expected PASS
run fr2-suspend                 negative                    # expected FAIL
run fr2-takeover                reviewed                    # expected FAIL
run fr2-takeover                repaired                    # expected PASS
run fr2-takeover                negative                    # expected FAIL
run fr2-hold-takeover           reviewed                    # expected FAIL
run fr2-hold-takeover           repaired                    # expected PASS
run fr2-hold-takeover           negative                    # expected FAIL
run fr2-suspend                 pages-while-suspended       # expected FAIL
run fr2-hold                    held-gate-waits             # expected FAIL
run jr1-crash-after             reviewed                    # expected FAIL
run jr1-crash-after             reviewed-kind               # expected FAIL
run jr1-crash-after             repaired                    # expected PASS
run jr1-crash-after             negative                    # expected FAIL
run jr1-crash-after-keep        reviewed                    # expected FAIL
run jr1-crash-after-keep        reviewed-kind               # expected FAIL
run jr1-crash-after-keep        repaired                    # expected PASS
run jr1-crash-after-keep        negative                    # expected FAIL
run jr1-crash-before            reviewed                    # expected PASS
run jr1-crash-before            reviewed-kind               # expected PASS
run jr1-crash-before            repaired                    # expected PASS
run jr1-crash-before            negative                    # expected PASS
run jr1-running                 reviewed                    # expected PASS
run jr1-running                 reviewed-kind               # expected PASS
run jr1-running                 repaired                    # expected PASS
run jr1-running                 negative                    # expected PASS
run po1-cohort                  reviewed                    # expected FAIL
run po1-cohort                  repaired                    # expected PASS
run po1-cohort                  negative                    # expected FAIL
run po1-open                    reviewed                    # expected FAIL
run po1-open                    repaired                    # expected PASS
run po1-open                    negative                    # expected FAIL
run po1-ordinary                reviewed                    # expected PASS
run po1-ordinary                repaired                    # expected PASS
run po1-ordinary                negative                    # expected PASS

# The whole matrix against its expected outcomes.
dotnet "$BIN" --all | sed -n '/^== summary ==/,$p'
matrix=${PIPESTATUS[0]}
echo "matrix exit $matrix"
exit "$matrix"
