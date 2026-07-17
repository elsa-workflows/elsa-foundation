# Extension points — Workflows.Runtime.Http domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Workflows.Runtime.Http` — a provider/feature project that ships default implementations of the HTTP endpoint behaviour contracts. The `IHttpEndpointRoutesResolver` contract is a feature contract (defined here); the `IHttpEndpointAuthorizationHandler` and `IHttpEndpointFaultHandler` contracts (with `AuthorizeHttpEndpointContext`, `HttpEndpointFaultContext`, `HttpBadRequestException`) moved to `Elsa.Http.Core` in spec 089 sub-unit C so the request middleware in `Elsa.Activities.Http` can consume them without a cross-module edge — this feature keeps their default implementations. This feature also contributes three `Elsa.Workflows.Runtime.Core` seam implementations: `IWorkflowTriggerIndexObserver` (keeps the route table fresh on publish), `IWorkflowTriggerIndexValidator` (publish-time `(template, method)` uniqueness, #592 item 2), and `IBookmarkLifecycleObserver` (keeps the route table fresh on mid-flow bookmark create/consume, spec 089 D) — and registers a route-table startup task plus the internal `IHttpEndpointRouteTableSynchronizer` (the single lock every refresh routes through, review fix). `DependsOn`: `Http` (for the `IRouteTable` implementation it refreshes) and `WorkflowsRuntimeTriggers` (for the trigger-binding store the resolver reads and the indexer the observer/validator hook).

---

## Overridable contracts

### `IHttpEndpointRoutesResolver` *(Feature contract — `Elsa.Workflows.Runtime.Http`)*
- **Signature:** `ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken ct = default)` — reshaped from the A-era `GetRoutes(string path)` echo (pre-release, no shim; this contract's only consumer is this feature's startup task and index observer).
- **Default impl:** `HttpEndpointRoutesResolver` (this feature) — unions the `http:template` metadata of every `HttpEndpoint` trigger binding (`IWorkflowTriggerBindingStore`) with that of every waiting, non-expired `HttpEndpoint` bookmark (`IGlobalBookmarkStimulusLookup.FindWaitingByTypeAsync`, expiry-aware) and projects the distinct endpoint-relative templates (never base-path-prefixed — the middleware strips the base path) into `HttpRouteData` (spec 089 D union). A cross-definition `(template, method)` conflict among **trigger bindings** only logs a **warning** and resolves anyway — the resolver also runs at shell startup and on HTTP-affecting observed publishes (the `RouteTableTriggerIndexObserver` gate below skips it for known non-HTTP publishes), so throwing would brick boot and HTTP-affecting publishes on one poisoned entry; publish-time uniqueness is enforced pre-write by `HttpEndpointRoutingUniquenessValidator` (below), and the middleware's request-time 409 guard covers serving. The uniqueness check is **trigger-binding-only**: a waiting mid-flow bookmark that shares a `(template, method)` with a published trigger is legal (instance-scoped, spec 089 D-D5), so bookmarks are exempt from the collision warning and never fail a publish.
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
- **Usage:** after the trigger indexer rewrites an artifact's bindings, this observer rebuilds the whole route table from `IHttpEndpointRoutesResolver` **when the publish could change the HTTP route set** (full re-projection, not an incremental diff — republish is delete-and-resave, and the durable index is the source of truth). The read-then-swap is delegated to `IHttpEndpointRouteTableSynchronizer` (below), which serializes it against every other refresh under one lock (review fix); the synchronizer opens the fresh DI scope. An exception propagates and fails the publish, per the seam's failure policy. **HTTP-affecting gate (#592 item 8):** the observer skips the refresh only when it is provably redundant — the snapshot `Bindings` contain no HTTP-endpoint binding *and* the artifact is in a process-local set of artifacts *positively known to contribute no HTTP routes* (a prior successful refresh already covered its no-HTTP state, so the table can hold nothing of its to reconcile out). Any other no-HTTP publish — including the first one seen after a process restart, where the artifact may have just dropped its last HTTP route (delete-and-resave) — still refreshes, so no removal is ever missed. The set is mutated only after a successful refresh (a failed, publish-failing refresh leaves no memory, so the retried publish refreshes again), and losing it on restart costs one extra refresh per non-HTTP artifact, never a stale route; the startup task rebuilds the whole table on shell start regardless. This is the sole consumer of `WorkflowTriggerIndexSnapshot.Bindings`.

### `HttpEndpointRoutingUniquenessValidator` → `IWorkflowTriggerIndexValidator` *(seam owned by `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contribution into the Core PRE-write index-validation seam (`TryAddEnumerable`, Scoped); it reads the access-bound trigger store in the publication operation scope.
- **Usage:** publish-time `(template, method)` uniqueness (issue #592 item 2). For each HTTP-endpoint binding about to be written it lists existing claimants of the same stimulus identity (`ListByStimulusAsync`) and throws `EndpointRoutingConflictException` when a **different** `DefinitionId` already owns it — failing the second, conflicting publish with the durable index untouched. Existing bindings of the artifact being republished are ignored (delete-and-resave supersedes them); same-`DefinitionId` claimants are allowed (versioning / duplicate node). Deliberately HTTP-specific: shared stimulus identity is legitimate fan-out for other stimulus types.

### `RouteTableBookmarkObserver` → `IBookmarkLifecycleObserver` *(seam owned by `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contribution into the Core bookmark-lifecycle seam (`TryAddEnumerable`, Singleton).
- **Usage:** after a bookmark's durable create/consume commits, this observer rebuilds the whole route table from `IHttpEndpointRoutesResolver` (the same full re-projection as the trigger observer — a consumed bookmark's template must survive if another instance still awaits it) so a mid-flow endpoint's route appears on suspension and disappears on resume (spec 089 D, D-D4). Non-`HttpEndpoint` stimulus types return without touching the synchronizer. The read-then-swap is delegated to `IHttpEndpointRouteTableSynchronizer` (below), which serializes it and opens the fresh DI scope. **Failure policy differs from the trigger observer:** this fires on the RUN path, so an exception is caught and logged by the `BookmarkLifecycleNotifier` and NEVER faults the run — a stale route 404s until the next refresh.

### `UpdateRouteTableStartupTask` → `IStartupTask` *(Core — `Elsa.Tasks.Core`)*
- **Kind:** Registered startup task (Scoped).
- **Usage:** populates the per-shell `IRouteTable` from the durable trigger index at startup by delegating to `IHttpEndpointRouteTableSynchronizer.RefreshAsync`, so a fresh host or a restart has every published HTTP endpoint's route before the endpoint middleware runs — serialized against any concurrent observer refresh under the same lock. The index/bookmark observers keep it fresh thereafter.

---

## Internal serialization seam

### `IHttpEndpointRouteTableSynchronizer` *(internal seam — `Elsa.Workflows.Runtime.Http`; default impl `HttpEndpointRouteTableSynchronizer`)*
- **Kind:** Registered `TryAddSingleton`; owns a `SemaphoreSlim(1,1)`.
- **Signature:** `ValueTask RefreshAsync(CancellationToken ct = default)`.
- **Usage:** the single serialization point every route-table refresh routes through — the trigger-index observer (publish), the bookmark lifecycle observer (run), and the startup task all delegate their read-then-swap to it. `RefreshAsync` acquires the lock, opens a FRESH scope, resolves `IHttpEndpointRoutesResolver` + `IRouteTable`, does the full resolve + `Refresh`, then releases (review fix). **Why (lost-wakeup race):** the route table's read-then-swap is not atomic across the resolver read and the table swap; before this seam each caller opened its own scope and swapped independently, so a refresh built from a stale read could clobber a newer swap and permanently drop a live waiting-bookmark route — with no self-heal, because the healing notification had already fired. Serializing every refresh closes the window: refreshes run one at a time, and since every notification fires post-commit, each refresh's read observes all commits whose notifications preceded its lock acquisition; a commit landing after a read has already queued its own refresh, so no update is lost. Exceptions propagate unchanged (lock released in `finally`) — callers keep their own failure policy (trigger-index observer fails the publish; bookmark observer's notifier swallows+logs). **Override:** `services.Replace(...)` — but the default's serialization guarantee is load-bearing; a replacement must preserve it.

---

## Cross-references

- HTTP content downloads: [`Elsa.Http/EXTENSION_POINTS.md`](../Elsa.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
