# Extension points — Activities.Http domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Activities.Http` — the activity-side HTTP package that ships the `HttpEndpoint` start/mid-flow trigger activity, the `WriteHttpResponse` activity, and the inbound `HttpEndpointMiddleware`. `DependsOn`: `Http` (the `IRouteTable`/`IHttpContentFactory` set used by response delivery) and `WorkflowsRuntimeHttp` (the route-table populators; see that domain's catalog for the endpoint-behaviour contracts — authorization, fault mapping, route resolution).

Activities are transiently activated by the runtime. Synchronous delivery remains request-owned: `WriteHttpResponse` returns one typed result inside its isolated attempt scope, and the middleware delivers the committed result after the inline drain.

---

## Request-scoped response delivery

### `HttpResponseInstructionDelivery` *(scoped service — `Elsa.Activities.Http.Services`)*
- **Kind:** Registered `AddScoped` in `ActivitiesHttpFeature`; reads `IActivityExecutionStateStore` and uses the composed `IHttpContentFactory` set.
- **Usage:** after a synchronous inline drain, `HttpEndpointMiddleware` passes the dispatched workflow execution ids to the delivery service. It selects the first committed `HttpResponseInstruction` completion in dispatch/execution order and writes status, headers, content type, and body to the request-owned response. No `HttpContext`, request service, or response stream enters an activity activation or durable state.
- **Degrade:** no committed instruction means the middleware returns the normal `202`; async mode never invokes delivery.

### `SyncHttpResponseSink` *(scoped compatibility marker — `Elsa.Activities.Http.Services`)*
- **Kind:** Registered `AddScoped` in `ActivitiesHttpFeature`; records a custom response that started directly during a synchronous dispatch.
- **Usage:** canonical `WriteHttpResponse` does not resolve the sink. The middleware preserves an already-started custom response before attempting committed-result delivery.

---

## Cross-references

- HTTP endpoint behaviour contracts (route resolution, authorization, fault mapping) + the route-table freshness seams: [`Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md`](../../Workflows/Runtime/Http/EXTENSION_POINTS.md).
- Dispatch-options passthrough (`IWorkflowStartDispatcher`/`IBookmarkResumeDispatcher`/`IStimulusRouter`): [`Elsa.Workflows.Runtime/EXTENSION_POINTS.md`](../../Workflows/Runtime/EXTENSION_POINTS.md).
- HTTP content factories / downloadable content: [`Elsa.Http/EXTENSION_POINTS.md`](../../Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
