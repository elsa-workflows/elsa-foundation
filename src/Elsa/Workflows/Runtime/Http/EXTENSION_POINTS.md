# Extension points — Workflows.Runtime.Http domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Workflows.Runtime.Http` — a provider/feature project that ships default implementations of the HTTP endpoint behaviour contracts. The `IHttpEndpointRoutesResolver` contract is a feature contract (defined here); the `IHttpEndpointAuthorizationHandler` and `IHttpEndpointFaultHandler` contracts (with `AuthorizeHttpEndpointContext`, `HttpEndpointFaultContext`, `HttpBadRequestException`) moved to `Elsa.Http.Core` in spec 089 sub-unit C so the request middleware in `Elsa.Activities.Http` can consume them without a cross-module edge — this feature keeps their default implementations. This feature also contributes one implementation each of the `Elsa.Workflows.Runtime.Core` `IWorkflowTriggerIndexObserver` and `IWorkflowTriggerIndexValidator` seams and registers a route-table startup task. `DependsOn`: `Http` (for the `IRouteTable` implementation it refreshes) and `WorkflowsRuntimeTriggers` (for the trigger-binding store the resolver reads and the indexer the observer/validator hook).

---

## Overridable contracts

### `IHttpEndpointRoutesResolver` *(Feature contract — `Elsa.Workflows.Runtime.Http`)*
- **Signature:** `ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken ct = default)` — reshaped from the A-era `GetRoutes(string path)` echo (pre-release, no shim; this contract's only consumer is this feature's startup task and index observer).
- **Default impl:** `HttpEndpointRoutesResolver` (this feature) — lists every `HttpEndpoint` trigger binding from `IWorkflowTriggerBindingStore`, reads each binding's `http:template` metadata, and projects the distinct (endpoint-relative) templates into `HttpRouteData`. A cross-definition `(template, method)` conflict found in the store only logs a **warning** and resolves anyway — the resolver also runs at shell startup and on HTTP-affecting observed publishes (the `RouteTableTriggerIndexObserver` gate below skips it for known non-HTTP publishes), so throwing would brick boot and HTTP-affecting publishes on one poisoned entry; publish-time uniqueness is enforced pre-write by `HttpEndpointRoutingUniquenessValidator` (below), and the middleware's request-time 409 guard covers serving.
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
- **Usage:** after the trigger indexer rewrites an artifact's bindings, this observer rebuilds the whole route table from `IHttpEndpointRoutesResolver` **when the publish could change the HTTP route set** (full re-projection, not an incremental diff — republish is delete-and-resave, and the durable index is the source of truth). Runs in a fresh DI scope (the indexer is a shell singleton; the resolver and `IRouteTable` are scoped, and the route table's state lives in the shared memory cache). An exception fails the publish, per the seam's failure policy. **HTTP-affecting gate (#592 item 8):** the observer skips the refresh only when it is provably redundant — the snapshot `Bindings` contain no HTTP-endpoint binding *and* the artifact is in a process-local set of artifacts *positively known to contribute no HTTP routes* (a prior successful refresh already covered its no-HTTP state, so the table can hold nothing of its to reconcile out). Any other no-HTTP publish — including the first one seen after a process restart, where the artifact may have just dropped its last HTTP route (delete-and-resave) — still refreshes, so no removal is ever missed. The set is mutated only after a successful refresh (a failed, publish-failing refresh leaves no memory, so the retried publish refreshes again), and losing it on restart costs one extra refresh per non-HTTP artifact, never a stale route; the startup task rebuilds the whole table on shell start regardless. This is the sole consumer of `WorkflowTriggerIndexSnapshot.Bindings`.

### `HttpEndpointRoutingUniquenessValidator` → `IWorkflowTriggerIndexValidator` *(seam owned by `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contribution into the Core PRE-write index-validation seam (`TryAddEnumerable`, Singleton).
- **Usage:** publish-time `(template, method)` uniqueness (issue #592 item 2). For each HTTP-endpoint binding about to be written it lists existing claimants of the same stimulus identity (`ListByStimulusAsync`) and throws `EndpointRoutingConflictException` when a **different** `DefinitionId` already owns it — failing the second, conflicting publish with the durable index untouched. Existing bindings of the artifact being republished are ignored (delete-and-resave supersedes them); same-`DefinitionId` claimants are allowed (versioning / duplicate node). Deliberately HTTP-specific: shared stimulus identity is legitimate fan-out for other stimulus types.

### `UpdateRouteTableStartupTask` → `IStartupTask` *(Core — `Elsa.Tasks.Core`)*
- **Kind:** Registered startup task (Scoped).
- **Usage:** populates the per-shell `IRouteTable` from the durable trigger index at startup via `IHttpEndpointRoutesResolver` + `IRouteTable.Refresh`, so a fresh host or a restart has every published HTTP endpoint's route before the endpoint middleware runs. The index observer keeps it fresh thereafter.

---

## Cross-references

- HTTP content downloads: [`Elsa.Http/EXTENSION_POINTS.md`](../Elsa.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
