# Data Model: DispatchWorkflow Parent Audit Remediation

## Runtime post-commit finalization

- Retains the original outbox identity, intent, attempt count, safe failure summary, claim fencing, and retry metadata.
- Distinguishes delivery retry from terminal consequence finalization.
- Finalization remains claimable until every deterministic observer consequence converges.

## Dispatch redrive evidence

- Uses the existing deterministic redrive request and outbox identities.
- An active lifecycle is a duplicate only when matching durable redrive evidence exists.
- Execution-time reconciliation may repair `DispatchFailed` to `Pending` before admitting the redriven child.

## Dispatch continuation

- Stable key: `(CreatedAt, DispatchId)`.
- Applies to lifecycle queries used by retention and TestRun cleanup.
- Repeated bounded pages must advance even when earlier records remain retained or already requested for cancellation.

## Conditional dispatch deletion

- Carries dispatch identity plus the expected terminal status and last-updated snapshot.
- Returns false on absence or snapshot mismatch.
- Provider implementations perform the comparison in the same conditional write as deletion.

## Safe failure projection

- Classification: known `WorkflowDispatchDeliveryFailureClassification` value only.
- Attempt count: non-negative bounded integer.
- Incident ID and dead-letter ID: deterministic dispatch-derived values.
- Retry scheduling: safe attempt and next-availability evidence only; never payload, exception, authority, tenant, or transport data.
