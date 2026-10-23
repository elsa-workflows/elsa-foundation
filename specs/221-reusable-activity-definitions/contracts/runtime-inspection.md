# Contract: Hierarchical Activity Execution Inspection

This contract extends the existing Runtime-owned activity-execution inspection from spec 079. It does not create a parallel “custom activity run” model. The existing detail remains the canonical lifecycle/evidence view; reusable activity boundaries add optional boundary and attempt facts plus lazy hierarchy/layout reads.

## 1. Routes

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}` | Existing activity execution detail, extended with optional attempt/boundary facts. |
| `GET` | `/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants` | Cursor-page committed descendants owned by this execution scope. |
| `GET` | `/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout` | Read the pinned boundary layout used by this execution. |

The workflow-instance detail route continues returning lightweight activity execution summaries. It does not eagerly hydrate descendant pages, layouts, or value payloads.

## 2. Extended activity execution detail

Existing fields remain as defined by `ActivityExecutionInspectionView`. Two optional fields are added:

```json
{
  "activityExecutionId": "actexec-calculate-total-attempt-2",
  "workflowExecutionId": "wfexec-checkout-1",
  "executableNodeId": "node-ns-outer-calculate-total",
  "authoredActivityId": "node-calculate-total",
  "activityType": "elsa.graph-activity",
  "activityTypeVersion": "1",
  "status": "Completed",
  "subStatus": null,
  "executionSequence": 42,
  "scheduledAt": "2026-07-15T12:00:00Z",
  "startedAt": "2026-07-15T12:00:01Z",
  "completedAt": "2026-07-15T12:05:00Z",
  "firstCheckpointId": "checkpoint:activity-entry:...",
  "lastCheckpointId": "checkpoint:activity-exit:...",
  "lastCommittedAt": "2026-07-15T12:05:00Z",
  "provenance": {},
  "outcomeNames": ["Done"],
  "bookmarks": [],
  "incidents": [],
  "valueSnapshots": [],
  "attempt": {
    "attemptNumber": 2,
    "firstAttemptActivityExecutionId": "actexec-calculate-total-attempt-1",
    "previousAttemptActivityExecutionId": "actexec-calculate-total-attempt-1"
  },
  "boundary": {
    "kind": "ActivityGraph",
    "definitionId": "activity-def-calculate-total",
    "definitionVersionId": "activity-ver-calculate-total-2",
    "version": "2.0.0",
    "templateHash": "sha256-template-2",
    "invocationOrigin": [
      { "kind": "WorkflowRoot", "id": "workflow-ver-checkout-4" },
      { "kind": "AuthoredNode", "id": "node-calculate-total" }
    ],
    "executionScopeId": "actexec-calculate-total-attempt-2",
    "hasChildren": true,
    "directChildCount": 4,
    "committedDescendantCount": 14,
    "aggregate": {
      "status": "Completed",
      "total": 14,
      "scheduled": 0,
      "running": 0,
      "suspended": 0,
      "completed": 13,
      "faulted": 1,
      "cancelled": 0,
      "blockingIncidentCount": 0,
      "retryCount": 1,
      "lastExecutionSequence": 41
    },
    "layoutAvailable": true
  },
  "metadata": {}
}
```

Rules:

- `attempt` is optional for legacy/non-retried executions; when present, attempt `1` points `firstAttemptActivityExecutionId` to itself and has no previous id.
- `boundary` is present only when the executable node declares a reusable activity boundary understood by Runtime inspection.
- The top-level `status` is the outer activity's own committed lifecycle. `boundary.aggregate.status` is derived from committed descendants and never overwrites it.
- A completed retry can have a prior faulted descendant/attempt; counts preserve that evidence.
- Value snapshots remain governed by existing Runtime payload capture policy and the caller's value permission.

## 3. Descendant page request

```http
GET /runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants
    ?cursor={opaque}
    &limit=100
    &include=outcomes,bookmarks,incidents
```

Rules:

- Omitting `cursor` starts a new committed snapshot and fixes its high watermark.
- `limit` is a page-size request, not a graph-depth limit. The server may cap it to a configured safe maximum and reports the effective value.
- `include` selects safe summaries only. Value payloads are never included in hierarchy pages; the existing per-execution detail endpoint owns value snapshots.
- The page contains executions whose nearest `ExecutionScopeId` equals the requested outer `activityExecutionId`. A nested reusable activity appears as one child boundary; its own descendants are loaded by calling the same route with that child execution id.
- The complete nested run is therefore inspectable by click-through without one unbounded response.

## 4. `ActivityExecutionHierarchyPageView`

```json
{
  "root": {
    "workflowExecutionId": "wfexec-checkout-1",
    "activityExecutionId": "actexec-calculate-total-attempt-2",
    "executionScopeId": "actexec-calculate-total-attempt-2",
    "definitionVersionId": "activity-ver-calculate-total-2",
    "templateHash": "sha256-template-2"
  },
  "committedThroughSequence": 84,
  "effectiveLimit": 100,
  "items": [],
  "nextCursor": null
}
```

`committedThroughSequence` fixes the snapshot. Executions committed after sequence `84` are not injected into later pages from the same cursor chain; a fresh first-page request observes a newer watermark.

## 5. `ActivityExecutionHierarchyItemView`

```json
{
  "activityExecutionId": "actexec-calc-discount-3",
  "workflowExecutionId": "wfexec-checkout-1",
  "executableNodeId": "node-ns-calc-discount",
  "authoredActivityId": "node-calc-discount",
  "activityType": "acme.calculate-discount",
  "activityTypeVersion": "3.2.0",
  "status": "Completed",
  "subStatus": null,
  "executionSequence": 39,
  "scheduledAt": "2026-07-15T12:01:00Z",
  "startedAt": "2026-07-15T12:01:01Z",
  "completedAt": "2026-07-15T12:01:02Z",
  "parentActivityExecutionId": "actexec-sequence-1",
  "schedulingActivityExecutionId": "actexec-sequence-1",
  "relativeDepth": 2,
  "branchId": null,
  "iterationId": "discount-loop:3",
  "outcomeNames": ["Done"],
  "bookmarkCount": 0,
  "incidentCount": 0,
  "blockingIncidentCount": 0,
  "attempt": null,
  "boundary": null,
  "metadata": {}
}
```

Rules:

- Items are ordered by `executionSequence`, then `activityExecutionId` ordinal.
- `relativeDepth` is relative to the requested boundary. It is computed iteratively and is not persisted as an unbounded call-stack path.
- `parentActivityExecutionId` expresses structural runtime parentage; `schedulingActivityExecutionId` remains temporal provenance and can differ.
- Repeated executions of one authored node remain separate items.
- A nested boundary uses the same compact boundary shape as detail (`kind`, exact activity version/template, counts, aggregate, layout availability), allowing the client to render an expansion affordance.

## 6. Cursor contract

The cursor is provider-opaque and cryptographically integrity-protected or otherwise unforgeable by the chosen store adapter. It binds:

- tenant and authorization/redaction profile,
- workflow execution id,
- root activity execution/scope id,
- include filter and effective page size,
- committed high watermark,
- last deterministic ordering position,
- store/provider schema version.

Outcomes:

- malformed cursor: `400 activity.request.invalid`;
- another root/query/tenant/permission profile: `409 activity.cursor.binding-mismatch`;
- expired/trimmed/unavailable snapshot: `410 activity.cursor.expired`;
- a valid terminal page: `200` with `nextCursor = null`.

The server never silently restarts an invalid cursor from the first page.

## 7. Pinned boundary layout

```http
GET /runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout
```

Response: `200 OK` with `ActivityExecutionLayoutView`.

```json
{
  "workflowExecutionId": "wfexec-checkout-1",
  "activityExecutionId": "actexec-calculate-total-attempt-2",
  "artifactId": "workflow-artifact-sha256-...",
  "sourceReferenceId": "source-ref-published-checkout-4",
  "selection": "ExecutedReference",
  "boundaryOrigin": [
    { "kind": "WorkflowRoot", "id": "workflow-ver-checkout-4" },
    { "kind": "AuthoredNode", "id": "node-calculate-total" }
  ],
  "templateHash": "sha256-template-2",
  "nodes": [
    {
      "templateNodeId": "template-node-entry",
      "authoredActivityId": "node-entry",
      "executableNodeId": "node-ns-entry",
      "x": 100,
      "y": 80,
      "width": 180,
      "height": 64,
      "additionalProperties": null
    }
  ],
  "connections": [
    {
      "source": { "executableNodeId": "node-ns-entry", "port": "Done" },
      "target": { "executableNodeId": "node-ns-map-output", "port": null },
      "vertices": null
    }
  ],
  "nestedBoundaries": [
    {
      "activityExecutionId": "actexec-nested-tax-1",
      "executableNodeId": "node-ns-tax",
      "templateHash": "sha256-tax-2",
      "layoutAvailable": true
    }
  ]
}
```

Rules:

- `selection` is `ExecutedReference`; Runtime never chooses current Design layout. If historical imported material lacks a sidecar, `selection=Automatic` and an empty/automatic record set may be returned explicitly.
- The application-layer inspector joins the executed artifact's structural material to the Source Reference's boundary segment; Runtime execution itself does not depend on layout.
- Node identities are the exact placed executable identities used by execution evidence. Template/authored ids remain as provenance for tools.
- Layout contains structure only, no captured values or provider source payloads.
- Missing layout for an otherwise valid boundary returns `200` with `selection=Automatic`, not `404`.

## 8. Aggregate status

Aggregate status is derived from committed descendant summaries at the page/detail watermark:

1. any blocking fault -> `Faulted`;
2. else any cancelling/cancelled terminal path with no running work -> `Cancelled`;
3. else any suspended descendant with no running work -> `Suspended`;
4. else any scheduled/running descendant -> `Running`;
5. else all terminal successful/skipped descendants -> `Completed`;
6. no committed descendants -> `Empty`.

Counts are always returned so clients need not infer meaning from the status string. A retry does not erase prior faulted counts; `blockingIncidentCount` and current winning attempt determine whether the aggregate remains blocking.

## 9. Authorization and redaction

Two independent permissions are evaluated on every detail, hierarchy, and layout read:

- **Structure inspection**: identities, statuses, hierarchy, layout, outcomes, bookmark/incident counts, and safe messages.
- **Sensitive value inspection**: captured payloads on the existing detail endpoint, still subject to Runtime payload-capture policy.

Having value permission cannot recover a value Runtime did not capture. Lacking value permission never removes the structural node; it returns the existing explicit redaction state where a value slot is visible. Tenant authorization is applied before existence disclosure, so unauthorized roots use the same not-found/denied policy as the rest of Runtime API.

## 10. Recovery invariants visible through inspection

- Entry checkpoint identity is visible before descendants appear.
- A descendant bookmark remains attached to its actual activity execution.
- After restart/resume, already committed descendant execution ids and sequences do not change or repeat.
- Exit checkpoint captures boundary outputs/outcome and terminal status once.
- Faulted attempts, causal inner incidents, outer boundary incident, and retry lineage remain linkable.
- Cancellation terminal status is not visible until descendant cleanup is committed.
