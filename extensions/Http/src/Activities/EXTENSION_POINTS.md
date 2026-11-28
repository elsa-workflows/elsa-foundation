# Extension points — Activities.Http domain

`Elsa.Activities.Http` ships the `HttpEndpoint`, `WriteHttpResponse` and `SendHttpRequest` activities.
`KernelActivitiesHttp` depends on `Http` and `WorkflowsRuntimeStandaloneHttp`, and mounts standalone and
isolated TestRun request intake when the shell supplies their required services. Actor Foundation serves the
published authored HTTP routes.

Activities are transiently activated by the runtime. Synchronous delivery remains request-owned: `WriteHttpResponse` returns one typed result inside its isolated attempt scope, and the middleware delivers the committed result after the inline drain.

Outbound logging (not an extension point): `KernelActivitiesHttpFeature` calls `RemoveAllLoggers()` on the named `Elsa.Activities.Http` client and adds one `IHttpClientLogger` (`HttpActivityClientLogger`, public sealed) with `AddLogger`, because the factory's default logging handlers keep raw request and response header values in their `Trace` lines' structured state (spec 188 FR-019). It logs under the default client handler's category, `System.Net.Http.HttpClient.Elsa.Activities.Http.ClientHandler`, with that handler's event ids, at `Information`: `RequestStart` (method and URI; the URI is scheme, host, a non-default port and path, with a query replaced by `*`), `RequestEnd` (elapsed milliseconds and status code) and `RequestFailed` (elapsed milliseconds and the exception's type name). It logs no header, exception message or exception object. This is a behavior change for every `SendHttpRequest` call's outbound logging, not only secret-bound ones: for this client, hosts lose the default `System.Net.Http.HttpClient.Elsa.Activities.Http.LogicalHandler` category and its scope, the per-header `Trace` lines, and the exception message and object on a failed send. Elsa 4 is unreleased, so no migration is owed; a host that filtered or scoped on `LogicalHandler` should filter on the `ClientHandler` category instead.

---

## Overridable contracts

### `IHttpConnectionApplicator` *(scoped service — `Elsa.Activities.Http.Connections`)*
- **Kind:** Replacement. `KernelActivitiesHttpFeature` registers `UnboundHttpConnectionApplicator` with `TryAdd`.
- **Usage:** `SendHttpRequest` applies a bound `ConnectionReference` through this seam after `RequestHeaders`. The unbound default is a no-op when the input is empty and refuses a bound name when Connections is not composed.
- **Usage on Actor Foundation:** the Actor HTTP effect applies the selected connection through `ActorHttpConnections` for supported bindings. The [Connections workflow catalog](../../../Connections/src/Workflows/EXTENSION_POINTS.md) describes its scope.
- **Replace:** `AddConnectionsWorkflows()` calls `services.Replace` with `HttpConnectionApplicator`, which resolves the http connection for the execution partition and applies the registered `IHttpRequestAuthenticator`.

---

## Request-scoped response delivery

### `HttpResponseInstructionDelivery` *(scoped service — `Elsa.Activities.Http.Services`)*
- **Kind:** Scoped compatibility response delivery. `KernelActivitiesHttp` adds it only when the shell explicitly supplies `IActivityExecutionStateStore`.
- **Usage:** standalone and TestRun request adapters use the configured delivery path when applicable. Actor-held responses use the Actor reply from the accepted occurrence. The live exchange stays in the request scope.

---

## Cross-references

- Standalone HTTP source metadata, exclusive reservation planning, producer-bound intake and transport observations: [feature README](StandaloneTriggers/README.md). Public source/store/model contracts are owned by the [Runtime source contracts](../../../../essentials/src/Workflows/Runtime/Core/StandaloneTriggers); the feature's presence does not establish composed acceptance.
- Test Run [trigger driver](../../../../essentials/src/Workflows/Runtime/Core/Contracts/ITestRunTriggerDriver.cs) and [wait driver](../../../../essentials/src/Workflows/Runtime/Core/Contracts/ITestRunWaitDriver.cs) contracts. The [HTTP driver implementation](Services/HttpTestRunDriver.cs) handles the authenticated Studio action.
- HTTP endpoint behaviour contracts (route resolution, authorization, fault mapping) + the route-table freshness seams: [`Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md`](../Workflows/Runtime/EXTENSION_POINTS.md).
- HTTP content factories / downloadable content: [`Elsa.Http/EXTENSION_POINTS.md`](../Elsa.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
