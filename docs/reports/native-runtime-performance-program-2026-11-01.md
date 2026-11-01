# Runtime Efficiency continuation evidence

Program [#2531](https://github.com/elsa-workflows/elsa-foundation/issues/2531), draft integration [#3601](https://github.com/elsa-workflows/elsa-foundation/pull/3601).
This is an in-progress receipt. It does not establish completion of M0–M5, current-main throughput, or independent-host capacity.
The [baseline assessment](native-runtime-performance-2026-10-31.md) and [first implementation receipt](native-runtime-performance-implementation-2026-10-31.md) retain their original cohorts and exclusions.

## Hydration and preparation

Hydration metadata (#2657) merged in #3598 as `11a07e86686c68bb523334409e66f39deaed068c`, after review and required checks on `f445cea8d370e24c7b9f80a9ce89c4ce6e927e27`.
The first main run was cancelled when main advanced. Containing main `1d77ed131de3305da8b8236f739c63085bff3e73` then passed [CI 41289485400](https://github.com/elsa-workflows/elsa-foundation/actions/runs/41289485400).

Integration head `36444c42792d96e13f9c05a98bd73e9c07ed3ddb` adds demand-driven input reads and weak immutable JavaScript/conversion preparation. Independent early review found fresh-hydration script misses and missing closed-scope read-budget proof; both were addressed and the next three-lens review was clear. Final integrated qualification remains due.

## Owned activity reads: rejected experiment

Three variants tested #2583: private provider-scope rows, sharing private rows under the actual live ownership lease, and priming that lease's bounded read memo from a successfully committed fresh checkpoint. Mutable runtime documents were still reconstructed on each read. Replay and uncertain outcomes never primed the memo. These were experiments and the entire prototype was removed.

| SQLite reference workflow | Current candidate, Durable | Read memo | Commit-primed memo |
|---|---:|---:|---:|
| Ten WriteLines | 336 | 336 | 336 |
| HTTP reference | 204 | 204 | 204 |
| Fork/join with resume | 253 | 252 | 251 |

Balanced/Ephemeral were 90, 104, 180 respectively; only fork/join changed, to 179 and then 178. All ten budget tests passed in each variant. The extra durable-state ownership and invalidation machinery did not justify saving two commands in one fixture. No budget was lowered and no throughput gain is attributed to #2583. The issue's acceptance remains open.

## Cooperative cancellation of a progressing native drain

The default actor composition publishes a mailbox-local signal while a valid Cancel waits behind an active turn. Discrete and replay-safe fused steps notice it before the next step. The orchestrator flushes its folded segment through the existing fenced checkpoint path and returns `Yielded`, releasing the lease and mailbox so the normal Cancel handler can execute. A cancelled waiter withdraws its signal. The configured concrete local provider is shared with distributed placement. An activity already running must finish or cooperate before handoff; delivery through remote transport is separately qualified.

Six real SQLite integration cases cover External and ReplaySafe loop bodies under Durable, Balanced and Ephemeral. Each starts an otherwise unbounded progressing While with no total-cycle cap, requests Cancel through the public actor API while the first body is executing, and proves one completed body, durable Cancelled state, a yielded turn and unchanged activity state after a resumption sweep. A bounded runaway guard makes removal of the behavior fail without hanging.

Three mailbox cases cover a valid waiting Cancel, cancellation of the waiter, and rejection of another partition. They also check that the signal is execution-specific and is cleared before the Cancel turn.

| Deliberate mutation | Failing controls | Restored |
|---|---:|---:|
| Remove scheduler yield check | 6/6 | 6/6 pass |
| Remove fused-step yield check | 3/6 | 6/6 pass |
| Keep a cancelled/acquired waiter's signal | 2/3 | 3/3 pass |
| Omit final folded-segment flush | 6/6 | 6/6 pass |

Whole affected suites on the restored candidate: runtime 3,122 passed, HTTP integration 101 passed, zero failed/skipped. Discovery accounts for all cases: HTTP lists 101; runtime lists 3,111 including three unexpanded theories that execute as 14 rows (4, 2 and 8), yielding 3,122. The nine SQLite command counts stayed 336/204/253 (Durable) and 90/104/180 (Balanced/Ephemeral). The new idle intent check adds no EF command; no budget was raised. These are correctness and command-count results, not throughput measurements.

The first compile attempt used the wrong start-dispatch overload; the first behavioral attempt omitted the resumption-service registration in the test fixture. Those setup failures are retained separately and are not causal red controls. A subsequent replay-safe case exposed one extra fused body iteration; the explicit fused-step check fixed it.

### Independent review corrections

The first cancellation review found a same-key retry race: the first Cancel turn could yield to a second waiting
Cancel, then the second returned Duplicate without draining the first one's queued cancellation. Cancel turns now
suppress cooperative yielding. The real SQLite matrix has twelve cases, adding two same-key waiters in each
mode/profile case. Removing that suppression fails the six duplicate cases; restoration passes all twelve.

Review also found that distribution-first registration could retain an unconfigured mailbox factory. Core and
Distributed now share one internal factory. The same-domain friend grant shares repository DI wiring; third-party
providers can resolve the configured public concrete mailbox and do not need this internal factory. Two behavioral
registration-order cases use the configured mailbox and real scheduler. Restoring the old distributed factory fails
only the distribution-first case; the shared factory passes both.

Recovery assertions now check sweep failures, settled outbox and empty scheduler backlog, followed by a second sweep
that does no work. Replacing the first sweep with a no-op fails all twelve cases; restoration passes all twelve.
The corrected whole suites pass: runtime 3,122, Distributed 114, HTTP integration 107, zero failed/skipped. Discovery
accounts for all cases (runtime's same three delayed theories expand 3,111 discoveries into 3,122 executions).
All nine reference EF command counts remain unchanged. Final independent re-review and program gates are still due.

## Unchanged native roots: candidate #2586

An eligible fenced native checkpoint can update an existing workflow row without acquiring another executable
root-write lease. The live session's durable-record flag is only an eligibility hint. The staging query checks the
actual row and exact full pinned identity inside the transaction; its normal revision CAS prevents deletion or
replacement from becoming an insert. Missing/changed roots roll back before SaveChanges and retry through the
ordinary leased path. Initial roots, model/admission writes, unfenced calls and dirty/externally transacted contexts
keep the leased path. Replay and uncertain-acknowledgement reconciliation retain the immutable marker contract.

| SQLite fixture | Before this change | Candidate | Removed commands |
|---|---:|---:|---:|
| Durable ten WriteLines | 336 | 329 | 7 |
| Durable HTTP reference | 204 | 183 | 21 |
| Durable fork/join/resume | 253 | 246 | 7 |
| Balanced/Ephemeral ten WriteLines | 90 | 90 | 0 |
| Balanced/Ephemeral HTTP reference | 104 | 97 | 7 |
| Balanced/Ephemeral fork/join/resume | 180 | 173 | 7 |

Disabling this optimization restores all measured pre-change counts. The previous committed budgets were
347/206/256 Durable and 101/106/183 Balanced/Ephemeral; those are budgets, not the measured baseline in this table.
Budgets are now lowered to the measured candidate. No throughput improvement is claimed from command counts alone.

Twelve focused controls cover unchanged-root update/replay, stale hints, changed artifact or equal-ID/different-hash
pins, absent record hints, other executions, unfenced/dirty calls, stale fencing, row revision/deletion races and
ambiguous commit acknowledgement. Disabling the optimization fails five controls; removing the actual-row guard
fails three; removing the workflow revision token fails the replacement case. Restoration passes all cases. The row
race controls mutate the SQL row within the test transaction immediately before SaveChanges; they establish the
conditional write and rollback behavior, and are not independent-host race evidence.

Whole local EF persistence: 1,851 passed; HTTP integration: 107 passed, zero failed/skipped. Discovery reconciles all
executions. Independent review, cluster root-deletion races and final program gates remain due. Initial fixture
attempts omitted required codecs/run-health state and expected the wrong stale-fence exception; they are retained
separately, not counted as causal controls. The saved mutation source differs from the final source only in its XML
summary; the final whole suites ran against the current source.

### Production relational providers

At `29df383ba1124575a26b6424ae854597e9e49245`, the strict native-provider suite passed all 236 discovered/executed
cases over PostgreSQL 16, SQL Server 2022 CU18 and MySQL 8.4.11, zero failed/skipped. The retained-root matrix
passed 24/24; disabling the optimization failed 18 and passed six, and restoring it passed 24/24. Together with
the SQLite controls this covers the actual fast path on all four providers, including public deletion and uncertain
acknowledgement; it does not establish independent-host race behavior or throughput.

Two earlier Azure attempts ran no tests because readiness failed. Retained startup logs identified a MySQL client
password variable interfering with first-time initialization. The next whole run passed 233 and failed three older
smoke tests whose run-health assertions used an unscoped `SingleAsync` against a shared database. Scoping those
assertions to their workflow and persistence scope produced the fresh 236/236 pass. All failed/setup receipts are
retained separately. The task-owned database containers bound only to loopback and were removed, with their
volumes, after every attempt. The existing Azure runner returns to its initial deallocated state after collection.

The core evidence archive was collected locally and its SHA256 verified as
`08a2b4720007004f6f85154c0dec89bc6103fee18968b836cc24c5870425b117`. Discovery and execution both account for
236 tests. The first local extraction attempt used a newer Python tar API than the available interpreter; the same
verified archive was then extracted with checked regular-file names, with no provider rerun or result change.

## Retained boundaries: prerequisite state corrections for #2828

Before enabling any new retained boundary, three controls reproduced stale-state defects. A new segment could
reapply a previously committed dispatch over a later durable lifecycle; an old overlay dispatch could override a
newer trailing checkpoint change; and a later cap fold could restage an already-delivered external outbox item as
Pending. The stores correctly rejected the first and third stale writes rather than silently accepting them, but
that still failed the later checkpoint. The trailing-change control returned Pending instead of Started.

Committed dispatch snapshots now leave the segment's pending write set, explicit trailing dispatch changes win
over the earlier overlay, and folds do not restage outbox items already persisted by an earlier segment. The
canonical claim-completion path also releases its settled immediate-continuation provenance. No new boundary
retains folding yet in this prerequisite slice.

All three baseline cases fail and the corrected cases pass. Individually restoring each stale path fails its
corresponding case and passes the other two; restoration passes all three. Whole suites pass: runtime 3,137,
EF persistence 1,851 and HTTP integration 113, zero failed/skipped, with complete discovery accounting. The next
step is to retain qualified suspension/child-dispatch boundaries, then prove their crash and takeover behavior;
these prerequisite controls do not establish that remaining acceptance.

## Original incomplete-start investigation

The original failed snapshots remain unexplained. A new cohort ran the unchanged original local Perf binary and runtime in 100 fresh macOS processes, with the original C1/Memory/Balanced/one-leaf shape and an exit-only startup hook to capture the first failing heap. All 1,425,291 total executions completed, with no capture triggered. Eight earlier original diagnostic processes and eight independent Linux diagnostic processes also completed, in separate cohorts. These passes establish neither a causal explanation nor a fix. No throughput inference is made from these shared-machine correctness runs.

### Immediate continuation hidden by clock rollback

A new bounded diagnostic, using the original runtime DLL and a modified harness, moves the clock backwards one
tick while validating `WorkflowStarted`. All three accepted starts stop Running at that checkpoint, with no
activity, incident or scheduler row. Their immediate outbox continuation is still pending. A separate end-to-end
fixture reproduces the stall in all six memory/SQLite by Durable/Balanced/Ephemeral cases on the current candidate
before correction. This establishes a concrete defect that can produce the observed shape. The historical runs did
not retain per-call clock readings or their outbox rows, so this does not confirm their particular cause.
The same diagnostic harness against the corrected candidate completes all three distinct starts with no residual
work or errors. This is a correctness control, with no timing inference.

The correction records local provenance for scheduler continuations a successful commit explicitly declared
without `NotBefore`. While that same segment owns them, its overlay evaluates their initial claim at their
availability timestamp when the wall clock is behind it. The shared claim/fence transition, canonical outbox fields
and checkpoint replay fingerprints remain unchanged. Explicit due times, retry waits, bare materialized rows,
other intent kinds and durable-store visibility retain their rules. The provenance is removed when delivery settles
and does not depend on the materialized-hop option.

All six engine cases now complete their one activity exactly once, with no incident, pending outbox or scheduler
work. Removing the provenance handoff fails all six and two focused owned-continuation cases. Ignoring an explicit
due time fails its control; inferring provenance from a bare outbox row fails its control. Restoration passes the
six engine and four focused cases; all 21 existing/new delayed-delivery cases pass. Earlier fixture compile errors
and an initial query-only candidate that omitted the overlay's claim path are retained, not counted as causal reds.
The restored whole suites pass: runtime 3,134, Distributed 114, EF 1,851 and HTTP integration 113, zero
failed/skipped. Discovery reconciles every execution. The final source differs from the saved mutation candidate
only in a comment describing the provenance handoff. Independent final review and program integration remain due.

The original memory harness counted successful state lookups over its submitted-ID bag, rather than independently
counting distinct stored executions. Its raw `PendingOutbox: 0` field was a placeholder; the incomplete snapshot
retained state/activity/incidents but no outbox observation. Those limitations do not invalidate the missing leaf or
the retained Running state, and must not be read as proof that the continuation was permanently lost.

### Source identifier collisions: independently reproduced defect

The former production default used a timestamp plus 22 random bits per millisecond. With the original runtime DLL
and a fixed clock, 100,000 generated IDs contained 1,142 duplicates. That finite source space also matters across
nodes; a worker number is not present. The candidate uses the platform's UUIDv7 generator for new workflow,
command, envelope, activity, ownership and folded-checkpoint source IDs. Persisted IDs remain opaque strings.
Deterministic replay-chain derivation is unchanged, and explicitly configured generators remain supported.
UUIDv7 has a larger random source space, not a mathematical uniqueness guarantee or a strictly monotonic clock.
See [the .NET API](https://learn.microsoft.com/en-us/dotnet/api/system.guid.createversion7?view=net-10.0) and
[RFC 9562 section 5.7](https://www.rfc-editor.org/rfc/rfc9562.html#section-5.7).

A bounded full-engine diagnostic freezes the clock and installs the actual production generator, replacing the
testing harness's deterministic generator. With the original runtime and compact source it rejected a fresh start
as Duplicate after 1,882 successful starts. The same current candidate with only the compact generator selected
also rejected a fresh start, after 3,472 successes. With the candidate default, both runtime-generated and
caller-generated workflow-ID cases completed 10,002/10,002 distinct executions, with no nonterminal state,
queued work or errors. The original-runtime caller-ID case also completed 10,002 executions. These are modified
diagnostic-harness runs, not unchanged original Perf runs or throughput measurements. Their failure fingerprint
is a Duplicate rejection, unlike the original Running/WorkflowStarted snapshots; the historical cause remains open.

Seven new focused cases cover each ID purpose under 65,536 concurrent fixed-clock calls, rollback of the clock,
ownership IDs and actual folded checkpoint IDs. Restoring the old source fails all seven; the candidate passes.
The frozen-clock allocation probe costs 88 rather than 48 bytes per generated ID, a 40-byte tradeoff for the larger
source space. Full affected suites pass: runtime 3,130, Distributed 114, EF persistence 1,851 and HTTP integration
107, zero failed/skipped. Discovery reconciles every execution, including the three delayed runtime theories.
Final integrated timing must use the actual production default; the original benchmark explicitly selected the
compact generator, and its memory state count did not independently establish distinct workflow IDs.

## Evidence custody and remaining work

Raw logs, TRX, source snapshots, rejected cache patches, hashes and experiment scripts are retained at `/private/tmp/elsa-runtime-efficiency-program` on the qualification machine. Frozen original binaries and original failed results remain under `/private/tmp/elsa-native-perf-20261031`. Heap files, if captured, are private diagnostic artifacts and are not committed.

Remaining acceptance is tracked on #2531 and spec 1183: reconcile older objectives, resolve the original anomaly and identifier concerns, deliver applicable persistence/scheduling/diagnostics work, run representative sustained and failure/recovery cohorts including independent hosts, then review and qualify the exact integration head and containing main. This report is not a substitute for those gates.
