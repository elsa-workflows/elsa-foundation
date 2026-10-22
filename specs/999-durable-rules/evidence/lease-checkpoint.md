# SQLite worker lease checkpoint

This internal checkpoint adds compatible-work claim/reclaim, renewal and release to the migrated SQLite Rules store. It does not complete T011 or T017: work snapshots, accepted/consumed receipt transitions and response-loss replay are still pending, as are actual runtime composition and package/process/native/four-provider/upgrade qualification.

## Transaction design

Claims page bounded addresses, then load one candidate's bounded state. Exact canonical package-pin identity and supported invocation/receipt/state schemas select compatible work; a retained unsupported candidate is left unchanged and does not hide later compatible work. Claim/reclaim increments the worker fence. Renewal preserves the fence, and release invalidates it. All preserve the semantic model revision. Full retained identity is checked before other state is parsed or returned; a simulated address collision yields Conflict without a lease payload. Unavailable is distinct from lost ownership. Both new enum values are appended, preserving existing numeric values.

All writes use tracked SaveChanges inside an explicit transaction, preserving the shared schema write gate and the row concurrency token. Renewal explicitly marks its expiry property modified even when the value is unchanged: otherwise EF could omit the write and its compare-and-swap. The provider samples database time after all writes and before commit. An expired lease or a clock sample earlier than the initial sample rolls the transaction back. Claim checks its new expiry; renew/release check the previous persisted expiry, never a caller's old timestamp or the proposed extended expiry. The row write lock spans the post-write check and commit. The guarantee is authorization at the transactional mutation/check point, not a commit-before-wall-clock-expiry promise.

SQLite uses integer UTC seconds and milliseconds from its VFS-backed SQL clock, avoiding floating-point arithmetic in .NET ticks. Each clock check is a separate SQL statement: [SQLite documents that `now` is stable within a sqlite3_step call](https://www.sqlite.org/lang_datefunc.html), not throughout a transaction. The real-clock regression confirms UTC conversion, millisecond precision and progress inside an open transaction. The deterministic interleaving tests replace only the tagged clock query result, while running actual migrated SQLite writes/transactions. They do not establish a server-provider qualification. Future PostgreSQL support must use a live clock such as [clock_timestamp](https://www.postgresql.org/docs/current/functions-datetime.html#FUNCTIONS-DATETIME-CURRENT), not a transaction-start timestamp. Undetected wall-clock adjustments and physical external effects remain separate operational limitations.

The internal store currently accepts a trusted host lease duration, default one minute, positive and at most one day. This is operational configuration rather than model-authored lease authority; public hosting options/registration remain part of composition work.

## Review and verification

Root review corrected no-op renewal bypassing the row CAS, unsupported retained envelopes blocking later candidates, lease address-collision classification, and omission of a pending receipt from lease-write projection sizing. Save now includes a newly tracked or retained pending receipt in the actual bound and refuses a missing record; new provider regressions verify missing/oversized pending state cannot grant a lease. Test review corrected shared-fixture eligible-work interference and moved the two-claimer barrier to the full provider row reads, so both callers observe the same original concurrency token. A preliminary compile caught a static interceptor factory being called through an instance; that test-only error was corrected. A separate source-only review found no remaining actionable lease defect. Another attempted review could not read this checkout from its environment and supplied only conceptual design feedback; it is not counted as code validation.

The real schema-gate regression admits the actual Rules module, stages a later finalized version through the shared finalization-store protocol, refreshes the gate and verifies that claim, renew and release are all refused without changing Rules rows. No raw UPDATE or shared-gate change was introduced.

An actual removal control suppressed the post-save lease-window check. All four targeted regressions failed: expired/backward-clock claims incorrectly returned Claimed, expired renewal returned Renewed, and expired release returned Released. The script restored the original source in finally; EfRuleRunStore.cs SHA-256 at that restoration boundary was `be409ea716a968ed88ed8871c4e0cfe80b25eaee07155ce274edf51c672b7911`. A subsequent reviewed change added pending-receipt projection checks without changing the expiry check; the final full focused suite was rebuilt afterward. This proves the regressions depend on the exercised transaction check. It is not the later acceptance/consumption/process fencing proof.

The final restored focused suite passed **118 tests, zero failures/skips**. The selected architecture, public API and Rules EF-closure checks passed **86 tests, zero failures/skips**; the architecture build emitted pre-existing unrelated dependency/analyzer warnings. The final rerun used the already-built architecture test assembly because the subsequent private sizing change does not alter the public API or dependency closure. Local map regeneration, generator check, solution-filter check and diff check passed. The regenerated findings show zero runtime-to-design references and no Rules-specific finding. Under specs 257/259, generated maps remain ignored local artifacts and the checks validate committed solution filters; no ignored map was staged.

## Commands and evidence

```sh
dotnet test Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj --no-restore
# Removal control temporarily suppresses the post-save LeaseWindow check, then restores source in finally.
dotnet test Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj --no-restore --filter 'FullyQualifiedName~Claim_rolls_back_all_lease_fields|FullyQualifiedName~Renew_and_release_roll_back_all_lease_fields'
# Rebuild/rerun the full focused suite after restoration.
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore --filter 'FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~PublicApiFilesGuardTests|FullyQualifiedName~Every_admitted_Rules_project_resolves_only_its_reviewed_EF_closure'
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- solution-filters-check
git diff --check
```

Raw local logs: `/tmp/3181-leases-first-tests.log` (94 tests before delegated test discovery), `/tmp/3181-leases-tests.log` (114 passing after test compilation repair), `/tmp/3181-leases-final-tests.log` (115 passing before the final expiry-boundary reclaim case), `/tmp/3181-lease-expiry-control.log`, `/tmp/3181-lease-expiry-control.json`, `/tmp/3181-leases-restored-tests.log`, `/tmp/3181-leases-architecture.log`, `/tmp/3181-leases-architecture-final.log`, and `/tmp/3181-leases-maps-{all,check,solution-filters-check}.log`. The checkpoint's executed counts and conclusions are durable; raw logs are local. No new dependency, migration, workflow, permission or tracked map change is required by this slice.
