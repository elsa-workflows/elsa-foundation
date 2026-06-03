# Extension points — Locking domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Locking.FileSystem` — the default-provider feature that ships `DistributedLockProviderAdaptor`. No contributor interfaces or events; only an overridable contract.

---

## Overridable contracts

### `IDistributedLockProvider` *(Core — `Elsa.Locking.Core`)*
- **Signature:** `IDistributedSynchronizationHandle? TryAcquireLock(string name)`, `ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, CancellationToken ct)`, `ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, CancellationToken ct)`.
- **Default impl:** `DistributedLockProviderAdaptor` (this feature) — wraps `Medallion.Threading.FileSystem`. Registered as a singleton by `FileSystemLockingFeature`.
- **Override:** replace with a different distributed lock backend (Redis, SQL Server, Azure Blob, in-memory for tests) by registering your own `IDistributedLockProvider` before or instead of this feature. Pure *replace-one-keep-rest* override — no other system contracts need to change.

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
