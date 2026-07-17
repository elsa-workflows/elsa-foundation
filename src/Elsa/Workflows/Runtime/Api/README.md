# Workflow Runtime API

`Elsa.Workflows.Runtime.Api` owns supported management-client operations over runtime state: immutable workflow executables, read-only executable provenance, workflow execution and instance inspection, detached dispatch inspection, stimulus dispatch, and runtime diagnostics settings. Design authoring and publication/source-reference mutation belong to their own domains.

See the [domain-owned API specification](../../../../../specs/092-domain-owned-apis/spec.md) and [Elsa glossary](../../../../../docs/glossary/elsa.md) for the lifecycle and ownership model.

## Composition

Add `WorkflowsRuntimeApiFeature` to the active shell. The feature composes the host-agnostic runtime engine with `AddWorkflowRuntime()`, registers API request handlers, and supplies the executable inspector. Compose durable Runtime stores separately when in-memory defaults are insufficient. Optional trigger, coalescing, resumption, HTTP, and garbage-collection features remain independently selectable.

This package does not depend on `Elsa.Server`; a worker, custom application, or reference server may compose it.

## Supported routes

| Area | Routes |
|---|---|
| Executables | `GET /runtime/workflows/executables`, `GET /runtime/workflows/executables/{artifactId}`, `GET /runtime/workflows/executables/{artifactId}/provenance` |
| Execution | `POST /runtime/workflows/executables/{artifactId}/execute`, `POST /runtime/workflows/stimuli` |
| Instances | `GET /runtime/workflows/instances`, `GET /runtime/workflows/instances/{workflowExecutionId}`, `GET .../incidents`, `GET .../activity-executions/{activityExecutionId}` |
| Detached dispatches | `GET /runtime/workflows/dispatches?parentWorkflowExecutionId=...|childWorkflowExecutionId=...|status=...`, `GET /runtime/workflows/dispatches/{dispatchId}` |
| Diagnostics | `GET/PUT /runtime/workflows/diagnostics/settings` |

Executable, provenance, instance, and diagnostics reads use `workflow-runtime.read`; execution/stimulus operations use `workflow-runtime.execute`; diagnostics mutation uses `workflow-runtime.manage`. The shared wildcard permission remains supported. Common FastEndpoints infrastructure supplies authentication and RFC 7807 errors.

Dispatch inspection is allowlist-only: it exposes lifecycle/linkage, child artifact/source type, input name/type capture descriptors, timestamps, and classified diagnostic code/category. It never serializes raw input/output values, tenant/partition/authority context, arbitrary metadata, exception messages, or stack traces.

Provenance is deliberately read-only here. Publishing owns creation and retirement of publication/test-run references, while Runtime owns artifact retention and garbage collection.

## Extension points

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) for the API-facing stores and inspector seam, and the [Runtime domain catalog](../EXTENSION_POINTS.md) for the full engine and persistence surface.
