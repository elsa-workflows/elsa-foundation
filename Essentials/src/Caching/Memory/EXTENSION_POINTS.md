# Extension points — Caching domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Caching.Memory` — the default-provider feature that ships the in-memory cache stack. No contributor interfaces or events; only overridable contracts.

---

## Overridable contracts

All three contracts below collaborate as a stack: `ICacheManager` depends on `IChangeTokenSignaler`, which depends on `IChangeTokenSignalInvoker`. Replace one independently or the whole stack. All defaults are registered as singletons by `MemoryCacheFeature`.

### `ICacheManager` *(Core — `Elsa.Caching.Core`)*
- **Signature:** `IChangeToken GetToken(string key)`, `ValueTask TriggerTokenAsync(string key, CancellationToken ct)` (+ caching primitives).
- **Default impl:** `CacheManager` (this feature) — in-memory; delegates invalidation to `IChangeTokenSignaler`.
- **Override:** `services.Replace(ServiceDescriptor.Singleton<ICacheManager, MyManager>())`.

### `IChangeTokenSignaler` *(Core — `Elsa.Caching.Core`)*
- **Signature:** `ValueTask TriggerTokenAsync(string key, CancellationToken ct)`, `IChangeToken GetToken(string key)`.
- **Default impl:** `ChangeTokenSignaler` (this feature) — delegates to `IChangeTokenSignalInvoker`.
- **Override:** `services.Replace(ServiceDescriptor.Singleton<IChangeTokenSignaler, MySignaler>())`.

### `IChangeTokenSignalInvoker` *(Core — `Elsa.Caching.Core`)*
- **Signature:** `ValueTask TriggerTokenAsync(string key, CancellationToken ct)`, `IChangeToken GetToken(string key)`.
- **Default impl:** `ChangeTokenSignalInvoker` (this feature) — manages the token store and invocations.
- **Override:** `services.Replace(ServiceDescriptor.Singleton<IChangeTokenSignalInvoker, MyInvoker>())`.

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
