# Contract: Internal Seams Touched (089)

Changes to module-internal contracts; each is catalogued in the owning module's EXTENSION_POINTS.md in the PR that lands it.

## A — start-input + host wiring

- `IStimulusRouter` (behavioral): start path forwards `StimulusDispatchRequest.Input` on the first-class `WorkflowExecutionStartDispatchRequest.StimulusInput` field → `WorkflowExecutionStartCommandPayload.StimulusInput` → `RuntimeCheckpointCommandPayload.SeedStimulusInput` → reserved durable channel (`RuntimeMetadataKeys.StimulusInputName`) → `IExecutionExpressionState.StimulusInput`. Never the workflow-inputs bag (collision/spoof-proof by construction; revised from the original seed-input-key design during the spec-089 code review).
- `ActivitiesHttpFeature : IMiddlewareShellFeature` (implemented on the existing feature, not a separate class): mounts `HttpEndpointMiddleware`. **Ordering caveat (review V8):** `IMiddlewareShellFeature` exposes no `Order` member and CShells applies middleware features in discovery order — there is no declarative ordering knob. Sub-unit C (Authorize enforcement) MUST either verify activation order places this middleware after authentication or add an ordering hook to CShells first; do not assume ordering.
- Transport guard: `HttpEndpointOptions.MaxRequestBodyBytes` (default 1 MiB, streaming-enforced, 413) bounds the body because the stimulus payload becomes durable state on the started instance; per-endpoint authored limits remain sub-unit C. Empty/root `BasePath` disables the middleware (never a host-wide catch-all); base-path matching is segment-bounded.
- Known platform limitation (review V16): CShells applies `UseMiddleware` to the `IApplicationBuilder` captured at `MapShells()` time — a shell activated dynamically after startup gets no middleware (endpoints have a dynamic source; middleware does not). Tracked as a CShells enhancement.

## B — routing (as-built)

- `TriggerStimulusDescriptor` +`Metadata: IReadOnlyDictionary<string,string>` (optional, ordinal snapshot). `WorkflowTriggerBindingExtractor` copies it verbatim into `WorkflowTriggerBinding.Metadata`; `WorkflowTriggerBinding.BuildId` now keys on (artifactId, nodeId, **stimulusHash**) so sibling descriptors on one node get distinct deterministic ids.
- `IActivityTriggerStimulusProvider.Describe` returns **zero-or-more** descriptors (`IReadOnlyCollection`; empty = not mine). HTTP emits one per (template, method); Timer/Cron/Event return single-element collections.
- Shared vocabulary `Elsa.Http.Core.HttpEndpointRouting` (StimulusType `HttpEndpoint`, metadata keys `http:template`/`http:method`) sits below both `Elsa.Activities.Http` (writer) and `Elsa.Workflows.Runtime.Http` (reader) — no cross-module edge.
- `IHttpEndpointRoutesResolver` reshaped to `ResolveRoutesAsync()` over `IWorkflowTriggerBindingStore.ListByStimulusTypeAsync` (new store member; no new index, no schema bump). Templates are stored **endpoint-relative, unprefixed** — the base path is exclusively `HttpEndpointMiddleware`'s concern, so the resolver takes no options dependency and the two BasePath settings cannot diverge.
- Freshness: new `IWorkflowTriggerIndexObserver` seam (TryAddEnumerable) invoked by `WorkflowTriggerIndexer.IndexAsync` after delete-and-resave; observer failure fails the publish. `RouteTableTriggerIndexObserver` does a full re-projection refresh; `UpdateRouteTableStartupTask` covers shell start. Both catalogued in the owning EXTENSION_POINTS.md files.
- Middleware: first deterministic route-table match wins for overlapping templates (elsa-core parity); ambiguity (= same (template, method) hash claimed by >1 `DefinitionId`) → `409` before any dispatch.
- BREAKING (pre-release): unauthored `SupportedMethods` defaults to `GET`; non-literal `SupportedMethods` fails the publish like a non-literal `Path`.

## C — parsing/auth/faults

- New request-body parse entry point in `Elsa.Http` reusing the prioritized `IHttpContentParser` set (response-side contract untouched).
- `IHttpEndpointAuthorizationHandler`, `IHttpEndpointFaultHandler` (existing, unwired): become load-bearing from the middleware; contexts unchanged.

## D — mid-flow resume

- `HttpEndpoint` gains `[ResumeTarget]` following the `Delay` pattern. Preferred: context-side accessor exposing the resume stimulus input on `IActivityExecutionContext` (small runtime-core addition — the one runtime-internals touch; fallback: `JsonElement`-parameter resume method).
- Middleware dispatch mode `StartOnly → StartAndResume` (router contract already supports it; self-resume protection unchanged).

## E — sync responses (spec-069 seam)

- `StimulusDispatchRequest` + start/resume dispatch requests gain an optional non-durable dispatch-options passthrough carrying `WorkflowExecutionCommandDispatchOptions.AmbientServices` to the actor enqueue. INVARIANT (spec-069 FR-001, tested): ambient services never serialize into `WorkflowExecutionCommandEnvelope` or any persisted state.
- `WriteHttpResponse` (behavioral): when ambient services expose `HttpContext`, writes the live response via `IHttpContentFactory`; always records `HttpResponseInstruction`.
