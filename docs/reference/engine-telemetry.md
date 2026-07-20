# Engine telemetry vs. the OpenTelemetry ingestion domain

Elsa has two telemetry surfaces that are easy to confuse because both mention "OpenTelemetry".
They point in opposite directions. This page draws the line and documents the engine's own
tracing conventions.

| | **Engine telemetry** (this page, MS-9) | **OpenTelemetry ingestion domain** |
|---|---|---|
| Package | `Elsa.Workflows.Runtime.Core` (contract) + `Elsa.Workflows.Runtime.Tracing` (feature) | `Elsa.Diagnostics.OpenTelemetry(.Core)` |
| Direction | **Emits** — the workflow engine instruments *its own* execution | **Receives** — an OTLP collector endpoint ingesting telemetry pushed by *other* processes |
| Mechanism | `System.Diagnostics.ActivitySource` spans | OTLP/HTTP protobuf parser + in-memory store + query/live-feed API |
| Consumer | Any `ActivityListener` / an OpenTelemetry SDK `AddSource("Elsa.Workflows.Runtime")` in the host | Dashboards/queries reading the ingested store |
| Feature | `WorkflowsRuntimeTracing` | `DiagnosticsOpenTelemetry` |

The engine never pushes its spans into the ingestion domain. Engine telemetry is standard .NET
`ActivitySource` instrumentation: a host wires an OpenTelemetry SDK (or a bare listener) to the
source name and exports wherever it likes. The ingestion domain is a *backend* — a place telemetry
from external processes lands. They can be composed in the same app, but they are independent
concerns and neither depends on the other.

## Enabling engine telemetry

Engine tracing is off by default and allocation-free until enabled. The runtime composition root
registers a no-op `IWorkflowEngineTracer` (`NullWorkflowEngineTracer`), so every instrumentation
site is a null-check that never allocates. Composing the feature replaces the no-op with the real
`ActivitySourceWorkflowEngineTracer`:

```csharp
shell.WithFeature<WorkflowsRuntimeTracingFeature>();
```

Even with the feature enabled, `ActivitySource.StartActivity` returns `null` when no listener is
attached, so there is still no per-span cost until a host actually subscribes to the source.

## Span taxonomy

All spans are emitted from the `ActivitySource` named **`Elsa.Workflows.Runtime`**. One span per
engine phase; nesting is established through `Activity.Current`, so a traced run produces a coherent
tree: **drain → dispatch → (activity.execute, checkpoint.commit)**.

| Phase | Span name | Emitted from | Meaning |
|---|---|---|---|
| Drain cycle | `elsa.runtime.drain` | `WorkflowSchedulerDrainer.DrainAsync` | One scheduler drain cycle for a workflow execution (root span). |
| Dispatch | `elsa.runtime.dispatch` | `WorkflowSchedulerDrainer.DispatchAsync` | Dispatching one drained work item to its handler (child of drain). |
| Activity execution | `elsa.runtime.activity.execute` | `RuntimeActivityInvokeMiddleware.InvokeAsync` | Running the work item's handler in the pipeline's Invoke slot (child of dispatch). |
| Checkpoint commit | `elsa.runtime.checkpoint.commit` | `RuntimeCheckpointCommitter.CommitAsync` | Committing one runtime checkpoint through the fenced commit path (child of dispatch). |

## Attribute conventions

Attribute names follow OpenTelemetry semantic-convention style (dotted, lowercase, `elsa.*`
namespaced). Values are **identifiers, kinds, and counts only** — never workflow variables,
expression results, secrets, or fencing tokens. This mirrors the redaction sensibility applied to
the ingestion domain: nothing that could carry user payload or a security token is placed on a span.

| Attribute | Span(s) | Value |
|---|---|---|
| `elsa.workflow.execution_id` | drain, dispatch, activity.execute, checkpoint.commit | The workflow execution id. |
| `elsa.drain.max_work_items` | drain | Requested work-item cap (absent when unbounded). |
| `elsa.drain.outermost` | drain | Present (`true`) when the drain is not nested inside another engine drain. Set by the engine at drain start — it knows nesting (a child workflow drains inside the parent's dispatch) — so listeners never re-derive "outermost" from parent ids, which fail when a host span (e.g. the ASP.NET Core request activity) is the drain's direct parent. |
| `elsa.drain.items_processed` | drain | Work items processed this cycle. |
| `elsa.drain.stop_reason` | drain | Why the cycle stopped, when a non-default reason applies. |
| `elsa.work_item.id` | dispatch, activity.execute | The work item's id. |
| `elsa.command.kind` | dispatch, activity.execute | The work item's command kind (e.g. `StartActivity`). |
| `elsa.handler.name` | dispatch | The selected scheduler work handler's name. |
| `elsa.outcome` | dispatch, activity.execute, checkpoint.commit | `completed` or `faulted`. |
| `elsa.checkpoint.id` | checkpoint.commit | The committed checkpoint's id. |
| `elsa.checkpoint.persistence_mode` | checkpoint.commit | The persistence mode the policy decided (e.g. `Persist`). |
| `elsa.checkpoint.mandatory` | checkpoint.commit | Whether the checkpoint is mandatory. |
| `elsa.checkpoint.post_commit_intents` | checkpoint.commit | Count of post-commit intents folded into the commit. |

These names are a **stable contract**. Renaming a span or an attribute is a breaking change for
anyone building dashboards or alerts on them. The constants live in
`WorkflowEngineTelemetry` (`Elsa.Workflows.Runtime.Core.Diagnostics`).

## Semantic-safety guarantees

Instrumentation is behaviour-preserving by construction:

- The no-op path allocates nothing; tags are only ever set through `activity?.SetTag(...)` after the
  value already exists, so a disabled tracer adds a single null check.
- No new `await`s are introduced inside fenced sequences (the drainer's peek → pause → dequeue →
  dispatch, or the committer's ownership → decide → commit). Spans wrap whole methods; the only
  ambient state added is `Activity.Current` (trace context, not service location).
- The W12 slot-invoked model and W9 coalescing boundaries are untouched — the tracer neither reorders
  slots nor changes the checkpoint-flush window.

## See also

- Glossary: *Engine telemetry* and *OpenTelemetry ingestion domain* in
  [`docs/glossary/elsa.md`](../glossary/elsa.md).
- Extension point: `IWorkflowEngineTracer` in
  [`src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`](../../src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md).
- Ingestion domain bucket: [`docs/program-goals/diagnostics-observability-readiness.md`](../program-goals/diagnostics-observability-readiness.md).
