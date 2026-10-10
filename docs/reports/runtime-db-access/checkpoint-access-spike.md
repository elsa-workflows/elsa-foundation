# Current-EF checkpoint access investigation

Program [#2382](https://github.com/elsa-workflows/elsa-foundation/issues/2382), adopted spike [#1239](https://github.com/elsa-workflows/elsa-foundation/issues/1239). Investigated source: `a1e837de0563f44c9df465442077ead13fefe927`, 7 October 2026.

## Decision

Retain the current checkpoint path for this program. The current EF adapter already stages participant changes in one context and transaction, with batched identity reads and a shared participant flush. The tested shortcut, combining that flush with the marker flush, saved no SQLite EF commands and violated the existing marker-last contract. Production source was restored exactly. This is a bounded no-change disposition, not a claim that every remaining access is optimal or that PostgreSQL could never batch the writes differently.

No checkpoint implementation is selected for #2404/#2405. Once this disposition is reviewed and delivered, those contingent tasks can close as not planned; they must not report an implementation or a performance gain. Scheduler/outbox investigation #2407 and final candidate accounting #2413 remain separate work.

## What the accesses do

The source mechanism is in [EfRuntimeCheckpointCommitStore](../../../src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointCommitStore.cs). The accepted [checkpoint/queue account](checkpoint-queue-accounting.md) supplies the measured historical command observations below. Source behavior and measured attribution are deliberately separate.

| Phase | Source-confirmed mechanism | Captured Coalesced evidence and limits |
|---|---|---|
| Validation | Validate scope, reserved ownership keys, storage limits and supported participants before marker I/O. | An in-memory validation is not a database command. |
| Marker lookup | Read the scoped commit marker; matching replay returns before lease acquisition or transaction creation. | Four marker lookups joined to four SELECT commands, all reporting zero rows returned. Failure/replay outcomes were not measured in that packet. |
| Root-write lease | A workflow-execution participant holds the executable-root write lease around the transaction callback. Closure loading, acquire and release have their own reads and conditional writes. | Four outer commit spans joined to 28 commands: 20 SELECT and eight UPDATE. This is compatible with that source path, but individual lease, renewal and closure-page attribution is unresolved. |
| Transaction and fence | Start one transaction and stage the execution fence first, checking current ownership before participant writes. | Transaction API calls are source-confirmed; the observer does not count their physical protocol exchanges. |
| Participant staging | Point-read singleton workflow/scheduler rows; batch-load collection identities by participant type, using bounded chunks where needed. Preserve pending sibling mutations in the shared context. | Twenty-two stage spans joined to 26 commands. Input participant counts are not affected-row counts. Different tables and validation needs account for distinct reads. |
| Participant and marker flush | Flush staged participants, add the immutable marker, flush the marker, then commit the same transaction. | Four participant flushes joined to four commands and four marker flushes to four commands. A command can contain multiple SQL statements. |
| Reconciliation | Roll back conflicts/cancellation/failures; reconcile the immutable marker after unique conflicts or ambiguous failures. | These are required source paths, not observed successful/failure distributions in the packet. |

The checkpoint subtotal is **66 EF command executions across 38 observed operations**. The complete Coalesced packet has 420 commands: 277 HTTP, 101 REST, and 42 background-sweep commands. Neither subtotal is a physical round-trip count. SQL statement count, transaction begin/commit exchanges and network round trips remain unmeasured.

The packet used an instrumented `b3f55bef32576011d39b93d664949a0cb705fb3b` candidate. Its checkpoint store overlay has a different hash because it adds diagnostic scopes. The clean Git checkpoint-store file at both `b3f55bef3` and the investigated `a1e837de0` hashes to `5632009e015024d85d561959951a68aaaea946c92c2807b1c3b313c78d21e8e9`. That proves this file's source continuity; it does not turn the older instrumented whole-host run into an exact-current measurement.

## Bounded candidate experiment

The existing `EfRuntimeCheckpointCommitStoreTests` SQLite fixture and command interceptor were sufficient. A temporary [test-local probe](evidence/checkpoint-access-probe-2026-10-07.patch) bracketed a new activity-execution/bookmark commit and its replay separately. Assertion readbacks were excluded from the count. This small commit has no workflow-execution participant, root-write lease or execution fence; its six-command count must not be substituted for the normal HTTP workload or a fully fenced checkpoint.

The only production trial removed the first `SaveChangesAsync`, added the marker to the staged set, and retained the second save and transaction commit. Validation, fencing, leases, replay/reconciliation and existing test assertions were unchanged. The disposable [trial patch](evidence/checkpoint-single-save-trial-2026-10-07.patch) is evidence of the rejected experiment, not an implementation recommendation.

| Run | First new commit | Replay | Correctness result |
|---|---|---|---|
| Unchanged source plus probe | Six SQLite EF commands | One marker SELECT | 45/45 checkpoint-store tests passed; zero skipped. |
| Single-save trial | Six SQLite EF commands | One marker SELECT | 43/45 passed; two existing marker-order tests failed; zero skipped. |
| Restored production source | Six SQLite EF commands | One marker SELECT | Probe and marker-order guard passed, 2/2; zero skipped. This is a focused restoration check, not another full-class run. |

For the activity/bookmark probe, both baseline and trial emitted `SELECT marker → SELECT activity execution → SELECT bookmark → INSERT activity execution → INSERT bookmark → INSERT marker`. Both persisted one marker, one activity execution and one bookmark; replay returned no pending work. No expected-saving assertion was introduced.

The broader unchanged fixture exposed why the shortcut is unacceptable: with a pre-staged scheduler sibling, EF ordered the marker before the scheduler write. `Marker_commit_flushes_a_pre_staged_sibling_in_the_same_transaction` failed its marker-last assertion. `Marker_failure_rolls_back_a_pre_staged_sibling_and_leaves_marker_reusable` also observed the marker insert before the failing scheduler insert. That test stopped at its ordering assertion, so its later rollback-state and retry assertions did not run. These failures demonstrate ordering-contract violations; they are not evidence of committed partial state or data loss. No test was relaxed to accept the trial.

Root independently parsed the three TRX files, checked the probe output and failures, and verified restoration against the source hash above. The [sanitized evidence record](evidence/checkpoint-access-spike-2026-10-07.json) retains source/artifact hashes, exact counts, failure names and measurement limits. Raw logs, TRX files and the pre-trial source backup remain in the control room's private evidence packet. The temporary probe is retained as a reproducible patch rather than added to the permanent test suite. Both patches use zero-context unified format (`git apply --unidiff-zero`) in a disposable worktree at the pinned source commit.

## Why the other proposed removals are not selected

- **Marker pre-read:** it recognizes replay before staging or acquiring the root lease. Unique/ambiguous commit reconciliation still needs the marker. Removing or relocating it changes the replay/error contract; this workload does not establish a safe benefit.
- **Lease and fence:** these enforce ownership while writing an executable-backed workflow. [#2286](https://github.com/elsa-workflows/elsa-foundation/issues/2286) separately owns the known per-attempt root-lease identity defect. This spike does not duplicate that fix, hoist the lease or weaken fencing.
- **Participant identity reads:** current EF already batches collections. They support insert/update choice, identity/corruption validation and revision checks. Replacing them with blind writes would need separately demonstrated equivalence and provider proof.
- **Consumed scheduler work:** its per-item read plus guarded delete is source-visible, but this captured checkpoint workload supplied zero such items. It is not a workload-justified checkpoint candidate here; #2407 owns scheduler/outbox selection.
- **Legacy #1305/#1308 directions:** Groundwork's former connection gate and per-document save path are obsolete. They do not justify changing current EF's shared-context batching or current root-write lease contract.

Combining saves remains rejected even if another provider could save a command, because the unchanged ordering contract already fails. No PostgreSQL trial or wire-level experiment was needed to establish that rejection. No database-access or latency improvement is claimed by this spike. Final HTTP/REST measurement must continue to report the actual integrated candidate and its remaining costs.
