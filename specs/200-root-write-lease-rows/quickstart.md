# Quickstart: Verifying Spec 200

## Fast local loop (SQLite)

```bash
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  --filter "FullyQualifiedName~EfRuntimeArtifactScopeTests"
dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj \
  --filter "FullyQualifiedName~RootWriteLease|FullyQualifiedName~GarbageCollector|FullyQualifiedName~CheckpointCommit"
```

Expected results:
- The three `Root_write_lease_*_is_not_failed_by_other_holders_of_the_same_artifact` tests pass.
- Every existing lease, guard and deletion test passes.
- The new race, legacy-lease, context-isolation, release-isolation and #2286 tests pass.

## PostgreSQL provider loop

Point the provider fixture at a disposable database, or let Testcontainers start one:

```bash
export ELSA_RUNTIME_BOOKMARKS_EF_POSTGRESQL_TEST_CONNECTION_STRING="Host=127.0.0.1;Port=55432;Username=postgres;Database=elsa_provider_tests;Maximum Pool Size=100"
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj \
  --filter "FullyQualifiedName~RuntimeArtifacts"
```

Expected result: `PostgreSql_concurrent_root_write_lease_holders_of_one_artifact_all_succeed` completes 160 of 160 cycles. The SQL Server and MySQL variants run in the CI container suites.

## Migrations

```bash
dotnet test tests/essentials/Persistence/EntityFrameworkCore/Migrations   # model/migration match + fresh install, 4 providers
bash tools/ef/module-migrate.sh script-check db/migrations              # reviewable SQL unchanged except the new table
```

## Bite-proof

1. Temporarily revert `EfWorkflowExecutableStore` to the single-row lease path. The four regression tests must fail.
2. Temporarily remove the post-commit guard check from acquire. The race test "guard committed between lease commit and check" must fail.
3. Restore the code. Nothing from these steps is committed.

## End-to-end acceptance (SC-001)

On the Azure runner (`rg-elsa-p2382-98e7664aa3` / `elsa-p2382-98e7664aa3`), run `e2e-tests/http/Test-RuntimeConcurrencyCorrectness.ps1` for SQLite and PostgreSQL, Immediate and Coalesced cadence, and 16 and 32 clients. All 8 cases must pass. Post the results on #2532.
