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

## Continuing folding after native boundaries (#2828)

The candidate now commits qualified nonterminal suspension, bookmark creation, child dispatch and subsequent
before-user-code attempts immediately, then keeps folding in a new segment. Terminal, admission and control
boundaries retain their conservative path. External outbox work uses durable claims and fresh lifecycle reads while
scheduler continuations retain their local overlay. Seven external-delivery controls include uncommitted exclusion,
renewal, bounded mixed claims and rejection after another owner takes the claim. Reinstating broad external overlay
routing fails five; reinstating stale external exact lookup fails three. Restoring the candidate passes all seven.

Twenty-four mode/boundary/buffer cases fail under the former stop rule and pass with continued folding. The actual
SQLite references execute the workflow and validate completion, resume, settled queues/outbox and no incidents:

| Reference | Mode | Former stop rule | Candidate EF commands |
|---|---|---:|---:|
| Ten WriteLines | Durable / Balanced / Ephemeral | 329 / 90 / 90 | 329 / 90 / 90 |
| HTTP reference | Durable / Balanced / Ephemeral | 183 / 97 / 97 | 183 / 97 / 97 |
| Fork/join with actual bookmark resume | Durable / Balanced / Ephemeral | 245 / 173 / 173 | 229 / 158 / 158 |
| Suspension before 100 WriteLines, then actual resume | Durable / Balanced / Ephemeral | 13,779 / 13,722 / 13,722 | 2,737 / 625 / 585 |

This is a controlled stop-rule comparison on otherwise-current source. Its Durable fork baseline of 245 differs
from the earlier whole-implementation baseline of 246; they are not the same experiment. Six of thirteen budget
cases fail when the former rule is restored, while their workflow outcome assertions still pass. All thirteen pass
after restoring the candidate. The 100 distinct WriteLine states are completed before resume and retain their IDs
and statuses afterward. These are database command counts, not workflows/second or exactly-once Console delivery.

The [bounded process fixture](../../specs/1183-runtime-efficiency-program/evidence/retained-boundaries/README.md)
passes six normal controls and 24 physical SIGKILL/restart cases, all modes by real Event suspension/child dispatch by
before commit/after commit/after queue advancement/mid-body. A fresh process reads the same SQLite database and
recovers using actual sweeps and bookmark resume. Previously durably completed activity identities are preserved;
none of their probe bodies executes in recovery. Uncommitted ReplaySafe work can repeat. Six additional paused-owner
cases let B expire A's lease with an offset clock, recover and finish, then release A. Every A is rejected with
`RuntimeStaleFencingTokenException`, and workflow, activity, incident, queue and outbox rows remain byte-for-byte
unchanged after B's result. These are physical processes on one macOS machine, not independent-host evidence.

The checked-in fixture and its source-hashed result ledger reproduce this witness. The initial private fixture's
missing activity registration and create-only source republish failures are setup failures, not engine failures.
Whole affected suites pass: runtime 3,168, EF persistence 1,851, HTTP integration 116, DispatchWorkflow 380; zero
failed/skipped, with full discovery accounting. The first whole EF run had four obsolete tests that seeded an
uncommitted external child-start claim; those now prove durable completion visibility. The first whole HTTP run
had six coverage failures because continued folding reduced formerly long drains to three/seven cycles. Their
fixtures now put an External step before each WriteLine, preserving the original greater-than-64-cycle assertion
under default options. Neither original coverage assertion nor workflow correctness assertion was removed.

The user guide and resumption mechanics describe the retained session and state that queues without targeted
deletion fall back to immediate checkpoints. That fallback has behavioral controls; its throughput is unmeasured.
Independent-host takeover, final provider integration, full review and containing-main gates remain open.

### Statement and wire attribution

The opt-in command-cost tool now includes the 103-activity reference. Its first run exposed a missing tool-only
activity-count mapping; the workflow itself completed before the report rejected the unknown reference. After that
mapping and its documentation were corrected, both whole-project measurement cases passed (SQLite and PostgreSQL,
zero failures/skips, discovery 2/2). Both providers ran all four configurations and all seven reference/series shapes.
The tool validates named caller attribution, one transaction per checkpoint marker and wire/savepoint/rollback accounting.

For Balanced: ten WriteLines cost 90 SQLite commands; PostgreSQL batches them into 66 EF commands containing 90
statements and uses 74 wire round trips. The HTTP reference is 97 SQLite commands versus 78 PostgreSQL EF commands,
97 statements and 94 round trips. The long suspension reference is 625 SQLite commands versus 387 PostgreSQL EF
commands, 625 statements and 448 round trips. Equivalent SQL counts do not imply equivalent provider command counts
or network latency. The current Durable ten-WriteLine floor is 329 SQLite commands / 269 PostgreSQL commands /
368 PostgreSQL round trips, and still exceeds #2584's original SQLite objective of under 300.

Raw command TSVs, run JSON and attribution/linear-fit report are in
`/private/tmp/elsa-runtime-efficiency-program/m4-command-cost-qualified/`; rerun using the
[tool instructions](../../Essentials/tests/Activities/Http/CommandCost/Tests/README.md). Nothing in this cohort is timed.

## Avoiding a second queue write for a committed parent decision (#2584)

A private typed value scope identifies the inline pump's current work item. After the actual checkpoint provider
succeeds, a fenced non-retaining outcome with matching execution, source work ID and command ID acknowledges that
exact parent completion. Queue advance then removes that item from its remaining frontier, while preserving the
existing active fused Schedule anchors. Attempt claims, retaining checkpoints and unrelated writes keep their
backstop. A nested scope restores the unfinished outer decision. The scope introduces no per-dispatch allocation,
public checkpoint member or storage format.

The current SQLite references pass with Durable ten WriteLines **269** commands (from 329), HTTP **171** (183), and
long suspension/resume **2,137** (2,737). Other reference counts are unchanged. Disabling only the successful-commit
receipt fails exactly those three lowered budget cases; restoration passes all thirteen. The ten-WriteLine result
meets #2584's original under-300 objective without reviving its obsolete per-hop persistence layer.

Nine focused cases cover accepted versus retaining/attempt/unfenced/unrelated decisions, failed physical writes and
nested receipt isolation. Eight independent mutations each fail their corresponding case and pass the other eight;
restoration passes nine. The checked-in process fixture now passes six normal controls, the original 24 boundary
kill/restarts and six takeovers, plus 16 parent-decision kill/restarts and four parent-decision takeovers. The latter
use an explicit cap of one in Durable/Balanced to reach the physical parent decision; Ephemeral does not produce
that cap-triggered boundary. The after-advance gate requires a physically committed marker, not just an accepted
deferred checkpoint. Across all forty crash cases, completed activity identities persist and their bodies do not run
in the fresh recovery process. All ten old writers are fenced out after B recovers, leaving the inspected durable
rows unchanged. A stale ReplaySafe owner can still enter code before its next physical fence; no exactly-once
external-effect guarantee is inferred. These remain single-machine process checks.

Whole suites pass with complete discovery reconciliation and zero failures/skips: Runtime 3,177, activity runtime
732, EF persistence 1,851, HTTP integration 116, DispatchWorkflow 380, Distributed 114 and BPMN 513. The first BPMN
attempt ran no tests because a referenced project lacked restored assets; normal restore repaired it. Both whole
command-cost measurement cases also pass. In PostgreSQL, Durable ten WriteLines falls from 269 to 209 EF commands,
25 to 15 transactions and 368 to 278 wire round trips, retaining twelve checkpoint markers. The long suspension
reference retains 109 markers while reducing 216 to 116 transactions and 3,068 to 2,168 round trips. HTTP retains six
markers and reduces eleven to nine transactions and 193 to 175 round trips. This is statement/wire attribution,
not timed throughput. Raw evidence is under `m4-pumped-ack-*` in the custody directory.

The broader atomic queue-publication prototype was removed. Its direct storage cases passed, but its corrected
reference cohort increased Durable ten WriteLines from 329 to 338 and the long suspension run from 2,737 to 2,823.
Those two budget failures were regressions; no budget was raised. An earlier prototype left a queued row, which was
corrected before that measurement. Compile/setup failures and all prototype sources are preserved separately at
`m4-rejected-atomic-queue/`. The narrower acknowledgement resolves the measured cost with the existing transport.
Independent-host/provider integration and final reviewed-head/main qualification remain due.

## Owner dispositions

[#2582 was retired](https://github.com/elsa-workflows/elsa-foundation/issues/2582#issuecomment-6559721729) as **not planned**
by the owner's explicit 1 November decision. The older controlled terminal-status experiment did not reduce simple
or HTTP command counts, and the owner chose to avoid that low-value plumbing. The required exact issue-reference
search on freshly fetched main `e5896da5f5b93d9c302d67ca99d529f0d7e064b9` returned no landed implementation. This is an
owner disposition, not a completed implementation or an optimization gain. #2583's rejected owned-state cache remains
a separate pending owner decision.

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

## Bounded pump dispatch (#1307)

`MaxConcurrentDispatches` is now configurable on durable timers, recurring starts and recovery re-drives, with a
conservative default of one. Independent executions/schedules can drain concurrently using separate ordinary
persistence scopes. Timers for one execution retain provider order. Each timer/recurrence renewal uses a separate
scope too; claims reclaimed while waiting cannot authorize an old dispatch. Direct hosts without a scope factory
and legacy timer providers retain sequential behavior. Known timer deadlines already wake adaptively (#3481), so
this change addresses the remaining inline-drain ceiling rather than attributing that earlier wakeup improvement
to this program.

Recovery discovery, outbox processing and cursor settlement remain ordered. Candidate purge/re-drive uses separate
scoped service instances, while indexed results preserve discovery order and existing fairness/accounting. Actual
timer/recurring dispatches, recovery re-drives and individual outbox deliveries contribute load charges without
shedding durable work. Nested work reuses its caller's charge. Empty scans and waiting claims carry no charge.
The outbox still processes items sequentially to preserve its continuation/dependency ordering.

The focused controls cover bounded overlap, same-execution order, tenant binding, fresh scope disposal, active
lease-renewal isolation, waiting-claim takeover, terminal purge, result order, faults, cancellation and admission
load cleanup. Eight outbox cases cover claimed and legacy delivery, ambient-charge reuse and empty scans. A real
SQLite contract holds two dispatcher queries open on separate EF contexts while renewal completes through another
context, for timers and recurring starts, and independently proves recovery re-drive context isolation.
Seven source mutations independently remove execution grouping, fresh renewal scopes, timer charges, recurring
parallelism, recovery parallelism, recovery result order and outbox charges. Each fails its corresponding behavioral
control; restored implementation qualification follows. Raw logs retain initial compilation/setup mistakes, the
missing reason in the first fake duplicate response, a scope-disposal assertion taken before its outer scope closed,
and an invalid first EF recurrence seed identity; those are test-fixture corrections, not engine regressions.

This establishes concurrency and ownership behavior. Degree-one versus higher-degree throughput, native-provider
concurrency and independent-host capacity remain part of integrated qualification; no numerical throughput gain is
claimed from these barrier-controlled tests.

Restored local whole-suite qualification after the pump change: **7,026 passed, zero failed, zero skipped** across
Runtime (3,188), Activities Runtime (732), EF Runtime (1,854), HTTP (116), DispatchWorkflow (380), Distributed Runtime
(114), BPMN (513), Scheduling (107) and Resumption (22). Discovery-to-execution accounting has no missing or unmatched
cases, including runtime-expanded theories. After extracting the EF context contract for native-provider reuse,
all three SQLite cases pass again; the native-provider project builds and discovers 245 cases, including nine new
pump cases. Native execution is pending. Raw custody: `m4-pumps-*` under the program's temporary evidence root.
The shared-contract extraction initially needed an explicit Resumption test reference and extension namespace;
those compilation attempts ran no tests and are retained separately from passing qualification.
