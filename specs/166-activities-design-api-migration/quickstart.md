# Quickstart: Validate the Activities Design API Migration

## Prerequisites

- .NET 10 SDK and restored repository-local tools
- PowerShell for backend E2E (`powershell` on Windows; `pwsh` elsewhere)
- Fresh Workbench SQLite database for live tests; follow `e2e-tests/README.md`

## Contract and owner gates

```bash
dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter 'FullyQualifiedName~FastEndpointsTransitionTests|FullyQualifiedName~ActivitiesDesign|FullyQualifiedName~DomainManagementApiCompositionTests'
```

Expected: the frozen 38-route HTTP/OpenAPI replay, authorization matrix, owner semantics, stable metadata, exact
38-removal ratchet, host composition, and three-cycle collectibility gates pass.

## Full repository gates

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build Elsa.Server.slnx --no-restore --nologo
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet format Elsa.Server.slnx --no-restore --verify-no-changes --include <changed-csharp-files>
git diff --check origin/main...HEAD
```

Expected: zero test/build/map/format/diff failures. Existing documented warnings may remain but no branch-introduced
warning is accepted without disposition.

## Live Workbench E2E

Stop Workbench, remove its SQLite database and schema-lock files, rebuild, apply the complete reference schema,
then start the `http` profile exactly as documented in `e2e-tests/README.md`. Run:

```bash
pwsh ./e2e-tests/get-endpoints/Test-DesignActivityGets.ps1
pwsh ./e2e-tests/write-endpoints/Test-DesignActivityWrites.ps1
pwsh ./e2e-tests/reusable-activities/Test-ReusableActivity.ps1
pwsh ./e2e-tests/reusable-activities/Test-ReusableActivityPinning.ps1
pwsh ./e2e-tests/reusable-activities/Test-ActivityUpgradePlan.ps1
```

Expected: catalog/definition/draft/version/dependency GETs, reusable authoring/publishing/execution, exact version
pinning, and staged upgrade-plan creation/apply/refresh all pass against the rebuilt host and fresh persistence.

## Evidence review

Review `docs/reports/activities-design-api-migration-2026-08.md` for baseline receipt/hashes, exact route and
registration counts, HTTP/OpenAPI approvals, permission matrix, unload weak-reference result, test commands,
E2E environment/result, remaining risks, and the keep/migrate/coexist recommendation state.
