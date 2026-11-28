# Extension points — Workflows.Runtime.Http domain

`WorkflowsRuntimeStandaloneHttp` supplies the standalone route projection and the default HTTP fault and
authorization handlers from `Elsa.Workflows.Runtime.Http`. It depends on `Http` for the shell route table and on
`Tasks` for startup and recurring convergence.

Each process maintains its own route table. The startup task loads persisted reservations and the post-commit
resource observer refreshes local changes. The recurring pump checks peer changes every
`RouteTableConvergenceIntervalSeconds` (default 5) and backs off on failure up to
`RouteTableConvergenceMaxBackoffSeconds` (default 60). A check materializes the scoped reservation rows and
refreshes the table when their fingerprint moves. Request admission rechecks the protected reservation.

## Overridable contracts

### `IHttpEndpointRoutesResolver` *(Feature contract — `Elsa.Workflows.Runtime.Http`)*
- **Signature:** `ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken ct = default)`.
- **Default impl:** `HttpEndpointRoutesResolver` reads reservation pages in the trusted physical scope, filters standalone resource owners, and projects method-specific routes relative to the HTTP base path. Security metadata describes the route for inventory. `IHttpEndpointAuthorizationHandler` enforces request authorization.
- **Convergence members:** `ResolveRouteSetAsync` returns routes and the fingerprint of the reservation rows used to project them. `ResolveRouteFingerprintAsync` fingerprints the current reservation rows. Owner and configuration generations and metadata participate, so revisions to access or mode trigger a refresh even when the route path is unchanged.
- **Override:** a replacement registered as a scoped service must compute both fingerprints from the same authority that supplies its routes.

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

### `UpdateRouteTableStartupTask` → `IStartupTask` *(Core — `Elsa.Tasks.Core`)*
- **Kind:** Registered startup task (Scoped).
- **Usage:** restores the per-shell `IRouteTable` from durable standalone reservations before request intake. It uses the same synchronizer as the resource observer and the convergence pump.

### `HttpEndpointRouteTableConvergencePumpTask` → `IRecurringTask` *(Core — `Elsa.Tasks.Core`; #2190)*
- **Kind:** Contribution into the Tasks recurring-task seam (`TryAddEnumerable`, Singleton), built on `BackoffSweepWorkerTask` with its adaptive schedule; runs on every node.
- **Usage:** each tick calls `IHttpEndpointRouteTableSynchronizer.ConvergeAsync`, which compares the current reservation fingerprint with the fingerprint of the last successful refresh and rebuilds only when they differ. Settings on the feature: `RouteTableConvergenceIntervalSeconds` (default 5, the convergence bound) and `RouteTableConvergenceMaxBackoffSeconds` (default 60); a non-positive or non-finite value fails `ConfigureServices`. Options type: `HttpEndpointRouteTableConvergenceOptions`. **Failure policy:** a failed check, a foreign `OperationCanceledException` such as a provider command timeout included, is logged at error level and widens the interval; only the shell's own cancellation ends a tick quietly.

---

## Internal serialization seam

### `IHttpEndpointRouteTableSynchronizer` *(internal seam — `Elsa.Workflows.Runtime.Http`; default impl `HttpEndpointRouteTableSynchronizer`)*
- **Kind:** Registered `TryAddSingleton`; owns a `SemaphoreSlim(1,1)`.
- **Signature:** `ValueTask RefreshAsync(CancellationToken ct = default)`.
- **Usage:** serializes startup, post-commit notifications and convergence. Each refresh opens a fresh scope, resolves `IHttpEndpointRoutesResolver` and `IRouteTable`, and replaces the table while holding the lock. The synchronizer remembers the fingerprint of the rows actually projected. A failed refresh clears it so the next check rebuilds the table. Replacements must preserve this ordering and retry behavior.

---

## Cross-references

- The classic authored route projection and observer contracts are described in the [pinned classic catalog](https://github.com/elsa-workflows/elsa-foundation/blob/ef70e5acd7fa8b07cfad4a6c0ca9a229a039f20a/extensions/Http/src/Workflows/Runtime/EXTENSION_POINTS.md).
- HTTP content downloads: [`Http/Elsa.Http/EXTENSION_POINTS.md`](../../Elsa.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`EXTENSION_POINTS.md`](../../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.

## Standalone resource route notification

`StandaloneTriggerResourceCommitted` is an inline post-commit management notification from Runtime Core. `StandaloneTriggerRouteObserver` handles HTTP resources by converging through the existing shared `IHttpEndpointRouteTableSynchronizer` before the management API acknowledges the change. Replaying an already committed authorized management request repeats the notification, repairing a failed local refresh without another mutation. It carries no invocation authority; the resolver reads the committed physical reservations. Peer nodes retain their existing background convergence. Direct store writes do not emit management notifications.
