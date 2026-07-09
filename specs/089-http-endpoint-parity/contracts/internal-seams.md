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
- DependsOn closure (review B2): `ActivitiesHttp` depends on **`WorkflowsRuntimeHttp`** (not just `Http`). `WorkflowsRuntimeHttp` contributes the route-table populators (`UpdateRouteTableStartupTask`, `RouteTableTriggerIndexObserver`, `IHttpEndpointRoutesResolver`); enabling the endpoint activity alone without them would compose an empty route table so every endpoint 404s. No cycle — `WorkflowsRuntimeHttp` depends only on `Http` + `WorkflowsRuntimeTriggers`, never back on `ActivitiesHttp`.
- Route-table swap (review B6): `RouteTable.Refresh` builds a complete new dictionary off to the side and publishes it in a single `IMemoryCache.Set` (atomic swap), so a concurrent reader observes either the old table or the fully-built new one — never the empty/partial intermediate the previous `Clear()`+`Add` loop exposed (transient 404s during any publish). Build-time duplicate routes still throw `InvalidOperationException`, but abort the swap and leave the live table intact. Incremental `Add`/`Remove` keep mutating the live dictionary.
- Middleware: first deterministic route-table match wins for overlapping templates (elsa-core parity); ambiguity (= same (template, method) hash claimed by >1 `DefinitionId`) → `409` before any dispatch.
- BREAKING (pre-release): unauthored `SupportedMethods` defaults to `GET`; non-literal `SupportedMethods` fails the publish like a non-literal `Path`.
- Strict string-only literals (review B12): both `Path` and every `SupportedMethods` array element must be authored as JSON **strings**. A non-string literal (number/bool/object) fails the publish with the same `ArgumentException` as a non-literal binding — it is no longer silently `ToString()`-coerced into a garbage route path or HTTP method. (Extraction shares one `RequireJsonString` helper — DRY finding V15.)

## C — parsing/auth/faults

- New request-body parse entry point in `Elsa.Http` reusing the prioritized `IHttpContentParser` set (response-side contract untouched).
- `IHttpEndpointAuthorizationHandler`, `IHttpEndpointFaultHandler` (existing, unwired): become load-bearing from the middleware; contexts unchanged.

## D — mid-flow resume

- `HttpEndpoint` gains `[ResumeTarget]` following the `Delay` pattern. Preferred: context-side accessor exposing the resume stimulus input on `IActivityExecutionContext` (small runtime-core addition — the one runtime-internals touch; fallback: `JsonElement`-parameter resume method).
- Middleware dispatch mode `StartOnly → StartAndResume` (router contract already supports it; self-resume protection unchanged).

## E — sync responses (spec-069 seam)

- `StimulusDispatchRequest` + start/resume dispatch requests gain an optional non-durable dispatch-options passthrough carrying `WorkflowExecutionCommandDispatchOptions.AmbientServices` to the actor enqueue. INVARIANT (spec-069 FR-001, tested): ambient services never serialize into `WorkflowExecutionCommandEnvelope` or any persisted state.
- `WriteHttpResponse` (behavioral): when ambient services expose `HttpContext`, writes the live response via `IHttpContentFactory`; always records `HttpResponseInstruction`.
