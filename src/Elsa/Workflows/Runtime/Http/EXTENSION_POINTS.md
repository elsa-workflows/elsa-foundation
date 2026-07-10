# Extension points — Workflows.Runtime.Http domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Workflows.Runtime.Http` — a provider/feature project that ships default implementations of the HTTP endpoint behaviour contracts. The `IHttpEndpointRoutesResolver` contract is a feature contract (defined here); the `IHttpEndpointAuthorizationHandler` and `IHttpEndpointFaultHandler` contracts (with `AuthorizeHttpEndpointContext`, `HttpEndpointFaultContext`, `HttpBadRequestException`) moved to `Elsa.Http.Core` in spec 089 sub-unit C so the request middleware in `Elsa.Activities.Http` can consume them without a cross-module edge — this feature keeps their default implementations. This feature also contributes implementations of two `Elsa.Workflows.Runtime.Core` seams that keep the route table fresh — `IWorkflowTriggerIndexObserver` (on publish) and `IBookmarkLifecycleObserver` (on mid-flow bookmark create/consume, spec 089 D) — and registers a route-table startup task. `DependsOn`: `Http` (for the `IRouteTable` implementation it refreshes) and `WorkflowsRuntimeTriggers` (for the trigger-binding store the resolver reads and the indexer the observer hooks).

---

## Overridable contracts

### `IHttpEndpointRoutesResolver` *(Feature contract — `Elsa.Workflows.Runtime.Http`)*
- **Signature:** `ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken ct = default)` — reshaped from the A-era `GetRoutes(string path)` echo (pre-release, no shim; this contract's only consumer is this feature's startup task and index observer).
- **Default impl:** `HttpEndpointRoutesResolver` (this feature) — unions the `http:template` metadata of every `HttpEndpoint` trigger binding (`IWorkflowTriggerBindingStore`) with that of every waiting, non-expired `HttpEndpoint` bookmark (`IGlobalBookmarkStimulusLookup.FindWaitingByTypeAsync`, expiry-aware) and projects the distinct endpoint-relative templates (never base-path-prefixed — the middleware strips the base path) into `HttpRouteData` (spec 089 D union).
- **Override:** `services.Replace(ServiceDescriptor.Scoped<IHttpEndpointRoutesResolver, MyResolver>())` — e.g. to load routes from a custom store, add caching, or filter routes.

### `IHttpEndpointAuthorizationHandler` *(Core contract — `Elsa.Http.Core`; default impl here)*
- **Signature:** `ValueTask<bool> AuthorizeAsync(AuthorizeHttpEndpointContext context)` — `AuthorizeHttpEndpointContext(HttpContext, string? Policy)` (the former Runtime-specific `Workflow` resource member was dropped when the contract moved to `Elsa.Http.Core`; the middleware authorizes before any workflow instance exists).
- **Default impl:** `AuthenticationBasedHttpEndpointAuthorizationHandler` (this feature).
- **Override:** swap via the feature's `AuthorizationHandlerType` property (replaces the DI registration at feature startup) — or `services.Replace(...)` directly for custom authorization logic.

### `IHttpEndpointFaultHandler` *(Core contract — `Elsa.Http.Core`; default impl here)*
- **Signature:** `ValueTask HandleAsync(HttpEndpointFaultContext context)` — `HttpEndpointFaultContext(HttpContext, IEnumerable<Exception> Exceptions, CancellationToken)`.
- **Default impl:** `HttpEndpointFaultHandler` (this feature) — maps timeout/`HttpBadRequestException`/other faults to 408/400/500.
- **Override:** swap via the feature's `FaultHandlerType` property — or `services.Replace(...)` for custom fault handling (custom error shapes, logging, alerting).

---

## Contributed implementations (not overridable here)

### `RouteTableTriggerIndexObserver` → `IWorkflowTriggerIndexObserver` *(seam owned by `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contribution into the Core index-observer seam (`TryAddEnumerable`, Singleton).
- **Usage:** on every publish, after the trigger indexer rewrites an artifact's bindings, this observer rebuilds the whole route table from `IHttpEndpointRoutesResolver` (full re-projection, not an incremental diff — republish is delete-and-resave, and the durable index is the source of truth). Runs in a fresh DI scope (the indexer is a shell singleton; the resolver and `IRouteTable` are scoped, and the route table's state lives in the shared memory cache). An exception fails the publish, per the seam's failure policy.

### `RouteTableBookmarkObserver` → `IBookmarkLifecycleObserver` *(seam owned by `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contribution into the Core bookmark-lifecycle seam (`TryAddEnumerable`, Singleton).
- **Usage:** after a bookmark's durable create/consume commits, this observer rebuilds the whole route table from `IHttpEndpointRoutesResolver` (the same full re-projection as the trigger observer — a consumed bookmark's template must survive if another instance still awaits it) so a mid-flow endpoint's route appears on suspension and disappears on resume (spec 089 D, D-D4). Non-`HttpEndpoint` stimulus types return without opening a scope. Runs in a fresh DI scope (the notifier is a shell singleton; the resolver and `IRouteTable` are scoped). **Failure policy differs from the trigger observer:** this fires on the RUN path, so an exception is caught and logged by the `BookmarkLifecycleNotifier` and NEVER faults the run — a stale route 404s until the next refresh.

### `UpdateRouteTableStartupTask` → `IStartupTask` *(Core — `Elsa.Tasks.Core`)*
- **Kind:** Registered startup task (Scoped).
- **Usage:** populates the per-shell `IRouteTable` from the durable trigger index at startup via `IHttpEndpointRoutesResolver` + `IRouteTable.Refresh`, so a fresh host or a restart has every published HTTP endpoint's route before the endpoint middleware runs. The index observer keeps it fresh thereafter.

---

## Cross-references

- HTTP content downloads: [`Elsa.Http/EXTENSION_POINTS.md`](../Elsa.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
