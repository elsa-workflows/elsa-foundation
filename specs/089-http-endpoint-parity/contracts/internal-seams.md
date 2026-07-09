# Contract: Internal Seams Touched (089)

Changes to module-internal contracts; each is catalogued in the owning module's EXTENSION_POINTS.md in the PR that lands it.

## A — start-input + host wiring

- `IStimulusRouter` (behavioral): start path forwards `StimulusDispatchRequest.Input` on the first-class `WorkflowExecutionStartDispatchRequest.StimulusInput` field → `WorkflowExecutionStartCommandPayload.StimulusInput` → `RuntimeCheckpointCommandPayload.SeedStimulusInput` → reserved durable channel (`RuntimeMetadataKeys.StimulusInputName`) → `IExecutionExpressionState.StimulusInput`. Never the workflow-inputs bag (collision/spoof-proof by construction; revised from the original seed-input-key design during the spec-089 code review).
- `ActivitiesHttpFeature : IMiddlewareShellFeature` (implemented on the existing feature, not a separate class): mounts `HttpEndpointMiddleware`. **Ordering (review V8 — RESOLVED by CShells 0.0.29-preview.145, cshells PR #124):** `IMiddlewareShellFeature` now has an `int Order` (default 0) and CShells applies middleware features via `OrderBy(f => f.Feature.Order)`. `ActivitiesHttpFeature` leaves the default; sub-unit C sets an explicit order if auth-sensitive middleware joins the shell pipeline.
- Transport guard: `HttpEndpointOptions.MaxRequestBodyBytes` (default 1 MiB, streaming-enforced, 413) bounds the body because the stimulus payload becomes durable state on the started instance; per-endpoint authored limits remain sub-unit C. Empty/root `BasePath` disables the middleware (never a host-wide catch-all); base-path matching is segment-bounded.
- Platform limitations (review V16 — RESOLVED by cshells PR #124): shell containers are guaranteed an `IMiddlewareFactory` (the feature's TryAdd workaround was removed when adopting preview.145), and per-shell dynamic pipelines mean shells activated after startup get their middleware too.

## B — routing

- `TriggerStimulusDescriptor` +`Metadata: IReadOnlyDictionary<string,string>` (optional; providers may omit). `WorkflowTriggerBindingExtractor` copies it verbatim into `WorkflowTriggerBinding.Metadata`.
- `IActivityTriggerStimulusProvider` (behavioral): a provider MAY return multiple descriptors per node (one per (template, method)); extractor accepts one-or-many.
- `IHttpEndpointRoutesResolver` (`Elsa.Workflows.Runtime.Http`): reimplemented over the binding store; feeds the revived `UpdateRouteTableStartupTask` + binding-change handler that maintain the per-shell `IRouteTable`.

## C — parsing/auth/faults

- New request-body parse entry point in `Elsa.Http` reusing the prioritized `IHttpContentParser` set (response-side contract untouched).
- `IHttpEndpointAuthorizationHandler`, `IHttpEndpointFaultHandler` (existing, unwired): become load-bearing from the middleware; contexts unchanged.

## D — mid-flow resume

- `HttpEndpoint` gains `[ResumeTarget]` following the `Delay` pattern. Preferred: context-side accessor exposing the resume stimulus input on `IActivityExecutionContext` (small runtime-core addition — the one runtime-internals touch; fallback: `JsonElement`-parameter resume method).
- Middleware dispatch mode `StartOnly → StartAndResume` (router contract already supports it; self-resume protection unchanged).

## E — sync responses (spec-069 seam)

- `StimulusDispatchRequest` + start/resume dispatch requests gain an optional non-durable dispatch-options passthrough carrying `WorkflowExecutionCommandDispatchOptions.AmbientServices` to the actor enqueue. INVARIANT (spec-069 FR-001, tested): ambient services never serialize into `WorkflowExecutionCommandEnvelope` or any persisted state.
- `WriteHttpResponse` (behavioral): when ambient services expose `HttpContext`, writes the live response via `IHttpContentFactory`; always records `HttpResponseInstruction`.
