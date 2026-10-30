# Studio package-store qualification

Architecture spike [#3353](https://github.com/elsa-workflows/elsa-foundation/issues/3353),
[Program #3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340).

## Work-unit plan

Qualify the released Nuplane 0.0.12 and CShells 0.0.31 surfaces before extracting Studio administration.
Success requires real coordinated enrollment, directory-feed install/update/removal, active and retained
reader protection, physical prune outcomes, and an admitted asset read overlapping reconciliation.
Use the existing upstream maintenance service and CShells adapter. Keep production code unchanged in this
spike; hand the bounded adoption to #3356/#3359. No replacement loader, store manager, release or customer
store operation belongs here. Own every temporary store and remove it after providers and leases drain.

Review the chosen API, refusal profiles, result mapping and actual executed evidence. Keep copied bytes
separate from package-backed response lifetimes. Upstream proof is provenance, not Studio acceptance.

## Selected reuse and bounded implementation delta

Replace `ElsaModuleManagementApi.PrunePackageInstallDirectories` with the released
`Nuplane.Admin` `INuplanePackageStoreMaintenanceOperations`. Explicitly register
`AddNuplanePackageStoreMaintenance`; `AddNuplaneAdmin` alone does not enable it.
Use Preview, inspect protection/inventory diagnostics, then explicitly confirmed Execute bound to the
configured root label, observed physical identity, enrollment epoch and retention policy. Execution replans
under ownership; never translate a preview candidate list into `Directory.Delete` calls. Map classification,
attempt and physical outcome separately, including partial/cancelled facts and AlreadyAbsent.

Pruning requires trusted coordinated composition and a complete roster, including offline clients. Enrollment
and migration require an explicit quiescent cutover; a management request must not enroll automatically or
assert that undeclared clients do not exist. Ordinary hosts remain supported. Their Preview can return
`Status=Preview` with `ProtectionKnowledge=Unknown`, incomplete inventory, no root identity/epoch and
`EnrollmentRequired`. Checking Status alone is insufficient. There is no qualified confirmation to execute;
an Execute request without exact confirmation is rejected. Preserve diagnostics and refuse deletion, with no
local filesystem fallback. Unsupported native admission and uncertain membership/protection remain refusals.

Replace package-backed `Results.File(path)` without protection with an admitted read whose protection outlives
all package access. The qualified small-asset route is `IPackageStoreAdmission.AcquireForInstallPathsAsync`,
`BorrowFor`, then `PackageContent.TryReadFile(installPath, relativePath, borrow)`. Release ownership only after
bytes are detached, then return the detached payload. Read package descriptors before admission: attempting a
second catalog read while holding root ownership correctly refuses the busy root. Handle that outcome as a
retryable conflict instead of attempting nested admission or bypassing it. The HTTP adapter still owns route,
authorization, content type, range/caching and error projection policy. Never accept arbitrary filesystem paths.

The proof deliberately uses a tiny text asset. `TryReadFile` buffers the full file. Production adoption must
bound accepted asset sizes/concurrency or qualify a protected streaming alternative before claiming support
for arbitrary large files. A returned physical-path result that opens the file after protection is disposed is
not equivalent. Linked/traversal content must pass Nuplane's guarded content reader rather than a second local
path resolver. Full Studio browser delivery and large/binary asset behavior remain #3363/#3364 acceptance.

Studio's current count/age union and modification-time ordering are not Nuplane's retention policy.
`PackageStoreRetentionPolicy(N)` keeps N inactive, unprotected NuGet-version groups; protection always wins,
and no supplied policy retains everything. #3359 must expose that supported policy explicitly and migrate the
existing settings/UI deliberately. Do not silently reinterpret age-based settings, count active versions against
the inactive budget, or add an app-owned age-based deletion pass. If an existing setting cannot be honored,
explain the unsupported policy and refuse execution until a supported policy is selected.

#3356 aligns the released dependencies and replaces the local observer/provider. #3359 adopts maintenance,
configurable root/roster policy and protected package-content access in the optional integration, retaining Studio
HTTP authorization and its atomic configuration writer. No Nuplane dependency belongs in the basic Studio host.
The host's existing protected assembly/catalog lifetimes remain owned by Nuplane/CShells.

## Executed evidence

The sibling [StorePruning probe](../../tools/studio/HostingReuse.Probe/StorePruning/Program.cs) consumes the released
Nuplane 0.0.12 family and CShells.Nuplane 0.0.31. It uses real native admission, public enrollment/maintenance,
real directory-feed `.nupkg` installation and loading, the real shell registry/adapter, and a loopback Kestrel
HTTP request. No source substitution or fake maintenance/catalog is used. Fixture packages and stores are owned
by the process; a content-only installer is stopped/disposed before the Studio composition opens the same
single-member store. The former install is a genuine obsolete candidate, not manually fabricated inventory.

The run on .NET 10.0.13 passed six groups:

1. Ordinary composition reports EnrollmentRequired, lacks execution binding and preserves the legacy sentinel;
   unconfirmed Execute is rejected.
2. Public enrollment completes and the enrolled store reopens after the installer closes.
3. First feature installation supplies the serving shell. Confirmed zero-inactive-retention prune retains the
   active/selected-recovery/live package and physically deletes the obsolete content-only install.
4. Update supplies the second feature. A first-generation scope still resolves its service; the first install
   is retained **solely** for ProtectedLiveUse, while the deleted install reports AlreadyAbsent on repetition.
5. A real HTTP Range request pauses with admitted ownership before reading bytes. Concurrent manual reconciliation
   returns Unavailable for busy root.lock; exact state bytes and serving-generation identity remain unchanged.
   Releasing the gate permits the protected read and an actual 206 response containing the requested bytes.
6. Retrying reconciliation removes the final package and replaces the serving registrations with an empty shell.
   Confirmed prune still retains the outstanding old reader; its scope and detached response bytes remain usable.

Preview is checked for no physical attempts and unchanged member-state bytes. The admission control
`--release-reader-before-read` disposes the borrow before the actual HTTP read; the run must exit 1 at the
range-response assertion. Restoring the normal run must exit 0. These controls do not claim actual old-context
collection after the retained scope drains. The 100ms deactivation timeout is a probe configuration, not a lease
expiry; ProtectedLiveUse survives it. No production garbage collection, pruning timer or timeout-based deletion
is introduced.

## Provenance and limits

The source baseline is Foundation `db632dd2a2c3107054160ebef971a2020e8daa8e`. Nuplane reports informational source
`80f74d02bef70a0410f9dd21064692bc088bb87c`; package identities are pinned in central versions and the probe locks.
Use the upstream [maintenance contract](https://github.com/valence-works/nuplane/blob/80f74d02bef70a0410f9dd21064692bc088bb87c/docs/package-store-maintenance.md)
and [public API source](https://github.com/valence-works/nuplane/blob/80f74d02bef70a0410f9dd21064692bc088bb87c/src/Nuplane.Admin/INuplanePackageStoreMaintenanceOperations.cs).
The [existing upstream program record](../program-goals/modular-hosting-upstream-delivery.md) provides the prior
multiplatform/lifetime/crash-recovery provenance. This spike does not rerun that matrix, qualify legacy external
clients, or change the parked program's status. Current execution covers this macOS ARM64 filesystem and .NET10,
small text assets, a single declared client and bounded loader/HTTP overlap. It is a decision/probe result;
production extraction and final isolated NuGet-only Studio/browser acceptance remain open.
