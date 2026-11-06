# Implementation Plan: Flowchart Starts at the Trigger That Fired

**Branch**: `238-flowchart-trigger-start` | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/238-flowchart-trigger-start/spec.md`

## Summary

Make a Flowchart start at the run's trigger node when that node is one of its direct children, and make the
design-time value-flow check treat every possible entry node as a root.

**Runtime** (Flowchart module only):
- `FlowchartGraph` gains `SelectEntryNode(triggerNodeId)`. It returns the direct child with that id, else
  `SelectStartNode()`.
- `FlowchartExecutionEngine` calls it, with `context.TriggerNodeId`, at its three initial-state sites
  ([R1](research.md#r1-where-the-flowchart-chooses-its-first-node)).
- Loop-edge classification roots trigger-marked children right after the start node
  ([R2](research.md#r2-loop-edge-classification-for-branches-reached-only-from-a-trigger)).
- No new state, contracts or metadata. Loops, nesting and redrive follow from the existing workflow-wide, persisted
  trigger id.

**Design** (`ValueFlowValidator` only):
- Entry points are `startNodeId` plus direct children with no inbound connection
  ([R3](research.md#r3-design-time-entry-points)).
- Dominance uses a virtual root over all entries ([R4](research.md#r4-multi-entry-dominance)).
- Writes that only different entries reach are exclusive ([R5](research.md#r5-exclusive-writes-across-trigger-branches)).
- With one entry, every result equals today's.

**Placement (revision D8–D13)**:
- `WorkflowTriggerBindingExtractor.Evaluate` checks the start-path rule for every trigger that produces start bindings
  ([R8](research.md#r8-why-a-placement-rule-and-why-the-start-path-form), [R9](research.md#r9-where-the-check-runs)).
  Each composite on the root-to-trigger path is asked, through a new Runtime.Core strategy
  `IStructuralStartEntryProvider` keyed by structure kind, which child a run started by that trigger begins at
  ([R10](research.md#r10-the-start-entry-seam)).
  - The Flowchart answers: the trigger, when it has no inbound connection or is the start node; else its start node.
  - The Sequence answers: child 0.
  - An unknown kind refuses.
- A refusal is a `WorkflowTriggerPreflightException` with facet `Placement`, translated to a 400 with guidance
  ([R11](research.md#r11-reporting-a-refusal)). Publish fails before anything is saved; restore and import refuse
  through their existing channels ([R12](research.md#r12-publications-made-before-the-rule-d12)).
- The BPMN importer authors `CanStartWorkflow = false` on nested processes. Runtime unit tests whose Sequence roots
  carry no structure are updated ([R13](research.md#r13-census-what-changes-for-existing-tests-and-fixtures)).
- Studio's misplaced-trigger warning is flipped to match the rule ([R14](research.md#r14-studio)).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: The existing Flowchart activity module (`Elsa.Activities.Flowchart`), workflow runtime
core (`IRuntimeActivityExecutionContext`, `TriggerNodeMetadata`) and workflow design validations. No new package or
project reference in production code.

**Storage**: None changed. `FlowchartExecutionState` schema v1 is unchanged ([data-model](data-model.md)).

**Testing**:
- xUnit only, no FluentAssertions.
- Runtime and behavioural tests use `WorkflowExecutionHarness`; the end-to-end test uses `HttpEndpointHostFixture`.
- The validator is tested with `ValueFlowValidatorTests`' JSON Flowchart helper.

**Target Platform**: Cross-platform .NET server/runtime

**Project Type**: Modular libraries (Essentials domain tree)

**Performance Goals**:
- The runtime adds one dictionary lookup per Flowchart start.
- Validator cost stays linear in the number of entries times reachability, the same order as today's per-call
  `Dominates`.

**Constraints**:
- No Design reference from Runtime projects (§E2.2).
- No behaviour change for single-entry flowcharts.
- Edge classification stays a pure function of the graph, because it is cached.
- In-flight runs are unaffected, because only initial-state creation changes.

**Scale/Scope**:
- Done: two production files in Flowchart (`FlowchartGraph`, `FlowchartExecutionEngine`) and one in Design
  Validations (`ValueFlowValidator`); tests in five test projects; the Flowchart README.
- Revision, production code:
  - one new Runtime.Core contract;
  - the extractor and its exception facet;
  - two strategy implementations, in Flowchart and Sequence, with their feature registrations;
  - the publishing exception translator;
  - the BPMN importer;
  - Studio's `flowchartStartNode.ts`.
- Revision, tests in the Workflows.Runtime, Flowchart, Sequence, Publishing API, BPMN interchange, HTTP integration
  and Studio test projects.
- Revision, docs: `EXTENSION_POINTS.md` in Workflows.Runtime (contract and extractor usage), Flowchart and Sequence
  (implementations), and the Flowchart and Sequence READMEs.

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.*

- **Ratification status**: Both constitutions are ratified (framework v6.1.0, Elsa v6.0.2). §2.24 sanctioned
  patterns, §E2.8 and §E2.9 are draft or provisional; this plan introduces no pattern and does not rely on them.
- **§E2.2, Runtime takes no Design reference**: PASS. The runtime change is inside `Elsa.Activities.Flowchart`,
  which already references `Workflows.Runtime.Core` for `TriggerNodeMetadata` and `IRuntimeActivityExecutionContext`.
  The design change is inside `Elsa.Workflows.Design.Validations` and computes entries from the draft alone
  (FR-013).
- **§E2.2.3, deployment shapes**: PASS. Design-only, Runtime-only and combined hosts are untouched; there is no
  cross-context call.
- **§E2.6, executable always runs**: PASS.
  - Published artifacts are not recompiled. The runtime reads only data the artifact already carries: child ids,
    the trigger marker and the seed's trigger id.
  - A multi-trigger artifact that used to park or run the wrong branch now runs the fired branch. That is the
    intended fix.
- **§2.1, §2.4, §2.17, `.Core` and helpers**: PASS. No `.Core` change and no new helper. The start-selection logic
  lives on `FlowchartGraph`, next to `SelectStartNode`.
- **§2.5, §2.6.x, features, events, contracts**: N/A. No registration, event, contributor or replacement contract
  changes. The `ValueFlowValidator` constructor is unchanged.
- **§2.21.1, existing tests**: PASS by construction (FR-012, SC-004). Any validator test whose result changes
  because a disconnected child becomes an entry point is named with its reason in the PR
  ([R6](research.md#r6-risk-to-existing-tests)).
- **§2.22, docs and `EXTENSION_POINTS.md`**: The Flowchart README gains a short "Where a run starts" section.
  `EXTENSION_POINTS.md` is unchanged, because no extension surface changes.
- **§2.23.1, §2.23.2, tests**: Risky paths get behaviour tests (FR-014 to FR-016). No test asserts on message text.
- **§2.23.3, visibility**: `FlowchartGraph` is already public in the `Internal` namespace. The new method follows
  `SelectStartNode` and adds no new public type. The module has no tracked public API files.
- **§4.1, §4.2, SemVer**: Additive method plus a bug fix: a minor or patch class. No removed or renamed member.
- **Review checklist**: no item is triggered beyond the above.
- **Post-design re-check**: unchanged. Phase 1 added no contract, entity or dependency.

### Constitution Check: placement revision

- **§2.1, `.Core` contents**: PASS. `Elsa.Workflows.Runtime.Core` gains one interface (`IStructuralStartEntryProvider`)
  and one facet constant. The implementations live in the Flowchart and Sequence modules.
- **§2.24, sanctioned patterns**: PASS. This is the Strategy pattern, keyed by structure kind like
  `IActivityStructureHandler`, and consulted by the extractor like `IActivityTriggerStimulusProvider`. It is not a
  new pattern, so no §2.24.3 gate.
- **§2.6.x, events and contributors**: N/A. No event or fan-in. The provider answers a query (`…Provider`, not
  `…Contributor`).
- **§2.5, §2.23.1, registration**: Flowchart and Sequence register one provider each, as plain multi-registrations.
  The §2.23.1 composition test covers those. Duplicate-kind detection is logic, so it gets a behaviour test.
- **§2.22.1, `EXTENSION_POINTS.md`**: Updated in the same change. Workflows.Runtime lists the new implementable
  contract and the extractor's placement step. Flowchart and Sequence list their implementations as intra-domain
  known implementations.
- **§E2.2, Runtime takes no Design reference**: PASS. The contract and check are runtime-only. The extractor never
  learns Flowchart or Sequence semantics (spec 070).
- **§E2.6, executable always runs**: PASS.
  - The rule is a publication gate: may this artifact be activated. Saved executables, in-flight runs, bookmarks and
    already-active bindings are untouched (FR-025).
  - Restoring a retired publication with a misplaced trigger is refused (D12, accepted). That is a domain gate on
    activation, not a loss of executability.
  - Startup reconciliation of mounted, already-active artifacts must not regress. It is verified with a pinning test
    (R12); if no clean seam exists, it goes back to Joey.
- **§2.21.1, existing tests**: The runtime tests whose `Elsa.Sequence` roots carry no structure are changed, each
  with its reason in the PR. No other existing test is expected to change.
- **§2.23.2, §2.23 (no text assertions)**: Placement tests assert the facet, node ids and status code, never the
  message text.
- **§4.1, §4.2, SemVer**: Additive. There is a new interface, a new facet constant and a new extractor constructor
  overload. The existing constructor keeps its signature, so it is a minor change for Workflows.Runtime(.Core),
  Flowchart and Sequence.
- **Studio**: a client-only predicate change with unit tests. There is no backend contract change.

## Project Structure

### Documentation (this feature)

```text
specs/238-flowchart-trigger-start/
├── spec.md
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
└── tasks.md             # Phase 2 (/speckit-tasks)
```

There is no `contracts/` folder: the unit changes no external interface (API, payload, descriptor or event).

### Source Code

```text
Essentials/src/Activities/Flowchart/
├── Internal/FlowchartGraph.cs              # SelectEntryNode; trigger-first DFS roots in ComputeBackwardEdges
├── Internal/FlowchartExecutionEngine.cs    # three call sites use SelectEntryNode(context.TriggerNodeId)
└── README.md                               # "Where a run starts"

Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/
└── Validators/ValueFlowValidator.cs        # entry set, multi-entry Dominates, EntriesReaching, exclusive writes

Essentials/tests/
├── Activities/Flowchart/Tests/             # FlowchartTriggerStartTests (+ FlowchartRuntimeFixture trigger overload)
├── Activities/Behavioral/Tests/            # FlowchartTriggerStartBehaviorTests: HTTP+HTTP, HTTP+Cron
├── Activities/Runtime/Tests/               # FlowchartTriggerStartLoopTests: For loop body restarts at trigger
├── Activities/Http/IntegrationTests/       # MultiTriggerFlowchartEndToEndTests (+ Flowchart ref and feature)
└── Workflows/Design/Tests/                 # ValueFlowValidatorTests: multi-entry cases + single-entry regressions
```

Placement revision:

```text
Essentials/src/Workflows/Runtime/Core/
├── Contracts/IStructuralStartEntryProvider.cs      # new strategy contract
└── Exceptions/WorkflowTriggerPreflightException.cs # Placement facet constant (+ composite id/kind)
Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/
├── Services/Triggers/WorkflowTriggerBindingExtractor.cs  # start-path walk for Registered triggers
├── (feature registration of the provider set, if not resolved by IEnumerable injection)
└── EXTENSION_POINTS.md
Essentials/src/Activities/Flowchart/Internal/FlowchartStartEntryProvider.cs   (+ ActivitiesFlowchartFeature, EXTENSION_POINTS.md, README.md)
Essentials/src/Activities/Sequence/Internal/SequenceStartEntryProvider.cs     (+ ActivitiesSequenceFeature, EXTENSION_POINTS.md, README.md)
Essentials/src/Workflows/Publishing/Api/Endpoints/WorkflowPublishingExceptionTranslator.cs  # Placement → 400
Essentials/src/Activities/Bpmn/Interchange/Services/BpmnDocumentImporter.cs   # nested process CanStartWorkflow=false
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/flowchartStartNode.ts  # misplaced = inbound and not start

Essentials/tests/
├── Workflows/Runtime/Tests/                 # extractor placement matrix (fake providers), duplicate kind, structure-less roots updated
├── Workflows/Runtime/Scheduling/Tests/      # RecurringTriggerScheduleIndexerTests roots updated
├── Activities/Flowchart/Tests/              # FlowchartStartEntryProviderTests
├── Activities/Sequence/Tests/               # SequenceStartEntryProviderTests
├── Workflows/Publishing/Api/Tests/          # publish endpoint 400 + nothing saved
├── Activities/Bpmn/Interchange/Tests/       # nested process authored non-starting
└── Activities/Http/IntegrationTests/        # real-host refusal (Sequence child 1) and acceptance (child 0)
Studio tests next to flowchartStartNode.ts   # misplaced-trigger predicate
```

**Structure Decision**: Changes stay inside the two modules that own the behaviour: the Flowchart activity module
for runtime start selection, and Design Validations for value flow. The only new references are test-only: the HTTP
integration test project gains a Flowchart project reference, and its host gains the Flowchart feature.

## Complexity Tracking

No constitution violations to justify.


## Studio follow-up plan — #2859 (6 November 2026)

The coordinator reviewed this bounded implementation route before coding. This is the existing US5/D11 follow-up,
`none/free-flow`; the runtime decisions and completed tasks above stay intact. Source investigation and acceptance
record: [the specification follow-up](spec.md#studio-follow-up--2859-planned-6-november-2026), FR-031–FR-034.

### Scope and design

- `Activities.Design.Api/Services/ActivityAuthoringCatalogReader.cs`: retain the existing six-argument constructor
  and add a constructor accepting `IWellKnownTypeRegistry`. For persisted CLR consumer rows only, read the existing
  `ClrActivityDescriptor.TypeAlias` and reuse `ActivityTypeMetadata.IsTrigger` to project the existing view's
  `ExecutionType`. The six-argument/registry-absent path preserves catalog reads. Undefined, null, malformed and
  unresolved descriptor payloads preserve the prior stored value; unrelated registry/service failures are not hidden.
  This never changes rows, hashes, reconciliation, visibility or version identity and never enumerates live types to
  create catalog entries. Add only the direct existing `Activities.Runtime.Core` dependency and corresponding
  project-only locked dependency metadata; no new package, facet or extension contract.
- `Workflows.Publishing.Api/Endpoints/WorkflowPublishingLegacyProblem.cs` and the existing fault renderer: add
  omitted-when-null trigger/composite/structure fields to the internal problem model and a Placement-only builder
  with code `workflow.trigger.placement`. Preserve existing 400/detail/errors. Remove the superseded unstructured
  Placement translation. Generic/coded and non-Placement preflight failures retain their existing shapes/statuses.
- `flowchartStartNode.ts` and `draftValidation.ts`: optional descriptor/map inputs preserve current callers while
  known trigger descriptors support potential starts and declared start-switch defaults. Explicit false and
  dynamic values stay non-starting. Pass the current catalog from the editor and graph projection; preserve
  no-inbound entry reachability and existing start assignments. Always-start warning copy does not offer a
  nonexistent start switch. Catalog capability/default-based placement guidance is a Warning with conditional
  copy, because BPMN/custom providers can be intentionally non-starting. Only a structured backend Placement
  refusal adds an authoritative Error; a current server error suppresses its redundant local advisory for that node.
- `workflow-editor/useWorkflowOperations.ts` and `WorkflowEditor.tsx`: parse the typed Placement fields, report
  transient `ValidationError` entries through a callback, and combine them with existing validations. Bind to
  document ID plus captured authored snapshot; only current errors open the existing validation panel. This panel
  lives below the Designer canvas, not in a right-hand tab: select Designer and focus its accessible region. Close
  a refused review so that it cannot trap focus above the guidance; retain accurate banner information when a
  promoted version already exists. Clear on
  edits/switches and reject delayed responses. Preflight, refreshed review and direct publication catches share the
  same path; no persisted validation mutation or new panel.

### Constitution check

- Framework §2.1/§2.17 and Elsa §E2.2: Design.Api uses an existing Runtime.Core metadata contract/helper, with no
  Runtime→Design or API→API edge. The small native descriptor read stays local rather than coupling to Publishing's
  internal resolver. Its consumer/type-alias resolution follows that resolver's existing semantics.
- Deployment shapes: no mandatory live registry is added; registry-absent DI and old constructor behavior are tested.
  Pure Design-only hosts retain reads and no guessed local hint. Runtime-only composition and existing executables
  are untouched.
- Catalog ownership: all entries and availability remain from the persisted catalog/policy. Live registered metadata
  only enriches an existing row, as feature attribution already does; it creates or hides no entries.
- Compatibility/ADR 0080 D3: preserve the existing reader constructor; new problem fields are internal DTO additions,
  absent for unrelated failures. New helpers stay private/internal. No new public seam or feature identity.
- Framework §2.22: update the existing API catalogs' consumption/failure descriptions in the same unit; no new
  domain or glossary explanation. The root extension index stays unchanged.
- Framework §2.23/ADR 0080 D1: tests cover decisions, actual DI constructor selection, malformed/missing metadata,
  untouched persisted identity/hash, real structured HTTP/no-write refusal and current-snapshot UI handling. No
  source-text or documentation assertions. Existing valid tests remain unchanged unless a behavior is intentionally
  superseded and named.
- Draft/provisional material: no dependency on §2.24, Model X/version refresh or §E2.9. No same-version write-once
  exception is introduced.

### Validation and actual UI acceptance

Before source changes, the current helper probe confirms a renamed descriptor with `ExecutionType=Trigger` and no
start switch is neither recognised nor reported as misplaced. This is a lightweight source probe, not a suite run.
Focused client tests must fail on that behavior and on the original structured-error handling, then pass with the fix.
Server catalog and actual endpoint tests require the same causal proof on an admitted remote checkout; local heavy
execution is held by the owner.

Run whole touched `Activities.Design.Api.Tests`, `Workflows.Publishing.Api.Tests` and Workflows client suites, scoped
Studio checks and the required architecture Release/Debug restores, whole Architecture and maps. A real owned
Workbench+Studio journey covers catalog-based Timer/Cron warning and focus, optional false/no-inbound correctness,
a Sequence child 1 backend refusal into node validation, corrected child 0 publication, stale-response/recovery and
read-only. Retain source-bound desktop/narrow light/dark screenshots with ARIA/traces; independent reviewer opens
all affected experience evidence. Required CI and normal review/merge gates remain.

Every runtime, whole-suite and visual row for this follow-up is currently NOT RUN. Source freeze must provide exact
head/tree, commands, finite budgets, expected discovery inventory, original controls, artifact and owned cleanup
requirements before the coordinator admits a sequential remote phase. No local heavy fallback or shared store/host.
