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

### US1/MVP second QA follow-up focused gate (2026-07-11)

Legacy extractor bindings that reference no executable node now fail preflight instead of producing a registered outcome with a synthetic activity type. Activity and recurring providers with null, empty, or whitespace ids are rejected safely when they claim a node or throw while describing it; failures use the `ProviderIdentity` facet, retain artifact/node context, and never pass the invalid id into the typed failure's provider-id collection.

| Focus | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Runtime extractor/indexer | 32 | 0 | 0 |
| Event provider | 8 | 0 | 0 |
| Timer/Cron providers | 11 | 0 | 0 |
| HttpEndpoint provider | 34 | 0 | 0 |
| Recurring schedule indexer | 15 | 0 | 0 |
| Publishing.Api trigger indexing | 11 | 0 | 0 |
| **Total** | **111** | **0** | **0** |

### Map freshness note

The narrow post-implementation refresh `bash tools/maps/generate-extension-point-map.sh` completed.
The generated map reports 52 source catalogs discovered, 51 root-indexed, one pre-existing unindexed
Runtime Distributed Groundwork provider catalog, and zero root-indexed catalogs missing on disk. No unsafe
drift was found. This narrow generator does not by itself establish whole-manifest freshness.

### US2 intentional non-start evidence (2026-07-11)

The T026 focused gate completed with zero failures and zero skipped tests:

| Focus | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Runtime extractor (`WorkflowTriggerBindingExtractorTests`) | 24 | 0 | 0 |
| HTTP provider + direct/mid-flow execution (`HttpEndpointTriggerStimulusProviderTests\|HttpEndpointExecutionTests`) | 53 | 0 | 0 |
| **Total** | **77** | **0** | **0** |

The extractor outcome retained the HTTP provider id for `Recognized([])`, and the activity execution
coverage preserved direct-run and mid-flow suspension behavior without creating start bindings.

### US3 compatibility evidence (2026-07-11)

- RED/sensitivity before compatibility assertions were finalized: all four legacy CLR catalog rows produced
  executable `Trigger` metadata against a temporary `Action` sentinel, and both catalog-hash pins rejected
  placeholder values. No production or planning files had been edited.
- Legacy `Action` catalog compile/republish matrix: 8 passed, 0 failed, 0 skipped across Event, Timer, Cron,
  and HttpEndpoint; invalid republish preserved the seeded bindings and recurring schedule where applicable.
- Same-version CLR catalog compatibility: 2 passed, 0 failed, 0 skipped. Trigger fixture and HttpEndpoint remain
  `Action` rows with pinned hashes `59F976C4B1CFBE75E153788F17FE0F8CAAB31E39DC4B91C0D28D603A2ECBFC03` and
  `504691EF9BED6726DFEADB1ADAD22E8F30A987A9DFCC3F47E86024ADBE460986` respectively.
- During pre-PR integration with `main`, the independently delivered activity-input editor work added cataloged
  checklist UI metadata to `HttpEndpoint.SupportedMethods`. That intentional authored-contract change moved the
  current HttpEndpoint hash to `89251E344255527968493DC31C6F5CF7207A2836B53165DE73915260E469C12A`; the
  trigger fixture hash stayed unchanged, and the legacy Action-to-Trigger republish matrix continues to cover the
  pre-change HttpEndpoint artifact shape.
- Executable compiler goldens: 10 passed, 0 failed, 0 skipped.
- Groundwork runtime document fixtures: 34 passed, 0 failed, 0 skipped with `GROUNDWORK_FIXTURE_REGEN` unset.
- No executable, trigger-binding, recurring-schedule, golden-fixture, or Groundwork schema-version drift was
  detected. T032 therefore requires no migration; `ElsaRuntimeDocumentVersions` was not changed.
- A provider and extractor compiled against pre-change commit `317caf8c` loaded against the current Runtime Core
  assembly and dispatched both additive default interface members. The compiler and publish-handler entry-point
  files are unchanged from that baseline. Runtime Core is MINOR-compatible; CI injects package versions through
  `.github/workflows/packages.yml`, so release owners must advance `env.base_version` from `4.0.0` to `4.1.0`
  and use a `v4.1.0`/`4.1.0` release tag rather than adding a project-local version.
- Section 4 final filters: `WorkflowExecutableCompilerTests` 21 passed and `ClrAssemblyScannerTests` 21 passed;
  both had 0 failed and 0 skipped.
- Section 5 architecture gate: 50 passed, 2 failed, 0 skipped. The failures match the pre-US3 baseline exactly:
  duplicate project references in `Elsa.Server` / `Elsa.Diagnostics.StructuredLogs.Persistence.Tests`, and the
  missing `ActivitiesHttpFeature` server-catalog assertion. No Runtime-to-Design boundary failure was reported.
- `dotnet build Elsa.Server.slnx`: succeeded with 0 warnings and 0 errors.
- Final post-fix `dotnet test Elsa.Server.slnx`: 3,626 passed, 2 failed, 0 skipped across 52 test projects. The
  only failures are the same two architecture baseline items above. The first full-suite pass also exposed a
  stale Runtime.Http observer fixture whose legacy binding referenced a node absent from its fake executable;
  the fixture now uses the executable's real root-node id, and its focused observer suite passes 8/8 without
  weakening the US1 executable-identity invariant.

### Finalization gate (2026-07-11)

- `dotnet build Elsa.Server.slnx`: succeeded with 0 warnings and 0 errors.
- `dotnet test Elsa.Server.slnx`: 3,626 passed, 2 failed, 0 skipped across 52 test projects.
- The two failures match the recorded pre-change architecture baseline exactly: duplicate project references
  in `Elsa.Server` / `Elsa.Diagnostics.StructuredLogs.Persistence.Tests`, and the missing
  `ActivitiesHttpFeature` server-catalog assertion. Neither failure is in the spec 090 change surface.

### Final scope audit

The complete diff from the approved planning baseline was inspected. It adds only trigger preflight/runtime
contracts and models, first-party provider identities, recurring pre-materialization, tests, compatibility
evidence, and documentation/maps. It adds no diagnostics API or persisted publication status, CShells or
startup-health behavior, Studio work, route-table invalidation behavior, stimulus-router/actor redesign,
durable schema change, or publication-wide transactionality. The sole route-table test-file change corrects
a stale fixture to reference its executable's real root-node id; it does not alter invalidation behavior.

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

Expected: no Runtime → Design dependency, no build warnings or errors, and no regression from the recorded
test baseline. The known architecture baseline failures remain documented above. If implementation changes a
Groundwork-persisted record despite the plan, stop and amend the spec/plan/tasks for explicit migration
approval before changing schema versions, upcasters, or fixtures.

Also verify the Runtime Core public API change is classified as MINOR-compatible, the canonical extension-point catalog resides at `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`, and the repository root `EXTENSION_POINTS.md` links to it.
