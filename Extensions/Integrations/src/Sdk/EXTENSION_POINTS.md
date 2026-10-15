# Extension points — Integrations SDK

The per-domain catalog (framework §2.22.1) for `Elsa.Integrations.Sdk`. Activities-only: no shell feature.

---

## Replaceable services

### `IConnectionLookup` *(runtime contract — `Elsa.Integrations.Sdk.Connections`)*
- **Kind:** Replacement. One implementation wins.
- **Usage:** tenant-scoped snapshot lookup. Production persistence is `Elsa.Connections` (plan D6). The testing harness registers `InMemoryConnectionLookup`.
- **Register:** `services.AddSingleton<IConnectionLookup, …>()`. `AddElsaIntegrationsRuntime` does not register a default.

### `ISecretValueResolver` *(core contract — `Elsa.Secrets.Core`)*
- **Kind:** Replacement owned by Secrets. The SDK consumes it; it does not replace or wrap it with a cache.
- **Usage:** `IConnectionHandle.GetSensitiveAsync` and authenticators resolve per activation.
- **Catalog:** [`Essentials/src/Secrets/Core/EXTENSION_POINTS.md`](../../../../Essentials/src/Secrets/Core/EXTENSION_POINTS.md).

---

## Implementable contributor interfaces

### `IConnectionAuthenticator` *(open scheme contract — plan D3)*
- **Kind:** Contributor. `IEnumerable<IConnectionAuthenticator>` is aggregated by `ConnectionAuthDelegatingHandler`.
- **What it does:** applies one authentication scheme to an outbound `HttpRequestMessage` using the short-lived handle. OAuth is one scheme; it is not the model and does not ship in this package.
- **Register:** `services.TryAddEnumerable(ServiceDescriptor.Singleton<IConnectionAuthenticator, T>())`.
- **Known implementations:**
  - `BearerConnectionAuthenticator` *(intra-domain — default)*
  - `ApiKeyConnectionAuthenticator` *(intra-domain — default)*
  - `BasicConnectionAuthenticator` *(intra-domain — default)*

---

## Named HTTP client

The SDK does not own a second `HttpClient`. `AddElsaIntegrationsRuntime` adds `ConnectionAuthDelegatingHandler` to the named `Elsa.Activities.Http` client (`HttpActivityConstants.HttpClientName`). Outbound logging stays `HttpActivityClientLogger` on that client (spec 188 FR-019). See [`Essentials/src/Activities/Http/EXTENSION_POINTS.md`](../../../../Essentials/src/Activities/Http/EXTENSION_POINTS.md).

---

## Design-time descriptors

`ConnectionTypeDescriptor`, `AuthSchemeDescriptor`, and `ConnectionFieldDescriptor` live in `Elsa.Integrations.Sdk.Design`. Runtime execution never loads them to execute (#2726 rule 8).

---

## Packaging copy

`ConnectorManifest.FromAssembly` is a build-time packaging copy of `[Activity]` / `[ActivityInput]` / `[Version]` / `[TriggerActivity]`. It is not an `IActivityReconciliationSource` and is never a catalog. CLR reconciliation remains the source of activity metadata.

---

## Cross-references

- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Connections plan: [`../../../../docs/plans/connections-and-secrets-model.md`](../../../../docs/plans/connections-and-secrets-model.md).
- Constitutional basis: §E2.2, §E2.8, §2.22.1.
