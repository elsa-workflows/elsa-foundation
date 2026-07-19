# Persistence.Core extension points

Provider-neutral persistence scope and operation-access selection. Domain and provider implementations
consume these seams without depending on Groundwork or another concrete database provider.

## Override — replacement contracts

| Contract | Default implementation | Signature | When to override |
|---|---|---|---|
| `IPersistenceAccessContextAccessor` *(Core — `Elsa.Persistence.Core`)* | Scoped nonblank default registered by `AddPersistenceCore`; its `Current` value is `PersistenceAccessContext.Scoped(new PersistenceScope("default"))` unless the host selects another default scope. | `PersistenceAccessContext Current { get; }` | Multi-tenant or privileged hosts replace this registration with a **scoped** selector that returns one immutable context for the current request/operation scope. Absence of tenant context must never grant global access. |
| `IPersistenceAccessContextBinder` *(Core — `Elsa.Persistence.Core`)* | The same scoped default instance as the accessor; accepts one explicit transported context per fresh DI scope. | `void Bind(PersistenceAccessContext context)` | A host that replaces the accessor and uses actor, distributed, or recurring background paths must also replace this contract with the **same scoped state holder**. Duplicate binding must fail closed. |
| `IPersistenceOperationScopeFactory` *(Core — `Elsa.Persistence.Core`)* | Singleton factory that creates a fresh async DI scope, binds the transported `PersistenceScope`, and verifies the effective accessor accepted it. | `CreateAsync(PersistenceScope, CancellationToken)` | Replace only when the host owns an equivalent scope-creation boundary; the replacement must bind before resolving persistence consumers and dispose failed scopes. |
| `IPersistenceScopeSource` *(Core — `Elsa.Persistence.Core`)* | Singleton finite source containing only the configured default scope. | `GetScopesAsync(CancellationToken)` | Multi-tenant hosts replace this with the finite, duplicate-free partition snapshot that recurring infrastructure must visit during one run. Returning no entry never means global access. |
| `IPersistenceScopeRunner` *(Core — `Elsa.Persistence.Core`)* | Singleton runner over `IPersistenceScopeSource` and `IPersistenceOperationScopeFactory`. | `RunAsync(operation, CancellationToken)` | Usually retained. A replacement must open one fresh ordinary operation scope per supplied partition, continue after non-fatal per-partition failures, and surface failures after the snapshot is exhausted. |

For request-only selection, replace the accessor with the ordinary Microsoft DI replacement mechanism,
retaining the scoped lifetime. Hosts that execute transported/background operations must register one scoped
holder implementing both accessor and binder, then map both contracts to that same instance:

```csharp
services.AddScoped<MyPersistenceAccessContext>();
services.Replace(ServiceDescriptor.Scoped<IPersistenceAccessContextAccessor>(sp =>
    sp.GetRequiredService<MyPersistenceAccessContext>()));
services.Replace(ServiceDescriptor.Scoped<IPersistenceAccessContextBinder>(sp =>
    sp.GetRequiredService<MyPersistenceAccessContext>()));
services.Replace(ServiceDescriptor.Singleton<IPersistenceScopeSource, MyPersistenceScopeSource>());
```

## Provider-neutral composition seam

| API | Responsibility | Rules |
|---|---|---|
| `AddPersistenceCore(IServiceCollection, string defaultScope = PersistenceScope.DefaultValue)` | Registers the provider-neutral current-context accessor when the host has not already supplied one. | Call before resolving persistence consumers. The scope value must be nonblank and have no surrounding whitespace. The registration uses `TryAddScoped`, so a host may register its scoped accessor before this method or replace the default afterward. |

`AddPersistenceCore` does not select a database provider and does not grant privileged, global, or
cross-scope access. Provider adapters validate and translate the selected `PersistenceAccessContext`
at their own persistence boundary. Actor command envelopes carry a provider-neutral partition value;
host-lifetime mailboxes and recurring tasks use the operation-scope factory/runner rather than retaining
a scoped accessor or store.

## Cross-references

- Groundwork adapter and provider admission: [`../Groundwork/EXTENSION_POINTS.md`](../Groundwork/EXTENSION_POINTS.md)
- Feature 094 operator and scope validation guide: [`../../../../specs/094-harden-groundwork-stores/quickstart.md`](../../../../specs/094-harden-groundwork-stores/quickstart.md)
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md)
