# Tasks: Elsa 3 Flowchart connections

**Input**: Design documents from `specs/239-elsa3-flowchart-connections/`: spec.md (G-1 to G-7, H-1, H-2),
plan.md, research.md (R-1 to R-12), data-model.md, contracts/diagnostic-codes.md, quickstart.md.

**Worktree**: `C:\Users\JoeyBarten\source\repos\w2705`, branch `2705-elsa3-flowchart-connections`, rebased onto
`main` after #2805 merged (2026-10-16). Never the main checkout. Never merge `main` in.

**Tests**:
- Required by framework §2.23: behaviour tests for every logic-bearing change (outcome reading, graph pass, join
  checks, loop check, start resolution, terminal rewrite, projection placement, materializer outcome names).
- No feature-registration test: the graph builder is a plain scoped registration, covered by the §2.23.1 composition
  test and the existing `Elsa3MappingFeatureRegistrationTests`.
- xunit only, no FluentAssertions. Real catalog (`RealActivityCatalog`) for every conversion test.
- **Cadence**: as spec 236, test-after (the derived constitution sets no Elsa test-cadence gate, D9). Each story's
  tests follow its implementation within the story.

**Halt points**: T033 is written but skipped until spec 238 (#2850) is on `main` and this branch is rebased (H-1).
The PR stays draft until H-1 is cleared (H-2). Nothing else waits for 238.

**Paths**:
- `M/` = `Extensions/Elsa3/src/Mapping/`
- `MO/` = `Extensions/Elsa3/src/Models/`
- `MT/` = `Extensions/Elsa3/tests/Mapping/Tests/`
- `FX/` = `MT/Fixtures/Flowcharts/`

**Format**: `[ID] [P?] [Story] Description`. `[P]` means a different file with no dependency on an incomplete task.

---

## Phase 1: Setup

- [X] T001 Create `FX/` and include `Fixtures/Flowcharts/*.json` as `CopyToOutputDirectory` in *(Done: the csproj already copied `Fixtures/**/*.json`; only the loader was added.)*
  `MT/Elsa3.Mapping.Tests.csproj` (the Flowchart and `Elsa.Workflows.Design.Validations` references already exist).
  Add a small loader in `MT/Support/Elsa3FlowchartFixtures.cs` that reads a fixture into `Elsa3WorkflowDefinition`.

---

## Phase 2: Foundational (blocks every story)

- [X] T002 [P] Add to `MO/Elsa3ActivityDiagnosticCodes.cs` the constants `GraphConnectionInvalid`,
  `GraphOutcomeUnmapped`, `GraphJoinUnsupported`, `GraphLoopUnsupported`, `GraphStartAmbiguous` and
  `DeadConnectionRemoved`, exactly as in `contracts/diagnostic-codes.md`, each with a one-line XML doc. Change the
  doc of `GraphConversionPending` to "No longer raised by any built-in entry (spec 239); kept because codes are never
  reused."
- [X] T003 [P] Create `M/ActivityMap/Elsa3GraphConstructKind.cs` (`Fork`, `Join`, `Start`) and add the variant
  `GraphConstruct(Elsa3GraphConstructKind Kind)` to `M/ActivityMap/Elsa3ActivityTarget.cs` (data-model §1).
- [X] T004 Add `Elsa3ActivityOutcomes` and `Elsa3DynamicOutcomes` records (data-model §2) in
  `M/Resolution/Elsa3ResolvedActivity.cs`, add `Outcomes` to `Elsa3ResolvedActivity` (default `DoneOnly` so existing
  constructions compile), and `GraphConstruct` to `Elsa3ResolvedSource`.
- [X] T005 In `M/Resolution/Elsa3ActivityResolver.cs`:
  - read the version's `elsa.outcomes` facet (`{ports:[{name,type}], dynamicOutcomes?:{kind,source,unmatched}}`,
    web JSON) into `Outcomes`; no facet → `DoneOnly`; an unreadable facet → `STRUCTURE-UNREADABLE` with a message
    naming the outcomes facet (R-3);
  - intrinsics (`Elsa3IntrinsicContracts.Resolve`) → `DoneOnly`;
  - replacements → `Outcomes.Ports` from `Elsa3ActivityExactReplacement.OutcomeNames` (null → `DoneOnly`);
  - `GraphConstruct` target → resolved without the catalog: `Source = GraphConstruct`, empty version id, no inputs,
    outputs or structure, `Outcomes = None`.
- [X] T006 Add `OutcomeNames` (`IReadOnlySet<string>?`, default null) to `Elsa3ActivityExactReplacement` in
  `M/Mappings/Elsa3ActivityToState.cs`. In `M/Services/Elsa3ReusableActivityImportMaterializer.cs` `BuildReplacements`,
  fill it from the target's Elsa 3 definition: `Done`, its declared `Outcomes`, and every literal outcome of its
  `Complete` nodes at any depth (spec 236 D-5/D-6; duplicate the small scan, §2.17). Ordinal set.
- [X] T007 Add `GraphConstruct` (`Elsa3GraphConstructKind?`) and `FollowedBy` (`Elsa3Activity?`) to `Elsa3NodePlan`
  in `M/Conversion/Elsa3ConversionPlan.cs`.
- [X] T008 Change the map in `M/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs` (data-model §1):
  - `Flowchart` → `Catalog(..., "Elsa.Activities.Flowchart.Activities.Flowchart", ...)` keeping slot
    `Flowchart.Activities`, structure `Variables`, graph `Connections` and `Start`;
  - `FlowFork` → `GraphConstruct(Fork)` with `Structure("Branches", Input)`;
  - `FlowJoin` → `GraphConstruct(Join)` with `Structure("Mode", Input)`;
  - `Start` → `GraphConstruct(Start)`.
  Update `Elsa3BuiltInActivityMap.Rules.cs` helpers as needed and the map coverage test expectations
  (`MT/Elsa3ActivityMapCoverageTests.cs`).
- [X] T009 [P] Tests in `MT/Elsa3ActivityResolverTests.cs`: a catalog activity's static outcomes (If: `True`,
  `False`, `Break`); an activity with no facet → `Done`; SendHttpRequest's dynamic rule (`ExpectedStatusCodes`,
  unmatched `Unmatched status code`); an intrinsic → `Done`; a replacement's `OutcomeNames`; FlowFork, FlowJoin, Start
  → `GraphConstruct` (replaces the `GraphConversionPending` inline data at line ~95).
- [X] T010 [P] Test in `MT/ReusableActivityCollectionApplyTests.cs` (or the materializer's existing test file):
  `OutcomeNames` of a replacement whose target declares `succeed`, `fail` and completes with `succeed` is
  `{Done, succeed, fail}`.

**Checkpoint**: the solution builds; every pre-existing test passes except the pending-code expectations changed by
T008 (they are updated in T011 and T024).

---

## Phase 3: User Story 1 — A flowchart converts with its connections (P1) 🎯 MVP

**Goal**: a flowchart of ordinary activities converts with its connections and start node.

**Independent test**: `decision-tree.json` (HttpEndpoint → FlowDecision → True/False WriteLines → WriteHttpResponse)
converts with 0 errors; every node keeps its id; connections carry Elsa 4 ports; `startNodeId` is the endpoint.

- [X] T011 [US1] In `M/Conversion/Elsa3ActivityNormalizer.cs`: a node with `Connections` whose resolved target is not
  the Elsa 4 Flowchart → `GRAPH-CONNECTION-INVALID` (FR-012); a resolved `GraphConstruct` sets
  `nodePlan.GraphConstruct`, and when its parent is not a flowchart → `UNSUPPORTED-MODE` ("only meaningful inside a
  Flowchart"). Remove the `descend = failure.Code == GraphConversionPending` special case. After the walk, run the
  graph builder over every flowchart node plan, innermost first (post-order).
- [X] T012 [US1] Create `M/Conversion/Elsa3FlowchartGraphBuilder.cs` (internal sealed, scoped), stages 1–5 and 9–11 of *(Done as a stateless internal static class called by the normalizer; nothing is registered, plan §2.5 row updated.)*
  data-model §4 for flowcharts without constructs:
  - collect `Elsa3GraphEdge`s in connection order; validate endpoints (`GRAPH-CONNECTION-INVALID`, path
    `{flowchart}.connections[i]`, metadata `TargetNodeId`, `Port`);
  - map ports (R-4 step 5): map `Elsa3OutcomeRule` first (`{match}` expands to the Switch's case matches, read from
    the node plan's slots or synthesized cases; empty rule → `NO-COUNTERPART` with the rule's reason); else verbatim
    when `Outcomes` contains it or the dynamic rule accepts it (literal items of the source input, or the unmatched
    name); else `GRAPH-OUTCOME-UNMAPPED`;
  - start node (R-7): first `canStartWorkflow` child, else first child with no inbound edge, else first child;
  - dedupe `(source, port, target)` keeping first-seen order;
  - write `StructureFields["connections"]` (data-model §3 JSON shape, no `id`, `target.port` null) and
    `StructureFields["startNodeId"]` (only when the flowchart has children).
  Register it in `M/Elsa3MappingFeature.cs` and inject it into the normalizer.
- [X] T013 [US1] Verify `M/Conversion/Elsa3NativeStructureBuilder.cs` writes the Flowchart payload (slot
  `activities`, `variables`, then structure fields) with web JSON naming; adjust only if the payload does not
  deserialize as `FlowchartAuthoredStructure`.
- [X] T014 [P] [US1] Fixtures in `FX/`: `decision-tree.json`, `if-switch-done.json` (Elsa 3 If and Switch with *(Done; flowchart variables and duplicate connections are inline tests in `Elsa3FlowchartConversionTests`, added after Code Review round 1 found them unasserted.)*
  embedded branches connected on `Done`), `nested-single-child.json` (flowcharts inside an If branch, a For body and a
  Switch case, one child each, no connections), `flowchart-variables.json`, `duplicate-connections.json`. Shapes
  only; names and values invented (no JTest content).
- [X] T015 [US1] Tests in `MT/Elsa3FlowchartConversionTests.cs` for US1 scenarios 1–7: no pending code; node ids
  kept; ports mapped (FlowDecision `True`/`False` verbatim; If `Done` → `True` and `False`; Switch `Done` → each match
  and `Default`); duplicates merged; target ports and vertices ignored; explicit `startNodeId`; nested flowcharts
  converted in their parent's payload; flowchart variables scoped.
- [X] T016 [US1] In `M/ActivityMap/Rewrites/Elsa3HttpTriggerResponseModeRewrite.cs`, when the endpoint's parent is a
  flowchart, insert the synthesized `set` node into the parent's `Activities` and set the endpoint plan's
  `FollowedBy`. In the graph builder, move the endpoint's outbound edges onto the synthesized node and add
  `endpoint:Done → node` before port mapping (R-10, FR-013). In a Sequence nothing changes.
- [X] T017 [US1] Test in `MT/Elsa3HttpRequestProjectionTests.cs`: an endpoint in a flowchart capturing `Headers`
  converts; the projection node sits between the endpoint and its former successors; consumers bind as in spec 236.

**Checkpoint**: US1 independently testable; the MVP converts a decision-tree flowchart.

---

## Phase 4: User Story 2 — Forks and joins become fan-out and implicit joins (P1)

**Goal**: FlowFork, FlowJoin and Start disappear; accepted joins convert, the rest are refused.

**Independent test**: `fork-wait-all.json` converts with no fork or join node; refused shapes produce
`GRAPH-JOIN-UNSUPPORTED`.

- [X] T018 [US2] Graph builder: drop dead edges (R-4 steps 2–3): fork ports outside its literal branches (`Done`
  when none) → `DEAD-CONNECTION-REMOVED`; FlowFork `Branches` bound by JavaScript → `UNSUPPORTED-MODE`; literal list
  or Object expression holding a JSON string array accepted. Construct outbound ports other than `Done` (join, Start)
  → `GRAPH-OUTCOME-UNMAPPED`.
- [X] T019 [US2] Graph builder: join checks (R-6) on the Elsa 3 forward graph, before collapsing:
  - fan-out points (fork with ≥ 2 kept branch edges; any node with ≥ 2 edges on one Elsa 3 port);
  - concurrency of inbound edge pairs (reachability over forward edges, not through the join; entry points never
    concurrent);
  - `WaitAny` (default when `Mode` is unset) → accept iff no concurrent pair; `WaitAll` → accept iff all inbound edges
    pairwise concurrent from one fan-out point and every node on those paths is unconditional (one outgoing edge, on
    `Done`, Elsa 4 outcomes ⊆ {`Done`, `Break`}); `WaitAllActive` → accept; mode by expression → refuse. Mode literal
    read case-insensitively as name or integer (`WaitAll = 0`, `WaitAllActive = 1`, `WaitAny = 2`). Failures →
    `GRAPH-JOIN-UNSUPPORTED` on the join node with guidance from the contract.
- [X] T020 [US2] Graph builder: collapse constructs (R-5) to a fixed point in document order: fork, join, Start;
  remove each consumed construct from the flowchart plan's `Slots` and from the clone's `Activities` so lowering and
  mapping never see it. Start: its single `Done` target becomes the start candidate; several targets, or a fork
  chosen as the start with several branch targets → `GRAPH-START-AMBIGUOUS`.
- [X] T021 [P] [US2] Fixtures in `FX/`: `fork-wait-all.json`, `fork-wait-all-active.json`, *(Done; `fork-into-join-chain` and `start-fork-ambiguous` are inline cases in `Elsa3FlowchartDiagnosticsTests`.)*
  `exclusive-wait-any.json` (decision paths into a WaitAny join), `implicit-join.json` (three inbound, one through a
  decision), `fork-wait-any.json` (refused), `conditional-wait-all.json` (refused), `fork-into-join-chain.json`,
  `start-activity.json`, `start-fork-ambiguous.json`.
- [X] T022 [US2] Tests in `MT/Elsa3FlowchartForkJoinTests.cs` for US2 scenarios 1–10, asserting exact edge lists
  after collapsing and the codes for refused shapes.

**Checkpoint**: US1 and US2 pass independently.

---

## Phase 5: User Story 5 — Unsupported graph shapes are refused with guidance (P1)

**Goal**: every unsupported shape reports a stable code with location and guidance; nothing throws.

**Independent test**: one input per cause yields exactly its code.

- [X] T023 [US5] Graph builder: back edges (R-8): depth-first from the entry points (start candidate, then every
  other child with no inbound edge, in document order); a back edge converts only when it targets the start node and
  the start node has no other inbound edge; a back edge into a fork or join, or any other back edge →
  `GRAPH-LOOP-UNSUPPORTED`. Run before the join checks so joins see forward edges only.
- [X] T024 [US5] Theory tests in `MT/Elsa3FlowchartDiagnosticsTests.cs`: one inline definition per row of
  data-model §6, asserting code, node id, JSON path and that no exception escapes; several causes in one definition
  are all reported in (path, code) order. Update `MT/Elsa3ConversionDiagnosticsTests.cs` (remove the four
  `graph:*` → `GraphConversionPending` rows; add construct-outside-flowchart → `UNSUPPORTED-MODE`).
- [X] T025 [US5] Update `MT/Elsa3NodeConverterTests.cs` and `M/Conversion/Elsa3DefinitionConverter.cs`:
  `PrepareAsync` no longer defers flowcharts (`Deferred` empty for built-in shapes); `ConvertNodeAsync` on a fork,
  join or Start node fails with `UNSUPPORTED-MODE`; a flowchart child converts as before (R-9).

**Checkpoint**: SC-003 holds for every new code.

---

## Phase 6: User Story 3 — Several start triggers convert as one workflow (P1)

**Goal**: multi-trigger flowcharts convert 1:1. Conversion needs nothing from spec 238.

**Independent test**: `two-http-triggers.json` converts with both endpoints and paths; `startNodeId` is the first.

- [X] T026 [P] [US3] Fixtures in `FX/`: `two-http-triggers.json`, `http-and-cron.json`, *(Done with `two-http-triggers` and `two-triggers-into-wait-any`; an HTTP + Cron variant adds no graph case.)*
  `two-triggers-into-wait-any.json`.
- [X] T027 [US3] Tests in `MT/Elsa3FlowchartConversionTests.cs` for US3 scenarios 1–2 (conversion only).

---

## Phase 7: User Story 4 — Terminals inside flowcharts (P2)

**Goal**: Complete, End and Break end what they ended in Elsa 3.

**Independent test**: a reusable workflow whose root flowchart routes to `Complete(succeed)` and `Complete(fail)`
converts with both outcomes declared and the connections into them kept.

- [X] T028 [US4] Graph builder: edges leaving `Complete`, `End` or `Break` (by Elsa 3 type) are removed with
  `DEAD-CONNECTION-REMOVED`, one per connection (G-7).
- [X] T029 [US4] `M/ActivityMap/Rewrites/Elsa3TerminalPlacementRewrite.cs`: `End` converts only when its parent is
  the definition's root flowchart, for both workflow kinds; elsewhere `UNSUPPORTED-MODE` with guidance "route the
  flowchart's last activity to nothing instead" (R-11, amends 236 D-3). `Complete` unchanged.
- [X] T030 [P] [US4] Fixtures in `FX/`: `reusable-two-completes.json`, `plain-root-complete.json`, *(Done with `reusable-two-completes` and `terminal-outbound`; nested End is tested inline in `Elsa3ReusableTerminalTests`.)*
  `terminal-outbound.json`, `nested-end.json`.
- [X] T031 [US4] Tests in `MT/Elsa3FlowchartTerminalTests.cs` for US4 scenarios 1–6; update
  `MT/Elsa3ReusableTerminalTests.cs` where it asserts `End` → `Complete` inside a nested container.

---

## Phase 8: Validation proof and paths

- [X] T032 Tests in `MT/Elsa3FlowchartValidationTests.cs` (SC-004): each converted fixture from T014, T021 (accepted
  shapes) and T030 deserializes as `FlowchartAuthoredStructure`, compiles through the Flowchart structure handler,
  and passes `ValueFlowValidator` (start node set, endpoints known, no duplicate hits, no rejected loop).
- [X] T033 Test (H-1 halt point) in `MT/Elsa3FlowchartValidationTests.cs`: the T026 multi-trigger fixtures pass *(Written skipped, verified to fail without spec 238 (`ValueFlow/UnavailableProducer`); unskipped and passing after rebasing onto #2856 (6b3ad8c3d), 2026-10-16 — H-1 cleared.)*
  `ValueFlowValidator`. Mark it `[Fact(Skip = "Needs spec 238 (#2850): multi-entry value-flow validation.")]`.
- [X] T034 Collection path: add one flowchart case (reusable workflow with a flowchart root and a caller connecting on
  its `succeed`/`fail` ports) to `MT/ReusableActivityCollectionApplyTests.cs`; assert the caller's connections use
  the contract names and the reusable's graph holds its connections.
- [X] T035 [P] Determinism test (SC-005) in `MT/Elsa3FlowchartConversionTests.cs`: converting every fixture twice
  yields byte-identical state JSON.

---

## Phase 9: Polish and cross-cutting

- [X] T036 [P] `M/README.md`: replace the "Flowcharts report graph conversion pending" bullet with the flowchart rules
  (connections, ports, fork/join collapse, accepted and refused joins, loops, start node, terminals, multi-trigger
  depending on spec 238); keep the Nesting budget table (Flowchart 4 levels).
- [X] T037 [P] `M/EXTENSION_POINTS.md` and `M/Conversion/IElsa3NodeConverter.cs` docs: graph conversion is part of
  the conversion pipeline; `Deferred` holds no built-in shape; `ConvertNodeAsync` refuses graph constructs.
- [X] T038 [P] `specs/236-elsa3-activity-map/member-map.md`: the R8 rule text and the Flowchart, FlowFork, FlowJoin,
  Start, If/Switch `Done`, HttpEndpoint outcome rows point to spec 239; End rows note the FR-011 amendment.
- [X] T039 Build and test only the touched projects (`Extensions/Elsa3/src/Models`, `M/`, `MT/`, and the EF import
  tests that cover the collection path); run `quickstart.md`. Check `uptime` before trusting a timing failure.
- [X] T040 SC-006: run the 36 JTest definitions (outside the repo) through the importer with a scratch harness, never *(Done 2026-10-16: no `GRAPH-CONVERSION-PENDING`; graph refusals `branching-fork-parallel`, `branching-wait-any`, `branching-wait-all`; every file is still refused for spec 236/#2717 causes.)*
  committing them; record the per-file result table for the PR description. Expected graph refusals:
  `branching-fork-parallel`, `branching-wait-any`.
- [ ] T041 Re-check #2705 and open PRs for competing work before committing (AGENTS.md); commit locally. Ask Joey
  before pushing or opening the draft PR against `main` that says it stacks on #2805 and waits on #2850 (H-2).

**Halt H-1 (after T041)**: once spec 238 (#2856) is on `main`, rebase with `git rebase origin/main`, remove the skip
from T033, run it, and only then mark the PR ready (H-2).

---

## Dependencies and execution order

- Phase 1 → Phase 2 → stories. T004 before T005; T003 before T005 and T008; T007 before T011.
- US1 (Phase 3) blocks US2, US5, US3 and US4: they extend the graph builder created in T012.
- Within the builder the stage order is fixed (data-model §4): T023 (loops) runs before T019 (joins) at run time,
  although it is written later; write T019 against forward edges computed by a helper that T023 then fills in.
  Simplest: implement the forward-edge classification in T012 as "no cycles" and complete it in T023.
- US2 → US5 → US3 → US4 in this order (all edit `Elsa3FlowchartGraphBuilder.cs`, so no parallel story work).
- Phase 8 after all stories; Phase 9 last. T033 stays skipped until H-1.

## Parallel opportunities

- Phase 2: T002, T003 together; T009 and T010 after T005/T006.
- Fixture tasks (T014, T021, T026, T030) can be written any time after T001, in parallel with builder work.
- Phase 9: T036, T037, T038 together.

## Implementation strategy

1. **MVP**: Phases 1–3. A decision-tree flowchart, the most common Elsa 3 shape (34 of 36 corpus files use
   connections, 92 of 245 connections are FlowDecision ports), converts end to end.
2. Add US2 (forks and joins), then US5 (refusals), US3 (multi-trigger conversion), US4 (terminals).
3. Prove validation (Phase 8), document (Phase 9), run the corpus, then stop at H-1/H-2.
