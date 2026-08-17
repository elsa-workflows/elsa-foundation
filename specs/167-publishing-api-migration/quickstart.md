# Quickstart: Validate the Publishing API Migration

Run from the repository root with the .NET 10 SDK and PowerShell available.

## 1. Verify immutable before evidence

Run the Publishing baseline tests and detached capture reproduction. Expect exactly 23 registrations/operations, byte-identical fixture hashes across two captures, a clean-content-guarded runner, and no production migration in the baseline commit.

## 2. Run owner compatibility and behavior

```bash
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj --no-restore
```

Expect the immutable HTTP/OpenAPI comparer, route/security/metadata tests, serialization and public-type compatibility, authorization matrix, and all retained publication/compiler/activation/slot/policy/preflight/activity/test-run semantic suites to pass.

## 3. Run architecture and unloadability

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
```

Expect exactly 23 registrations removed, zero first-party FastEndpoints registrations, one Publishing mapping in combined hosts, stable OpenAPI contract types, and three consecutive real route/auth/serialization/OpenAPI/test-run unload cycles with all tracked weak references collected.

## 4. Build and generated gates

```bash
dotnet build Elsa.Server.slnx --no-restore --nologo
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet format Elsa.Server.slnx --no-restore --verify-no-changes --include <changed C# files>
git diff --check origin/main...HEAD
```

Expect zero build errors, fresh maps, no changed-file formatting drift, and a clean diff check. Record existing unrelated warnings separately from branch-introduced warnings.

## 5. Run live Workbench E2E

Rebuild Workbench, deploy a fresh SQLite Groundwork schema, launch the server, then run:

- `e2e-tests/get-endpoints/Test-PublishingGets.ps1`
- `e2e-tests/write-endpoints/Test-PublishingWrites.ps1`
- affected reusable-activity publication, upgrade, workflow/activity test-run, pinning, outcome, and nesting scripts;
- the new Publishing lifecycle script covering runtime preflight, snapshot review/publish, policy CAS, slot unpublish/restore, publication receipt replay, activity test-run lookup/cancel, and route/body precedence.

Record the exact reachable source/build identity, schema deployment, server command, script list, pass counts, and authentication/persistence path in the final report.

## 6. Review and publication

Recheck issue comments and open PRs, run an independent five-axis review, resolve every Critical/Required finding, update Spec Kit checkboxes and report claims, create the draft wave PR, publish verification evidence on the issue/PR, merge only when all required checks are green, and verify exact-main CI, HTTP performance, maps, packages, code quality, and Docker gates.
