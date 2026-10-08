# PR #2511 review round 1: queue-reconciliation correction evidence

**Status:** root accepts the scoped correction and the passing local checks below. The failed broader worker timeout is retained with unresolved cause. Current-head hosted verification, review, merge and resulting-main gates remain open; issue #2497 is not delivered.

Greptile review found two queue-reconciliation defects: the implementation treated provider `ListAllAsync` order as dequeue order, and it could reject or mishandle legitimate persisted outbox work arriving during an active coalescing session. The candidate now reconciles known work by identity, supports targeted deletion only when the provider advertises it, refreshes after continuation/outbox delivery where needed, and falls back to immediate durable writes for providers without that capability. The candidate source and focused test identities are listed in the [companion JSON](cap-recovery-review-round1.json).

## Executed regression evidence

Two retained reds demonstrate the findings. Against unchanged PR-667a production, `AdvanceInnerQueue_RemovesConsumedSeededItemsByIdentity_WhenListOrderDiffersFromDequeueOrder` failed at the actual wrong-FIFO-head assertion (`work-5`). A separate EF test failed when only `RuntimeCoalescingSession.cs` was restored to the PR-667a version: the Start command returned `AcceptedButFaulted` because the session rejected the queue state after persisted outbox delivery. That second run is a one-file mutation bite, **not** a full-main baseline; the run receipt records restoration of the candidate afterward.

Focused v2 initially passed 44/44 Runtime tests and 13/16 EF tests. Its three EF failures were stale expectations that a completed nested child-A Schedule anchor remain durable; observed queue state correctly retained the still-active parent and successor B. The v3 fixture changed those assertions to the required active anchors and kept the durable recovery checks. Focused v3 then passed Runtime 44/44 and EF 16/16, with no skipped cases.

Reproduce the focused checks with these commands (the recorded runs also selected isolated results directories):

```text
dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore -m:1 --filter FullyQualifiedName~RuntimeCheckpointCoalescingTests --logger 'trx;LogFileName=focused.trx'
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --no-restore -m:1 --filter FullyQualifiedName~EfReplaySafeFusionCapRecoveryTests --logger 'trx;LogFileName=focused.trx'
```

## Measured queue API observations

The EF focused TRX records these deltas at the named boundary. `ListAsync` is the number of provider page-call observations. These are scheduler-queue API counts, not SQL statement counts, round trips, latency, or a general database-cost estimate.

| Boundary | Capture point | Enqueue | List pages | Dequeue | Targeted delete |
|---|---|---:|---:|---:|---:|
| ActivityStarted, cap 2 | inner-store return | 1 | 1 | 0 | 0 |
| ActivityStarted, cap 2 | decorator return | 1 | 1 | 0 | 1 |
| ActivityAttemptClaimed, cap 1 | inner-store return | 0 | 0 | 0 | 0 |
| ActivityAttemptClaimed, cap 1 | decorator return | 0 | 0 | 0 | 0 |
| Typed ActivityScheduled, cap 1 | inner-store return | 1 | 1 | 0 | 0 |
| Typed ActivityScheduled, cap 1 | decorator return | 1 | 1 | 0 | 1 |
| Fusable intrinsic ActivityScheduled, cap 1 | inner-store return | 1 | 1 | 0 | 0 |
| Fusable intrinsic ActivityScheduled, cap 1 | decorator return | 1 | 1 | 0 | 1 |
| Nested D2 ActivityCompleted occurrence 2 | inner-store return | 0 | 0 | 0 | 0 |
| Nested D2 ActivityCompleted occurrence 2 | decorator return | 0 | 1 | 0 | 0 |

The persisted-outbox interleaving case observed a concrete continuation intent/work-item delivered before session refresh. Targeted-delete calls were 2 before the gated release and 4 after release. The tested under-cap span without an intervening durable boundary adds no per-span queue writes. At a continuing boundary, ensuring absent anchors can require enqueue plus provider paging; no SQL-count or constant-database-cost claim follows from these results.

## Limits and remaining gates

The tests preserve at-least-once handling; they do not establish exactly-once delivery against a later retry after a reconciliation snapshot. Initial overlay seeding still uses the provider's listed order; this correction addresses durable identity cleanup and does not claim to redefine the overlay's initial ordering semantics. The persisted SQLite cut is interruption evidence, not an operating-system process-kill test.

The full-gates attempt passed Runtime 2,131/2,131 and Resumption 21/21, but Runtime EF passed 919 and failed one of 920 tests. `WorkerOidcHostTests.Real_bearer_actor_executes_resumes_revokes_and_reloads_across_a_child_process_restart` hit its 30-second HTTP timeout at the first authorized execute. Mac load averages of 107.93/110.27/84.03 were observed afterward; load is context, not established causation. This failed run remains retained and is not a full-suite pass. Root and independent source review confirmed that the fixture checks an exact 19-feature composition without `WorkflowsRuntimeCheckpointPersistence`; the changed coalescing/fusion path is not active. Its fixture discarded child stderr and deleted the owned database, so the original timeout cannot be attributed from retained host state. Current-head hosted suite proof remains required; no retry or causal repair is claimed. The remaining complete projects passed: Activities Runtime 352/352, Sequence 18/18 and Flowchart 105/105. Workbench rebuilt successfully and all five normal-host controls passed on a fresh isolated Coalesced cap-two database: HTTP methods, HTTP echo, Sequence, valid REST companion and WorkflowFlow. The owned host was stopped and the database retained. Architecture passed 635/635; map freshness passed without another refresh. Independent scoped source review and root diff review found no material introduced defect. Current-head hosted/native-provider checks, final review and resulting-main verification remain required; earlier-pin green receipts are not proof for this candidate.

See the companion JSON for exact source, receipt, TRX, and retained-red hashes. The corrective spec still marks review corrections in progress. Root has accepted C001–C006, including these bounded queue API observations. C007–C008 remain open for broader verification and delivery.
