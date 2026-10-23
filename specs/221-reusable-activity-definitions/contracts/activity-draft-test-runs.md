# Activity draft Test Run contract

## Dispatch

`POST /publishing/activity-drafts/{draftId}/test-runs`

The request supplies `expectedRevision`, a client-generated `idempotencyKey`, tri-state inputs, and an optional
correlation ID. The draft ID comes only from the route. Dispatch binds the key permanently to the complete request
fingerprint for that draft. Reusing the key with different revision, inputs, or correlation is a conflict.
Idempotency keys and correlation IDs are each limited to 200 characters before hashing or persistence.

Inputs have exact tri-state semantics: an omitted input is absent, `Absent` must not carry a value, and `Present`
must carry a value. JSON `null` is a valid explicit present value. Unknown or contradictory states are rejected
with structured validation diagnostics before Runtime dispatch.

The Test Run identity and Runtime workflow execution identity are deterministic for the caller's durable operation
scope, draft, and key. Each caller tenant has a distinct scope, including when multiple tenants run the same global
draft, and the explicit tenantless operation scope is distinct even from a tenant whose identifier is `global`.
The receipt retains the resource tenant separately from its operation owner. Replay first derives the caller's
operation scope and performs a tenant-scoped receipt lookup, without dereferencing the mutable draft. A receipt is
returned only after its persisted operation owner and resource tenant are authorized; a foreign caller therefore
cannot replay or learn it. A missing receipt, or a `Preparing` receipt without dispatch material, then dereferences
and authorizes the exact draft revision. The synthetic wrapper remains an implementation detail: it does not
become authored activity identity and its payload is never projected to the client.

## Durable lookup

- `GET /publishing/activity-test-runs/{testRunId}`
- `GET /publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}`

Both routes return the same authorization-filtered projection. A receipt survives source-reference expiry and
contains only the hash of the raw idempotency key. Lookup reconciles the receipt with Runtime workflow and activity
execution state, so `outerActivityExecutionId` appears when the root boundary execution has been materialized.

The `status` field first reports receipt/dispatch states and then Runtime execution states once Runtime Evidence
exists. Validation rejection has failure kind `Validation`; a Runtime start/dispatch rejection has failure kind
`RuntimeDispatch`. Safe failure codes and ordered validation diagnostics may be returned. Raw Runtime exceptions
and wrapper/provider payloads may not be returned.

`expiration` reports these independent facts:

- source-reference expiry and whether the reference is still retained;
- whether Runtime Evidence is retained;
- whether the Run is still active;
- receipt expiry.

Source-reference expiry does not imply deletion while a retained execution still pins the artifact. Receipt
expiry is later and does not claim that Runtime Evidence has been deleted.

## Cancellation

`POST /publishing/activity-test-runs/{testRunId}/cancel`

The response advertises whether cancellation is a host capability and whether policy currently permits it.
Allowed cancellation enqueues the ordinary Runtime `Cancel` control-plane command with a stable idempotency key.
Repeated requests therefore do not create distinct cancellation effects. Projection reports `Requested`,
`Cancelling`, `Terminal`, or `Unavailable`; `Available` is reported only while cancellation is actually allowed.
An initial receipt without sufficient Runtime evidence and a policy-denied request report and persist
`Unavailable`. The terminal Runtime status remains the authoritative execution truth.

## Retrying ambiguous dispatch

If Runtime accepted a start but its acknowledgement was lost, the receipt records `DispatchAmbiguous`. Retrying
the original request with the same key first reconciles the deterministic execution identity against Runtime
Evidence. If evidence has not materialized yet, dispatch repeats from the persisted artifact and Source Reference
using the same Runtime idempotency key. It never rereads or recompiles a later mutable draft. A `Preparing` receipt
has not persisted dispatch material and therefore cannot have dispatched; it may resume only while the exact draft
revision remains available, otherwise it terminates as a validation rejection. Runtime duplicate recognition
reconciles the receipt without creating a second Run. A deliberate rerun uses a new key and therefore creates a
new workflow execution while content-addressed artifacts may still be reused.
