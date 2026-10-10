# Extension points — Activities.Http domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Activities.Http` — the activity-side HTTP package that ships the `HttpEndpoint` start/mid-flow trigger activity, the `WriteHttpResponse` activity, and the inbound `HttpEndpointMiddleware`. `DependsOn`: `Http` (the `IRouteTable`/`IHttpContentFactory` set used by response delivery) and `WorkflowsRuntimeHttp` (the route-table populators; see that domain's catalog for the endpoint-behaviour contracts — authorization, fault mapping, route resolution).

Activities are transiently activated by the runtime. Synchronous delivery remains request-owned: `WriteHttpResponse` returns one typed result inside its isolated attempt scope, and the middleware delivers the committed result after the inline drain.

Outbound logging (not an extension point): `ActivitiesHttpFeature` calls `RemoveAllLoggers()` on the named `Elsa.Activities.Http` client and adds one `IHttpClientLogger` (`HttpActivityClientLogger`, public sealed) with `AddLogger`, because the factory's default logging handlers keep raw request and response header values in their `Trace` lines' structured state (spec 188 FR-019). It logs under the default client handler's category, `System.Net.Http.HttpClient.Elsa.Activities.Http.ClientHandler`, with that handler's event ids, at `Information`: `RequestStart` (method and URI; the URI is scheme, host, a non-default port and path, with a query replaced by `*`), `RequestEnd` (elapsed milliseconds and status code) and `RequestFailed` (elapsed milliseconds and the exception's type name). It logs no header, exception message or exception object. This is a behavior change for every `SendHttpRequest` call's outbound logging, not only secret-bound ones: for this client, hosts lose the default `System.Net.Http.HttpClient.Elsa.Activities.Http.LogicalHandler` category and its scope, the per-header `Trace` lines, and the exception message and object on a failed send. Elsa 4 is unreleased, so no migration is owed; a host that filtered or scoped on `LogicalHandler` should filter on the `ClientHandler` category instead.

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
- Dispatch-options passthrough (`IWorkflowStartDispatcher`/`IBookmarkResumeDispatcher`/`IStimulusRouter`): [`Elsa.Workflows.Runtime/EXTENSION_POINTS.md`](../../Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md).
- HTTP content factories / downloadable content: [`Elsa.Http/EXTENSION_POINTS.md`](../../Http/Elsa.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
