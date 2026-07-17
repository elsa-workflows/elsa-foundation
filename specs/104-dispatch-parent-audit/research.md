# Research: DispatchWorkflow Parent Audit Remediation

## Decision: terminal delivery requires a durable finalization phase

**Rationale**: A child-start delivery item cannot become irreversibly terminal before its deterministic dispatch failure, incident, and optional parent-resume consequences are recoverable. Finalization must be claimable independently from re-delivering the original child-start request.

**Alternatives considered**: notifying observers before recording delivery failure would re-run the child-start handler after lease expiry; terminalizing first without a replay marker preserves the reported crash gap.

## Decision: redrive evidence is the source of duplicate classification

**Rationale**: `Pending` and `Started` alone do not prove a redrive was requested. Deterministic redrive outbox evidence permits concurrent requests and crash recovery to converge without inventing a second identity.

**Alternatives considered**: state-first persistence leaves `Pending` without work; work-first persistence without execution-time reconciliation can admit the child while the dispatch still reads `DispatchFailed`.

## Decision: spec 101 safe dispositions supersede the draft error-shape finding

**Rationale**: The merged replacement program ratified an authenticated redrive API that returns a bounded disposition view for accepted, idempotent, conflicting, ineligible, and missing requests. Replacing those responses with a different error envelope would contradict the narrower canonical feature contract.

**Alternatives considered**: preserving the earlier parent-review expectation would rewrite an accepted public contract without a product decision.

## Decision: retries are explicit durable intent metadata

**Rationale**: Parent resume and cancellation delivery already use the runtime outbox retry model. Stamping bounded retry metadata preserves provider neutrality and makes the intended behavior visible in stored work.

## Decision: paging uses stable keyset continuation

**Rationale**: Fixed first-page sweeps can starve later records, while unbounded reads violate the storage contract. Stable `(CreatedAt, DispatchId)` continuation makes every sweep bounded and progressive.

## Decision: deletion is conditional on the inspected lifecycle snapshot

**Rationale**: Retention must not delete a dispatch that redrove after inspection. The delete command therefore carries the expected status and update/version evidence and fails closed when the snapshot changed.

## Decision: API failure projection is allowlisted

**Rationale**: Incident metadata is persistence input, not trusted API output. Classification is parsed to a known enum, counts are bounded numeric values, and incident/dead-letter identifiers are deterministic or validated.

## Decision: bounded outbox selection preserves null-as-immediate availability

**Rationale**: The runtime contract treats a null `AvailableAt` as immediately eligible. Groundwork
therefore uses a separate, null-aware bounded route with an exact null predicate and merges its
results with the ordinary due-time route. This keeps every provider request bounded without relying
on provider-specific null ordering, changing the public outbox model, or silently abandoning
already-persisted null values.

**Alternatives considered**: normalizing null to a sentinel during save would change round-trip
semantics, while filtering only `AvailableAt <= now` excludes valid work.

## Decision: caller limits do not become collection capacities

**Rationale**: Positive query limits include `int.MaxValue`; multiplying or preallocating directly
from that value can overflow or attempt an unnecessary allocation before the bounded provider query
runs. Candidate and claim collections therefore grow only from actual returned rows.

## Decision: boundedness evidence distinguishes requests from physical I/O

**Rationale**: Store tests prove a fixed number of admitted queries, stable ordering, propagated
`Take`, and bounded returned rows. SQLite tests prove functional provider behavior. The legacy
SQLite test adapter materializes before emulating new routes, so it is not evidence of physical rows
scanned and the audit does not present it as such.

## Decision: composite routes must fit SQL Server's physical index budget

**Rationale**: Groundwork's SQL Server provider rejects index keys wider than 1,700 bytes during
route admission. Status and deterministic identity projections use their actual bounded widths,
test-scope IDs and intent kinds enforce portable public limits, and outbox routes rely on
Groundwork's existing ordinal document-identity tie-breaker instead of duplicating the outbox ID in
every composite. A no-connection provider test constructs the admitted dispatch stores so the
provider validator executes in CI.

**Alternatives considered**: retaining 450-character defaults makes every new composite invalid;
adding hash fields changes the persisted JSON shape and leaves prior documents without backfillable
values; testing only SQLite cannot exercise SQL Server's key-width validator.

## Decision: generated maps remain untouched by generators

**Rationale**: The user explicitly skipped generated-map refresh. The remediation will reconcile audit language and any pre-existing tracked inconsistency without executing map scripts.
