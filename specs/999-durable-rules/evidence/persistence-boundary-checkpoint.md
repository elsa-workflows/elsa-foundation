# Rules persistence-boundary checkpoint

This checkpoint follows `a2490ca492682ec4ccf0f1dbc8e86482e6ba35a4`. It prepares T009–T012; no durable store, migrations, installed-package qualification, worker registration or native integration is claimed.

## Implementation and review

- Moved common bounded record framing from Runtime into `Rules.Persistence.Core.RuleRecordCodec`. Providers can recover opaque invocation artifacts, semantic pins, facts, receipts, terminal results and delivery records without referencing the interpreter. Finite artifact interpretation and complete-round validation stay in Runtime.
- Added canonical recovery readers with actual representation bounds, full fact/receipt/delivery integrity checks and canonical re-encoding comparison. Shared immutable test fixtures avoid duplicating invocation and native-route setup.
- Added `IRuleAdmissionValidator` and `IRuleImplementationIdentitySource` replacement contracts plus the internal Runtime validator. Synthetic unit-test identities exercise decisions only; the real trusted deployment source and composition are pending. Providers must first look up existing identity, then validate an absent invocation, then recheck a concurrent winner even after validation failure. No existing-identity replay or cancellation ordering has been proved yet.
- Added Rules-owned EF entities, provider-neutral context, SQLite mapping, independent concurrency token and model revision, separate work/delivery fences, retained full identity beside the digest key, and append-only receipt/fact history. The project builds without Runtime or Design references. SQLite migration/snapshot, real provider transitions and database verification remain pending; T009 is not complete.
- ADR 0088 and a narrow restored-dependency guard admit only the new EF project's four EF Core/Relational packages. Public API files track the added contracts and common codec. No actor framework, universal model registry or task-runner layer was added.

Separate read-only review and root review found two classification bugs and fixed them: unsupported common invocation/destination framing must return Unavailable rather than Invalid; unsupported terminal schemas must raise NotSupportedException rather than a corruption diagnosis. The recheck was clean within this scope. Tests include both unsupported common framing cases, corrupted artifact/receipt/fact/delivery data, noncanonical records, opaque old semantics and cancellation before identity lookup.

## Executed evidence

The restored focused suite passed 65 tests, zero failed/skipped. All 86 selected architecture/public-API/Rules dependency tests passed, zero failed/skipped. Existing unrelated architecture-build warnings remain. Local map generation and both generator/filter checks completed successfully. Under the current specs 257/259 map policy, maps and their manifest are ignored local outputs published by CI, not committed source; `check` validates the committed solution filters. No tracked map files changed. Reviewed findings show no new Rules runtime-to-design dependency. The schema-only project build passed with zero warnings/errors. Debug and Release restores were performed for the narrow EF dependency gate. The record tests do not instantiate a database or replace the planned durable recovery suite.

An actual removal control deleted the receipt reader's semantic digest verification. `Complete_receipt_roundtrip_preserves_all_firings_and_checks_digest` failed because the corrupt receipt no longer threw. The source was restored in a finally block; SHA-256 `21616fa02dfc41e51d899d54684bc7496911fb6ef1352a41db48e057ece5bbf7`. The restored full focused suite passed as reported above.

Executed scoped commands:

```sh
dotnet test Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj --no-restore
dotnet build Extensions/Rules/src/Persistence/EntityFrameworkCore/Elsa.Rules.Persistence.EntityFrameworkCore.csproj
dotnet restore Extensions/Rules/src/Persistence/EntityFrameworkCore/Elsa.Rules.Persistence.EntityFrameworkCore.csproj -p:Configuration=Release
dotnet restore Extensions/Rules/src/Persistence/EntityFrameworkCore/Elsa.Rules.Persistence.EntityFrameworkCore.csproj -p:Configuration=Debug -p:MSBuildProjectExtensionsPath=obj/ef-guard/Debug/
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --filter 'FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~PublicApiFilesGuardTests|FullyQualifiedName~Every_admitted_Rules_project_resolves_only_its_reviewed_EF_closure'
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- solution-filters-check
git diff --check
```

Raw local logs: `/tmp/3181-record-read-control.log`, `/tmp/3181-record-read-control-result.json`, `/tmp/3181-record-final-tests.log`, `/tmp/3181-ef-schema-build.log`, `/tmp/3181-ef-release-restore.log`, `/tmp/3181-ef-debug-restore.log`, `/tmp/3181-record-architecture.log`, `/tmp/3181-record-maps-refresh.log`, `/tmp/3181-record-maps-check.log`, `/tmp/3181-record-filters-check.log`. The retained commands/outcomes are the public evidence; these raw files are local.

## Next implementation boundary

Implement and apply a real migration before database acceptance; do not use EnsureCreated. Add transactional admission/refusal/cancellation and identity-first replay, then claim/accept/consume with trusted provider time and atomic revision/fence recheck. Enforce the 64 KiB metadata and 8 MiB current-projection bounds in actual provider writes. Keep full records and history readable without the original interpreter. Preserve T007 settlement controls, T008 actual feature composition, four-provider/package/process/native/upgrade gates and the existing whole-leaf claim.

A read-only tooling investigation found that the shared `tools/ef/Elsa.EntityFrameworkCore.Tooling` factory list does not contain Rules or ETL internal contexts. `generate-module-migrations.sh` removes matching snapshot directories before consulting that list, so it must not be invoked blindly for this context. The Notes sample demonstrates a dedicated non-packable startup using the shared generic factory, but it does not prove access to an internal Rules context. Resolve the same-domain tooling access and explicit dependency closure during T009, then scaffold and exercise the migration. Do not expose context types solely for tooling, add cross-domain friend access, copy unverified generated files, or mistake ETL's retained artifacts for an executable generation recipe.
