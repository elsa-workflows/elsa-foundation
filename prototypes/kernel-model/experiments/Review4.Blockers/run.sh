#!/usr/bin/env bash
# Gate 2, fourth review: FR-1 (R4-B1) and RT-1 (R4-B2) under every rule set.
# Run from anywhere; it works in prototypes/kernel-model. Each run prints its report and its exit code
# (0 = every assertion held, 1 = an assertion failed). --all exits 0 only when every run matches the expected matrix.
set -u
cd "$(dirname "$0")/../.."
dotnet restore experiments/Review4.Blockers --locked-mode
dotnet build experiments/Review4.Blockers -c Release --no-restore
BIN=experiments/Review4.Blockers/bin/Release/net10.0/Kernel.Review4.Blockers.dll

run() { dotnet "$BIN" --case "$1" --rules "$2"; echo "exit $? ($1, $2)"; echo; }

# FR-1 (R4-B1): the released timer R for frame F, the drain control C, the late native.cancel A.
for c in fr1-base fr1-takeover-before-complete fr1-takeover-after-complete; do
  run "$c" reviewed
  run "$c" repaired
  run "$c" negative
done
# FR-1 held variant: the model holds the cohort row; A is the input it waits for.
run fr1-held reviewed
run fr1-held repaired
run fr1-held negative
run fr1-held held-blocks

# RT-1 (R4-B2): P -> A -> G -> G2 with job J on A; A in an unprotected parked-delete drain.
for c in rt1-a rt1-b; do
  run "$c" reviewed
  run "$c" repaired
  run "$c" negative
done
run rt1-nonparent reviewed
run rt1-nonparent repaired
run rt1-nonparent negative
run rt1-nonparent cascade-any
run rt1-successor reviewed
run rt1-successor repaired
run rt1-successor negative
# Open: P's delayed terminate after a successor creation of A's id (see README, "Open point").
run rt1-successor-gap reviewed
run rt1-successor-gap repaired
run rt1-successor-gap negative

# The whole matrix against its expected outcomes.
dotnet "$BIN" --all | sed -n '/^== summary ==/,$p'
echo "matrix exit ${PIPESTATUS[0]}"
