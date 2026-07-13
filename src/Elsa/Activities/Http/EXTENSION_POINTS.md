# Extension points — Activities.Http domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Activities.Http` — the activity-side HTTP package that ships the `HttpEndpoint` start/mid-flow trigger activity, the `WriteHttpResponse` activity, and the inbound `HttpEndpointMiddleware`. `DependsOn`: `Http` (the `IRouteTable`/`IHttpContentFactory` set the middleware and `WriteHttpResponse` consume) and `WorkflowsRuntimeHttp` (the route-table populators; see that domain's catalog for the endpoint-behaviour contracts — authorization, fault mapping, route resolution).

Most of this package needs no DI registration: the activities are resolved by the runtime's `ClrActivityConstructor`, and the middleware/trigger-stimulus provider are wired by the feature. The one replaceable request-scoped seam is the synchronous-response sink (spec 089 sub-unit E).

---

## Overridable contracts

### `SyncHttpResponseSink` *(scoped seam — `Elsa.Activities.Http.Services`)*
- **Kind:** Registered `AddScoped` in `ActivitiesHttpFeature`; a request-scoped state holder (`HttpContext? HttpContext`, `bool ResponseWritten`, `Populate(HttpContext)`, `MarkResponseWritten()`).
- **Usage (spec 089 E-D2/E-D3):** the live-write discriminator for synchronous endpoints. `HttpEndpointMiddleware` populates the REQUEST scope's instance with the live `HttpContext` **only** for a `ResponseMode.Sync` dispatch and passes `context.RequestServices` as the dispatch's ambient services (`WorkflowExecutionCommandDispatchOptions.AmbientServices`, forwarded by `StimulusRouter` — see the Core catalog's `IWorkflowStartDispatcher`/`IBookmarkResumeDispatcher`/`IStimulusRouter` entries). Because the in-process actor drains INLINE on the caller's async flow, a `WriteHttpResponse` the run reaches resolves this same instance and writes the live response in the same exchange, then calls `MarkResponseWritten()`; the middleware reads `ResponseWritten` after `RouteAsync` returns to choose between returning the workflow-authored response and degrading to `202`. An empty/absent sink (async mode, durable resume, non-HTTP start, or the feature unregistered) ⇒ `WriteHttpResponse` stays artifact-only.
- **Load-bearing design guard — never `IHttpContextAccessor`.** The discriminator MUST be this ambient-scope-only holder. The inline drain runs on the caller's async flow, so the AsyncLocal-backed `IHttpContextAccessor` would resolve the live `HttpContext` even for an ASYNC-mode dispatch and even from a fresh internal scope, turning every async endpoint into an accidental live write. A `WriteHttpResponse` resolving the sink from a fresh internal scope (async mode) gets a distinct, unpopulated instance and stays artifact-only. See the XML remarks on `SyncHttpResponseSink` and `WriteHttpResponse`.
- **Override:** `services.Replace(ServiceDescriptor.Scoped<SyncHttpResponseSink, MySink>())` — a subclass can, e.g., add tracing or a distributed request-affinity hook. A replacement must keep the scoped lifetime (a singleton would leak one request's `HttpContext` into another) and the populate-only-for-sync contract.

---

## Cross-references

- HTTP endpoint behaviour contracts (route resolution, authorization, fault mapping) + the route-table freshness seams: [`Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md`](../../Workflows/Runtime/Http/EXTENSION_POINTS.md).
- Dispatch-options passthrough (`IWorkflowStartDispatcher`/`IBookmarkResumeDispatcher`/`IStimulusRouter` — the ambient-services seam the sink rides): [`Elsa.Workflows.Runtime/EXTENSION_POINTS.md`](../../Workflows/Runtime/EXTENSION_POINTS.md).
- HTTP content factories / downloadable content: [`Elsa.Http/EXTENSION_POINTS.md`](../../Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
