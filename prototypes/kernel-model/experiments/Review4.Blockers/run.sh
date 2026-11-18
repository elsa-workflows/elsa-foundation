#!/usr/bin/env bash
# Gate 2, fourth review: FR-1 (R4-B1) and RT-1 (R4-B2) under every rule set.
# Run from anywhere; it works in prototypes/kernel-model. Each run prints its report and its exit code
# (0 = every assertion held, 1 = an assertion failed). The script exits with --all's result: 0 only when every run
# matches the expected matrix.
# Setup failures stop the script; a case that fails its assertions (exit 1) does not.
set -u
cd "$(dirname "$0")/../.." || { echo "cannot enter prototypes/kernel-model" >&2; exit 2; }
dotnet restore experiments/Review4.Blockers --locked-mode || { echo "locked restore failed" >&2; exit 2; }
dotnet build experiments/Review4.Blockers -c Release --no-restore || { echo "Release build failed" >&2; exit 2; }
BIN=experiments/Review4.Blockers/bin/Release/net10.0/Kernel.Review4.Blockers.dll

run() { dotnet "$BIN" --case "$1" --rules "$2"; echo "exit $? ($1, $2)"; echo; }

# Every case and rule set (the README maps each run to its page case). A failing case does not stop the script.
run fr1-base                       reviewed                  # expected FAIL
run fr1-base                       repaired                  # expected PASS
run fr1-base                       negative                  # expected FAIL
run fr1-takeover-before-complete   reviewed                  # expected FAIL
run fr1-takeover-before-complete   repaired                  # expected PASS
run fr1-takeover-before-complete   negative                  # expected FAIL
run fr1-takeover-after-complete    reviewed                  # expected FAIL
run fr1-takeover-after-complete    repaired                  # expected PASS
run fr1-takeover-after-complete    negative                  # expected FAIL
run fr1-takeover-before-complete   memory-precedence         # expected FAIL
run fr1-takeover-after-complete    memory-precedence         # expected FAIL
run fr1-trial-takeover             reviewed                  # expected FAIL
run fr1-trial-takeover             repaired                  # expected PASS
run fr1-trial-takeover             negative                  # expected FAIL
run fr1-trial-takeover             trial-plain-seq           # expected FAIL
run fr1-trial-takeover             skip-target               # expected PASS
run fr1-trial-takeover             no-cohort-mark            # expected PASS
run fr1-poll-limit                 reviewed                  # expected FAIL
run fr1-poll-limit                 repaired                  # expected PASS
run fr1-poll-limit                 negative                  # expected FAIL
run fr1-poll-limit                 trial-plain-seq           # expected FAIL
run fr1-poll-limit                 skip-target               # expected PASS
run fr1-poll-limit                 no-cohort-mark            # expected FAIL
run fr1-poll-limit                 no-shelf                  # expected FAIL
run fr1-trial-takeover-open        reviewed                  # expected FAIL
run fr1-trial-takeover-open        repaired                  # expected PASS
run fr1-trial-takeover-open        negative                  # expected PASS
run fr1-trial-takeover-open        trial-plain-seq           # expected PASS
run fr1-trial-takeover-open        skip-target               # expected FAIL
run fr1-trial-takeover-open        no-cohort-mark            # expected PASS
run fr1-held-poll-limit            reviewed                  # expected FAIL
run fr1-held-poll-limit            repaired                  # expected PASS
run fr1-held-poll-limit            negative                  # expected FAIL
run fr1-held-poll-limit            one-row-read-counts-held  # expected FAIL
run fr1-control-after-complete     reviewed                  # expected FAIL
run fr1-control-after-complete     repaired                  # expected PASS
run fr1-control-after-complete     negative                  # expected FAIL
run fr1-control-after-complete     mark-ignores-controls     # expected FAIL
run fr1-control-crash              reviewed                  # expected FAIL
run fr1-control-crash              repaired                  # expected PASS
run fr1-control-crash              negative                  # expected FAIL
run fr1-control-crash              boundary-holds-cohort     # expected FAIL
run fr1-ticket-control             reviewed                  # expected PASS
run fr1-ticket-control             repaired                  # expected PASS
run fr1-ticket-control             negative                  # expected PASS
run fr1-ticket-control             controls-wait-on-ticket   # expected FAIL
run fr1-ticket-control-parent      reviewed                  # expected PASS
run fr1-ticket-control-parent      repaired                  # expected PASS
run fr1-ticket-control-parent      negative                  # expected PASS
run fr1-ticket-control-parent      controls-wait-on-ticket   # expected FAIL
run fr1-ticket-control-suspend     reviewed                  # expected PASS
run fr1-ticket-control-suspend     repaired                  # expected PASS
run fr1-ticket-control-suspend     negative                  # expected PASS
run fr1-ticket-control-suspend     controls-wait-on-ticket   # expected FAIL
run fr1-model-end                  reviewed                  # expected PASS
run fr1-model-end                  repaired                  # expected PASS
run fr1-model-end                  negative                  # expected PASS
run fr1-model-end-recall           reviewed                  # expected PASS
run fr1-model-end-recall           repaired                  # expected PASS
run fr1-model-end-recall           negative                  # expected PASS
run fr1-held                       reviewed                  # expected PASS
run fr1-held                       repaired                  # expected PASS
run fr1-held                       negative                  # expected PASS
run fr1-held                       held-blocks               # expected FAIL
run rt1-a                          reviewed                  # expected FAIL
run rt1-a                          repaired                  # expected PASS
run rt1-a                          negative                  # expected FAIL
run rt1-a-g-deleted                reviewed                  # expected FAIL
run rt1-a-g-deleted                repaired                  # expected PASS
run rt1-a-g-deleted                negative                  # expected FAIL
run rt1-b                          reviewed                  # expected PASS
run rt1-b                          repaired                  # expected PASS
run rt1-b                          negative                  # expected PASS
run rt1-nonparent                  reviewed                  # expected FAIL
run rt1-nonparent                  repaired                  # expected PASS
run rt1-nonparent                  negative                  # expected FAIL
run rt1-nonparent                  cascade-any               # expected FAIL
run rt1-successor                  reviewed                  # expected PASS
run rt1-successor                  repaired                  # expected PASS
run rt1-successor                  negative                  # expected PASS
run rt1-successor                  successor-inherits        # expected FAIL
run rt1-successor                  root-retains              # expected FAIL
run rt1-successor-unreachable      reviewed                  # expected FAIL
run rt1-successor-unreachable      repaired                  # expected PASS
run rt1-successor-unreachable      negative                  # expected FAIL
run rt1-successor-gap              reviewed                  # expected FAIL
run rt1-successor-gap              repaired                  # expected PASS
run rt1-successor-gap              negative                  # expected FAIL
run rt1-successor-gap              successor-admitted        # expected FAIL

# The whole matrix against its expected outcomes.
dotnet "$BIN" --all | sed -n '/^== summary ==/,$p'
matrix=${PIPESTATUS[0]}
echo "matrix exit $matrix"
exit "$matrix"
