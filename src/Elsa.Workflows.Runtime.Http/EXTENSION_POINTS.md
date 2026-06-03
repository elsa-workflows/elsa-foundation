# Extension points — Workflows.Runtime.Http domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Workflows.Runtime.Http` — a provider/feature project that ships default implementations of the HTTP endpoint behaviour contracts. All three contracts are feature contracts (defined here, not in a `.Core`). No contributor interfaces or published events.

---

## Overridable contracts

### `IHttpEndpointRoutesResolver` *(Feature contract — `Elsa.Workflows.Runtime.Http`)*
- **Signature:** `Task<IEnumerable<HttpRouteData>> GetRoutes(string path, CancellationToken ct)`
- **Default impl:** `HttpEndpointRoutesResolver` (this feature) — resolves HTTP endpoint routes registered in the workflow catalog.
- **Override:** `services.Replace(ServiceDescriptor.Scoped<IHttpEndpointRoutesResolver, MyResolver>())` — e.g. to load routes from a custom store, add caching, or filter routes.

### `IHttpEndpointAuthorizationHandler` *(Feature contract — `Elsa.Workflows.Runtime.Http`)*
- **Signature:** `ValueTask<bool> AuthorizeAsync(AuthorizeHttpEndpointContext context, CancellationToken ct)`
- **Default impl:** `AuthenticationBasedHttpEndpointAuthorizationHandler` (this feature).
- **Override:** swap via the feature's `AuthorizationHandlerType` property (replaces the DI registration at feature startup) — or `services.Replace(...)` directly for custom authorization logic.

### `IHttpEndpointFaultHandler` *(Feature contract — `Elsa.Workflows.Runtime.Http`)*
- **Signature:** `ValueTask HandleAsync(HttpEndpointFaultContext context, CancellationToken ct)`
- **Default impl:** `HttpEndpointFaultHandler` (this feature) — writes a problem-details response.
- **Override:** swap via the feature's `FaultHandlerType` property — or `services.Replace(...)` for custom fault handling (custom error shapes, logging, alerting).

---

## Cross-references

- HTTP content downloads: [`Elsa.Http/EXTENSION_POINTS.md`](../Elsa.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
