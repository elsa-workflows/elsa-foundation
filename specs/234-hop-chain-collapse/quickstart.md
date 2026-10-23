# Quickstart: validating the hop-chain collapse

**Spec**: [spec.md](spec.md) · **Tasks**: [tasks.md](tasks.md)

Build and test only the projects this feature touches (AGENTS.md, "Builds on a shared machine"). Check `uptime` before
trusting any failure that looks timing-shaped. All commands run from the repository root.

## 1. Fast checks (unit and composition)

```bash
dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj \
  --filter "FullyQualifiedName~RuntimeRoutingPass|FullyQualifiedName~RuntimeHopCollapse|FullyQualifiedName~RuntimeDrainStepSingleTransaction|FullyQualifiedName~RuntimeSchedulerWorkHandlerBase|FullyQualifiedName~WorkflowsRuntimeCheckpointPersistenceFeature|FullyQualifiedName~RuntimeCheckpointCommitValidator"
dotnet test tests/essentials/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj \
  --filter "FullyQualifiedName~ReplaySafeFusion|FullyQualifiedName~WorkflowParentActivityCompletion|FullyQualifiedName~WorkflowInvokeActivity"
```

Expected: green. The fusion guardrails prove the shared `HopRoutingClassifier` did not change spec 228's decisions.

## 2. EF provider behaviour (SQLite)

```bash
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  --filter "FullyQualifiedName~RuntimeCheckpointCommit|FullyQualifiedName~SchedulerWorkParticipant|FullyQualifiedName~EfHopCollapse"
```

Expected: the contract cases of [contracts/checkpoint-commit-continuations.md](contracts/checkpoint-commit-continuations.md),
the ON/OFF guardrail and the nine crash kill points (K1–K9, [research R12](research.md#r12--the-crash-suite-named-in-sc-003))
pass. Each crash case also runs with the switch off as its control.

Native providers run in CI ("EF container suites (runtime)"); locally, with Docker available:

```bash
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj \
  --filter "FullyQualifiedName~SchedulerWorkParticipant"
```

## 3. Commit and command budgets (SC-001, SC-002, SC-004, SC-005)

```bash
dotnet test tests/essentials/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj \
  --filter "FullyQualifiedName~EfCommandBudgetTests" --logger "console;verbosity=detailed"
```

The test output prints one line per workflow and mode, for example:

```text
EF commands: workflow 'http-reference', mode 'Immediate': actual N, budget N.
Durable commits: workflow 'http-reference', mode 'Immediate': actual C, budget C.
```

- SC-001: the `http-reference` / `Immediate` durable-commit count is at most 52 with the root-write lease (#2586)
  unchanged, and at most 45 once #2586 lands (owner decision 2026-10-11; projection in research R11).
- SC-002: every `Immediate` command count is below its pre-feature budget (1482 / 569 / 507), and the budget JSON is
  lowered to the new counts in the same change.
- SC-004: the `Immediate.CollapseOff` cases equal the pre-feature counts exactly (the test asserts equality, not `≤`).
- SC-005: the `Coalesced` counts are unchanged (136 / 103 / 180 commands at the time of planning).

Optional cross-check of SC-001 against the #2553 method (manual, Linux only): run a Release Workbench on SQLite with
persisted diagnostics off, send one request to the reference workflow, and count fsyncs on `elsa.db-wal` with an
`LD_PRELOAD` fsync counter. The figure should match the durable-commit count within the liveness/placement writes.

## 4. Diagnostics (FR-010)

In any of the tests above, `RuntimeSchedulerDispatchDiagnostics.InProcessRoutingSteps` is non-zero for an Immediate EF
run with the switch on and zero with it off or under Coalesced; `InProcessRoutingFallbacks` counts child-fault
evaluations and non-ReplaySafe parents; the workflow tail is routed, not a fallback.

## 5. Rollback lever

Set the feature setting in a host's shell configuration (CShells feature settings) and restart:

```json
{ "WorkflowsRuntimeCheckpointPersistence": { "Mode": "Immediate", "CollapseHopChain": false } }
```

Expected: behaviour, commits and durable state equal the pre-feature build (SC-004).

## 6. End-to-end (owner-run)

1. Backend e2e regression on a rebuilt Workbench with a fresh database (`e2e-tests/README.md`): the `http/` and runtime
   suites.
2. A1 matrix (#2532): `e2e-tests/http/Test-RuntimeConcurrencyCorrectness.ps1` against an owner-started Workbench for
   SQLite and PostgreSQL, `-Cadence Immediate` and `-Cadence Coalesced`, `-Clients 16` and `-Clients 32`. All 8 cases must
   pass (SC-003). Post the results on #2532 and #2584.

## 7. Repository gates

```bash
dotnet test tests/essentials/Architecture/Tests/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -p:RestoreLockedMode=true -- check
```
