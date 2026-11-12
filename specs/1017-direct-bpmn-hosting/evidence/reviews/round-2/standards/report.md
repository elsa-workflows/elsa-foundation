# PR3410 complete-PR author review — ROUND2/6 — STANDARDS

Reviewed head: `bbb21b25b1a2fa40c900d4374c2a979ff21377f4`.
Merge base: `931123c7ed45aa896539d857c6227a267d08b28f`.
Result: **three actionable standards findings** (two P2 exception boundaries; one P3 mechanical naming gate). This is an independent standards lens, not merge clearance.

## Findings

### S1 — P2 — Normalize provider failures at the new model-reader boundary

Location: `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeModelExecutionReader.cs:25` (same issue at lines 35 and 61).

Rule: ratified framework constitution §2.23.5, `.specify/memory/constitution-framework.md:210`: infrastructure/third-party exceptions must not escape the feature boundary unwrapped; the domain exception must retain its original cause and localizing identifiers. This applies to the new public `IRuntimeModelExecutionReader` port implemented by this provider.

The three queries await EF directly with no provider-failure normalization. An unavailable/misconfigured SQLite database therefore exposes `Microsoft.Data.Sqlite.SqliteException` through `FindAsync`, `FindInputAsync` and `ListPageAsync`, without a stable domain type or execution/input context. `BpmnModelRuntime.FindExecutionAsync` calls this port directly, so this is a consumed feature boundary, not an unobservable internal helper. The existing native `EfWorkflowExecutionStateStore.FindAsync` wraps the same query failure in `WorkflowExecutionStateEntityFrameworkPersistenceException` and preserves the provider exception as InnerException.

Private proof: `probe-ef/Probe.csproj` compiles the exact reviewed `EfRuntimeModelExecutionReader.cs` source and calls all three read methods against a private SQLite in-memory database whose model tables are unavailable. All three throw raw `SqliteException` (no InnerException); the native comparison wraps it. See `probe-ef.log`. The missing-table setup is a failure injection, not a claim that a qualified host should accept an unmigrated schema.

Required correction: normalize provider failures around all three new queries, preserving requested cancellation, the provider cause and operation/scope/execution/input context. Add focused failure-boundary regressions.

### S2 — P2 — Human-work inspection bypasses the repaired owned-JSON boundary

Location: `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnModelRuntime.cs:122`.

Rules: framework §2.23.5 (`constitution-framework.md:210`), and the actual public contract `Essentials/src/Activities/Bpmn/Core/IBpmnModelRuntime.cs:7`, which promises a contextual `BpmnModelValidationException` for invalid owned persisted model JSON.

`ListWorkAsync` directly deserializes the opaque persisted payload into the model-owned Document. A valid runtime header carrying a malformed nested work shape therefore escapes the public `IBpmnModelRuntime.ListWorkAsync` boundary as raw `JsonException`, without execution context or inner-cause wrapping. This also selects the API renderer's malformed-request 400 branch (`Api/Endpoints/BpmnHostingApi.cs:113`) even though the failure came from persisted state, not the GET request. The new round1 `BpmnDirectExecutionDriver.Read` wrapper already handles this exact ownership boundary, and `ListIncidentsAsync` uses it at line 147; human-work inspection bypasses it.

Private proof: `probe/Probe.csproj` compiles the exact reviewed `BpmnModelRuntime.cs`. A stub of the provider-neutral opaque state reader returns a valid RuntimeModelExecutionState whose payload contains `work: "invalid-shape"`; `ListWorkAsync` throws raw `System.Text.Json.JsonException`, InnerException absent. See `probe.log`. No persisted production fixture was edited. This is a separate public inspection path left outside round1's repaired publication/driver entry points.

Required correction: route human-work inspection through the model-owned checked Read boundary (or an equivalent contextual domain wrapper), and regress the public inspection call. Preserve the original JSON cause and execution identity.

### S3 — P3 — Three introduced owned type names exceed the ratified hard cap

Primary location: `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Extensions/RuntimeModelHostingServiceCollectionExtensions.cs:12`.

Rule: Elsa constitution §E6 R1, `.specify/memory/constitution.md:132`: five CamelCase components is the hard cap. Its scope at line 130 applies to introduced/renamed Elsa-owned types; external-contract mirrors and persisted/wire names are exempt.

The new public entry-point container has six components: Runtime / Model / Hosting / Service / Collection / Extensions. The same concrete gate failure occurs in two new implementation-only types:

- `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/RuntimeNativeModelWorkWriteVersion.cs:10`: Runtime / Native / Model / Work / Write / Version.
- `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeModelCheckpointParticipantStaging.cs:9`: Ef / Runtime / Model / Checkpoint / Participant / Staging.

These are owned CLR type names, not immutable wire identifiers or names of external contracts. No explicit naming exception was located. The counterexample is the mechanical naming gate itself; no runtime failure is alleged. Shorten redundant namespace-qualified components and update the new public API entry for the extension container. Preserve Checkpoint and every serialized identifier. No source-text regression test is requested.

## Scope and verification

The review used the complete base-to-head changed-file inventory (6,346 paths, 397 outside evidence), not only the latest repair. Relevant production paths inspected cover the new shared runtime Core contracts/models/event, registry/aggregator and scoped dispatch, BPMN Core/API/native adapter/driver/bindings, model checkpoint/material/retention integration, EF rows/families/upcasters and shared migration-history repair, feature registration, public API/catalog/README changes and affected tests/tooling. The retained-chain generator guard executes the tool against a private tree and checks its behavior; it is not a forbidden assertion on source text.

Governing inputs: fresh org AGENTS snapshot f1a8cd8 and repository AGENTS, contributor review checklist/merging requirements, framework 6.1.0 and Elsa 6.3.0 ratified rules, ADR0080, spec1017 plan/tasks/contracts/quickstart, and the supplied PR/issue discussion snapshots (no review records or open review threads). Round1 receipt 6516922901 and its repair notes were considered. The named registration event is now public Runtime Core; the returning registration interface is Source; the early retry eligibility and unconditional Cancelled-result settlement repairs are present. No independent round2 reviewer output was read.

Both private probes completed successfully as diagnostic executables. Their two implicated production classes were compiled directly from the reviewed worktree, using prebuilt dependency assemblies (EF informational revision 7fc0217a). `provenance.json` records their exact source hashes. The EF reader source is also unchanged between that dependency build revision and the reviewed head. Expected CS0436 warnings state that the directly compiled source wins over the same imported internal type. No whole affected project suite, actual host, provider matrix, Studio journey or current-head CI was rerun in this lens.

No repository edits, commits, pushes, GitHub writes, host/browser/store operations, or other reviewers' outputs were used. The worktree remained clean at the exact reviewed head. All new probe state and outputs are under this assigned directory; SQLite state was in memory.

## Existing holds and limits

The unanswered API/magnitude owner card (Core180, Runtime4 and shared-EF4 source declarations; not binary ApiCompat), needs:decision, deferred GitGuardian39189012/neutral-size scan, final exact-head CI, merge and resulting-main verification remain existing delivery holds, not new findings. No owner decision is inferred. #2735 delivery, #2898/#2907/program Partial status, #3242 native fault policy, and Orleans/task-runner deferrals retain their supplied status. Source-bound SQLite/single-node receipts do not prove other placement/provider-host or first-class direct-model Studio acceptance.

Draft/provisional framework §2.24 and Elsa §E2.8 versioning/Model X/§E2.9 were not treated as ratified blocking standards.
