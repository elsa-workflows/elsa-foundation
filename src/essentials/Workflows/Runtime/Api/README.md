# Workflow Runtime API

`Elsa.Workflows.Runtime.Api` owns supported management-client operations over runtime state: immutable workflow executables, read-only executable provenance, workflow execution and instance inspection, detached dispatch inspection, stimulus dispatch, and runtime diagnostics settings. Design authoring and publication/source-reference mutation belong to their own domains.

See the [domain-owned API specification](../../../../../specs/092-domain-owned-apis/spec.md) and [Elsa glossary](../../../../../docs/glossary/elsa.md) for the lifecycle and ownership model.

## Composition

Add `WorkflowsRuntimeApiFeature` to the active shell. The feature composes the host-agnostic runtime engine with `AddWorkflowRuntime()`, registers the operation services the endpoint classes dispatch to, and supplies the executable inspector. Compose durable Runtime stores separately when in-memory defaults are insufficient. Optional trigger, coalescing, resumption, HTTP, and garbage-collection features remain independently selectable.

This package does not depend on `Elsa.Workbench`; a worker, custom application, or reference server may compose it.

Durable alteration plans require a restart-stable AES-256 key ring. Configure
`WorkflowAlterationPayloadProtectionActiveKeyId` and its matching base64 entry in
`WorkflowAlterationPayloadProtectionKeys`; retain old entries until every plan encrypted with them has expired.
The reference server's committed key is development/demo-only. Its Production overlay deliberately selects an
unconfigured key so alteration admission cannot silently fall back to process-local protection. For example, supply:

```text
CShells__Shells__default__Features__WorkflowsRuntimeApi__WorkflowAlterationPayloadProtectionActiveKeyId=primary
CShells__Shells__default__Features__WorkflowsRuntimeApi__WorkflowAlterationPayloadProtectionKeys__primary=<base64-encoded 32-byte key>
```

## Supported routes

| Area | Routes |
|---|---|
| Executables | `GET /runtime/workflows/executables`, `GET /runtime/workflows/executables/{artifactId}`, `GET /runtime/workflows/executables/{artifactId}/provenance` |
| Execution | `POST /runtime/workflows/executables/{artifactId}/execute`, `POST /runtime/workflows/stimuli` |
| Activation slots | `GET /runtime/workflows/activation-slots/{definitionId}`, `GET /runtime/workflows/activation-slots/{definitionId}/{slotName}` |
| Instances | `GET /runtime/workflows/instances`, `GET /runtime/workflows/instances/page`, `GET /runtime/workflows/instances/{workflowExecutionId}`, `GET .../incidents`, `GET .../activity-executions/{activityExecutionId}` |
| Detached dispatches | `GET /runtime/workflows/dispatches?parentWorkflowExecutionId=...|childWorkflowExecutionId=...|status=...`, `GET /runtime/workflows/dispatches/{dispatchId}` |
| Diagnostics | `GET/PUT /runtime/workflows/diagnostics/settings` |
| Alteration plans | `POST /runtime/workflows/alteration-plans`, `GET /runtime/workflows/alteration-plans/{planId}`, `GET /runtime/workflows/alteration-plans/{planId}/jobs/page`, `GET /runtime/workflows/alteration-plans/{planId}/jobs/{jobId}`, `POST /runtime/workflows/alteration-plans/{planId}/cancel` |

Executable, provenance, instance, and diagnostics reads use `workflow-runtime.read`; execution/stimulus operations use `workflow-runtime.execute`; diagnostics mutation uses `workflow-runtime.manage`. The shared wildcard permission remains supported. Foundation Identity policy authorization and the host's ASP.NET Core authentication middleware establish the principal and challenges; the module mapper emits the RFC 7807 error contract.

`POST .../execute` and `POST .../stimuli` are **synchronous to quiescence**: the in-process actor drains the run inline (ADR 0031 sticky single-writer drain) before the response is written, so the workflow has already reached completion, a fault, or its first durable suspension by the time the caller responds. The response is not an async hand-off acknowledgement — it returns `200 OK` and the body's `commandDispatchStatus` reflects the actual drain outcome (`Accepted`, `AcceptedButFaulted`, `Duplicate`, or `Deferred`). A `Rejected` dispatch returns `409 Conflict`. `AcceptedButFaulted` still returns `200` with a body: the drain completed but the drain encountered a failure; a `WaitForIntervention` policy can preserve a Running workflow with a blocking incident, which callers detect from `commandDispatchStatus`, not the HTTP code. `GET .../instances/{workflowExecutionId}` remains the polling surface for later state.

`POST .../stimuli` accepts an optional `idempotencyKey` for its start path. With a key, each matching workflow is started under an execution id derived from the key and the workflow's artifact (#2195), so a repeated call never starts it twice: reusing a key for the same artifact is answered `SkippedDuplicate` **permanently**, on every node and after restarts, and a caller that wants a second start sends a new key. Without a key, a repeated call may start a second instance.

Dispatch inspection is allowlist-only: it exposes lifecycle/linkage, child artifact/source type, input name/type capture descriptors, timestamps, and classified diagnostic code/category. It never serializes raw input/output values, tenant/partition/authority context, arbitrary metadata, exception messages, or stack traces.

Provenance is deliberately read-only here. Publishing owns creation and retirement of publication/test-run references, while Runtime owns artifact retention and garbage collection.

Executable detail includes a compact BPMN graph projection only for the persisted `elsa.bpmn.structure` schema `1.0.0`: `bpmnStructure.elements` carries element IDs, element types, bound child-node IDs, and names; `bpmnStructure.sequenceFlows` carries flow IDs, source/target references, names, outcome conditions, and default-flow flags. The API reads these fields directly from the versioned JSON payload without loading the BPMN activity module. It omits arbitrary properties, expressions, event definitions, and diagram data; other structure kinds or BPMN schemas return `bpmnStructure: null`.

Activation slots are the runtime-owned ledger for the live activation of a `(definitionId, slotName)` pair.
The read-only views expose the slot identity, active activation, source ownership, revision, and update time;
they do not join to publishing records. Runtime deliberately exposes no deactivation endpoint.

## Withheld and protected values

Workflow secret safety (spec 188) decides what the inspection reads show for a secret-bound input and for any value
whose policy marks it sensitive or as requiring encryption. A secret reference names where a value comes from and is
not a value, so the reads show it; they never show the value.

- **Executable detail** (`GET .../executables/{artifactId}`) shows no binding source detail at all: every input binding's
  `summary`, literal, expression, references, conversion plan, metadata and `secret` are null there.
- **Compiled input sources** (`GET .../executables/{artifactId}/source-references/{sourceReferenceId}/input-sources`):
  a binding whose effective policy is sensitive or requires encryption has `isSensitive: true` and access state
  `redacted`, and its `summary` and every source detail are null. A secret read (`source: "SecretRead"`) is such a
  binding, but it still shows its reference: `secret` carries the name, type and scope, and `summary` the name.
- **Authored input sources** in the same read: an authored value is shown only when its own sensitivity flag, and
  every compiled binding and pinned input contract the executable holds for that input, allow it. So a value on an
  input that the activity declares sensitive is redacted although its author did not mark it, and a value the
  executable holds neither a binding nor an input contract for is redacted as well.
- **Activity-execution detail** (`GET .../instances/{workflowExecutionId}/activity-executions/{activityExecutionId}`):
  a value evidence record (`valueSnapshots[]`) of a withheld input carries `withheldKind` (`SecretReference` or
  `PolicyRequiresEncryption`) and, for a secret reference, `secretReferenceName`, for every caller that may inspect
  the execution. It is always `isSensitive: true` with access state `unavailable`, and the value-payload read
  (`.../value-evidence/{evidenceId}/payload`) answers `unavailable` for it, whatever the record carries beside its
  marker. For any other record, `isSensitive` is the flag the runtime recorded: the invoke path flags an input when
  its value's effective policy or its pinned input contract marks it sensitive.
- **Descendants** (`.../activity-executions/{activityExecutionId}/descendants`) carry no value evidence.

## Alteration plan API

`POST /runtime/workflows/alteration-plans` is the sole submission path. It requires
`workflow-runtime.manage`, an `Idempotency-Key` header, and one explicit execution-ID selector or a frozen query
selector. A new or idempotent replay returns `202 Accepted`; reusing the same tenant-scoped key with different
canonical content returns `409`. Submission validates known exact alteration kind/schema-version pairs and static
composition, but Runtime preflight conflicts are durable job outcomes rather than synchronous failures. Admission
backpressure occurs before plan creation and returns `429` with `Retry-After`.

Plan/job polling requires `workflow-runtime.read`; cancellation requires `workflow-runtime.manage`. The capability
relations are `workflow-alteration-plans`, `workflow-alteration-plan`, `workflow-alteration-plan-jobs-page`,
`workflow-alteration-job`, and `workflow-alteration-plan-cancel`. Jobs page through a server-issued cursor. A caller
outside the sealed tenant/authority scope receives the same safe `404` as for a missing resource.

The Runtime API intentionally does not expose a payload schema or construct custom handler instances for client
authoring. A trusted host can inspect descriptor identity through `IWorkflowAlterationRegistry`; the REST surface
accepts only the stable `kind`, `schemaVersion`, and JSON payload envelope. Plan reads project only kind/version from
the protected envelope; they never return payloads, idempotency keys, variable values, secrets, exception details, or
handler CLR identities.

## Extension points

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) for the API-facing stores and inspector seam, and the [Runtime domain catalog](../EXTENSION_POINTS.md) for the full engine and persistence surface.

## Incident health

Instance summaries preserve historical `incidentCount` and add current `activeIncidentCount` (Open or Blocking)
and `blockingIncidentCount`. A Running instance with blocking incidents needs intervention; lifecycle and health
remain separate. Resolved and Suppressed incidents remain historical evidence without an active alarm.

The `workflow-instances-health-filter` capability points to the paged instance route. Clients should request
`incidentHealth=active|blocking|none` only when that relation is advertised. The filter applies before the authorized
total count and cursor page; invalid values and cursors from a different health query are rejected. Older hosts
without current counts cannot establish current health from their historical total.

Incident stores provide aggregate health counts; EF reads its existing status projection without loading fault
or captured-value content. EF implements the optional `IWorkflowHealthQuery` capability with a same-scope incident
predicate before its database count and keyset page. Native paging is used only when that capability confirms
compatibility with the selected incident store; EF requires both stores to share their context and access
accessor. Mixed compositions use the selected incident store through the bounded fallback, so an unused EF
incident table cannot determine health membership, totals or cursors. The explicit allow-all development adapter can return
that page directly when no sensitive correlation filter is present. Production and custom inspection contexts
authorize candidates before composing the visible count and cursor page; EF first narrows those candidates by
health. This authorization path still scans all matching provider pages and retains authorized matches in
memory. Other providers apply health while traversing bounded candidate pages; no path limits filtering to
the first page. A tenant-scope label alone never grants inspection access.
