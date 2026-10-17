# Unified execution: BPMN operational proof

This is an isolated, experimental operational witness for the Elsa unified-execution investigation. It uses the real BPMN interpreter from a pinned upstream source checkout. It does not modify Foundation runtime behavior and is **not a current Elsa integration**.

## Source pin and reproducibility

The witness fetches `valence-works/bpmn` at commit `be9c6eaa963b846104d5aebf38d337bafab08710` into ignored `.source/bpmn/` and references the Model and Semantics projects directly. Bootstrap refuses to overwrite a dirty source checkout, verifies the exact commit and clean staged/unstaged/untracked state, then restores from the committed `packages.lock.json` in locked mode. `verify` repeats the source commit/clean check and writes the commit, tree SHA, package-lock SHA-256, and project SHA-256 to `artifacts/source-integrity.json`.

```bash
./tools/experiments/UnifiedExecution.BpmnProof/bootstrap.sh
dotnet run --project tools/experiments/UnifiedExecution.BpmnProof -- verify
```

The upstream Semantics unit suite was separately verified at this source pin (171/171 tests). That is source-library evidence, separate from this host witness.

## What the witness exercises

The SQLite host checkpoints interpreter state, the work-handle ledger, and owed host commands in a local transaction. It launches fresh processes between fork/join checkpoints, restores owed work, waits at a join, and completes after the other branch resumes. Other scenarios exercise both timer/human winner orders, late completion absorption once, caught versus propagated worker faults, incident policy, changed-definition restore rejection, and foreign-work-handle rejection.

The direct-versus-composed comparison runs the same fork/join BPMN child directly and through a small persisted Sequence-style host adapter. The adapter stores `waiting-child`, the completed child outcome, `next-child-pending`, `waiting-tail`, and parent completion; it starts a second BPMN child after the first completes. The adapter and child each cross fresh-process restart boundaries. The witness compares the normalized operational trace of the first BPMN child between the direct and composed runs. This is a narrow host-side composition experiment, not Elsa's native Sequence implementation or an Elsa integration.

The timer race advances the host's simulated clock. It is timer behavior evidence, not an alteration API. The accepted external operation is narrower: `DeliverModeledMessage` only accepts a live listener handle owned by that scope and a message name declared on that BPMN listener; BPMN semantics then decide interruption and return the cancellation command. Mismatched, stale, or foreign listener handles are rejected. An arbitrary `move-token` alteration is rejected without changing interpreter state. The interpreter exposes no public arbitrary alteration API, so runtime alteration support remains unproven.

## Evidence boundary

The upstream interpreter owns BPMN routing, join accounting, interrupting event semantics, late-handle absorption, graph/capability validation, and caught/propagated fault disposition. This proof host owns persistence, handles, its simulated clock, callbacks, and command dispatch. Work is represented by local SQLite rows: there is no external worker, broker, lease, retry policy, or cross-system transaction. The witness demonstrates recovery across process restarts and trace equivalence for one composed scenario; it does not prove distributed execution or exactly-once external effects.
