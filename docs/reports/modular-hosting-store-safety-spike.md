# Nuplane Package-Use Safety Spike

Program: [Modular Hosting Upstream Delivery](../program-goals/modular-hosting-upstream-delivery.md). Research for [Nuplane #108](https://github.com/valence-works/nuplane/issues/108); source baseline `21e2c24fe8070a92ded13f7cc391353f7c6df856`, inspected 2026-10-08. Status: design evidence only; destructive implementation remains deferred.

## Decision

A crash-safe per-process lease appears feasible on the same filesystem assumptions as Nuplane's existing `.lock` protocol. This is an inference from source and a local lock-primitive probe; the complete record/admission protocol is not implemented or proven. A lease can protect exact resolved install paths across cooperating Nuplane processes while a prune operation holds the existing store lock. A tiny .NET 10/macOS probe below verified exclusive-open denial in the same process and a sibling process, then immediate reacquisition after killing the owner.

The protocol must be integrated at `PackageLoader` before any package-directory inspection (not after a successful load), must serialize lease publication with pruning using the exact same store lock, and must keep a lease through actual collectible-context death. `Unload()` alone is insufficient. Host-integrated/non-collectible contexts need process-lifetime leases. This permits live manual pruning of unreferenced/unleased installs; it cannot safely promise pruning of any package directory used by an old or non-participating process. That compatibility boundary is a product/operations decision, not something a file lock can discover.

## Grounded source facts

Snapshot: `/tmp/upstream-contracts.7hqYy5/nuplane`, HEAD `21e2c24`.

- `src/Nuplane/Store/State/StoreLock.cs:13-31,79-110`: the reconciliation lock is a persistent zero-length sibling of the state file, opened with `FileShare.None`; source comments specify Windows sharing exclusion and Unix advisory `flock`. It distinguishes `Unavailable` from `NotLockable`. `ReconciliationService.cs:258-275` holds it for the whole read/modify/write pipeline and refreshes persisted state while held. The lock is optional (`EnableStoreLock` defaults true; no resolved state path means `NotRequired`). Existing reconciliation permits `NotLockable`; destructive prune must require **Outcome == Acquired**, not merely `CanProceed`.
- `src/Nuplane.Loading/PackageLoader.cs:127-175`: graph mode selection and activation gates precede the load-path call. `:370-408` then resolves package assemblies from install paths and loads them. `:401-407` creates the graph context and opens assemblies. Registration must happen before advisors/gates if they may inspect disk, and certainly before `ResolveGraphPackages` / `ResolveMainAssemblyPath` / assembly/native resolution. The input graph already contains all `ResolvedPackage.InstallPath` entries, including dependencies; lease the complete graph, not just root IDs or loadable assemblies.
- `PackageGraphLoadContext` is collectible by default; `HostIntegratedPackageGraphLoadContext` passes `isCollectible: false`. `PackageLoader.cs:462-470,682-689,694-734` requests unload on replaced/inactive collectible contexts, but does not verify collection. `PackageUnloadCoordinator.cs:101-127` demonstrates the stronger existing unload proof: `Unload()` followed by a `WeakReference` liveness loop, reporting `UnloadPending` when still alive. Do not release on an unload request or catalog removal.
- `PackageAutoLoadingObserver.cs:92-95` unloads removed/superseded contexts before loading the next set. It is invoked inside the reconciliation pipeline, while `ReconciliationService` still owns the file store lock. A naïve lease manager that reacquires `IStoreLock` from `PackageLoader` will self-contend: `StoreLock` does not support reentrant `FileShare.None` opens (confirmed by the probe). A cross-layer lock handoff/ownership scope is required, or the design must demonstrate a safe deferred registration path. Do not silently ignore `Unavailable`.
- `src/Nuplane/Feeds/PackageInstallStore.cs:15-58`: completed installs are `<installRoot>/<sanitized-feed>/<sanitized-package-id>/<sanitized-version>/`, with `.nuplane-ready` as completion marker; `.tmp` is staging. Candidate identity should use the actual full `InstallPath`, not just package/version (feed path is part of identity).
- `StoreStateRecord.cs:16-23` has active version, last-known-good version, active package descriptors, and active graph records. For prune eligibility, refresh this state from disk under the existing store lock and protect active, LKG, and every path in their resolved dependency/graph closure. Runtime leases supplement those persisted protections; they do not replace them.
- Dependency direction: `Nuplane.Loading.csproj` references `Nuplane.csproj`; core `Nuplane.csproj` references only `Nuplane.Abstractions`, not Loading. Any shared use-lease contract must live in core or a lower shared abstraction, with the concrete provider in core (or another lower package). Do not make core reference `Nuplane.Loading` or Elsa/CShells. The lease mechanism belongs below any Workbench policy/API.

## Minimal protocol

Use the same resolved store identity and same `IStoreLock` used by reconciliation. Put immutable lease records and their lock sentinels in a Nuplane-owned sidecar directory derived from the resolved state-file path (for example `<state-path>.package-use/`). Do not put the records in a package install directory or in the state JSON. A memory-only/unresolved state store has no cross-process prune guarantee.

1. **Load admission.** Before any read/scan/load from a graph's package paths, acquire the store lock or borrow an explicit, valid ownership scope from the reconciliation operation that already holds it. Require actual acquired/borrowed ownership. While holding it, create a unique per-generation lease record containing schema version, opaque lease ID, and the full normalized install paths for every package in the resolved graph. Write/flush the immutable record, then open/hold its separate sentinel with `FileShare.None`. Only after both are established may the lock scope end and the loader touch package files. Unique never-reused IDs avoid stale-file/name-reuse races. Normalize paths with one OS-aware canonicalizer used by both loader and pruner.
2. **Prune snapshot + deletion.** Acquire the same store lock and require `Acquired`; refresh current persisted state while held; enumerate inventory and lease records; exclude active/LKG closures. For each lease, make exactly one nonblocking exclusive-open attempt on its sentinel. `IOException` means a live cooperating owner: read its immutable metadata and protect those exact paths, then continue; never wait or retry while holding the store lock. If exclusive open succeeds, keep that probe handle while treating the record as stale, then reap its record and sentinel. Malformed metadata under a live sentinel fails closed for that prune attempt; malformed metadata whose sentinel is exclusively acquired is stale because a loader acquires the sentinel before any package read. Plan and delete only remaining paths while still holding the store lock. Report per-candidate deletion failures; never report a failed delete as removed. Keep the state and package metadata unchanged by prune, since only non-active/non-LKG inventory is deleted.
3. **Lease lifetime.** On successful load, attach lease ownership to the actual load-context generation, not `PackageId@Version` alone. Retain for process lifetime when `IsCollectible == false`. For collectible contexts, call `Unload()` through existing paths and release only after a weak reference proves the context dead. If proof is pending/fails, keep the lease. On partial load failure, the same rule applies: a collectible context retains until dead; a non-collectible context may have irreversibly loaded files and therefore keeps the lease until process exit. A scan that proves no context was created/assembly loaded may dispose its transient lease after the scan.
4. **Crash recovery.** OS release of the sentinel is the liveness source, not PID/heartbeat/timeout. After a process crash, its sentinel can be opened exclusively, so stale metadata is ignored/reaped under the store lock. A partially written record is not visible to prune while its writer holds the store lock; if the process crashes before sentinel acquisition and lock release, its record is stale and no package read has started. Never steal a live sentinel based on age or PID.
5. **Lock ordering and deadlock avoidance.** A generation may retain its per-lease sentinel for its lifetime and later enter reconciliation, so lease-holder → global-store-lock acquisition is valid and expected. Load admission during a reconciliation borrows the already-owned global lock; it does not open it again. Prune holds the global store lock, but probes each lease sentinel **nonblocking** and never waits/retries for one. If a lease sentinel is busy, prune protects that record's paths and continues. This breaks the cycle where a process holding an old lease waits for the global lock while prune holds that lock and waits for the lease. The process retains lease sentinels without holding the global lock between operations. Weak-unload cleanup closes its sentinel (and may then remove its unique record) without first acquiring the global lock; unique IDs plus the global lock around publication/probing/reaping prevent record replacement races. The reconciliation callback path already owns the global lock, so it needs a real scoped ownership token/reentrant participant path; a second direct `FileShare.None` open demonstrably fails. An ambient token must be scoped/invalidation-safe across async continuations, or passed explicitly through an additive reconciliation/load boundary; a blanket AsyncLocal "someone in this process owns the lock" bypass is too broad.

## Minimal API seam

Core should expose the smallest cross-module contract needed by its prune operation and `Nuplane.Loading` integration, e.g. a core-owned `IPackageUseLeaseRegistry` with:

- `Acquire(paths, storeLockScope)` returning an opaque `IDisposable` lease;
- a prune-only `GetProtectedPathsUnderStoreLock(scope)` / snapshot operation which requires proof that the caller owns the same acquired store lock;
- a scope/capability for the existing reconciliation owner so the observer can publish a lease without nested OS-lock acquisition.

The precise public vs internal surface depends on how existing `Nuplane` composition is exposed; avoid returning file handles, exposing Nuplane's internal `IStoreLock`, or taking an `AssemblyLoadContext` dependency in core. `Nuplane.Loading` owns the association between the opaque lease and load-context generation, and is the only layer that knows collectibility/unload status. A new public API should be added only if the existing public builder/observer seams cannot carry this internal capability safely. The core package can own sidecar format, locking and file operations; Loading supplies lifetime notifications through the opaque token. `Nuplane.Abstractions` is a fallback if the contract genuinely must be implemented by an independently packaged provider, but is not automatically needed because Loading already references Nuplane.

## Practical limits and owner decisions

- **Legacy/non-cooperating host:** impossible to detect. A process that loads packages but never creates Nuplane leases can continue using an install directory while a participating process deletes it. The existing reconciliation lock does not cover the lifetime after a cycle ends. Before destructive readiness, the owner must choose and ratify the admission boundary: either prove/require every process sharing the install root runs the lease-capable version (an explicit protocol-version/admission barrier for rolling upgrades), or explicitly exclude legacy/third-party shared-root users from the safety guarantee. A stale lease scan cannot infer old binaries; without an accepted boundary, live destructive prune must not be declared safe for that shared root.
- **`EnableStoreLock=false`, missing state path, un-lockable filesystem:** live destructive prune cannot safely proceed with this protocol. Keep existing runtime behavior unchanged, but have prune return a clear lock-unavailable/unsupported result. The default configured persistent-store path with lock enabled remains usable; do not weaken failure handling to preserve the endpoint.
- **Non-collectible host-integrated contexts:** those package paths are protected until process exit. Pruning other unused installs remains available. If owner requires pruning host-integrated packages during the same process lifetime, that conflicts with their non-collectible load-context contract and must be resolved at that owner/product level.
- **Automatic vs manual policy:** the existing `PackageCleanupService` is a policy evaluator (eligible/kept decisions), not an authoritative store deletion service. Keep cleanup policy and user-triggered prune orchestration distinct. Product owner should decide whether live prune is explicitly manual/preview-confirmed or also automatic; the technical lease protocol does not choose deletion policy.
- **Filesystem portability:** `FileShare.None` claim currently documented by Nuplane and observed on local macOS; deployments on network/overlay filesystems need capability validation. Fail closed if sentinel or store lock semantics cannot be relied on. Do not claim leases fence noncooperating processes or arbitrary external deletes.

## Acceptance proof tests for a product implementation

1. Same-process and cross-process: live lease protects its exact graph paths; sibling prune cannot delete them; an unrelated unleased package is still pruned. Include a second Nuplane composition in one process.
2. Crash: child process publishes lease, confirms load admission, then is killed; next prune under the store lock sees stale sentinel and can reap it and prune its paths. Also kill between record write and sentinel acquisition; assert no loader file read occurred and recovery safely reaps.
3. Races: (a) prune holds store lock, loader cannot publish lease/read paths until prune completes; (b) loader publishes first, prune observes paths and keeps them; (c) reconcile owns lock then invokes observer loader, publishes without a second lock open; (d) concurrent reconcile and prune never use stale in-memory active/LKG state.
4. Lifetime: collectible context lease survives `Unload()` while weak reference is alive and releases only after it dies; `UnloadPending` keeps protection. Non-collectible and partial-failure non-collectible contexts remain protected until process exit. Replaced shared graphs only release after the last key stops referencing the context and weak-death proof completes.
5. State graph: active roots, LKG roots, active dependencies/support assets, and any package path reached by runtime assembly/native resolution are protected even if not a standalone active package ID. Candidate paths include feed identity and are canonicalized consistently.
6. Fail closed: lock contention, `NotLockable`, `NotRequired`, malformed live metadata, unsupported store identity, permission/delete failure, and unexpected IO do not delete candidates; result explains skipped/failed candidates. A lease-file left by crash is recoverable without trusting PID or timeout.
7. Behavior preservation: existing automatic cleanup policy decisions and default reconciliation remain unchanged; loading works with no prune configured; prune with no registered Loading module still protects persisted active/LKG data.

## Local lock/liveness spike

Owned temp-only files: `/tmp/nuplane-store-use-spike/probe/LeaseProbe.csproj`, `Program.cs`, and `probe-output.txt`. No Nuplane package store was touched and no Nuplane source was edited. The probe tests the same `FileStream(FileShare.None)` primitive used by `StoreLock`; the owner child is killed, not asked to clean up its lock.

Exact command:

```sh
dotnet run --project /tmp/nuplane-store-use-spike/probe/LeaseProbe.csproj > /tmp/nuplane-store-use-spike/probe-output.txt 2>&1
```

The machine wrapper was used (`command -v dotnet` => `/Users/sipke/.local/share/dotnet-build-slots/bin/dotnet`); SDK `10.0.300`, macOS arm64 8 CPU cores. Load was high at invocation (reported load averages 106.71/61.56/47.33), so no timing claim is made. The probe used a unique lock path under its owned temp directory, bounded owner readiness and exit waits, kills the owner in `finally` on every failure path, and deletes only that unique lock file. Preserved output:

```text
same-process-second: DENIED(IOException)
same-process-after-dispose: ACQUIRED
cross-process-contender: DENIED(IOException)
after-owner-crash: ACQUIRED
```

This proves the local OS/runtime lock primitive and crash release, not the proposed Nuplane record protocol, cross-platform deployment filesystems, or deletion safety. The probe deletes its task-owned unique lock path and leaves only source, report, and output proof; no Nuplane install directory was created or touched.

## Reproduction source

The following standalone probe is preserved for reproducibility. Create these two files in a task-owned temporary directory and run the command above, updating the project path. It is separate from product code and tests; passing it does not authorize deletion.

`LeaseProbe.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

`Program.cs`:

```csharp
using System.Diagnostics;
using System.Reflection;

if (args.Length > 0 && args[0] == "hold")
{
    using var held = Open(args[1]);
    Console.WriteLine("OWNER_READY");
    Console.Out.Flush();
    Console.ReadLine();
    return;
}

var ownedDirectory = "/tmp/nuplane-store-use-spike/probe/owned";
Directory.CreateDirectory(ownedDirectory);
var path = Path.Combine(ownedDirectory, $"lease-{Guid.NewGuid():N}.lock");
try
{
    using (var sameProcessFirst = Open(path))
    {
        Console.WriteLine($"same-process-second: {Attempt(path)}");
    }
    Console.WriteLine($"same-process-after-dispose: {Attempt(path)}");

    var dll = Assembly.GetEntryAssembly()!.Location;
    var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    using var owner = new Process
    {
        StartInfo = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true
        }
    };
    owner.StartInfo.ArgumentList.Add(dll);
    owner.StartInfo.ArgumentList.Add("hold");
    owner.StartInfo.ArgumentList.Add(path);

    try
    {
        owner.Start();
        var ready = await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        if (ready != "OWNER_READY")
            throw new InvalidOperationException($"Unexpected owner output: {ready}");

        Console.WriteLine($"cross-process-contender: {Attempt(path)}");
        owner.Kill(entireProcessTree: true);
        await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine($"after-owner-crash: {Attempt(path)}");
    }
    finally
    {
        if (!owner.HasExited)
        {
            owner.Kill(entireProcessTree: true);
            await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
finally
{
    if (File.Exists(path))
        File.Delete(path);
}

static FileStream Open(string path) => new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
static string Attempt(string path)
{
    try { using var stream = Open(path); return "ACQUIRED"; }
    catch (IOException) { return "DENIED(IOException)"; }
}
```
