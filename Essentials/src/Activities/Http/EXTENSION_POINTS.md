# Extension points — Activities.Http domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Activities.Http` — the activity-side HTTP package that ships the `HttpEndpoint` start/mid-flow trigger activity, the `WriteHttpResponse` activity, and the inbound `HttpEndpointMiddleware`. `DependsOn`: `Http` (the `IRouteTable`/`IHttpContentFactory` set used by response delivery) and `WorkflowsRuntimeHttp` (the route-table populators; see that domain's catalog for the endpoint-behaviour contracts — authorization, fault mapping, route resolution).

Activities are transiently activated by the runtime. Synchronous delivery remains request-owned: `WriteHttpResponse` returns one typed result inside its isolated attempt scope, and the middleware delivers the committed result after the inline drain.

Outbound logging (not an extension point): `ActivitiesHttpFeature` calls `RemoveAllLoggers()` on the named `Elsa.Activities.Http` client and adds one `IHttpClientLogger` (`HttpActivityClientLogger`, public sealed) with `AddLogger`, because the factory's default logging handlers keep raw request and response header values in their `Trace` lines' structured state (spec 188 FR-019). It logs under the default client handler's category, `System.Net.Http.HttpClient.Elsa.Activities.Http.ClientHandler`, with that handler's event ids, at `Information`: `RequestStart` (method and URI; the URI is scheme, host, a non-default port and path, with a query replaced by `*`), `RequestEnd` (elapsed milliseconds and status code) and `RequestFailed` (elapsed milliseconds and the exception's type name). It logs no header, exception message or exception object. This is a behavior change for every `SendHttpRequest` call's outbound logging, not only secret-bound ones: for this client, hosts lose the default `System.Net.Http.HttpClient.Elsa.Activities.Http.LogicalHandler` category and its scope, the per-header `Trace` lines, and the exception message and object on a failed send. Elsa 4 is unreleased, so no migration is owed; a host that filtered or scoped on `LogicalHandler` should filter on the `ClientHandler` category instead.

---

## Overridable contracts

### `IHttpActivityRequestExecutor` *(scoped service — `Elsa.Activities.Http.Contracts`)*
- **Kind:** Replacement. `ActivitiesHttpFeature` registers the ordinary module executor with `TryAdd`. Compose exactly one unkeyed executor: preselect one before the feature or use `services.Replace` to deliberately replace the default. A shell Prepare check rereads the completed service collection and refuses zero or duplicate unkeyed registrations, including providers added by a later feature, before constructing a transport provider. Keyed registrations are separate and neither satisfy nor count toward this selection.
- **Usage:** `SendHttpRequest` captures immutable `HttpActivityRequest` data, then delegates request construction, connection application, transport, the one refreshable-credential retry and response collection through this seam. Its timeout, workflow cancellation and outcome mapping remain in the activity. Request header dictionaries are copied; URI, string body and content type are immutable values. The connection reference is copied and carries no resolved credential.
- **Consumer change:** direct construction now takes `IOptions<HttpActivityOptions>` and `IHttpActivityRequestExecutor`. Hosts should resolve the executor from their normal HTTP feature composition. Elsa 4 is unreleased; the previous constructor taking a client factory and connection applicator has been removed so it cannot bypass the composed executor.
- **Runtime gate:** the default executor requires `IRuntimeTestEffectGate`, records the original request digest before creating a client or applying credentials, and consumes the trusted admission's mode and configuration. No gate means missing composition, never an ordinary fallback. A retry revalidates the same admitted responsibility before refreshing credentials. Unknown outcomes retain responsibility; disposition writes use a separate bounded wall deadline so request cancellation cannot silently discard them.
- **Module profiles:** `elsa.http-response-substitute/1` accepts an object containing required numeric `statusCode` (100–599), optional string/null `body`, and optional `headers` object with arrays of string values. It constructs no client and reads no bound connection. `elsa.http-test-system/1` requires `url` (an explicit absolute HTTP/HTTPS destination without user information or fragment) and `connection` (`name`, optional HTTP `type`). Its optional `headers` object accepts only explicit admitted string values for `Accept`, `Accept-Language` and `User-Agent`; the default is empty. It copies no original activity headers, including authorization, cookies and arbitrary custom secret headers. The selected connection owns credentials. Unknown/duplicate fields, missing mappings and unsupported profiles refuse before credentials; there is no original-reference or original-URL fallback. Imported configuration supplies no authority.
- **Test-system transport:** the module creates and disposes a private client/handler per admitted operation, sharing request construction, connection application, response collection and the one credential-refresh retry with ordinary execution. It bypasses the ordinary named-client pipeline and its shared state. Automatic redirects, cookies, default credentials and proxies are disabled, and no default request headers are configured. Each operation owns its handler and connections until disposal. The applicator must leave the exact mapped absolute URI unchanged and supply no explicit `Host` header; each application, including refresh, is checked before transport. Query-mutating authentication and custom handler/transport pipelines are unsupported by this initial profile and need separate qualification. Ordinary HTTP client configuration is unchanged.
- **Qualification:** these module adapters advertise no public Controlled test profile or runner. Durable policy/receipt providers, pre-activation and secret guards, permissions, complete effect closure and composed-host qualification are required before admission can advertise support. Connection authorization and authenticator/credential-provider qualification belong to the owner admitting the retained mapping. An authenticator can perform its own I/O while applying credentials (including OAuth token endpoints and refresh through another client); the private destination transport and its post-application URI check do not isolate those calls. Unsupported provider paths must be refused at preflight before credentials. A transport-only mock cannot establish credential isolation.

### `IHttpConnectionApplicator` *(scoped service — `Elsa.Activities.Http.Connections`)*
- **Kind:** Replacement. `ActivitiesHttpFeature` registers `UnboundHttpConnectionApplicator` with `TryAdd`.
- **Usage:** the ordinary request executor applies a bound `ConnectionReference` through this seam after `RequestHeaders`. The unbound default is a no-op when the input is empty and refuses a bound name when Connections is not composed.
- **Replace:** `ConnectionsWorkflows` calls `services.Replace` with `HttpConnectionApplicator`, which resolves the http connection for the execution partition and applies the registered `IHttpRequestAuthenticator`.

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

- Test Run trigger and wait drivers (`ITestRunTriggerDriver`, `ITestRunWaitDriver`), including the authenticated Studio action contract: [`Elsa.Workflows.Runtime` extension points](../../Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md#workflow-test-run-trigger-and-wait-drivers).
- HTTP endpoint behaviour contracts (route resolution, authorization, fault mapping) + the route-table freshness seams: [`Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md`](../../Workflows/Runtime/Http/EXTENSION_POINTS.md).
- Dispatch-options passthrough (`IWorkflowStartDispatcher`/`IBookmarkResumeDispatcher`/`IStimulusRouter`): [`Elsa.Workflows.Runtime/EXTENSION_POINTS.md`](../../Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md).
- HTTP content factories / downloadable content: [`Elsa.Http/EXTENSION_POINTS.md`](../../Http/Elsa.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
