# Fenced attachment, acknowledgement and durable retry checkpoint

Scope: T012 and T016 in [spec 996](../spec.md), issue #3151, draft PR #3156.
This checkpoint consolidates the earlier store/runtime implementation and strengthens its acceptance
proof. It does not complete the finite HTTP ETL leaf or the two-process interruption matrix (T013).

## Transaction and replay evidence

- `EtlConnectorHttpPageStoreTests` covers immutable attachment/replay, conflicting content and stale
  ownership. The expiry fixture now captures a page **before** arming its save interceptor and calls
  `AttachHttpPageAsync` directly. Previously the convenience attachment helper could expire ownership
  during source-attempt allocation, never reaching the attachment write: that was insufficient proof.
- `Expired_http_page_attachment_rolls_back_metadata_and_ledger_accounting` uses the same corrected
  ordering and a fresh verification context. It compares position, retained input/metadata, run and
  partition source reservations, and every field of the retained request journal, including its capture,
  digest and metadata reserves. No page or page-accounting progress survives the failed transaction.
- `Http_delivery_completion_replays_identical_receipt_and_refuses_conflicting_receipt_after_store_reopen`
  commits a 204 acknowledgement, disposes the context, and reopens the store. Repeating that receipt is
  `Replay`; changing it to 201 is `Conflict`. Retained output JSON/digest, effect/attempt identity,
  delivery journal/digest, metadata, outstanding quota, position, settlement and accepted/rejected/filtered
  counters remain unchanged. Delivery acknowledgement does not itself accept input progress.
- Existing `EtlConnectorAcceptanceStoreTests` cover matching file/HTTP obligations, refusal before all
  receipts exist, atomic progress and quota settlement, duplicate acceptance, recovery of partial/full
  batches and filtered terminalization. The acceptance expiry test arms its interceptor after preparing
  the owed output and acknowledgements, then calls acceptance directly; it tests the intended transaction.

## Durable delay evidence

The source and delivery restart cases now use three separately disposed SQLite contexts/stores. They
persist 429/Retry-After, release the owner, let another partition progress, refuse a claim immediately and
one tick before eligibility, then reacquire at the exact due time. Source request hash, route body/effect,
first-route acknowledgement, attempt history and original deadline survive. A retry receives a new
physical request identifier/ownership fence without changing logical work identity.

`EtlHttpTransportTests` exercises real loopback 429 responses with delta-seconds and RFC 1123 HTTP-date
headers. Delta time is bounded by observations before and after the request rather than an arbitrary
wall-clock tolerance. The date is checked as an exact UTC instant. Existing source/delivery transition
suites cover 408/429/500/502/503/504, uncertain outcomes, capped backoff, deadline and attempt exhaustion,
and refusal to shorten a Retry-After that exceeds the remaining budget.

`Http_only_worker_yields_a_retry_slot_and_runs_another_shard_before_retrying` exercises the actual shared
worker with one physical slot and real HTTP: partition B runs while A waits; A does not retry early.
Reopened stores are persistence proof within one test process, not process-death proof. T013 remains open.

## Review and controls

Root review plus a bounded independent audit found the two masked attachment tests and the missing EF
acknowledgement replay/conflict proof. Those gaps are corrected without changing production behavior.
The final review also added complete source-reservation snapshots and position/settlement counters.

- Removing the connector claim's not-before predicate made both reopened-store retry tests fail by
  selecting the delayed partition. Production source was restored byte-for-byte (SHA-256
  `9efffa56c5578dd937255f9119c5f1aea7d4361bbc6ee87c6d97982a7964babc`).
- Removing the attachment transaction's post-save ownership check made **both** corrected rollback tests
  fail on persisted page/position state. Production source was restored byte-for-byte (SHA-256
  `a33cce0e3d2071cc1cf4ec1d3fdaf08fb92e514786bdf9ce439d41b591a8937e`).

## Validation and limits

An initial full run reported **927 passed, 1 failed, 0 skipped**. The retry-worker journey hit its existing
30-second deadline while waiting in `EfSqliteSerialOpenInterceptor.ConnectionOpeningAsync`, before its
ordering/timing assertions. Machine load observed around that failure was 104.23; the later focused run
at roughly 12 passed all four targeted tests, including the worker journey. This is evidence of a
non-reproduced timeout, **not a proven root cause**. No timeout, production scheduling or workflow setting
was changed. The failure trace is retained at `/tmp/3151-retry-restart-final-etl.log` on the working host.
A preliminary focused build also caught an unfinished delegated `ValueTask.ContinueWith` assertion;
it was removed in favor of direct awaited results before the successful focused run.

Restored-source full ETL validation passed **929 tests, zero failures/skips**. Final review then expanded
only the delivery test snapshot to include the run's aggregate accepted/rejected/filtered counters;
the final affected delivery/source/attachment/transport subset passed **30 tests, zero failures/skips**.
Architecture passed **584 tests, zero failures/skips**; generated maps, solution filters and diff checks
passed. Relative evidence/program links were verified. No generated map changed.

The bounded self-review loop completed three passes: correcting masked attachment paths and adding EF
receipt replay proof, strengthening ledger/counter snapshots, then reviewing the final evidence and diff.
No actionable finding remains in this checkpoint. T012/T016 are complete: **12/22 tasks** in the HTTP
leaf are now complete; the full program and leaf remain active and the PR remains a draft.

Commands:

```bash
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters-check
git diff --check
```

Physical external effects remain at least once. No historical receipt/HTTP timeout cause is established.
The current main-CI tracker #2293 remains independent of these local results. Exact new-head CI and final
post-merge gates are still required. Frozen predecessor-worker resume, process interruptions, corruption,
native cancellation, policy/telemetry, operator views and real Workbench/browser acceptance remain open.
