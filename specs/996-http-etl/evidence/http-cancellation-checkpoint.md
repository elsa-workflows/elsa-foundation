# HTTP native cancellation and terminal ordering checkpoint

Scope: T015 in [spec 996](../spec.md), issue #3151, draft PR #3156.
T015 is complete; **15/22 tasks** are complete. The HTTP leaf remains an incomplete draft.

## Native cancellation

`Native_HTTP_cancellation_retires_the_wait_and_refuses_late_receipts` executes twelve combinations:
Sequence and Flowchart, whole-workflow and placed-composite cancellation, and three work boundaries.
It uses the existing native publication fixture, a real v2 publication and pinned native selection,
SQLite persistence, the DI-composed background worker, actual native cancellation commands and
post-commit intents, and a real loopback HTTP receiver.

| Boundary | Observation |
|---|---|
| Before worker startup | Cancelled admitted work sends no GET or POST after the worker starts. |
| GET response received and immutable cache published, before SQL capture | The late source capture is refused; source journal, retained accounting and page position remain unchanged. |
| Receiver applied POST and returned 204, before SQL acknowledgement | The late delivery receipt is refused; its unsettled batch, owed progress and accounting remain unchanged. The external write remains applied once. |

The test adapter gates only the store call after actual transport returns. After durable cancellation,
it deliberately submits that receipt with an uncancelled token, proving the store's terminal check
independently of cooperative transport cancellation. It does not manufacture a transport response.
The production worker and normal cleanup then finish; this is test teardown, not a remote-stop guarantee.

Every case verifies one terminal row and outbox, the Cancelled result, zero accepted/rejected records,
no accepted output manifest, removal of the bookmark and acknowledgement with `ProvenParentTerminal`.
The cancelled RunEtl and containing composite do not execute their following sibling. Cancelling a placed
composite also leaves its outer Sequence running with no following outer sibling, preserving existing
native scope-cancellation semantics. The common cancellation-tree assertions are shared with the existing
file-based cases, which remain in the full suite.

## Store ordering

Fresh SQLite contexts exercise each state transition. These are deterministic committed-order proofs,
not a probabilistic simultaneous-thread stress test.

- Cancellation before a valid in-flight delivery receipt refuses that receipt, acceptance, a new route
  permit and a new source permit in a sibling partition. Full retained-row snapshots and accounting
  remain unchanged, including the unsettled owed batch and one Cancelled terminal/outbox.
- Cancellation after the last batch's required acknowledgement but before acceptance refuses acceptance,
  preserving owed work and outstanding quota. The batch is otherwise eligible for final acceptance.
- Final successful acceptance before cancellation preserves its success result, acknowledged route,
  accepted progress and zero outstanding quota. Cancellation returns the existing conflicting result
  without overwriting it or creating another terminal/outbox.

## Review and verification

Root review identified that an initial late-acceptance assertion still had an unacknowledged route.
The separate fully eligible acceptance-order case closes that gap. Independent review additionally found
that the before/after cancellation comparison should include source capacity, not only delivery accounting.
The shared snapshot now includes all connector, partition, batch, request and page rows plus run counters.
Four bounded root/independent review passes covered those refinements, the native adapter, shared fixture
and evidence; no actionable finding remains.

Two independent regression controls remove the terminal/running checks from source capture and destination
acknowledgement, one at a time. Each causes all four applicable native cases to fail with `Applied` instead
of `Terminal`. Each production file is restored byte-for-byte. No production changes are retained.
A third control removes acceptance's terminal/running checks. The otherwise-ready late acceptance then
attempts duplicate terminal delivery and fails with SQLite's unique outbox constraint, rather than returning
`Terminal`. This proves the test reaches final acceptance, not an unrelated missing-route refusal. That
source is also restored exactly. The final affected run passes all fifteen native/store cases on the final
source, including the strengthened accounting snapshots.

| Gate | Result |
|---|---|
| Full ETL suite | 956 passed, 0 failed, 0 skipped |
| Architecture suite | 584 passed, 0 failed, 0 skipped |
| Final affected native/store cases | 15 passed, 0 failed, 0 skipped |
| Source/delivery/acceptance guard controls | Expected failures: 4 / 4 / 1; production sources restored exactly |
| Maps and solution filters | Passed; no generated changes |
| Relative documentation links and diff | Passed |

Commands: `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore`,
`dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore`,
`dotnet run --project tools/maps/Elsa.Maps.Generator -- check`, and the same generator with
`solution-filters-check`. The final affected run selects `Native_HTTP_cancellation`,
`Cancellation_that_commits`, and `Final_http_acceptance_that_commits_first` by fully qualified name.
The full ETL run preceded the last assertion-only accounting refinement; the fifteen affected cases were
rebuilt and rerun afterward. There are no production behavior changes. Workbench HTTP/browser proof remains
T019; exact-head CI and resulting-main verification are separate final gates.

This proof does not undo already-started external effects, promise physical cancellation of a server, or
turn delivery into exactly once. External effects remain at least once. Frozen predecessor compatibility,
policy/auth/telemetry, operator inspection, rebuilt Workbench/browser acceptance and final delivery gates
remain open. The task runner and workflow-scoped worker pause remain follow-up work. Historical
receipt/HTTP/SQLite-open timeout causes remain unproved; this checkpoint does not close main tracker #2293.
