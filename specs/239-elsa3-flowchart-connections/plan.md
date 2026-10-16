# Implementation Plan: Elsa 3 Flowchart connections

**Branch**: `2705-elsa3-flowchart-connections` (stacked on `236-elsa3-activity-map`) | **Date**: 2026-10-16 |
**Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/239-elsa3-flowchart-connections/spec.md`; research in
[research.md](research.md).

## Summary

**What.** Elsa 3 flowcharts convert to native Elsa 4 Flowcharts with their connections and an explicit start node.
FlowFork, FlowJoin and Start disappear into fan-out edges, implicit joins and `startNodeId`. Shapes Elsa 4 cannot
express exactly are refused with five new stable error codes and one warning. `GRAPH-CONVERSION-PENDING` is no longer
raised.

**How.**
- The Flowchart map entry targets the Elsa 4 Flowchart activity like any container (R-1).
- FlowFork, FlowJoin and Start become *graph constructs*: resolved without the catalog, kept long enough for member
  rules and diagnostics, then consumed by the graph pass (R-2).
- After normalization, one graph pass per flowchart (innermost first) maps connections by outcome (R-3, R-4), checks
  joins, loops and the start node (R-6 to R-8), collapses constructs (R-5), and writes `connections` and
  `startNodeId` into the flowchart's structure fields. Lowering and mapping then run unchanged.
- Resolved activities carry their Elsa 4 outcomes, read from the catalog's `elsa.outcomes` facet (R-3).

**Dependency boundary.** Nothing here needs spec 238 to build or test, except the H-1 validation test for
multi-trigger fixtures (skipped with a reason until 238 is on `main`) and H-2 (ready for review and merge).

## Technical Context

**Language/Version**: C# / .NET (repo SDK, `global.json`), nullable enabled

**Primary Dependencies**: as spec 236 (`System.Text.Json`, `Elsa.Activities.Design.Persistence.Core`,
`Elsa.Workflows.Design.Core`). No new package or project reference in production code.

**Storage**: none.

**Testing**:
- xunit only (no FluentAssertions).
- `Extensions/Elsa3/tests/Mapping/Tests`: graph tests per user story and per code, against `RealActivityCatalog`
  (already scans the Flowchart and Graph assemblies).
- SC-004 needs the Flowchart structure handler and `ValueFlowValidator` in the Mapping test project: test-only
  references to `Elsa.Activities.Flowchart` and `Elsa.Workflows.Design.Validations` (check which already exist).
- Collection path: the existing EF import tests gain one flowchart case.

**Target Platform**: server library, Elsa 4 Design side. No Runtime project changes. No Essentials changes.

**Project Type**: library inside the modular solution.

**Performance Goals**: linear in nodes plus connections per flowchart, except the join checks, which are
O(fan-out points × edges) reachability walks per join; flowcharts are small (corpus maximum 22 nodes).

**Constraints**:
- Deterministic output (FR-015): edges kept in first-seen order, constructs collapsed in document order.
- No exception from input (FR-015).
- Nesting budget unchanged: a Flowchart adds 4 levels; connections sit beside `activities`.
- §E2.7: no Elsa 3 knowledge outside `Elsa3.*`.

**Scale/Scope**: 4 map entries change (Flowchart, FlowFork, FlowJoin, Start), 1 new target kind, 1 graph pass, 1
resolver extension (outcomes), 2 rewrite changes (End placement, HttpEndpoint projection placement), 6 new codes.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md`,
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 behind the import boundary | PASS | All changes in `Extensions/Elsa3`. No Essentials change |
| §E2.2 Runtime takes no Design reference | PASS | No Runtime project touched |
| §E2.6 published artifact always runs | PASS, improves | Converted flowcharts are native Elsa 4 Flowcharts with explicit start nodes; shapes the engine rejects at run time (multi-inbound loop targets, duplicate hits) are refused at conversion |
| §E2.8 catalog as source of truth | PASS | Outcomes are read from the catalog's `elsa.outcomes` facet, never from assemblies |
| §E2.9 (provisional) no generic child collection on `ActivityNode` | PASS | Children go into the Flowchart's native payload |
| §E5 versioning | PASS | Additive members only (`Elsa3ResolvedActivity.Outcomes`, `Elsa3ActivityExactReplacement.OutcomeNames`, new code constants) |
| §E6 naming R1–R8 | PASS | `Elsa3FlowchartGraphBuilder` (Builder), `Elsa3GraphConstructKind`, `Elsa3ActivityTarget.GraphConstruct` |
| §2.5 / §2.5.1 features | PASS | The graph builder is an internal collaborator of the normalizer, registered scoped in `Elsa3MappingFeature` |
| §2.6.2 replacement contracts | PASS | No new contract. `IElsa3NodeConverter` keeps its shape; its documentation changes (R-9) |
| §2.17 duplication beats dependency | PASS | The outcome-set rule for reusable targets duplicates spec 236's D-5/D-6 scan in the materializer (a few lines) rather than calling the converter |
| §2.22 / §2.22.1 docs and extension points | ACTION | Mapping `README.md`, `EXTENSION_POINTS.md` (`IElsa3NodeConverter` wording), 236's member-map rows pointing to spec 239, the 239 diagnostic contract |
| §2.23.1–§2.23.3 tests and visibility | PASS | Behaviour tests per story and per code; new types internal sealed |
| §2.23.5 exception boundaries | PASS | An unreadable outcomes facet becomes a diagnostic, as the structure facet does; nothing new escapes |
| §2.24 (draft) sanctioned patterns | PASS | Strategy (rewrites), a pass inside an existing pipeline, replacement contracts. No new pattern |
| §2.21.1 refactor preserves behaviour | ACTION | Tests asserting `GRAPH-CONVERSION-PENDING` for Flowchart, FlowFork, FlowJoin and Start (`Elsa3ConversionDiagnosticsTests`, `Elsa3ActivityResolverTests`, `Elsa3NodeConverterTests`) change, because the behaviour they describe is removed; the PR names them. `End` in a nested reusable flowchart changes from `Complete` to `UNSUPPORTED-MODE` (FR-011, amends 236 D-3) |

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/239-elsa3-flowchart-connections/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── diagnostic-codes.md
├── checklists/requirements.md
└── tasks.md                 # /speckit-tasks
```

### Source Code

```text
Extensions/Elsa3/src/Models/
└── Elsa3ActivityDiagnosticCodes.cs                 # + 5 errors, 1 warning; GraphConversionPending documented as no longer raised
Extensions/Elsa3/src/Mapping/
├── ActivityMap/
│   ├── Elsa3ActivityTarget.cs                      # + GraphConstruct(kind)
│   ├── Elsa3GraphConstructKind.cs                  # new: Fork, Join, Start
│   ├── Elsa3BuiltInActivityMap.Entries.cs          # Flowchart → Elsa 4 Flowchart; FlowFork/FlowJoin/Start → GraphConstruct
│   └── Rewrites/
│       ├── Elsa3TerminalPlacementRewrite.cs        # End only in the root flowchart (R-11)
│       └── Elsa3HttpTriggerResponseModeRewrite.cs  # records FollowedBy in a flowchart (R-10)
├── Resolution/
│   ├── Elsa3ResolvedActivity.cs                    # + Outcomes (static ports, dynamic rule)
│   └── Elsa3ActivityResolver.cs                    # reads elsa.outcomes; resolves GraphConstruct; intrinsics → Done
├── Conversion/
│   ├── Elsa3ConversionPlan.cs                      # Elsa3NodePlan: + GraphConstruct, FollowedBy
│   ├── Elsa3ActivityNormalizer.cs                  # runs the graph pass after the walk; construct outside a flowchart → UNSUPPORTED-MODE
│   ├── Elsa3FlowchartGraphBuilder.cs               # new: R-4 to R-8
│   ├── Elsa3DefinitionConverter.cs                 # ConvertNodeAsync refuses a construct (R-9)
│   └── IElsa3NodeConverter.cs                      # docs: flowcharts no longer deferred
├── Mappings/Elsa3ActivityToState.cs                # + OutcomeNames on Elsa3ActivityExactReplacement
├── Services/Elsa3ReusableActivityImportMaterializer.cs  # fills OutcomeNames (R-3)
├── Elsa3MappingFeature.cs                          # registers the graph builder
├── README.md, EXTENSION_POINTS.md
specs/236-elsa3-activity-map/member-map.md          # R8 rows point to spec 239's rules

Extensions/Elsa3/tests/Mapping/Tests/
├── Fixtures/Flowcharts/*.json                      # test-owned corpus shapes (SC-002)
├── Elsa3FlowchartConversionTests.cs                # US1, US3
├── Elsa3FlowchartForkJoinTests.cs                  # US2, join checks
├── Elsa3FlowchartTerminalTests.cs                  # US4
├── Elsa3FlowchartDiagnosticsTests.cs               # US5, theory per code (SC-003)
├── Elsa3FlowchartValidationTests.cs                # SC-004; multi-trigger cases skipped until 238 (H-1)
└── (existing tests updated: pending-code cases)
```

**Structure Decision**: all code in `Elsa3.Mapping` and `Elsa3.Models`; no new project. The graph pass lives in
`Conversion/` next to the normalizer that runs it.

## Implementation order (for /speckit-tasks)

1. **Foundations.** Code constants; `GraphConstruct` target and kind; `Elsa3ResolvedActivity.Outcomes` and the facet
   reader with tests (catalog activity, no facet → `Done`, intrinsic, dynamic outcomes); `OutcomeNames` on
   replacements, filled by the materializer.
2. **Map entries.** Flowchart → Elsa 4 Flowchart; FlowFork/FlowJoin/Start → constructs. Update the pending-code tests.
3. **Graph pass, US1.** Connection validation and outcome mapping (R-4), start node (R-7), dedupe, structure fields;
   nested flowcharts. MVP: a decision-tree flowchart converts.
4. **US2.** Fork and join collapse (R-5) and join checks (R-6); Start construct.
5. **US5.** Loops (R-8), invalid connections, unmapped outcomes, construct outside a flowchart, start ambiguity.
6. **US4.** Terminal outbound edges (G-7); End placement (R-11).
7. **US3.** Multi-trigger fixtures convert (no 238 needed); their validation test is written and skipped (H-1).
8. **HttpEndpoint projection in a flowchart** (R-10).
9. **Validation proof (SC-004)** for all non-multi-trigger fixtures; collection-path case; determinism (SC-005).
10. **Docs.** README, EXTENSION_POINTS, member-map pointers, diagnostic contract. Run the quickstart; run SC-006
    against the JTest checkout and record it for the PR.

**Halt H-1**: after step 10, enable the skipped multi-trigger validation test only once spec 238 is on `main` and this
branch has been rebased onto `main` (after #2805 is squash-merged:
`git rebase --onto origin/main origin/236-elsa3-activity-map 2705-elsa3-flowchart-connections`; `main` is never merged
in). **Halt H-2**: the PR stays draft until H-1 is cleared.

## Complexity Tracking

None.
