# Research: DispatchWorkflow Parent Audit Remediation

## Decision: terminal delivery requires a durable finalization phase

**Rationale**: A child-start delivery item cannot become irreversibly terminal before its deterministic dispatch failure, incident, and optional parent-resume consequences are recoverable. Finalization must be claimable independently from re-delivering the original child-start request.

**Alternatives considered**: notifying observers before recording delivery failure would re-run the child-start handler after lease expiry; terminalizing first without a replay marker preserves the reported crash gap.

## Decision: redrive evidence is the source of duplicate classification

**Rationale**: `Pending` and `Started` alone do not prove a redrive was requested. Deterministic redrive outbox evidence permits concurrent requests and crash recovery to converge without inventing a second identity.

**Alternatives considered**: state-first persistence leaves `Pending` without work; work-first persistence without execution-time reconciliation can admit the child while the dispatch still reads `DispatchFailed`.

## Decision: retries are explicit durable intent metadata

**Rationale**: Parent resume and cancellation delivery already use the runtime outbox retry model. Stamping bounded retry metadata preserves provider neutrality and makes the intended behavior visible in stored work.

## Decision: paging uses stable keyset continuation

**Rationale**: Fixed first-page sweeps can starve later records, while unbounded reads violate the storage contract. Stable `(CreatedAt, DispatchId)` continuation makes every sweep bounded and progressive.

## Decision: deletion is conditional on the inspected lifecycle snapshot

**Rationale**: Retention must not delete a dispatch that redrove after inspection. The delete command therefore carries the expected status and update/version evidence and fails closed when the snapshot changed.

## Decision: API failure projection is allowlisted

**Rationale**: Incident metadata is persistence input, not trusted API output. Classification is parsed to a known enum, counts are bounded numeric values, and incident/dead-letter identifiers are deterministic or validated.

## Decision: generated maps remain untouched by generators

**Rationale**: The user explicitly skipped generated-map refresh. The remediation will reconcile audit language and any pre-existing tracked inconsistency without executing map scripts.
