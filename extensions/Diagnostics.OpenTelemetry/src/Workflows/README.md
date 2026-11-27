# Elsa.Diagnostics.OpenTelemetry.Workflows

Feature `DiagnosticsOpenTelemetryEngineBridge` forwards the workflow engine's spans into the OpenTelemetry ingestion
store, so Studio's timing view is populated in a host that runs no OpenTelemetry SDK exporter. The engine emits spans on
the `Elsa.Workflows.Runtime` `ActivitySource` once `WorkflowsRuntimeTracing` is enabled; `DiagnosticsOpenTelemetry` only
receives telemetry. See [engine telemetry](../../../../docs/reference/engine-telemetry.md) for how the two relate.

## Enabling it

Add the feature to a shell beside its two dependencies:

```json
"Features": {
  "DiagnosticsOpenTelemetry": {},
  "WorkflowsRuntimeTracing": {},
  "DiagnosticsOpenTelemetryEngineBridge": {}
}
```

The feature `DependsOn` `DiagnosticsOpenTelemetry` and `WorkflowsRuntimeTracing`, and has no settings. A host that
compiles the package in finds it with `WithHostAssemblies()`; a host that loads features from a Nuplane feed installs the
package. The Workbench enables it in `shells.json`.

## What it registers

- `EngineTracingBridge`, one per shell that enables the feature. It attaches an in-process `ActivityListener` to the engine source. The listener is process-wide and filters by source name only, so two shells that both enable the feature each ingest the other's engine spans. When the
  outermost drain span of a trace stops and no other engine span of that trace is open, it folds the trace's spans into
  one `OpenTelemetryBatch` and hands it to `IOpenTelemetryIngestor`, the contract the OTLP receiver uses, so the batch
  goes through redaction, the store and the live feed unchanged. Spans that never sit under a drain, such as the
  coalescing quiescence flush commit, are published once they have been quiet for 30 seconds. A trace with spans still
  open after five minutes is dropped, and at most 512 traces are buffered.
- `BridgeStartupTask`, a scoped `IStartupTask` that attaches the listener when the shell starts.
- `BridgeTerminator`, an `IShellTerminator` that detaches it and clears the buffer when the shell stops.

Ingestion runs off the engine's drain thread. A failed ingest is logged and the batch is dropped.

## Cross-domain contributions

This feature implements a contributor contract from another domain:

- **`IStartupTask`** *(Core, `Elsa.Tasks.Core`)*: `BridgeStartupTask` attaches the engine-span listener at shell start.
  - Known impl: `BridgeStartupTask`.
  - Catalog: [`Elsa.Tasks/EXTENSION_POINTS.md`](../../../../essentials/src/Tasks/Elsa.Tasks/EXTENSION_POINTS.md)

## Why a separate project

It connects two domains that stay independent of each other: the workflow runtime emits spans and the OpenTelemetry
ingestion domain receives telemetry. Folding the bridge into either side would make one reference the other.

On the runtime side the package references only the contracts package `Elsa.Workflows.Runtime.Core`, for the source,
span and tag names in `WorkflowEngineTelemetry`. It does not bring the runtime implementation along: the host supplies
`Elsa.Workflows.Runtime`, which provides the `WorkflowsRuntimeTracing` feature.
