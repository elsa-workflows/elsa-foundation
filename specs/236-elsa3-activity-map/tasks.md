# Tasks: Elsa 3 activity map

**Input**: Design documents from `specs/236-elsa3-activity-map/`: plan.md (D-1 to D-10), spec.md, member-map.md,
research.md (R-1 to R-10), data-model.md, contracts/, quickstart.md.

**Worktree**: `C:\Users\JoeyBarten\source\repos\w236`, branch `236-elsa3-activity-map`. Never the main checkout.

**Tests**:
- Required by framework §2.23: behaviour tests for every logic-bearing implementation (resolver, normalizer, native
  structure builder, rewrite strategies, converter, alias index, scanner facet, materializer content).
- No feature-registration test: `Elsa3MappingFeature` wiring has no conditional logic, so the §2.23.1 composition test
  covers it.
- xunit only, no FluentAssertions.
- **Cadence**: the derived constitution sets no Elsa test-cadence gate (D9 in its sync record), and framework §2.21.2
  leaves cadence to the application. Tests below follow the code they cover (test-after). The coverage test is written
  early only because it drives the map table to completion. This is a recorded decision, not an open gap.

**Paths**:
- `M/` = `Extensions/Elsa3/src/Mapping/`
- `MT/` = `Extensions/Elsa3/tests/Mapping/Tests/`
- `IMP/` = `Extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import/`

**Format**: `[ID] [P?] [Story] Description`. `[P]` means a different file with no dependency on an incomplete task.

---

## Phase 1: Setup

- [X] T001 Add test-only project references to `MT/Elsa3.Mapping.Tests.csproj`:
  - the Elsa 4 activity assemblies the map targets (Primitives, Flow/control, Http, Scheduling, JavaScript, Graph
    Runtime, which holds `Elsa.Activities.Graph.Runtime.Activities.Complete`);
  - `Elsa.Activities.Design.Reconciliation.Clr` (scanner);
  - `Elsa.Activities.Graph.Design` (`GraphActivityProvider`, R-10).

  The production `M/Elsa3.Mapping.csproj` gains **no** Graph reference (plan, Testing). Restore and build the test
  project.
- [X] T002 [P] Create the test-owned Elsa 3 surface fixture `MT/Fixtures/elsa3-activity-surface.json` (R-4):
  - start from `docs/reports/evidence/activity-contract-parity/` (67 classes in five modules);
  - remove the spurious `Result` outputs;
  - add `SetOutput`, the plain properties, the ports (`then`, `else`, `body`, `cases`, `default`, `branches`) and the
    engine-internal classes member-map.md lists (`Start`, `End`, `Complete`, `Workflow`, `CompositeWithResult`,
    `NotFoundActivity`, `DynamicActivity`, `Inline`);
  - mark it `CopyToOutputDirectory` in the csproj.

---

## Phase 2: Foundational (blocks every story)

- [X] T003 [P] Create `Extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs`: a public static class with the 14
  `ELS3-ACTIVITY-*` and 12 `ELS3-BEHAVIOUR-*` constants exactly as in `contracts/diagnostic-codes.md`. That includes
  `OutcomeUndeclared` and `OutcomeUnreached`, and excludes the removed `BoundaryOutcomeUnsupported` (D-10).
- [X] T004 [P] Create the public map model types in `M/ActivityMap/` (data-model §1–§4, immutable records, ordinal keys):
  - `Elsa3ActivityMapEntry.cs`;
  - `Elsa3ActivityTarget.cs` (closed hierarchy: `CatalogActivity`, `Intrinsic`, `GraphLevel`, `NoCounterpart`);
  - `Elsa3MemberRule.cs` with `Elsa3MemberKind`, `Elsa3MemberRuleKind` and `Elsa3ValueContract`;
  - `Elsa3OutcomeRule.cs`;
  - `Elsa3NodeRewriteKind.cs` (the 8 kinds of data-model §6).
- [X] T005 Create `M/ActivityMap/IElsa3ActivityMap.cs` (public replacement contract) and
  `M/ActivityMap/Elsa3BuiltInActivityMap.cs` (internal sealed). The static table is empty for now, with ordinal
  lookup dictionaries built once. Register it as a singleton in `M/Elsa3MappingFeature.cs` (`ConfigureServices` stays
  virtual).
- [X] T006 [P] Create the conversion contracts and models in `M/Conversion/` (data-model §7, §7a, §8;
  `contracts/converter-api.md`):
  - `IElsa3DefinitionConverter.cs`, `IElsa3NodeConverter.cs`;
  - `Elsa3ConversionRequest.cs`;
  - `Elsa3ConvertedDefinition.cs` (`State`, `OutputMappings`, `BoundaryOutcomes` as
    `IReadOnlyList<ActivityOutcomeContract>`, `OutcomeMappings`);
  - `Elsa3GraphOutputMapping.cs`, `Elsa3GraphOutcomeMapping.cs`.
- [X] T007 Create `MT/Support/RealActivityCatalog.cs`: it runs `ClrAssemblyScanner` over the Elsa 4 activity assemblies
  from T001 into an in-memory `IActivityDefinitionVersionStore` / `IActivityDefinitionLookup`, so tests resolve real
  type keys and versions (SC-002). It also supports adding test-assembly activities and extra versions (US1 scenario
  3, US3).
- [X] T008 Create `MT/Elsa3ActivityMapCoverageTests.cs` (SC-001, R-4):
  - **Elsa 3 side**: every activity and member in `elsa3-activity-surface.json` has exactly one map entry and member
    rule.
  - **Elsa 4 side**: every targeted type key, member key, slot and outcome exists ordinally in `RealActivityCatalog`.
  - Every `BehaviourWarning` the map declares is an `Elsa3ActivityDiagnosticCodes` constant.
  - Every behaviour constant is used by a map entry or a rewrite strategy (`OutcomeUndeclared` and `OutcomeUnreached`
    by `TerminalPlacement`).
  - The test is red until T015.
- [X] T009 Create `M/Resolution/Elsa3ActivityResolver.cs` (internal sealed, scoped; R-5; data-model §5):
  - resolution order: plan replacement → built-in map → *(alias step, filled in T037)* → verbatim catalog key →
    `UnmappedType`;
  - highest valid-SemVer pick via `IActivityDefinitionLookup.ListVersions` + `SemVer.TryParse`/`CompareTo`, build
    metadata ignored, else `VersionUnavailable`;
  - intrinsic targets resolve without the catalog;
  - `JsonException`/`EntityNotFoundException` → diagnostics; only cancellation escapes;
  - per-scope cache by Elsa 3 type name.

  Register it as scoped in `M/Elsa3MappingFeature.cs`.
- [X] T010 [P] Change `M/Services/Elsa3ValueFlowLowerer.cs` to take an `IReadOnlyDictionary<string,string>` of
  diagnostic metadata instead of `Elsa3WorkflowDefinitionImportInput` (R-8). `M/Services/Elsa3WorkflowDefinitionImporter.cs`
  builds the dictionary from its input exactly as today. Existing `Elsa3MemoryReference*Tests` stay green.
- [X] T011 Create `M/Conversion/Elsa3DefinitionConverter.cs` (internal sealed, scoped; FR-015):
  - pipeline: clone → normalize (T017) → collect memory-reference graph + lower → map to `WorkflowDefinitionState`;
  - never mutates `request.Definition`;
  - gathers every diagnostic, ordered by (JSON path, code) (FR-014);
  - an unexpected non-cancellation exception becomes one `ELS3-MIGRATION-DEFINITION-MAPPING-FAILED`;
  - returns `Elsa3MigrationResult<Elsa3ConvertedDefinition>`.

  Register `IElsa3DefinitionConverter` and `IElsa3NodeConverter` in `M/Elsa3MappingFeature.cs`.
- [X] T012 Create `MT/Elsa3ActivityResolverTests.cs`:
  - each resolution step and its precedence;
  - the SemVer pick (`1.0.0` vs `1.1.0+abc` → `1.1.0+abc`; non-SemVer ignored; none valid → `VersionUnavailable`);
  - intrinsics;
  - caching;
  - a catalog exception becomes a diagnostic.

**Checkpoint**: contracts, models, resolver and converter skeleton compile, and the resolver tests are green. The
coverage test is red.

---

## Phase 3: User Story 1: built-in activities convert to their Elsa 4 counterparts (P1) 🎯 MVP

**Goal**: every built-in Elsa 3 activity converts to its Elsa 4 target at the installed catalog version, with members
renamed, defaults made explicit (R2), ports routed to native child slots, and intrinsics in place.

**Independent test**: `Elsa3BuiltInConversionTests` converts a definition with one node of each convertible built-in
against `RealActivityCatalog`. It finishes with 0 errors and warnings only where W applies.

- [X] T013 [US1] Fill `M/ActivityMap/Elsa3BuiltInActivityMap.cs` for the **Workflows.Core control and primitives**
  sections of member-map.md:
  - If, FlowDecision → If, Switch, FlowSwitch → Switch, For, ForEach, While, Break, Sequence, Parallel, Fork, Delay,
    WriteLine, ReadLine, Fault;
  - the intrinsics SetVariable, SetOutput, Correlate, SetName, Finish, Complete, End;
  - Flowchart, FlowFork, FlowJoin and Start as `GraphLevel` (#2705);
  - the shared R9 base list (`name`, `metadata`, `nodeId`);
  - `Elsa3OutcomeRule`s per FR-004d.
- [X] T014 [US1] Fill `M/ActivityMap/Elsa3BuiltInActivityMap.cs` for the **Workflows.Runtime, Http, Scheduling and
  JavaScript** sections:
  - DispatchWorkflow, ExecuteWorkflow → DispatchWorkflow;
  - HttpEndpoint, WriteHttpResponse, SendHttpRequest, FlowSendHttpRequest → SendHttpRequest;
  - Timer, Cron, StartAt;
  - RunJavaScript;
  - every §3 activity as `NoCounterpart` with its reason;
  - `Inline`, `DynamicActivity`, `NotFoundActivity`, `StateMachine`, `Workflow`, `CompositeWithResult` per member-map.md.
- [X] T015 [US1] Run `MT/Elsa3ActivityMapCoverageTests.cs` and fix the table until both sides are green (SC-001).
- [X] T016 [US1] Create `M/Conversion/Elsa3NativeStructureBuilder.cs` (internal sealed; R-6): it builds a container's
  native Elsa 4 structure from the resolved target's structure design facet (kind, schema, slots, cardinality, initial
  payload). It routes Elsa 3 `activities` and port children into slots by the entry's `ChildSlot` rules. A Flowchart
  container yields `GraphConversionPending`, but its children are still converted per node (FR-006).
- [X] T017 [US1] Create `M/Conversion/Elsa3ActivityNormalizer.cs` (internal sealed): per node it resolves through
  `Elsa3ActivityResolver` and applies the member rules:
  - R1 carry/rename to the exact Elsa 4 key, with case-insensitive matching of Elsa 3 names;
  - R2 emits the Elsa 3 default;
  - R3 removed input: silent when default, else `RemovedInputBound`, always when bound by an expression;
  - R4 lost output: exact projection, else `OutputNotRepresentable`;
  - R5 contract checks → `ValueOutsideContract`;
  - R9 display-only;
  - W warnings per bound member/activity (FR-004b);
  - aliased/verbatim nodes bind by name case-insensitively, leftovers → `UnknownMember` (FR-010);
  - a required input left unbound → `RequiredInputUnbound` (FR-004c);
  - `MaxNestingDepth` (14) → `NestingTooDeep`;
  - two properties binding one input → `DuplicateInputBinding`;
  - it dispatches the entry's `Rewrite` to the strategies below.
- [X] T018 [P] [US1] Create the rewrite contract `M/ActivityMap/Rewrites/IElsa3NodeRewrite.cs` and the strategies
  `Elsa3SwitchValueSynthesisRewrite.cs` (MatchFirst literal or absent only; synthesized `Value` returns the first true
  case's label; `cases[].match` = label; MatchAny or expression mode → `UnsupportedMode`) and
  `Elsa3VariableReferenceRewrite.cs` (`VariableReference(referenceKey, declaringScopeId)` with the computed declaring
  scope).
- [X] T019 [P] [US1] Create the strategies:
  - `M/ActivityMap/Rewrites/Elsa3ExecuteWorkflowAsDispatchRewrite.cs`: `WaitForCompletion = true`; captured `Result`
    or expression target → unconvertible; `ExecuteWorkflowDurable` warning;
  - `Elsa3ParallelBranchNamesRewrite.cs`: `Branch {n}`, 1-based document order; JoinMode → `threshold`;
  - `Elsa3LoopVariableReferencesRewrite.cs`: registers `index` / `currentItem` / `currentIndex` scoped variables with
    the loop as declaring scope;
  - `Elsa3HttpTriggerResponseModeRewrite.cs`: `ResponseMode = Sync` plus the `Result`/`Headers`/`QueryStringData`
    projections.
- [X] T020 [US1] Create `M/ActivityMap/Rewrites/Elsa3TerminalPlacementRewrite.cs`, **plain-workflow branch only**
  (data-model §6). Finish and End/Complete in the root flowchart with ≤ 1 literal outcome → `elsa.intrinsic.finish@1`
  with `outcome`; anything else → `UnsupportedMode`. The reusable branch comes in T048. Until then, a call with
  `IsReusable = true` returns `UnsupportedMode`, so nothing falls back to `finish`.
- [X] T021 [US1] Rewire `M/Mappings/Elsa3ActivityToState.cs`:
  - use `Elsa3ActivityResolver` and `Elsa3NativeStructureBuilder` instead of `GetVersion`'s `"{n}.0.0"` lookup and the
    opaque `elsa3.imported-activity.structure`;
  - remove every input-caused throw, including the Flowchart `Connections` `NotSupportedException`, which becomes
    `GraphConversionPending`.
- [X] T022 [US1] Rewire `M/Services/Elsa3MemoryReferenceGraph.cs`: `GetVersionAsync` delegates to
  `Elsa3ActivityResolver`, so lowering and mapping cannot disagree (FR-011). Normalizing first means it sees Elsa 4
  member names (R-8).
- [X] T023 [US1] Move `M/Services/Elsa3WorkflowDefinitionImporter.cs` onto `IElsa3DefinitionConverter`
  (`IsReusable = false`, no replacements). Keep `ELS3-REUSABLE-SELECTION-INVALID` for reusable definitions unchanged.
- [X] T024 [US1] Migrate the existing tests to real keys (plan §2.21.1 ACTION): the stub lookups that answer any key in
  `MT/Elsa3ActivityToStateTests.cs`, `MT/Elsa3MemoryReferenceImportFixture.cs`, `MT/Elsa3MemoryReferenceImportTests.cs`
  and `MT/Elsa3MemoryReferenceDiagnosticsTests.cs` move to `RealActivityCatalog` or explicitly registered real keys.
  Delete `MT/Elsa3ImportedActivityStructureTests.cs`, whose behaviour is removed by R-6, and record the deletion for
  the PR description.
- [X] T025 [US1] Create `MT/Elsa3BuiltInConversionTests.cs` covering US1 scenarios 1–8 and SC-002:
  - WriteLine → Primitives WriteLine with `text`;
  - the renames (`Duration`, `Expression`, `Collection`, `Body`/`Headers`, `Inputs`, `Value`);
  - the SemVer pick;
  - `For.EndInclusive = true`;
  - the intrinsics with their inputs;
  - `HttpEndpoint.ResponseMode = Sync`;
  - `If.Then`/`If.Else` native slots;
  - FlowDecision → If and FlowSwitch → Switch;
  - one all-built-ins definition with 0 errors.
- [X] T026 [P] [US1] Create `MT/Elsa3NativeStructureBuilderTests.cs` and `MT/Elsa3NodeRewriteTests.cs`: Parallel branch
  names and threshold, Switch synthesis and case order, VariableReference declaring scopes, loop variables, the
  HttpEndpoint projections, and plain-workflow `TerminalPlacement`.
- [X] T027 [US1] Add a determinism test (SC-005, FR-020) to `MT/Elsa3BuiltInConversionTests.cs`: converting twice
  against the same catalog gives byte-identical serialized state and identical diagnostics.

**Checkpoint**: single-definition import converts built-ins against a real catalog. US1 is independently shippable.

---

## Phase 4: User Story 2: unconvertible input yields a stable diagnostic, never an exception (P1)

**Goal**: every unconvertible cause returns its stable code with full location, and diagnostics are complete and
ordered. No exception escapes either path.

**Independent test**: one input per cause gives exactly that code. The whole Elsa 3 suite has 0 escaped exceptions.

- [X] T028 [US2] Audit `M/Mappings/Elsa3ActivityToState.cs`, `M/Mappings/Elsa3WorkflowDefinitionToState.cs`,
  `M/Services/Elsa3MemoryReferenceGraph.cs`, `M/Services/Elsa3ValueFlowLowerer.cs` and the new `M/Conversion/*` for
  any remaining `throw` reachable from input or catalog content. Each one becomes an Error diagnostic carrying
  definition id and version, node id, JSON path, property, Elsa 3 type and guidance (FR-012).
- [X] T029 [US2] Create `MT/Elsa3ConversionDiagnosticsTests.cs` with a theory of one row per `ELS3-ACTIVITY-*` code
  (SC-003), with exact code and location:
  - `NoCounterpart`: StartAt, StateMachine, ParallelForEach, RunTask, BulkDispatchWorkflows, DownloadHttpFile,
    WriteFileHttpResponse;
  - `UnmappedType`;
  - `UnknownMember`;
  - `MemberSemanticsChanged`: `SendHttpRequest.ParsedContent` captured;
  - `GraphConversionPending`: FlowFork, FlowJoin, Flowchart, Start;
  - `VersionUnavailable`;
  - `RemovedInputBound`: `exposeFileTooLargeOutcome = true`; R3 HttpEndpoint members absent or false are silent;
  - `UnsupportedMode`: Switch with a JavaScript `mode`;
  - `ValueOutsideContract`, `OutputNotRepresentable`, `RequiredInputUnbound`, `NestingTooDeep`,
    `DuplicateInputBinding`.
- [X] T030 [US2] Add to `MT/Elsa3ConversionDiagnosticsTests.cs`:
  - multi-cause completeness and deterministic (JSON path, code) order (US2 scenario 7, FR-014);
  - a theory of one row per `ELS3-BEHAVIOUR-*` code from a map entry (`FormPostNotParsed` from a captured
    `HttpEndpoint.parsedContent` converts with one warning, US2 scenario 10; the rest per `contracts/diagnostic-codes.md`);
  - cancellation still throws `OperationCanceledException`.
- [X] T031 [US2] Add a suite-wide guard to `MT/Elsa3ConversionDiagnosticsTests.cs`: converting every Elsa 3 definition
  fixture used across `MT/` returns a result and never throws, and `ELS3-MIGRATION-DEFINITION-MAPPING-FAILED` is
  unreachable from those inputs (R-3).

**Checkpoint**: US1 + US2 make the P1 increment: built-ins convert, and everything else is reported by code.

**MVP implementation notes (2026-10-15), for the checkpoint review:**
- *Pulled forward from US4.* T042 (credential walk over native structures; `Elsa3ImportedActivityStructure` removed) and
  the core of T039 (the materializer converts each item through `IElsa3DefinitionConverter` and refuses the apply with
  `ReusableActivityImportValidationException`). Native structures land in US1, and without these the collection import
  would lose nested nodes. Still open in US4: T039 lowering metadata and replacement checks, T040, T041, T043–T054.
- *Model additions.* `Elsa3MemberRuleKind.UseRestriction` and `SemanticsChanged`, `Elsa3MemberKind.Synthetic`,
  `Elsa3MemberRule.WhenBound`, `Elsa3ValueContract.HttpMethodSupported`. They let the table express R6 uses, the captured
  `ParsedContent` rule, Elsa 4-only defaults and the ContentType rule as data.
- *Rewrites not needed as code.* ExecuteWorkflowAsDispatch and HttpTriggerResponseMode are expressed as member rules
  (a synthetic default and default flips); their enum values stay, with no strategy registered.
- *Nesting.* A JSON-level budget (58 levels for a node) replaces the flat 14-container limit. Sequence nesting keeps
  14 (`Elsa3ActivityNormalizer.MaxSequenceNestingDepth`); Parallel/Switch nesting allows fewer, which the old flat limit
  silently overran.
- *Value-flow type check.* VF-IMP-008 now compares simple type names, treats Object/JsonElement/Any as untyped, and lets
  a String expression or reference bind a scalar member. A String literal must still parse as a numeric, boolean, time
  span, GUID or date member. Secret-typed values pass through lowering unchanged, like literals.
- *Order of stages.* Lowering runs only when node conversion reported no errors, so an unresolved node is not also
  reported as a value-flow error.
- *Behaviour changes asserted by migrated tests.* A duplicate binding, too-deep nesting and an unknown member are
  diagnostics (422 on the collection apply instead of a 400 ArgumentException). A ForEach body comes from its `body` port.
- *Review decisions (2026-10-15, maintainer).*
  - VF-IMP-008 loosening: kept.
  - JSON-depth nesting budget: kept, on condition that the per-kind budgets are documented where users meet them
    (Mapping README, member-map.md and the `NestingTooDeep` message): what each container kind allows and why.
    Added to T056.
  - 422 instead of 400 on the collection apply: accepted.
  - Fork default join mode: verified in elsa-core `5b0c2d73`: `JoinMode = ForkJoinMode.WaitAll`, enum
    WaitAll = 0, WaitAny = 1. Absent now converts to a Parallel with no threshold and no warning. Fixed.
  - `customProperties.commitStrategy`: left as is (silently dropped by the Elsa 3 model).
  - `HttpEndpoint.Headers`/`QueryStringData` captures: synthesized workflow variable with the Elsa 3 shape (plan D-11 to
    D-13, Phase 4b).
  - Output capture (plan D-17): an output captured into a workflow variable becomes an Elsa 4 Variable output
    binding; container-scope capture and loop values read outside their loop are OUTPUT-NOT-REPRESENTABLE. Implemented
    with Phase 4b; D-11 now writes the captured workflow variable when there is one (D-11a).
  - Elsa 3 JavaScript: verified (research R-11). 236 closes the silent gaps (D-14), wraps statement-form expressions
    (D-15) and rewrites RunJavaScript scripts (D-16), in Phase 4c. Widening what converts is the follow-up (#2717).

---

## Phase 4b: HttpEndpoint request projections (plan D-11 to D-13, added 2026-10-15)

**Goal**: a captured Elsa 3 `HttpEndpoint.Headers` or `QueryStringData` converts through a synthesized workflow variable
that keeps the Elsa 3 shape, so every consumer kind keeps working.

**Independent test**: an endpoint in a Sequence whose captured headers are read by a WriteLine (direct binding) and by a
JavaScript expression converts with 0 errors. The synthesized `set` node follows the endpoint, and both consumers read
`elsa3.http.{node}.headers`.

- [X] T061 [US1] Implement `M/ActivityMap/Rewrites/Elsa3HttpTriggerResponseModeRewrite.cs` (kind `HttpTriggerResponseMode`,
  set on the HttpEndpoint entry):
  - for each captured `Headers`/`QueryStringData` property, plan a synthesized workflow variable and a `set` node
    (ids per D-13);
  - remove the property from the endpoint;
  - register the memory id so lowering binds consumers to the variable;
  - an endpoint at the root or in a single-child slot → `OutputNotRepresentable` (D-12).

  Change the two rules in `Elsa3BuiltInActivityMap.Entries.cs` from `Lost(..., "Request.*")` to `Structure(...)` outputs
  consumed by the rewrite, and keep the coverage test green.
- [X] T062 [US1] Insert the synthesized node in `M/Conversion/Elsa3ActivityNormalizer.cs`: an `Elsa3Activity` placed right
  after the endpoint in its parent's `Activities` (and slot list), with a plan that resolves to `elsa.intrinsic.set@1`, a
  workflow-scoped Object variable target, and a `value` input. The value is a portable JavaScript expression with one
  `ActivityResult` parameter bound to the endpoint's `Request` output, whose source rebuilds the Elsa 3 shape.
  Declare the variable on the clone's workflow variables.
- [X] T063 [US1] In `M/Services/Elsa3ValueFlowLowerer.cs` (or via the memory-reference graph), resolve a consumer of the
  captured memory id to a `Variable` binding on the synthesized variable (workflow scope); JavaScript reads resolve
  through the same variable.
- [X] T064 [US1] Tests in `MT/Elsa3HttpRequestProjectionTests.cs`:
  - headers read by a direct binding and by JavaScript;
  - query string;
  - both captured on one endpoint;
  - deterministic ids;
  - endpoint at the root → `OutputNotRepresentable`;
  - the synthesized value's source maps a one-item list to a string and keeps a several-item list (asserted on the
    expression source).

---

## Phase 4c: JavaScript gaps (plan D-14 to D-16, research R-11, added 2026-10-15)

**Goal**: no Elsa 3 JavaScript converts silently and then fails or returns null at run time: what Elsa 4 cannot run is
refused with a stable code, statement-form expressions are wrapped faithfully, and RunJavaScript scripts are rewritten.

**Independent test**: each gap construct of R-11 gives its code. `var x = 1; x + 1` converts to an IIFE returning
`x + 1`. A RunJavaScript `getA() + getB()` written with `getVariable` converts to `return args.a + args.b;` with
`arguments` bound.

- [X] T065 [US2] In `M/Services/Elsa3ExpressionRewriter.cs`, refuse (VF-IMP-005) `new Date()`; member access or calls on
  `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, `Random`, `_`, `moment`; reads of a bare `args`; and
  assignment or update through a lowered read (D-14). Remove the lowering of `getOutput`/`input.x`/`output.x`, which
  Elsa 3 does not have: those names are ordinary locals.
- [X] T066 [US2] In `M/Services/Elsa3ExpressionRewriter.cs`, wrap statement-form expressions (D-15): several statements,
  or one non-expression statement, become `(function () { …; return <last expression>; })()`. A last statement that
  is not an expression → VF-IMP-006.
- [X] T067 [US1] Rewrite RunJavaScript scripts (D-16):
  - a script mode in the rewriter: same lowering and refusals, `setOutcome(s)` refused, the final expression statement
    turned into `return`;
  - the lowerer sends the literal `script` of a RunJavaScript node through it and writes the parameter bindings to
    Elsa 4's `arguments` input;
  - map rule `Script` stays Carry; `arguments` is filled by the rewrite.
- [X] T068 [US2] Tests `MT/Elsa3JavaScriptConversionTests.cs`:
  - one row per D-14 construct with its code;
  - the D-15 wrap (completion value preserved, non-expression tail refused);
  - D-16 script lowering, `arguments` and `return`, `setOutcome` refused;
  - existing VF tests stay green.
- [ ] T069 Comment on #2717 with R-11: the list of refusals 236 now reports, that `getHttpRequestBody` is a host
  extension, the accessor-precedence and value-shape risks. Only with the maintainer's go-ahead.

---

## Phase 5: User Story 3: rebuilt custom activities are found through an alias (P2)

**Goal**: an Elsa 4 CLR activity marked `[ReplacesActivityType("Acme.SendInvoice")]` is found for that Elsa 3 type.

**Independent test**: a test assembly activity resolves; a second claimant is ambiguous; a new version without the mark
is unmapped.

- [X] T032 [P] [US3] Create `Essentials/src/Activities/Runtime/Core/Attributes/ReplacesActivityTypeAttribute.cs`
  (data-model §10): public sealed,
  `[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]`, `TypeName`; a blank value throws
  `ArgumentException`. No Elsa 3 name or reference (§E2.7).
- [X] T033 [US3] Record the facet in `Essentials/src/Activities/Design/Reconciliation/Clr/Services/ClrAssemblyScanner.cs`
  (and `ReflectionOnlyAttributes.cs` if attributes are read reflection-only there):
  - `ActivityDesignFacet("elsa.replaced-activity-types", "1", { "typeNames": [...] })`;
  - names trimmed, distinct and ordinally sorted, read from the class only;
  - no facet when absent;
  - the payload never carries `ports`.
- [X] T034 [US3] Add to `Essentials/tests/Activities/Design/Tests/Unit/ClrAssemblyScannerTests.cs`:
  - facet present, sorted and de-duplicated for one or several attributes;
  - not inherited from a base class;
  - no facet without the attribute;
  - the content hash of an un-attributed activity unchanged (hash stability).
- [X] T035 [P] [US3] Document the facet in `Essentials/src/Activities/Design/Reconciliation/Clr/README.md` and
  `Essentials/src/Activities/Design/Reconciliation/Clr/EXTENSION_POINTS.md`: kind, schema and payload, and the
  version-bump consequence of the hash (R-2).
- [X] T036 [US3] Create `M/Resolution/Elsa3ReplacedActivityTypeIndex.cs` (internal sealed; data-model §9):
  - built once per scope from `IActivityDefinitionVersionStore.ListAsync()`;
  - highest valid-SemVer version per definition;
  - `typeNames` → definition id;
  - more than one id → `AmbiguousAlias` naming both.
- [X] T037 [US3] Plug the alias step into `M/Resolution/Elsa3ActivityResolver.cs` between the built-in map and the
  verbatim key. The built-in map wins over an alias. Aliased nodes bind members by name case-insensitively (FR-010).
- [X] T038 [US3] Create `MT/Elsa3ReplacedActivityTypeTests.cs` with a test-assembly activity (US3 scenarios 1–3):
  - it resolves to the highest version and binds properties case-insensitively;
  - two claimants → `ELS3-ACTIVITY-AMBIGUOUS-ALIAS`;
  - a new version without the attribute → `ELS3-ACTIVITY-UNMAPPED-TYPE`;
  - a full Elsa 4 key resolves verbatim;
  - a built-in-mapped type ignores an alias.

**Checkpoint**: aliases work end to end through CLR reconciliation.

---

## Phase 6: User Story 4: the collection import converts and lowers like single-definition import, including reusable outputs and Complete/End (P2)

**Goal**:
- The materializer converts every selected item through `IElsa3DefinitionConverter`: lowering, replacements,
  warnings, and all-or-nothing on errors.
- A reusable workflow gets a schema-2 manifest with `outputMappings` (FR-004e) and `Complete` nodes for Elsa 3
  `Complete`/`End`, with its declared boundary outcomes (D-1 to D-7).
- Reusable output is proven by `GraphActivityProvider` validate + compile (R-10, D-9).
- **Not in scope**: publishing imported reusables (#2796).

**Independent test**: collection apply stores the same bindings as single-definition import (SC-004). A converted
reusable workflow validates and compiles with the expected `outputMappings`, `outcomeMappings` and `completeOutcomes`
(SC-006 at compile level).

### Collection lowering, replacements, warnings

- [X] T039 [US4] Move `M/Services/Elsa3ReusableActivityImportMaterializer.cs` onto `IElsa3DefinitionConverter` (FR-016):
  - convert a private clone of each selected definition with `IsReusable = item.IsReusable` and the plan's
    replacements (`BuildReplacements` unchanged);
  - pass diagnostic metadata built from the plan item (R-8);
  - any Error across items → `ReusableActivityImportValidationException` with all items' diagnostics, persisting
    nothing (FR-018);
  - `BuildActivity` / `BuildWorkflow` / `BuildWrapperState` / `BuildGraphManifest` keep their shape (FR-019).
- [X] T040 [US4] In `M/Services/Elsa3MemoryReferenceGraph.cs` / `M/Resolution/Elsa3ActivityResolver.cs`, ensure a node
  the plan rewrites to a reusable activity takes inputs and outputs from its `Elsa3ActivityExactReplacement` and is
  never looked up in the catalog (FR-017, US4 scenario 2).
- [X] T041 [P] [US4] Add the warnings channel (R-7):
  - optional `IReadOnlyList<Elsa3MigrationDiagnostic> Warnings` (default empty) on `ReusableActivityImportMutation`
    and `ReusableActivityImportApplyResult` in `IMP/Models/ReusableActivityImportPlan.cs`;
  - copy them in `IMP/Services/ReusableActivityCollectionImporter.cs`;
  - return them from `IMP/Endpoints/Collections/Apply/Endpoint.cs`;
  - the persisted receipt is unchanged.
- [X] T042 [US4] Replace the credential-literal admission walk in `IMP/Services/ReusableActivityCollectionImporter.cs`:
  - the shared design tree walk over native structures replaces `Elsa3ImportedActivityStructure.Nodes()` (R-6);
  - update the class remarks that describe the opaque structure;
  - then delete `IMP/Models/Elsa3ImportedActivityStructure.cs` once nothing references it, keeping `MaxNestingDepth`
    = 14 wherever the normalizer now owns it.
- [X] T043 [US4] Update `MT/ReusableActivityCollectionCredentialLiteralTests.cs` and `MT/ReusableActivityImportFixtures.cs`
  for native structures and real keys. A refused binding nested in a native child slot is still found and refuses the
  whole apply.
- [X] T044 [US4] Create `MT/Elsa3CollectionLoweringTests.cs` (SC-004, US4 scenarios 1–4):
  - every value-flow case of the single-definition tests (literal, request, variable, result, loop, JavaScript,
    Liquid), applied as a collection item, stores identical bindings;
  - the replacement-consuming node is lowered from the replacement's contract;
  - a dangling reference → `VF-IMP-001`, nothing persisted;
  - the caller's collection is not mutated;
  - warnings surface on the apply result.

### Reusable outputs (FR-004e)

- [X] T045 [US4] Create `M/ActivityMap/Rewrites/Elsa3ReusableOutputWriteRewrite.cs` (data-model §6, §8; member-map
  "SetOutput in reusable workflows"):
  - in a reusable workflow, `SetOutput` naming a declared output `O` → `elsa.intrinsic.set@1` on the synthesized
    variable `elsa3.output.{sha256(O)[..16]}`, with `O`'s type and storage driver;
  - otherwise → `elsa.intrinsic.set-output@1`;
  - `OutputName` literal-only (R5);
  - emit `Elsa3GraphOutputMapping`s and the synthesized variables on `Elsa3ConvertedDefinition`.
- [X] T046 [US4] In `M/Services/Elsa3ReusableActivityImportMaterializer.cs` (content only, FR-019):
  - `BuildGraphManifest` appends the synthesized variables and writes `outputMappings` (`{ outputReferenceKey, source:
    { syntax: "Variable", value: { referenceKey, declaringScopeId } } }`);
  - `BuildWrapperState` gives the wrapper a root `Sequence[graph node, set-output(O ← graph node output O) …]` instead
    of null outputs.

### Complete and End in reusable workflows (D-1 to D-7)

- [X] T047 [P] [US4] Create `M/Conversion/Elsa3OutcomeKey.cs` (internal static; D-4, data-model §7a):
  - `"Done"` → (`done`, `Done`); else (`s`, `s`) verbatim, ordinal;
  - detects a key collision (`"done"` with `"Done"`).
- [X] T048 [US4] Complete the **reusable branch** of `M/ActivityMap/Rewrites/Elsa3TerminalPlacementRewrite.cs`:
  - at any depth, `Complete` with absent/empty or one literal outcome, and `End`, → one node whose target is the
    catalog type `Elsa.Activities.Graph.Runtime.Activities.Complete`, resolved by `Elsa3ActivityResolver` like any CLR
    activity, with input `outcome` = literal reference key (`End` → `done`) (D-1, D-3);
  - several outcomes, an expression outcome or a key collision → `UnsupportedMode` (D-2, D-4);
  - never `finish`;
  - record each literal outcome and node for T049.
- [X] T049 [US4] In `M/Conversion/Elsa3DefinitionConverter.cs`, assemble for `IsReusable = true` (data-model §7a):
  - `BoundaryOutcomes`, in ordinal reference-key order:
    - `done`/`Done`, emitted, always (D-5);
    - each outcome a `Complete` names, emitted, with one `ELS3-BEHAVIOUR-OUTCOME-UNDECLARED` per node whose outcome
      `Elsa3WorkflowDefinition.Outcomes` lacks (D-6);
    - each declared outcome other than `Done` that no `Complete` reaches is left out, with one
      `ELS3-BEHAVIOUR-OUTCOME-UNREACHED` per outcome (D-7);
  - `OutcomeMappings = [done → done]`.
- [X] T050 [US4] In `M/Services/Elsa3ReusableActivityImportMaterializer.cs` (content only): `ProviderSchemaVersion =
  "2"` and the manifest's `outcomeMappings` from `Elsa3ConvertedDefinition.OutcomeMappings`. `BoundaryOutcomes` is
  returned but not persisted, since the contract's home is #2796 (D-9). Leave a short code comment naming #2796 where
  the outcomes stop.
- [X] T051 [US4] Create `MT/Support/GraphProviderProof.cs`: given an `Elsa3ConvertedDefinition` and the reusable's
  inputs and outputs, it builds the `DesignActivityContract` (inputs, outputs, `BoundaryOutcomes`) and the schema-2
  `ActivityProviderManifest`. It runs `GraphActivityProvider.ValidateAsync` and `CompileAsync` against
  `RealActivityCatalog` and returns diagnostics plus the compiled boundary payload (R-10).
- [X] T052 [US4] Create `MT/Elsa3ReusableTerminalTests.cs` (D-1 to D-8; quickstart step 8; reusable definitions with
  non-Flowchart containers such as Sequence, loops and Parallel, since Flowchart is pending until #2705):
  - `Complete("fail")` nested in a loop inside Parallel converts to a `Complete` node with `outcome = "fail"`;
  - `End` → `outcome = "done"`;
  - a natural end needs no node;
  - `GraphProviderProof` validates clean, and `completeOutcomes` holds `fail`→`fail` plus the `done` mapping;
  - `Complete(["a","b"])` and an expression outcome → `UnsupportedMode`;
  - `"done"` + `"Done"` → `UnsupportedMode`;
  - an undeclared outcome → declared plus `OUTCOME-UNDECLARED`;
  - a declared, unreached outcome → absent plus `OUTCOME-UNREACHED`;
  - names are kept verbatim ("Not Found" stays "Not Found");
  - a real `Break` still maps to `Break` (D-8);
  - plain-workflow Complete/End still give `finish` or `UnsupportedMode`;
  - determinism of the outcome order.
- [X] T053 [US4] Add to `MT/Elsa3CollectionLoweringTests.cs` (SC-006 at compile level, US4 scenario 5):
  - a reusable workflow setting `CustomOutput` via `SetOutput` → the manifest maps `CustomOutput` to the synthesized
    variable the converted node writes;
  - `GraphProviderProof` validates and compiles with that output mapping;
  - the wrapper exposes `CustomOutput`;
  - the stored catalog row has `ProviderSchemaVersion = "2"`.
- [X] T054 [US4] Update the EF import tests under `Extensions/Elsa3/tests/Activities/Design/Import/Persistence/EntityFrameworkCore/`
  (`Tests/Support/ImportFixtures.cs` and the apply/retention tests) and `MT/ReusableActivityCollectionApplyTests.cs` /
  `MT/ReusableActivityCollectionImportTests.cs` for real keys, schema 2 and warnings on the apply result. Run them
  green.

**US4 implementation notes (2026-10-15):**
- *Provider proof is validation only.* `GraphProviderProof` runs the real `GraphActivityProvider.ValidateAsync` with
  the native structure handlers and Complete detection through the real catalog, on the exact manifest the import
  writes. `CompileAsync` needs every dependency published (publication and template), which is #2796's work, so the
  compile half of R-10's proof moves to #2796.
- *Bug found by the proof.* The materializer wrote a manifest variable's `collectionKind` as `None`, which the graph
  manifest rejects. It now writes the exact `CollectionKind` name.
- *Wrapper.* A reusable workflow with outputs gets a Sequence wrapper (graph node, then one `set-output` per output,
  read through an `ActivityResult` binding). The materializer resolves Sequence's catalog version through
  `IActivityDefinitionLookup`.
- *Warnings.* The apply response's receipt carries them; the EF codec stores the receipt without them.
- T054: the EF import tests still use their single-row stub catalog, and they pass with schema 2 and the warnings
  channel.

**Checkpoint**: every story is complete. Reusable output is proven at compile level, and the run-time proof is #2796's.

---

## Phase 7: Polish and cross-cutting

- [ ] T055 [P] Update `M/EXTENSION_POINTS.md` (§2.22.1): overridable contracts `IElsa3DefinitionConverter`,
  `IElsa3NodeConverter`, `IElsa3ActivityMap` (layer badge, default implementation, signature, when to override, what
  depends on it).
- [ ] T056 [P] Update `M/README.md` (and document the nesting budget per container kind in README, member-map.md and the `NestingTooDeep` message):
  - the conversion pipeline;
  - the resolution order;
  - the diagnostic families;
  - reusable outputs and Complete/End emission;
  - the #2705 and #2796 boundaries.
- [ ] T057 [P] Add "replaced activity type" to `docs/glossary/elsa.md` (the attribute, the facet, and highest-version
  semantics).
- [ ] T058 Run the boundary guards: `Elsa3MigrationBoundaryTests` and
  `Runtime_projects_do_not_reference_elsa3_compatibility_projects` (§E2.7, §E2.2).
- [ ] T059 Regenerate the extension-point and dependency maps with `-- all` and inspect `git diff --stat docs/`.
  Don't trust `-- check` on Windows, because CRLF makes it report every map stale.
- [ ] T060 Run the full Elsa 3 and affected Essentials test projects (`Extensions/Elsa3/tests/**`,
  `Essentials/tests/Activities/Design/Tests`, `Essentials/tests/Activities/Graph/Tests`) and walk quickstart.md
  steps 1–8. Record the SC-006 run-time check as deferred to #2796.

---

## Dependencies and execution order

- **Phase 1 → Phase 2**: T001 before T007 and T051. T002 before T008.
- **Phase 2 blocks all stories**: T003–T006 in parallel; then T005 → T009 → T011; T007 → T008/T012.
- **US1 (Phase 3)**: T013 → T014 → T015. T016, T017 and T018–T020 follow T009. T021–T023 need T016/T017. T024–T027
  come last.
- **US2 (Phase 4)**: needs US1's normalizer and converter (T017, T021–T023). The diagnostics mostly originate in US1
  code; US2 closes the gaps and proves them.
- **US3 (Phase 5)**: needs only Phase 2 (T009) and can run alongside US1/US2 after T009. T032 → T033 → T034; T036 →
  T037 → T038.
- **US4 (Phase 6)**: needs US1 (the converter and normalizer in use) and benefits from US2. T039 → T040/T044;
  T041–T043 in parallel with T045; T047 → T048 → T049 → T050; T051 → T052/T053; T054 last.
- **Polish**: after the stories it documents.

Story order: US1 → US2 (the P1 MVP), then US3 and US4 (P2, independent of each other).

## Parallel examples

- **Phase 2**: T003, T004 and T006 together (separate files).
- **US1**: T018 and T019 together (separate strategy files); T026 alongside T025.
- **US3 vs US1**: after T009, T032–T035 (Essentials) can run while T013–T017 (Mapping) proceed.
- **US4**: T041 (Import package) and T047 (outcome key) alongside T045.
- **Polish**: T055, T056 and T057 together.

## Implementation strategy

1. **MVP = Phases 1–4 (US1 + US2).** Single-definition import converts every built-in against a real catalog and
   reports everything else by stable code. Stop and validate: coverage green, SC-002, SC-003, SC-005.
2. **US3**: custom-activity aliases, with the one Essentials attribute and scanner facet.
3. **US4**: the collection path, reusable outputs and Complete/End, proven at compile level.
4. **Polish**: docs, guards, maps, full run.
5. **Remaining dependencies**:
   - #2705 activates Flowchart roots, so Complete/End reaches real Flowchart-based reusables;
   - #2796 publishes imported reusables, which gives SC-006 its run-time proof;
   - spec 235 (#2707) resumes after #2705 and #2706.
