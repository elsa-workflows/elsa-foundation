# Elsa.Workflows.ExecutionEvidence

Records what a workflow actually did — activities executed, outcomes taken, variables written, incidents
raised, lifecycle transitions — and serves it back as one ordered stream over HTTP so an automated test
suite can assert on it. Install it in a test or QA host, exercise workflows, query the evidence, assert.

Feature name (manifest / `shells.json` key): **`WorkflowsExecutionEvidence`**.

Every request and response below was captured verbatim from a running `Elsa.Workbench` with this feature
enabled. Where a response is shown abridged, it says so.

## What this is not

This is an MVP. It deliberately makes **no** durability, restart, failover, distribution, or
definitive-negative claim:

- Evidence lives in process memory and dies with the process. There is no durable store and no provider.
- Buffers are bounded and drop the oldest evidence when full. A reader detects the loss through
  `firstSequence` (see _Detecting dropped evidence_) — but it is a detection signal, not a guarantee.
- A query timeout is inconclusive. "No record arrived" is not proof that nothing happened.
- There is no evidence-session concept. The correlation key is the workflow execution id, which the caller
  already holds because it started the workflow.
- **There is no tenant or per-caller isolation.** Any caller authorized for these endpoints can read any
  workflow's evidence on the host, including raw variable values and full exception messages.

For the end-state design — sessions, checkpoint-atomic durable intent, completeness/integrity barriers,
governed kind catalogs, capture profiles, Groundwork durability — see
[the Runtime Execution Evidence PRD](../../../../docs/plans/runtime-execution-evidence-prd.md) and epic
[#1132](https://github.com/elsa-workflows/elsa-foundation/issues/1132). This module is a reduced,
process-local demonstration and does not close [#1133](https://github.com/elsa-workflows/elsa-foundation/issues/1133).

## Enabling it

Three edits in the host (already done for `Elsa.Workbench`):

1. `<ProjectReference Include="...\Elsa\Workflows\ExecutionEvidence\Elsa.Workflows.ExecutionEvidence.csproj" />`
2. `typeof(WorkflowsExecutionEvidenceFeature).Assembly` in the CShells `.WithAssemblies(...)` catalog
3. `"WorkflowsExecutionEvidence": {}` under `CShells:Shells:default:Features` in `shells.json`

A host that omits all three carries no evidence behavior at all — no registrations, no endpoints, no
capture cost. That is how a production host should be composed.

## How capture works

`ExecutionEvidenceCheckpointEnricher` is an `IRuntimeCheckpointCommitEnricher`. The runtime resolves
enrichers with `GetServices<>`, so registering one more is purely additive and order-independent: **the
runtime required no changes to support this module.** The enricher returns the commit reference unchanged
and swallows every *capture* exception (logging a Warning), so a collector defect cannot fault the
workflow it observes.

Each `RuntimeCheckpointCommit` becomes one batch of records, applied to the buffer atomically:

| Source on the commit | Becomes |
|---|---|
| `Checkpoint.Name` | one lifecycle record whose `kind` is that name verbatim |
| `StateChanges.ActivityExecutionInspections` | one `Activity` record each, with type, authored id, status, outcomes |
| `StateChanges.WorkflowExecution.State.RootVariableFrame` | one `VariableSet` record per key whose captured state changed since the last checkpoint |
| `StateChanges.Incidents` | one `Incident` record each, with the failure message |

Batches are de-duplicated by checkpoint id, so checkpoint replay cannot fabricate duplicate facts.

## Endpoints

All three sit under `/_elsa/execution-evidence` and require an authenticated caller
(`ConfigurePermissions()`, i.e. the `*` wildcard permission). The samples below authenticate with a
cookie jar:

```bash
curl -s -c /tmp/elsa.jar -X POST http://localhost:5199/_elsa/identity/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"Password123!"}'
```

### 1. Read one workflow's evidence

```bash
curl -s -b /tmp/elsa.jar \
  "http://localhost:5199/_elsa/execution-evidence/workflows/12VyOGEJbS8?waitMs=20000"
```

| Parameter | Where | Default | Meaning |
|---|---|---|---|
| `workflowExecutionId` | route | — | The id returned by `POST /runtime/workflows/executables/{artifactId}/execute`. Blank or whitespace → `400`. |
| `after` | query | `0` | Exclusive cursor. Pass back the previous response's `lastSequence`. |
| `waitMs` | query | `0` | Long-poll budget in ms, clamped to `60000`. Returns as soon as records appear or the run goes terminal. |

`waitMs` is the reason a test suite needs no `sleep`: the first read happens before any delay, so it
costs nothing when the evidence is already there, and a sub-50ms budget never overshoots.

Response — abridged to four of the twenty-nine records, otherwise verbatim:

```json
{
  "records": [
    {
      "sequence": 1,
      "workflowExecutionId": "12VyOGEJbS8",
      "kind": "WorkflowStarted",
      "occurredAt": "2026-08-07T00:00:00.597616+00:00",
      "checkpointId": "checkpoint:5d727277dff3eced:checkpoint:WorkflowStarted",
      "correlationId": null,
      "activityExecutionId": null,
      "activityType": null,
      "authoredActivityId": null,
      "status": "Running",
      "outcomes": null,
      "name": null,
      "value": null,
      "valueDisposition": null,
      "message": null
    },
    {
      "sequence": 14,
      "workflowExecutionId": "12VyOGEJbS8",
      "kind": "VariableSet",
      "occurredAt": "2026-08-07T00:00:00.93586+00:00",
      "checkpointId": "checkpoint:947474e82aa7f496:start:12VyOJ0SN7i:intrinsic:12VyOJ0SN7i",
      "correlationId": null,
      "activityExecutionId": null,
      "activityType": null,
      "authoredActivityId": null,
      "status": null,
      "outcomes": null,
      "name": "orderId",
      "value": "ORD-1001",
      "valueDisposition": "captured",
      "message": null
    },
    {
      "sequence": 24,
      "workflowExecutionId": "12VyOGEJbS8",
      "kind": "Activity",
      "occurredAt": "2026-08-07T00:00:01.441747+00:00",
      "checkpointId": "checkpoint:59f878243f278285:invoke:12VyOJfBR0O:activity-completed:12VyOJfBR0O",
      "correlationId": null,
      "activityExecutionId": "12VyOJfBR0O",
      "activityType": "Elsa.Activities.Primitives.Activities.WriteLine",
      "authoredActivityId": "greet",
      "status": "Completed",
      "outcomes": ["Done"],
      "name": null,
      "value": null,
      "valueDisposition": null,
      "message": null
    },
    {
      "sequence": 28,
      "workflowExecutionId": "12VyOGEJbS8",
      "kind": "WorkflowCompleted",
      "occurredAt": "2026-08-07T00:00:01.476187+00:00",
      "checkpointId": "checkpoint:8f80a2ec4c2c309a:checkpoint:WorkflowCompleted:12VyOHK7jwb",
      "correlationId": null,
      "activityExecutionId": null,
      "activityType": null,
      "authoredActivityId": null,
      "status": "Completed",
      "outcomes": null,
      "name": null,
      "value": null,
      "valueDisposition": null,
      "message": null
    }
  ],
  "firstSequence": 1,
  "lastSequence": 29,
  "terminal": true,
  "matchedWorkflows": 1
}
```

Every optional field is emitted explicitly as `null` rather than omitted, so a typed client can bind the
whole record without presence checks. Enum values serialize as camelCase strings (`"captured"`).

**Cursor is exclusive.** Re-querying with the returned `lastSequence` yields nothing new:

```bash
curl -s -b /tmp/elsa.jar \
  "http://localhost:5199/_elsa/execution-evidence/workflows/12VyOGEJbS8?after=29"
```
```json
{"records":[],"firstSequence":1,"lastSequence":29,"terminal":true,"matchedWorkflows":1}
```

**An unknown workflow is not an error** — it is an empty, non-terminal page with no matches. This is the
shape that makes a timeout inconclusive rather than a negative proof:

```bash
curl -s -b /tmp/elsa.jar \
  "http://localhost:5199/_elsa/execution-evidence/workflows/does-not-exist"
```
```json
{"records":[],"firstSequence":0,"lastSequence":0,"terminal":false,"matchedWorkflows":0}
```

A blank or whitespace route segment is a `400`, so a client that interpolated an empty id learns that
rather than silently reading an empty stream:

```bash
curl -s -b /tmp/elsa.jar "http://localhost:5199/_elsa/execution-evidence/workflows/%20"
```
```
A non-empty workflow execution id is required.
```

### 2. Read a correlation's evidence

Spans several workflow executions without needing an evidence-session concept. Supports `after` and
`waitMs` on exactly the same terms as endpoint 1.

```bash
curl -s -b /tmp/elsa.jar \
  "http://localhost:5199/_elsa/execution-evidence?correlationId=order-1001&after=0&waitMs=5000"
```
```json
{"records":[],"firstSequence":0,"lastSequence":0,"terminal":false,"matchedWorkflows":0}
```

A blank `correlationId` is a `400`:

```bash
curl -s -b /tmp/elsa.jar "http://localhost:5199/_elsa/execution-evidence?correlationId="
```
```
A non-empty 'correlationId' query parameter is required.
```

> **Two caveats, both verified against the running server.**
>
> `POST /runtime/workflows/executables/{id}/execute` takes no `correlationId`, so a directly-executed run
> has `correlationId: null` and this endpoint returns nothing for it — which is why the sample above is
> empty. Correlation is populated on stimulus-dispatched runs (`POST /runtime/workflows/stimuli` accepts a
> `correlationId`) and inherited by child workflows. Use endpoint 1 for a run you started yourself.
>
> **`terminal` is not a completeness signal here.** It means every workflow *currently matched* is
> terminal — and a child that has not yet committed its first checkpoint is not matched at all. A parent
> that fans out to three children reports `terminal: true` with `matchedWorkflows: 1` the moment the parent
> finishes. Assert on `matchedWorkflows` reaching the fan-out you expect, not on `terminal` alone.
>
> Sequences are assigned per workflow execution, so `after` is applied independently to each matched
> workflow rather than to one global order.

### 3. Reset between tests

```bash
# One workflow
curl -s -b /tmp/elsa.jar -X DELETE \
  "http://localhost:5199/_elsa/execution-evidence?workflowExecutionId=12VyOGEJbS8"    # 204

# Everything on the host — explicit opt-in required
curl -s -b /tmp/elsa.jar -X DELETE "http://localhost:5199/_elsa/execution-evidence?all=true"   # 204
```

An unqualified `DELETE` is a `400`, because the buffer is shared and a host running two suites at once must
not make "destroy the other suite's evidence" the easiest call to type:

```bash
curl -s -b /tmp/elsa.jar -X DELETE "http://localhost:5199/_elsa/execution-evidence"
```
```
Supply 'workflowExecutionId' to drop one workflow execution's evidence, or 'all=true' to drop every workflow execution's evidence on this host.
```

## Record shape

| Field | Type | Populated for |
|---|---|---|
| `sequence` | `long` | always — strictly increasing per workflow execution; the query cursor |
| `workflowExecutionId` | `string` | always |
| `kind` | `string` | always — see the vocabulary below |
| `occurredAt` | `DateTimeOffset` | always — the checkpoint's time, not the observation time |
| `checkpointId` | `string` | always — the runtime checkpoint this fact came from, and the replay dedupe key |
| `correlationId` | `string?` | runs that carry one |
| `activityExecutionId` | `string?` | `Activity`, and incidents attributed to an activity |
| `activityType` | `string?` | `Activity` — the full CLR type key |
| `authoredActivityId` | `string?` | `Activity` — the node id you authored, e.g. `greet` |
| `status` | `string?` | `Activity` (activity status) and lifecycle records (workflow status) |
| `outcomes` | `string[]?` | `Activity` — e.g. `["Done"]` |
| `name` | `string?` | `VariableSet` — the variable name |
| `value` | JSON | `VariableSet`, only when `valueDisposition` is `captured` |
| `valueDisposition` | `string?` | `VariableSet` — see below |
| `message` | `string?` | `Incident` — the failure message |

### Value dispositions

A `value` of `null` is never self-explanatory, so every `VariableSet` says why it holds what it holds:

| Disposition | Meaning |
|---|---|
| `captured` | The inline value is on the record. |
| `null` | The variable was explicitly assigned null. |
| `absent` | Declared, never assigned. |
| `external` | The runtime holds the value by external reference; this module does not resolve external storage. |
| `sensitive` | Withheld — the value's protection policy marks it sensitive. |
| `truncated` | Withheld — the value exceeded `MaxInlineValueLength`. |

A withheld value still emits a record whenever the underlying value *changes*: change detection uses a
SHA-256 digest of the full content, so rotating a secret or rewriting an over-large payload produces
evidence that a write happened, without the value itself ever entering the buffer.

### Kinds

`Activity`, `VariableSet` and `Incident` are derived by this module. Every other kind is a runtime
checkpoint name reused verbatim from `RuntimeCheckpointNames`, so the vocabulary stays meaningful against
the runtime's own inspection API: `WorkflowStarted`, `WorkflowCompleted`, `WorkflowSuspended`,
`WorkflowFaulted`, `WorkflowCancelled`, `ActivityScheduled`, `ActivityStarted`, `ActivityCompleted`,
`ActivitySuspended`, `ActivityCancelled`, `ActivityAttemptClaimed`, `ActivityInspectionCaptured`,
`IntrinsicCompleted`, `BookmarkCreated`, `BookmarkConsumed`, `IncidentRecorded`, and others.

## Detecting dropped evidence

`firstSequence` is the lowest sequence still buffered. Compare it against your cursor on every read:

```
if (page.firstSequence > cursor + 1) → evidence between them was evicted by the buffer caps
```

When that fires, **negative assertions over the range are unsound** — "no incident was recorded" may
simply mean the incident was trimmed. Fail the test loudly rather than concluding absence. `firstSequence`
is `0` when nothing is buffered for the workflow at all.

## Worked example: a complete run

A `Sequence` containing a set-variable intrinsic and one `WriteLine`, with an `orderId` variable declared
with an empty default. The full 29-record stream, exactly as captured:

```
  1  WorkflowStarted            [Running]
  2  VariableSet                orderId="" (captured)
  3  ActivityScheduled
  4  Activity                   Sequence @root [Scheduled]
  5  ActivityStarted
  6  Activity                   Sequence @root [Running]
  7  ActivityAttemptClaimed
  8  ActivityInspectionCaptured
  9  Activity                   Sequence @root [Running]
 10  ActivityScheduled
 11  Activity                   set @assign-order [Scheduled]
 12  IntrinsicCompleted         [Running]
 13  Activity                   set @assign-order [Completed] -> Done
 14  VariableSet                orderId="ORD-1001" (captured)
 15  ActivityAttemptClaimed
 16  ActivityInspectionCaptured
 17  Activity                   Sequence @root [Running]
 18  ActivityScheduled
 19  Activity                   WriteLine @greet [Scheduled]
 20  ActivityStarted
 21  Activity                   WriteLine @greet [Running]
 22  ActivityAttemptClaimed
 23  ActivityCompleted
 24  Activity                   WriteLine @greet [Completed] -> Done
 25  ActivityAttemptClaimed
 26  ActivityCompleted
 27  Activity                   Sequence @root [Completed] -> Done
 28  WorkflowCompleted          [Completed]
 29  Activity                   Sequence @root [Completed]
```

Note sequence 2: the variable's declared default is recorded before any activity runs.

## Worked example: a failing run

The same shape with a `WriteLine` whose `text` input is a JavaScript expression that throws at runtime.
Captured verbatim:

```json
{
  "sequence": 7,
  "workflowExecutionId": "12VyOSeVod6",
  "kind": "Incident",
  "occurredAt": "2026-08-07T00:00:04.395998+00:00",
  "checkpointId": "checkpoint:12VyOSeVod6:scheduler-poison:bc0fe6e20dfe9415",
  "correlationId": null,
  "activityExecutionId": null,
  "activityType": null,
  "authoredActivityId": null,
  "status": null,
  "outcomes": null,
  "name": null,
  "value": null,
  "valueDisposition": null,
  "message": "Scheduler work item '3d57bd9fe44ac304:start:12VyOSr3yw7' (StartActivity) was poisoned during dispatch by handler 'WorkflowStartActivitySchedulerWorkHandler' after 1 failure(s): System.InvalidOperationException: Input 'text' on executable node 'boom' failed to materialize or evaluate its portable 'JavaScript' expression with fingerprint 'sha256:647c1bf6e5b645cd260d586b448882e96a55f9b5f39cd370511ffd73081813f7'. ---> Jint.Runtime.JavaScriptException: evidence demo failure"
}
```

That page had 7 records and **`"terminal": false`** — a run blocked by an incident is parked, not
terminated. A suite that waits only for `terminal` will burn its whole `waitMs` on a failing test. Wait for
`terminal || records.any(kind == "Incident")` instead.

## Assertion recipes

```
# "activity X ran and took outcome Y"
records.any(r => r.kind == "Activity"
              && r.authoredActivityId == "greet"
              && r.status == "Completed"
              && r.outcomes.contains("Done"))

# "variable orderId ended up as ORD-1001" — check the disposition, not just the value
last = records.where(r => r.kind == "VariableSet" && r.name == "orderId").last()
last.valueDisposition == "captured" && last.value == "ORD-1001"

# "the run completed without incident" — only sound when no evidence was dropped
page.firstSequence <= 1 && page.terminal && records.none(r => r.kind == "Incident")

# "these activities ran in this order" — sequence is the ordering authority, not occurredAt
records.where(r => r.kind == "Activity" && r.status == "Completed")
       .orderBy(r => r.sequence)
       .select(r => r.authoredActivityId)

# poll loop — always bounded; an unstarted workflow returns empty and non-terminal forever
after = 0
deadline = now + 30s
loop {
    page = GET /_elsa/execution-evidence/workflows/{id}?after={after}&waitMs=5000
    if (page.firstSequence > after + 1) fail("evidence was evicted; assertions are unsound")
    consume(page.records)
    after = page.lastSequence
    if (page.terminal || page.records.any(kind == "Incident")) break
    if (now > deadline) fail("timed out — inconclusive, not a negative result")
}
```

## Known behavior

- **Declared-but-unset variables produce an `absent` record.** Assert on `valueDisposition`, not merely on
  the record's presence.
- **The enricher runs before the persistence decision.** A `Skip` decision records a fact whose checkpoint
  was never persisted, as does a checkpoint whose store call subsequently fails. This matters in practice:
  `Elsa.Workbench` runs `WorkflowsRuntimeCheckpointPersistence` in `"Mode": "Coalesced"`, where checkpoints
  are folded and a `CoalescedSegmentFlush` kind appears in the stream. Replay is handled by checkpoint-id
  dedupe; skip is knowingly over-recorded. A durable implementation would observe the commit store instead.
- **The replay-dedupe window is bounded** by `MaxTrackedCheckpointsPerWorkflow`. A replayed checkpoint older
  than that window can be recorded twice.
- **`DELETE` for one workflow resets its sequences to 1.** A caller holding an older cursor sees a rewind,
  and `firstSequence` cannot signal it. Don't delete a workflow's evidence while still polling it.
- **The stream is verbose.** 29 records for a two-activity workflow, because every checkpoint name becomes a
  record. Filter by `kind`; do not assert on record counts.
- **Only root-frame variables are captured.** Container- and iteration-scoped frames are not diffed.
- **Ordering authority is `sequence`, not `occurredAt`.** Several records routinely share one checkpoint
  timestamp.

## Settings

Bindable from `shells.json` under the feature's key, e.g.
`"WorkflowsExecutionEvidence": { "MaxRecordsPerWorkflow": 50000 }`.

| Setting | Default | Effect |
|---|---|---|
| `MaxRecordsPerWorkflow` | `10000` | Oldest records dropped past this; the drop is visible via `firstSequence`. |
| `MaxWorkflows` | `1000` | Least-recently-written workflow evicted whole past this. |
| `MaxTrackedCheckpointsPerWorkflow` | `10000` | Replay de-duplication window. |
| `MaxInlineValueLength` | `8192` | Values whose raw JSON is longer are recorded as `truncated`. |
| `RedactSensitiveValues` | `true` | Withhold values whose protection policy marks them sensitive. |

## Tests

`tests/Elsa/Workflows/ExecutionEvidence/Tests` — 75 tests covering feature registration and settings flow,
the commit-to-record mapping, all six value dispositions and their change detection, replay dedupe and its
bounded window, atomic append under mid-batch failure, buffer caps and the `firstSequence` gap signal,
concurrent capture and query, and all three endpoints over a TestServer host.
