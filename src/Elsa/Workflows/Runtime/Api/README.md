# Workflow Runtime API

`Elsa.Workflows.Runtime.Api` owns supported management-client operations over runtime state: immutable workflow executables, read-only executable provenance, workflow execution and instance inspection, detached dispatch inspection, stimulus dispatch, and runtime diagnostics settings. Design authoring and publication/source-reference mutation belong to their own domains.

See the [domain-owned API specification](../../../../../specs/092-domain-owned-apis/spec.md) and [Elsa glossary](../../../../../docs/glossary/elsa.md) for the lifecycle and ownership model.

## Composition

Add `WorkflowsRuntimeApiFeature` to the active shell. The feature composes the host-agnostic runtime engine with `AddWorkflowRuntime()`, registers API request handlers, and supplies the executable inspector. Compose durable Runtime stores separately when in-memory defaults are insufficient. Optional trigger, coalescing, resumption, HTTP, and garbage-collection features remain independently selectable.

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
| Instances | `GET /runtime/workflows/instances`, `GET /runtime/workflows/instances/{workflowExecutionId}`, `GET .../incidents`, `GET .../activity-executions/{activityExecutionId}` |
| Detached dispatches | `GET /runtime/workflows/dispatches?parentWorkflowExecutionId=...|childWorkflowExecutionId=...|status=...`, `GET /runtime/workflows/dispatches/{dispatchId}` |
| Diagnostics | `GET/PUT /runtime/workflows/diagnostics/settings` |
| Alteration plans | `POST /runtime/workflows/alteration-plans`, `GET /runtime/workflows/alteration-plans/{planId}`, `GET /runtime/workflows/alteration-plans/{planId}/jobs/page`, `GET /runtime/workflows/alteration-plans/{planId}/jobs/{jobId}`, `POST /runtime/workflows/alteration-plans/{planId}/cancel` |

Executable, provenance, instance, and diagnostics reads use `workflow-runtime.read`; execution/stimulus operations use `workflow-runtime.execute`; diagnostics mutation uses `workflow-runtime.manage`. The shared wildcard permission remains supported. Foundation Identity policy authorization and the host's ASP.NET Core authentication middleware establish the principal and challenges; the module mapper emits the RFC 7807 error contract.

`POST .../execute` and `POST .../stimuli` are **synchronous to quiescence**: the in-process actor drains the run inline (ADR 0031 sticky single-writer drain) before the response is written, so the workflow has already reached completion, a fault, or its first durable suspension by the time the caller responds. The response is not an async hand-off acknowledgement — it returns `200 OK` and the body's `commandDispatchStatus` reflects the actual drain outcome (`Accepted`, `AcceptedButFaulted`, `Duplicate`, or `Deferred`). A `Rejected` dispatch returns `409 Conflict`. `AcceptedButFaulted` still returns `200` with a body: the drain completed but the workflow ended the turn faulted, which callers detect from `commandDispatchStatus`, not the HTTP code. `GET .../instances/{workflowExecutionId}` remains the polling surface for later state.

Dispatch inspection is allowlist-only: it exposes lifecycle/linkage, child artifact/source type, input name/type capture descriptors, timestamps, and classified diagnostic code/category. It never serializes raw input/output values, tenant/partition/authority context, arbitrary metadata, exception messages, or stack traces.

Provenance is deliberately read-only here. Publishing owns creation and retirement of publication/test-run references, while Runtime owns artifact retention and garbage collection.

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
