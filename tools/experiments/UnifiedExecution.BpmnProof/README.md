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

The upstream interpreter owns BPMN routing, join accounting, interrupting event semantics, late-handle absorption, graph/capability validation, and caught/propagated fault disposition. The base fork/join witness host owns persistence, handles, its simulated clock, callbacks, and command dispatch; that scenario stores work in local SQLite rows and does not prove broker-backed distributed execution or exactly-once external effects. The later ETL service-task extension below runs local worker processes against the shared WorkProof policy, but still has no broker-backed worker fleet or atomic transaction across the BPMN and ETL stores.

The service-task extension invokes the shared `UnifiedExecution.WorkProof` `PartitionPipelinePolicy` through an experimental bridge. The BPMN store commits the service handle first. A later process commits one ETL partition and exits at a simulated crash; a fresh process recovers the same service handle, verifies and reuses that partition, creates the remaining partition, and starts the pipeline under a stable handle-derived ID. A second fresh provisioning retry proves that neither source rows nor initial work are duplicated. Batch workers then run in separate processes. The success path also commits ETL completion before creating a BPMN-side result delivery receipt; the interpreter callback and receipt advancement commit atomically in the BPMN store. A process exit after that commit is recovered by restore and duplicate result delivery, which observes the receipt and does not invoke the callback twice. This is idempotent within the local BPMN store; it does not make the preceding ETL-to-BPMN handoff atomic.

The bridge records four adapter paths: success; a completed ETL run whose business validation error is caught by a BPMN error boundary; a transform worker fault that cancels remaining batch work and becomes a propagated BPMN incident; and an interrupting BPMN message that cancels owed/in-flight pipeline work and rejects a late worker receipt. The success and business-error pipelines complete. The shared ETL policy has no failed/cancelled terminal state, so fault/cancel leave the WorkProof composition `Running` with its work cancelled; the evidence reports this boundary explicitly. The ETL and BPMN stores have no atomic cross-system commit.

The current interpreter API has no arbitrary token-alteration command. The existing modeled message operation remains an externally delivered BPMN event whose token transition is decided by interpreter semantics; `move-token` remains explicitly rejected. Thus the proof does not claim runtime migration support.
