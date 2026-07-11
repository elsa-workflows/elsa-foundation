# Quickstart: Validate Trigger Publication Hardening

## Prerequisites

- .NET 10 SDK available.
- Repository dependencies restored.
- Feature implemented according to [plan.md](plan.md).

## Implementation evidence

Record baseline and final focused/full-suite counts here during implementation. Record any pre-existing failure before source edits so the unit does not silently absorb it.

### US1/MVP baseline (2026-07-11, before source edits)

All focused commands from sections 1–3 passed with zero failures and zero skipped tests:

| Focus | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Runtime extractor/indexer | 13 | 0 | 0 |
| Event provider | 5 | 0 | 0 |
| Timer/Cron providers | 6 | 0 | 0 |
| HttpEndpoint provider | 32 | 0 | 0 |
| Recurring schedule indexer | 5 | 0 | 0 |
| Publishing.Api trigger indexing | 3 | 0 | 0 |
| **Total** | **64** | **0** | **0** |

No pre-existing focused failure was observed.

### US1/MVP final focused gate (2026-07-11)

All section 1–3 commands passed after implementation:

| Focus | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Runtime extractor/indexer | 27 | 0 | 0 |
| Event provider | 8 | 0 | 0 |
| Timer/Cron providers | 11 | 0 | 0 |
| HttpEndpoint provider | 34 | 0 | 0 |
| Recurring schedule indexer | 7 | 0 | 0 |
| Publishing.Api trigger indexing | 11 | 0 | 0 |
| **Total** | **98** | **0** | **0** |

RED evidence was confirmed before production edits: Runtime exact-one/preflight cases failed 9 of 27; recurring ordering/exhaustion cases failed 3 of 7; first-party provider-id assertions failed against the CLR fallback; and all four invalid Publishing.Api family rows failed because raw provider exceptions escaped. The approved exhausted-Cron objective correction is now covered by a real seeded-store test: no-future-occurrence fails with `WorkflowTriggerPreflightException` before either bindings or schedules mutate.

### US1/MVP QA follow-up focused gate (2026-07-11)

The T019 publication matrix now invokes the real Event, Timer, Cron, and HttpEndpoint providers with authored inputs. Timer and Cron also run through the real recurring providers, calculator, decorator, and in-memory schedule store. Recurring failures carry the selected provider id, expression context, and preserved parser/calculator inner exception; the default legacy extractor projection resolves activity type from the executable node.

| Focus | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Runtime extractor/indexer | 28 | 0 | 0 |
| Event provider | 8 | 0 | 0 |
| Timer/Cron providers | 11 | 0 | 0 |
| HttpEndpoint provider | 34 | 0 | 0 |
| Recurring schedule indexer | 9 | 0 | 0 |
| Publishing.Api trigger indexing | 11 | 0 | 0 |
| **Total** | **101** | **0** | **0** |

### Map freshness note

`docs/maps/manifest.json` reports no relevant inputs were dirty when its snapshot was generated, but its authoritative input fingerprint predates the current spec 090 inputs. The relevant map is therefore treated as stale for this work. Because the implementation changes Runtime extension-point contracts, the narrow post-implementation refresh is `bash tools/maps/generate-extension-point-map.sh`; execution remains assigned to T036 outside the US1/MVP checkpoint.

## 1. Shared provider and index contract

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --filter "FullyQualifiedName~WorkflowTriggerBindingExtractorTests|FullyQualifiedName~WorkflowTriggerIndexerTests"
```

Expected:

- zero-provider and multiple-provider claims fail with contextual preflight errors;
- each strategy is evaluated at most once per executable trigger node;
- one provider id is recorded for registered and intentionally non-starting outcomes;
- one invalid node preserves all prior bindings;
- `Recognized([])` succeeds with no binding.

## 2. Event, Timer, Cron, and HttpEndpoint provider matrix

```bash
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --filter "FullyQualifiedName~EventTriggerStimulusProviderTests"
dotnet test tests/Elsa/Activities/Scheduling/Tests/Elsa.Activities.Scheduling.Tests.csproj --filter "FullyQualifiedName~TimerCronProviderTests"
dotnet test tests/Elsa/Activities/Http/Tests/Elsa.Activities.Http.Tests.csproj --filter "FullyQualifiedName~HttpEndpointTriggerStimulusProviderTests"
```

Expected: each provider satisfies its row in [trigger-contract-matrix.md](contracts/trigger-contract-matrix.md), including HTTP's explicit non-start case.

## 3. Recurring preflight ordering

```bash
dotnet test tests/Elsa/Workflows/Runtime/Scheduling/Tests/Elsa.Workflows.Runtime.Scheduling.Tests.csproj --filter "FullyQualifiedName~RecurringTriggerScheduleIndexerTests"
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj --filter "FullyQualifiedName~PublishWorkflowTriggerIndexingTests"
```

Expected:

- complete schedules are materialized before the inner indexer runs;
- exhausted Cron and invalid Timer/Cron inputs fail before prior bindings or schedules change;
- successful republish still replaces old schedules;
- an inner indexing failure leaves schedules unchanged.
- the publish-level Event/Timer/Cron/HttpEndpoint matrix produces complete expected bindings for valid cases and preserves prior registrations for invalid cases.

## 4. Publication and compatibility

```bash
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj --filter "FullyQualifiedName~WorkflowExecutableCompilerTests"
dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj --filter "FullyQualifiedName~ClrAssemblyScannerTests"
```

Expected:

- invalid publications preserve seeded trigger/schedule registrations;
- legacy catalog rows compile with correct trigger projection;
- same-version reconciliation hashes remain stable;
- existing executable shapes remain readable.

## 5. Boundary and full-suite gate

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build Elsa.Server.slnx
dotnet test Elsa.Server.slnx
```

Expected: no Runtime → Design dependency, no warnings or errors, and all existing tests remain green. If implementation changes a Groundwork-persisted record despite the plan, stop and amend the spec/plan/tasks for explicit migration approval before changing schema versions, upcasters, or fixtures.

Also verify the Runtime Core public API change is classified as MINOR-compatible, the canonical extension-point catalog resides at `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`, and the repository root `EXTENSION_POINTS.md` links to it.
