# Quickstart: Validate Durable-Value Page Reuse

This is a validation guide for the implementation phase. It does not claim that the feature is implemented or that any command has run. Do not start a host or database as part of this planning-only task.

## Prerequisites

- Work from the feature implementation branch after the reviewed plan and tasks are accepted.
- Use the repository's `dotnet` build-slot wrapper; a message that a build is waiting means another build owns the slot. Do not bypass it.
- For the live regressions, use an owned rebuilt Elsa Workbench/runtime host and a fresh disposable PostgreSQL database as described by [specification 193's quickstart](../193-bounded-coalesced-pagination/quickstart.md). Do not reuse a database from an earlier host build.
- Keep connection strings, signing keys, and credentials in the approved local environment. Do not put them in command arguments, source, logs, or exported probe artifacts.

## 1. Run the focused runtime and persistence suites

After implementation adds the deterministic page-reuse tests, run the Runtime suite:

```bash
dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore --verbosity minimal
```

Run the EF integration suite, including the first-party eligibility and non-empty enabled/disabled page-count scenario:

```bash
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --no-restore --verbosity minimal
```

Run the supported EF provider suite for the changed page behavior:

```bash
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj --no-restore --verbosity minimal
```

The positive reduction oracle must be a normal, non-empty typed-start → deferred `ActivityStarted` → invoke execution, run once with `CoalesceDurableValueReads=true` and once false under otherwise identical Coalesced configuration. It must count actual backing durable-value page requests through a narrow test-only counter or EF command interceptor, and compare serialized inputs, identity, variable visibility, and final outcome. The empty-state T07 probe does not meet this oracle.

## 2. Run the existing PostgreSQL HTTP reference

After building and starting the owned test host with the required Coalesced provider configuration, pass its candidate commit and local endpoint to the existing script:

```powershell
pwsh -NoProfile -File ./e2e-tests/http/Capture-RuntimeDbPaging2392Reference.ps1 `
  -HostCandidateSha <40-hex-commit> `
  -BaseUrl http://127.0.0.1:<owned-port>
```

The existing assertion is a successful HTTP `200` response containing `Alice Smith` and a completed workflow. Preserve this response/terminal-state check; do not infer a page-call reduction from the HTTP assertion alone.

## 3. Run the valid REST regression

Use the same owned host and provide the existing artifact/source-reference identifiers:

```powershell
pwsh -NoProfile -File ./e2e-tests/http/Capture-RuntimeDbRest2386Control.ps1 `
  -HostCandidateSha <40-hex-commit> `
  -BaseUrl http://127.0.0.1:<owned-port> `
  -HttpArtifactId <artifact-id> `
  -HttpSourceReferenceId <source-reference-id>
```

The valid REST control checks its expected response and terminal workflow behavior. It is a correctness regression, not a performance oracle. Do not use `-CompanionOnly` for the required valid REST path.

## 4. Review lifecycle and safety evidence

Confirm the test reports preserve current staged upserts/deletes, per-call scope/cancellation and cursor rejection, codec-instance isolation, direct and checkpoint write fencing, failure/cancellation, overlapping reads and writes, late completion, nested ownership, disposal/recovery, complete overflow fallback, JSON/metadata detachment, and authored cap-option copying. Verify Immediate and custom/in-memory composition still use the uncached provider path.

Capture the before/after page-call count and semantic comparison as deterministic test evidence. Do not claim a latency result, primary-workload query reduction, or changed HTTP/REST behavior until that exact evidence is available. T17/T18 owns any bounded latency comparison; no performance CI threshold is introduced here.
