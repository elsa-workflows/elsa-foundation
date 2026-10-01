# Extension points — Locking domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Locking.FileSystem` — the default-provider feature that ships `DistributedLockProviderAdaptor` — and covering its database sibling `Elsa.Locking.Database`. No contributor interfaces or events; only an overridable contract.

---

## Overridable contracts

### `IDistributedLockProvider` *(Core — `Elsa.Locking.Core`)*
- **Signature:** `IDistributedSynchronizationHandle? TryAcquireLock(string name)`, `ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, CancellationToken ct)`, `ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, CancellationToken ct)`. A handle's `HandleLostToken` fires when the lock is lost before it is released; a provider that cannot detect loss returns `CancellationToken.None`.
- **Default impl:** `DistributedLockProviderAdaptor` (this feature) — wraps `Medallion.Threading.FileSystem`. Registered as a singleton by `FileSystemLockingFeature`.
- **Override:** replace with a different distributed lock backend (Redis, SQL Server, Azure Blob, in-memory for tests) by registering your own `IDistributedLockProvider` before or instead of this feature. Pure *replace-one-keep-rest* override — no other system contracts need to change.

---

## Providers, and which one a composition needs

A lock excludes only the processes that reach the same lock store. Everything that relies on a lock across nodes —
`[SingleNodeTask]` ([its guarantee](../../Tasks/EXTENSION_POINTS.md#singlenodetask-one-at-a-time-at-shell-start)), the
design-side draft locks, the activity publisher — therefore needs a store every node shares (#2192).

### `FileSystemDistributedLocking` *(`Elsa.Locking.FileSystem`)*
- Lock files in `LocksFolderPath`, by default `App_Data/locks` under the process's working directory. That folder is
  **node-local**: it excludes the processes of one machine that share it, and no others.
- **Refused in a cluster on its default folder.** A host that composes a durable cluster membership provider (the EF
  provider, enabled by `Elsa:Cluster:Membership:EntityFrameworkCore:Enabled`) refuses this feature at shell start while
  `LocksFolderPath` is not configured, naming the provider and both ways out: compose `DatabaseDistributedLocking`, or set
  `LocksFolderPath` to a folder every node shares. A configured folder is trusted to be shared, even one equal to the
  default, because setting a shared path is that remedy; only a default nobody chose is known not to be. In-process
  (non-durable) membership gets no refusal.
- **SQLite compositions keep it.** SQLite has no database lock provider, so a SQLite composition uses this feature and is
  single-node by definition. Several processes on that one machine (durable membership on SQLite serves only those) must
  name a `LocksFolderPath` they all share.

### `DatabaseDistributedLocking` *(`Elsa.Locking.Database`)*
- Locks held by the shared database's own session locks through Medallion's providers: a PostgreSQL advisory lock
  (`DistributedLock.Postgres`), SQL Server's `sp_getapplock` (`DistributedLock.SqlServer`) or MySQL's `GET_LOCK`
  (`DistributedLock.MySql`). No table and no migration; a lock is released when its connection closes, including when the
  process dies, and a handle's `HandleLostToken` fires when its connection is lost.
- **Settings:** `Provider` (`PostgreSql`, `SqlServer` or `MySql`; required, and `Sqlite` is refused with a pointer back to
  the file-system feature), `ConnectionString`, or `ConnectionName` naming an entry under `ConnectionStrings`, with
  `ConnectionStrings:Elsa` as the fallback, as for the EF modules; and `LockAcquisitionTimeoutMinutes` (10). Every node
  must reach the same primary, normally the database the EF modules use.

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
