# Extension points — Integrations SDK

The per-domain catalog (framework §2.22.1) for `Elsa.Integrations.Sdk`. Activities-only: no shell feature.

---

## Replaceable services

### `ISecretValueResolver` *(core contract — `Elsa.Secrets.Core`)*
- **Kind:** Replacement owned by Secrets. The SDK consumes it; it does not replace or wrap it with a cache.
- **Usage:** credential inputs (`[ActivityInput(IsCredential = true)]`) resolve at activation through the runtime's `IRuntimeSecretResolver`, which sits on this contract. Connector tests use `InMemorySecretValueResolver`.
- **Catalog:** [`Essentials/src/Secrets/Core/EXTENSION_POINTS.md`](../../../../Essentials/src/Secrets/Core/EXTENSION_POINTS.md).

---

## Named HTTP client

The SDK does not own a second `HttpClient`. `AddElsaIntegrationsRuntime` adds `IntegrationsHttpAuthHandler` to the named `Elsa.Activities.Http` client (`HttpActivityConstants.HttpClientName`). Outbound logging stays `HttpActivityClientLogger` on that client (spec 188 FR-019). See [`Essentials/src/Activities/Http/EXTENSION_POINTS.md`](../../../../Essentials/src/Activities/Http/EXTENSION_POINTS.md).

`IntegrationsHttpAuthScope.Push` is the connector-side applicator. It is not an auth-scheme catalog. The `http` connection type (api-key, basic, bearer), `[ActivityConnection]`, `SensitiveValue`, and `ConnectionUnavailable` belong to #2729 (`Elsa.Connections`). Rebase this handler into that pipeline when it lands rather than stacking a second one.

---

## Packaging copy

`ConnectorManifest.FromAssembly` is a build-time packaging copy of `[Activity]` / `[ActivityInput]` / `[Version]` / `[TriggerActivity]`. It is not an `IActivityReconciliationSource` and is never a catalog. CLR reconciliation remains the source of activity metadata.

---

## Cross-references

- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Connections plan: [`../../../../docs/plans/connections-and-secrets-model.md`](../../../../docs/plans/connections-and-secrets-model.md).
- Named credentials (host infrastructure, #2810): not this package.
- Constitutional basis: §E2.2, §E2.8, §2.22.1.
