# Quickstart: Verify Durable Runtime Alterations

## Prerequisites

- .NET 10 SDK from `global.json`.
- PowerShell for backend e2e tests (`powershell` on Windows, `pwsh` elsewhere).
- For durable e2e, the reference server's default SQLite Groundwork composition and a restart-stable
  alteration payload protection key.
- A fresh server database after rebuilding, per `e2e-tests/README.md`.

## 1. Build the affected solution

```bash
dotnet build Elsa.Server.slnx
```

Expected: no new Runtime-to-Design project reference and no nullable/analyzer errors in affected
projects.

## 2. Run focused contract and runtime tests

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj
```

Required evidence:

- descriptor/registration/composition validation;
- canonical idempotency and protected/redacted payloads;
- capture paging, restart, seal, cancellation, claims, and count invariants;
- complete preflight and atomic fake-handler rollback;
- checkpoint/job acknowledgement reconciliation;
- every built-in success and rejection branch;
- capability and permission behavior.

## 3. Run persistence and architecture gates

Use the actual project paths recorded by the implementation tasks:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```

Required evidence:

- InMemory and Groundwork alteration-store conformance;
- target/job keyset paging and claim expiry;
- atomic workflow mutation plus terminal job result;
- commit replay/fingerprint equality;
- document-kind/version golden fixtures;
- SQLite/PostgreSQL/SQL Server/MongoDB Groundwork composition remains admissible;
- Runtime remains Design-free and extension points remain registered.

## 4. Run relevant existing regression suites

At minimum run the cancellation, scheduler, recovery/incident, variable, persistence-query, and API
test filters/projects identified by `tasks.md`. Then run the full solution test set before delivery:

```bash
dotnet test Elsa.Server.slnx
```

Do not remove or weaken an existing test to make the feature pass.

## 5. Run the backend REST e2e suite

Stop an old server and remove only the documented reference-server SQLite database files. Rebuild and
start:

```bash
dotnet run --project src/Apps/Elsa.Server/Elsa.Server.csproj --launch-profile http
```

In another shell:

```bash
pwsh ./e2e-tests/runtime-alterations/Test-AlterationPlans.ps1
pwsh ./e2e-tests/runtime-alterations/Test-AlterationReplayAndRestart.ps1
```

The scripts must authenticate, author/publish/execute fixtures, submit plans through real HTTP, and
assert:

- single and multi-page bulk target capture;
- no job begins before seal;
- cancel, variable, schedule, reschedule, and migration outcomes;
- one failing target does not stop others;
- failed multi-alteration jobs commit no earlier mutation;
- idempotency replay/conflict;
- cooperative plan cancellation;
- restart/replay without duplicate effects;
- no sensitive payload appears in plan/job JSON.

Run existing relevant black-box suites against the rebuilt server:

```bash
pwsh ./e2e-tests/write-endpoints/Test-RuntimeWrites.ps1
pwsh ./e2e-tests/persistence-querying/Test-InstancePaging.ps1
pwsh ./e2e-tests/orchestration-controls/Test-SuspendResume.ps1
pwsh ./e2e-tests/durability/Test-RestartRecovery.ps1
pwsh ./e2e-tests/variables/Test-TypedVariables.ps1
pwsh ./e2e-tests/fault-handling/Test-FaultActivity.ps1
```

## 6. Refresh generated maps after implementation

Check `docs/maps/manifest.json`, then run the narrowest relevant generators authorized by the selected
map-shell preference. Runtime contracts, projects, dependencies, and extension points are expected to
change, so review generated findings before committing snapshots.

## 7. Final delivery gate

- `git diff --check`
- no `[NEEDS CLARIFICATION]` or unchecked mandatory checklist item;
- spec/plan/tasks/contracts match implemented route and wire values;
- ADR 0049 and Runtime Alterations program goal link the PR and final evidence;
- all changed files committed locally;
- organization branch pushed;
- draft PR includes `Fixes #1016`, behavior summary, migration/security impact, and exact checks run.
