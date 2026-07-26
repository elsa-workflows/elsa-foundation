# Runtime Alteration API Contract

## Routes and permissions

| Method | Route | Permission | Success |
|---|---|---|---|
| POST | `/runtime/workflows/alteration-plans` | `workflow-runtime.manage` | `202 Accepted` |
| GET | `/runtime/workflows/alteration-plans/{planId}` | `workflow-runtime.read` | `200 OK` |
| GET | `/runtime/workflows/alteration-plans/{planId}/jobs/page` | `workflow-runtime.read` | `200 OK` |
| GET | `/runtime/workflows/alteration-plans/{planId}/jobs/{jobId}` | `workflow-runtime.read` | `200 OK` |
| POST | `/runtime/workflows/alteration-plans/{planId}/cancel` | `workflow-runtime.manage` | `202 Accepted` or terminal no-op `200 OK` |

All lookups are tenant/authority scoped. An inaccessible object and a nonexistent object both return
`404`.

## Submission

`Idempotency-Key` is a required nonblank header. The request contains exactly one target selector and
one or more ordered alteration envelopes.

```json
{
  "target": {
    "executionIds": ["exec-a", "exec-b"]
  },
  "alterations": [
    {
      "kind": "CancelWorkflow",
      "schemaVersion": 1,
      "payload": {}
    }
  ]
}
```

Query target:

```json
{
  "target": {
    "query": {
      "definitionId": "orders",
      "status": "Suspended",
      "runKind": "Workflow",
      "from": "2026-07-01T00:00:00Z",
      "to": "2026-07-26T23:59:59Z",
      "correlationId": "customer-42",
      "workflowExecutionId": null,
      "artifactId": null,
      "matchAllAuthorized": false
    }
  },
  "alterations": [
    {
      "kind": "ModifyVariable",
      "schemaVersion": 1,
      "payload": {
        "variableKey": "order.status",
        "value": "ReadyForReview"
      }
    }
  ]
}
```

All non-null query predicates are ANDed. `tenantId`, paging, and cursor are not accepted because
authorization supplies the scope and the capture pump owns paging. An otherwise empty query is valid
only when `matchAllAuthorized` is explicitly true. Unknown enum values and `from > to` are `422`.

Explicit IDs are trimmed, deduplicated, and sorted for request canonicalization. Every requested ID
gets one target record; missing or inaccessible IDs become safe failed jobs after sealing without
revealing cross-tenant existence.

## Idempotency

The tenant/authority scope, normalized target, ordered alteration list, exact kind/version, and
canonical JSON payload form the canonical request hash.

- Same key and hash: return the same plan with `submissionDisposition: Replayed` and `202`.
- Same key and different hash: `409` with code `AlterationIdempotencyConflict`.
- A new request rejected before plan creation does not consume the key.

The key and plaintext request are not exposed by reads. Results never contain payloads.

## Submission response

```json
{
  "planId": "alteration-plan-...",
  "status": "CapturingTargets",
  "submissionDisposition": "Accepted",
  "createdAt": "2026-07-26T12:00:00Z",
  "links": {
    "self": "/runtime/workflows/alteration-plans/alteration-plan-...",
    "jobsPage": "/runtime/workflows/alteration-plans/alteration-plan-.../jobs/page",
    "cancel": "/runtime/workflows/alteration-plans/alteration-plan-.../cancel"
  }
}
```

The response carries `Location` with the `self` link.

## Plan read

```json
{
  "planId": "alteration-plan-...",
  "status": "Running",
  "createdAt": "...",
  "sealedAt": "...",
  "startedAt": "...",
  "completedAt": null,
  "cancellationRequestedAt": null,
  "target": {
    "kind": "Query",
    "filterNames": ["definitionId", "status"]
  },
  "alterations": [
    { "ordinal": 0, "kind": "ModifyVariable", "schemaVersion": 1 }
  ],
  "counts": {
    "capturedSoFar": 420,
    "targetCount": 420,
    "pending": 300,
    "running": 8,
    "succeeded": 100,
    "failed": 12,
    "cancelled": 0
  },
  "failure": null,
  "links": {}
}
```

Stable plan states:

- `CapturingTargets`
- `Queued`
- `Running`
- `Cancelling`
- `Completed`
- `CompletedWithFailures`
- `Failed`
- `Cancelled`

There is no public paused state. Capacity rejection before admission is `429` plus `Retry-After`.
Transient capacity loss after acceptance retains the current active phase and retries with
backoff; a non-retryable orchestration failure becomes `Failed`.

## Job paging and order

`GET .../jobs/page?take=25&cursor=...` defaults to 25 and clamps at 100. The opaque cursor is scoped to
the plan and invalid under another plan. Order is `captureOrdinal` ascending, then `jobId` ordinal.
Execution-state changes never affect order.

```json
{
  "items": [],
  "nextCursor": null,
  "hasNext": false,
  "count": 0,
  "totalCount": 420
}
```

During capture, jobs are not publicly claimable; the page may expose admitted target summaries, but
`totalCount` is progress rather than final until `sealedAt` is non-null.

Stable job states:

- `Pending`
- `Running`
- `Succeeded`
- `Failed`
- `Cancelled`

## Job read and outcomes

```json
{
  "jobId": "alteration-job-...",
  "planId": "alteration-plan-...",
  "workflowExecutionId": "exec-a",
  "status": "Failed",
  "createdAt": "...",
  "startedAt": "...",
  "completedAt": "...",
  "failure": {
    "code": "AlterationPreflightFailed",
    "message": "The requested alteration is not valid for the current execution."
  },
  "outcomes": [
    {
      "ordinal": 0,
      "kind": "ModifyVariable",
      "schemaVersion": 1,
      "status": "Failed",
      "code": "VariableRevisionConflict",
      "message": "The captured variable frame is no longer current.",
      "recordedAt": "..."
    },
    {
      "ordinal": 1,
      "kind": "ScheduleActivity",
      "schemaVersion": 1,
      "status": "Skipped",
      "code": "SkippedAfterPreflightFailure",
      "message": "Not applied because an earlier alteration failed preflight.",
      "recordedAt": "..."
    }
  ]
}
```

If complete preflight fails at ordinal N, ordinals before N are also `Skipped` with
`NotAppliedDueToPreflightFailure`; they are never labelled successful because no mutation committed.

## Cancellation

- Active plan first request: transition to `Cancelling`, return `202`.
- Repeated request while cancelling: return current plan summary with `202`.
- Terminal plan: no-op, return current plan summary with `200`.
- Missing/inaccessible plan: `404`.

Cancellation stops capture or pending jobs. A running job finishes its atomic checkpoint.

## Error status contract

| Status | Meaning |
|---:|---|
| 400 | Malformed JSON/header/path/cursor or invalid page syntax |
| 401/403 | Framework authentication/permission result |
| 404 | Missing or inaccessible plan/job |
| 409 | Idempotency content conflict or job/plan path mismatch |
| 422 | Unknown kind/version, invalid selector/query, invalid built-in payload, or invalid alteration composition |
| 429 | Admission backpressure before plan creation; includes `Retry-After` |
| 500 | Unexpected error; ProblemDetails contains no raw exception |

Runtime preflight conflicts are durable failed job outcomes, not synchronous submission errors.

## Capability links

Add these relations to Runtime API capability version 1:

- `workflow-alteration-plans`
- `workflow-alteration-plan`
- `workflow-alteration-plan-jobs-page`
- `workflow-alteration-job`
- `workflow-alteration-plan-cancel`

They are advertised only when the Runtime API has composed alteration orchestration and its selected
stores.
