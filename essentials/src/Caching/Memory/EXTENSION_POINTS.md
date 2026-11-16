# Extension points — Caching domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Caching.Memory` — the default-provider feature that ships the in-memory cache stack. No contributor interfaces or events; only overridable contracts.

---

## Overridable contracts

`ICacheManager` is the one override seam. Its default builds on two internal classes, `ChangeTokenSignaler` and `ChangeTokenSignalInvoker`, which hold and trigger the change tokens. `MemoryCacheFeature` registers all three as singletons.

### `ICacheManager` *(Core — `Elsa.Caching.Core`)*
- **Signature:** `IChangeToken GetToken(string key)`, `ValueTask TriggerTokenAsync(string key, CancellationToken ct)` (+ caching primitives).
- **Default impl:** `CacheManager` (this feature), in memory; it delegates invalidation to `ChangeTokenSignaler`.
- **Override:** `services.Replace(ServiceDescriptor.Singleton<ICacheManager, MyManager>())`.

---

## Cross-references

- Repo-wide index: [`EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
