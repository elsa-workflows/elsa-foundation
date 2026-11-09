# Rules admission and cancellation transaction checkpoint

This checkpoint follows `364c7ba6023097fdbced91040eaa143b367b8595`. It adds the first migrated SQLite transactions inside the existing #3181 leaf. It does not qualify the full durable Rules engine, four providers, packages, native parent continuation or deployment recovery.

## Implementation and review

- Scaffolded the Rules SQLite Initial migration and snapshot with the pinned EF tool. A nonpackable Rules migration host links the shared design-time factory source, uses same-domain internal access, and preserves existing migrations. Review moved it into the recognized `Extensions/Rules/src/Tooling` domain. Migration implementation types remain internal. The migration is exercised with `Database.MigrateAsync`; `EnsureCreated` is not used.
- Added an internal partial store for admission, refusal, cancellation and bounded status/terminal reads. It is not registered as the complete public store and does not contain placeholder implementations of worker operations. Full retained identity and frozen fingerprint are checked before interpreter availability. Concurrent validation failure must re-read the durable key winner. Unique-key and concurrency failures discard the failed context and retry from authoritative state.
- Admission and initial fact history share an explicit transaction. Refusal/cancellation terminal results and frozen native delivery records share the same transaction. Pre-admission cancellation has empty facts; post-admission cancellation preserves initial and committed facts while leaving pending additions unconsumed in immutable history. No cross-store transaction or native delivery acknowledgement is claimed.
- Appended the first-admission `Refused` disposition without renumbering existing values. Matching prior refusal replays its original diagnostic. A new explicit refusal requires the compatible validator's exact diagnostic. Identity-address collisions return command Conflict without identifiers and query NotFound without payload; full identity, not the digest address alone, defines a match.
- Actual UTF-8 projection and encoded metadata bounds are enforced before provider writes, independently of the runtime capacity proof. Queries validate canonical records, fact/identity hashes, derived terminal/delivery IDs and coherent lifecycle/lease metadata without loading the interpreter. Unsupported or inconsistent records remain unavailable and unchanged.

Root integration review and a separate read-only review repaired two corruption paths: running rows could carry stale delivery acknowledgement/lease state into cancellation, and internally consistent terminal/delivery records could use IDs not derived from the immutable invocation transition. Regressions seed those states directly, including canonical/hash-consistent records, then assert unavailable reads/cancellation and unchanged database state. Delivery-acknowledgement records deliberately remain unsupported until T029 supplies a canonical durable-proof reader; non-null opaque JSON is never accepted as proof. A final review corrected retry-budget accounting so a validation pass in the last retry slot still gets a fresh durable-winner lookup. Null validator responses normalize to Unavailable, avoiding an unbounded validation loop.

## Verification

The final Rules suite passed 92 tests, zero failures/skips, after the retry-limit correction. An actual removal control replaced the post-validation reread with Unavailable: all three targeted concurrent-cancellation regressions failed with expected AlreadyCancelled versus actual Unavailable. Source was restored in a finally block (restoration SHA-256 `2260360a60700e51d78b3e7bb1112450e84ccfcfe87959eb27570f24d42a33a4`), then the retry-budget correction was applied and the final 92-test suite passed. All 86 selected architecture/public-API/Rules EF closure tests passed, zero failures/skips, after correcting the migration host's solution-folder placement. The full architecture build completed first; the source-only solution-folder correction was then checked with the current built test assembly. Both Debug and Release restored closures were checked. Existing unrelated architecture-build warnings remain. Local map generation, generator check and solution-filter check passed. The relocated tooling discovered RulesSqliteDbContext, and attempting to add Initial again failed during preflight with the existing migration preserved. No tracked map output changed.

The database fixture uses a private file and fresh contexts, unique identities per test, and closes connections before teardown. A save interceptor injects failure after the database save but before commit to prove admission/history and terminal/native-delivery rollback. The competing-cancellation test gates two real provider reads before either caller proceeds, proving that competing updates from the same revision have one winner. A proposed generic DataTable buffering change failed because SQLite exposes integers through provider-specific Boolean getters; that test-harness change was removed, preserving the actual provider reader. Synthetic validator/package fixtures test store behavior only; they are not installed-package qualification.

## Remaining work

T010 snapshot-stable progress reads and the worker lease/claim/accept/consume transitions remain pending. T007 durable fixed-point/budget proof, T008 actual composition, complete T016 cancellation races against worker transitions, T029 delivery-proof handling and all provider/package/process/native/upgrade acceptance remain open. Shared map policy is specs 257/259: generated maps are ignored local artifacts published by CI, while generator `check` validates committed solution filters. No ignored maps should be force-added.

Next-slice source discovery found no shared database-clock API: ETL uses injected TimeProvider samples around SaveChanges. Raw ExecuteUpdate would bypass the shared schema write-gate interceptor. Rules must therefore provide a gate-compatible provider clock and atomic lease-expiry predicate rather than presenting process-clock samples as database-time evidence. This is an implementation decision for the existing lease slice, not a relaxation of its contract.

## Executed commands and retained evidence

```sh
ELSA_EF_CONFIGURATION=Debug bash tools/rules/generate-rules-migrations.sh Initial
dotnet test Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj --no-restore
dotnet restore Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj -p:Configuration=Release
dotnet restore Extensions/Rules/src/Tooling/Elsa.Rules.Tooling.csproj -p:Configuration=Release
dotnet restore Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj -p:Configuration=Debug -p:MSBuildProjectExtensionsPath=obj/ef-guard/Debug/
dotnet restore Extensions/Rules/src/Tooling/Elsa.Rules.Tooling.csproj -p:Configuration=Debug -p:MSBuildProjectExtensionsPath=obj/ef-guard/Debug/
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --filter 'FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~PublicApiFilesGuardTests|FullyQualifiedName~Every_admitted_Rules_project_resolves_only_its_reviewed_EF_closure'
# After the source-only solution-folder fix, rerun the same selected tests with --no-build --no-restore.
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- solution-filters-check
dotnet ef dbcontext list --project Extensions/Rules/src/Persistence/EntityFrameworkCore/Elsa.Rules.Persistence.EntityFrameworkCore.csproj --startup-project Extensions/Rules/src/Tooling/Elsa.Rules.Tooling.csproj --configuration Debug --no-build
git diff --check
```

The initial scaffold preceded the migration-host relocation; discovery and migrated-database tests verify the final location and artifacts. The duplicate-name preflight exits 2 as expected; it is a rejected operation, not a failed generation attempt to retry. Raw local logs are `/tmp/3181-rules-migration-scaffold.log`, `/tmp/3181-store-recheck-control.log`, `/tmp/3181-store-recheck-control.json`, `/tmp/3181-store-restored-tests.log` (the reverted DataTable test-harness failure), `/tmp/3181-store-complete-tests.log`, `/tmp/3181-store-architecture.log` (the corrected solution-folder failure), `/tmp/3181-store-complete-architecture.log`, `/tmp/3181-store-maps-all.log`, `/tmp/3181-store-maps-check.log`, `/tmp/3181-store-maps-solution-filters-check.log`, `/tmp/3181-store-tooling-context.log`, and `/tmp/3181-store-tooling-duplicate.log`. Executed commands and results above are the durable evidence; raw files are local.
