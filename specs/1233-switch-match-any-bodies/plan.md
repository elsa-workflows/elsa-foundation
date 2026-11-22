# Implementation Plan: Switch MatchAny case activities

**Branch**: `agent/codex/3052-s12-switch-match-any` | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

**Input**: `specs/1233-switch-match-any-bodies/spec.md`

## Summary

Extend Switch to schedule all selected populated cases and defer normal completion until all those direct children succeed. Reuse pinned condition inputs, parent-scoped durable execution records and structural continuations. Preserve ordered labels, Default/no-match behavior and the existing blocking child-fault policy. The proposed multi-body Break rule uses existing atomic live-child cancellation before propagating Break.

Remove only the corresponding converter/compile/runtime refusal, correct child concurrency facts, and expose the existing mode in Studio's Switch Slots experience. No new join mode, hidden Parallel, project or persistence entity is added. See [research](research.md), [data model](data-model.md) and [contract](contracts/switch.md).

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript / React 19 with the pinned repository toolchain.

**Primary Dependencies**: Existing Activities.ControlFlow, Workflows.Runtime.Core state-store/scheduling contracts, Design structure handlers, Elsa3.Mapping and Studio Workflows shared authoring controls. No new library dependency.

**Storage**: Existing immutable invocation inputs, parent-scoped activity execution records and checkpoint/cancellation machinery. No mutable duplicate selection document or migration.

**Testing**: Existing xUnit projects and WorkflowExecutionHarness; focused EF-backed restart coverage; Vitest/Playwright and a new composed Switch journey.

**Target Platform**: Supported .NET hosts and composed Workbench/Studio. If Windows Application Control blocks execution, retain the failure and use an established permitted Linux validation path, without disabling policy.

**Project Type**: Existing activity, import adapter and authoring UI extension.

**Performance Goals**: Linear selection/scheduling in case count; completion reads only this invocation's direct children, not all workflow executions. Independent waits do not serialize sibling starts.

**Constraints**: Immutable inputs, atomic checkpoints, default downstream join, one completion, faithful-or-refuse import, exact-head visual evidence. Use affected projects and the shared-machine build queue; inspect load before classifying timing failures.

**Scale/Scope**: Supported case lists in retained literal-match and current predicate schemas; no new hard case-count cap.

## Constitution Check

Both constitutions and `docs/contributing/review-checklist.md` were read. Framework is v6.1.0; the initiative's Elsa specialization is v6.5.0. Draft/provisional sections are not newly ratified by this work.

| Gate | Application |
|---|---|
| Framework §2.1/§2.6.4; Elsa §E2.2/.3 | Runtime uses Runtime.Core contracts only; Design owns compilation/facts; Elsa3 stays in the adapter. Preserve design-only, runtime-only and combined deployments. |
| Elsa §E2.6 | The executable pins body IDs, mode and typed inputs; runtime never loads drafts or Design source. |
| Elsa §E2.7 | Convert once to native Switch; no legacy runtime path, dual execution or legacy view model. |
| Framework §2.16/§2.17; ADR 0080 | Extend existing mechanisms; no meta-project, global registry, join mode or shared abstraction. |
| Framework §2.21/§2.23 | Risk-based behavior tests cover selection, lifecycle, persisted recovery and negative controls. Elsa declares no TDD cadence; this is an extension, not unresolved greenfield policy. Representative mutations must discriminate behavior. No text-assertion tests. |
| Framework §2.23.3/.5; Elsa §E6 | Private helpers internal sealed; existing store contracts; wrap infrastructure exceptions at feature boundaries and retain stable diagnostic IDs. |
| Framework §2.22 | Update Switch README/EXTENSION_POINTS and converter guidance with concurrency and input-consumption behavior. No new event/feature registration expected. |
| Framework §4; Elsa §E5 | Classify any public construction/API delta. Store injection may replace Switch's public parameterless constructor: if so, record the Major break in PublicAPI and PR evidence under pre-GA cleanup. Do not hand-maintain package versions or claim an unchanged API. |
| Elsa §E2.8/.9, provisional | Reuse current catalog/structure mechanisms without ratifying provisional rules. No new workflow-state member or generic child collection. Check descriptor/version/hash impact if changed. |
| Elsa §E7 | Existing test projects keep their tiers; no new project planned. |
| Studio acceptance | Define goal/reference/states below; inspect the composed host and exact-head downloadable captures independently. |

**Before research and after design:** No planned architectural violation/new ADR. Coordinator agreement explicitly covers the proposed multi-body Break rule. If implementation needs an agreed decision changed, amend through the initiative first.

## Project Structure

### Documentation

`specs/1233-switch-match-any-bodies/` contains spec, plan, research, data-model, contracts/switch, quickstart, tasks and requirements checklist. Review/acceptance receipts belong on the implementation PR. Spec 1234 is reserved only and is not drafted here.

### Source and tests

| Surface | Repository paths |
|---|---|
| Switch runtime/structure | `Essentials/src/Activities/ControlFlow/Switch/Activities/Switch.cs`, `Internal/SwitchNavigator.cs`, `Internal/SwitchStructureHandler.cs` |
| ControlFlow tests | `Essentials/tests/Activities/ControlFlow/Tests/Switch/`, `Tests/ChildTimeRelationTests.cs` |
| Publication/dataflow tests | `Essentials/tests/Workflows/Publishing/Api/Tests/OutcomeSelectionPublicationTests.cs`, `Essentials/tests/Workflows/Design/Tests/UnorderedVariableWriteAnalysisTests.cs` |
| Persisted recovery | New `Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/SwitchRecoveryEfTests.cs` in the existing project |
| Converter | `Extensions/Elsa3/src/Mapping/ActivityMap/Rewrites/Elsa3SwitchValueSynthesisRewrite.cs`, `src/Models/Elsa3ActivityDiagnosticCodes.cs`, mapping README |
| Converter tests | `Extensions/Elsa3/tests/Mapping/Tests/Elsa3NodeRewriteTests.cs`, `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/ExplicitOutcomesRunTests.cs` |
| Studio | `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/InspectorPanel.tsx`, `CollectionSlotEditor.tsx`, `../collectionSlots.ts`, existing condition/slot tests |
| Composed acceptance | New `Studio/tests/acceptance/switchMatchAnyJourney.ts`, `journeys.spec.ts`, finite inventory in `tools/ux-acceptance/run.py` |

**Structure decision:** Existing activity-owned slots and snapshot/store contracts suffice. Small private accounting helpers stay in ControlFlow; no generic join coordinator is extracted.

## Implementation design

1. Retain typed input materialization. Navigator exposes ordered selected cases and optional bodies while retaining structure, mode, slot and pinned-input checks. Remove populated-MatchAny refusal only with runtime support.
2. Schedule all selected populated children before deferring, with distinct branch provenance and this parent execution ID. Empty matches still suppress Default. A no-match run uses the existing Default path.
3. Inject the existing execution state-store contract. On child completion, reconstruct selection from pinned inputs, validate selected direct-child membership, and inspect only this parent's records. Count each selected body's successful completion once; defer while any is unfinished; return all labels in case order after all succeed.
4. Preserve the current no-child-fault-handler policy. Do not add fault-reschedule or input-rematerialization markers. Faulted, cancelled and superseded rows are non-success. Existing parent-work deduplication and atomic processed-child handling remain authoritative.
5. On a selected child's Break, stage live-child subtree cancellation and Complete(Break) together. Prove running waits cancelled and queued starts suppressed; enclosing loop, Finish and workflow cancellation retain their semantics.
6. Publish MatchAny case/case facts as Unordered and case/Default as MutuallyExclusive. Keep conservative sibling-result facts and check real unordered-write warnings and fixed input/result guards.
7. Convert bodies recursively while keeping ItemId/ConditionProperty together; preserve Default, diagnostics, paths and Done mappings. In `Extensions/Elsa3/src/Mapping/Conversion/Elsa3SwitchSafety.cs`, validate effective rewritten outcome flow after alias/replacement normalization and script lowering, before native structure output. For newly enabled MatchAny cases, refuse reachable or unproven native Break at the case boundary with UnsupportedMode. Cover Break activities, literal/computed script selections, container propagation and nested Switch/FlowSwitch labels; use the existing script outcome analysis where applicable, without a new cross-domain implementation dependency. A declared port is only a possibility set, not proof of reachability; preserve ordinary bodies with no Break-producing path and supported case-local consuming loops. Add the activity/script/nested-label refusal matrix and unreachable-port/case-local-loop positive controls.
8. Add a visual mode choice and accurate explanation within the existing Slots editor. Ordinary draft updates/shared controls preserve all nested content and wires.

The new body-containing native MatchAny shape also needs the bounded FR-012a Done-label check in shared Draft/compile/runtime rules. Imported body-containing MatchAny additionally validates the owning Switch's outward labels under FR-015b: reject Break/Done with localized UnsupportedMode and rename guidance, while preserving ordinary labels and existing modes. Include the enclosing-Sequence successor counterexample and ordinary-label positive control; do not stop at descendant/body analysis.

## Studio acceptance record

**User goal:** From Workflow definitions, create two true populated Switch cases, choose MatchAny, save/reopen/publish/run and observe both bodies before its successor. Change to MatchFirst and observe the first body alone.

**Reference:** Existing Switch Slots/case-condition editor, standard authoring controls and `Studio/docs/module-ui.md`; Workflow definitions is the composed reference screen. Retain inspector hierarchy and slot navigation.

**Observable states:** Default MatchFirst; MatchAny/two bodies; mixed empty/populated; no match/Default; empty list; duplicate/condition validation and repaired publication; save/reopen; undo/redo/reorder/rename; read-only execution view; keyboard/focus; desktop/narrow and light/dark variants.

**Coverage:** Register a finite journey/capture inventory. Desktop proves nested authoring, real publication/execution and both modes. Narrow proves mode/conditions/body navigation readability and focus; explicitly identify any graph-wiring exclusion. Preserve authored data through failed publication and recovery. Evidence identifies source/head, dirty state and every failure/skip/retry. Independent acceptance review opens exact-head screenshots/traces and inspects the composed experience.

**Exclusions:** New screens/themes, unrelated layouts and new permission policies. Existing read-only enforcement is covered because the mode control could bypass it.

## Validation strategy

Follow [quickstart.md](quickstart.md). Prove premature-completion, live-condition re-selection, foreign/duplicate children, lost Default, dropped imported bodies, unsound concurrency facts and missing Break cleanup are detected by meaningful negative controls. Record representative temporary mutations and restored-source results on the implementation PR.

Run affected ControlFlow, Workflows.Design, Publishing.Api, Elsa3.Mapping/import-integration projects, filtered EF recovery and relevant Studio unit/browser/composed checks. Rebuild touched/dependent projects only; do not run parallel solution builds. Inspect load before trusting timing failures. Preserve Windows policy failures and not-run platform coverage.

After every implementation review round, verify findings against source/reproductions, fix all surviving findings in new commits, reply with fixing SHAs and obtain a fresh independent round. Continue through minor findings until no verified findings remain. Keep exact-head local/visual evidence. CI waits are deferred by dispatch; no workflows/protection are changed and no CI pass is claimed.

## Approval and delivery sequence

The coordinator approved the prepared requirements and explicitly directed implementation in this same PR, overriding the earlier separate implementation branch/proposal-merge prerequisite for S-12. The separate main-sync helper remains the only active sync lane. Set the spec to Implemented only after tasks/acceptance complete. Final initiative integration, review and authorized merge gates remain explicit; this delivery instruction grants no merge permission.

## Complexity Tracking

No ratified-gate exception is planned. Provisional catalog/versioning text remains context, not newly ratified policy. A potential public constructor break is tracked rather than hidden behind a nullable service or global store.

## Implemented API classification

Switch construction changes from `Switch()` to `Switch(IActivityExecutionStateStore)`: this is a Major public C# source/binary break accepted under pre-GA cleanup. The owning ControlFlow project has no PublicAPI baseline files; no unrelated baseline or package version is fabricated. CLR activation uses existing DI. Activity version 4.1.0, descriptor shape and structure schema 2.0.0 stay unchanged: existing artifacts retain the same pinned contract shape, and new bodies already participate in structure/artifact hashing. No stored-state schema or new versioning policy is introduced.

## Import proof boundary

The normalized/lowered body analysis records actual setter selections separately from declared ports, preserving safe unused ports. It distinguishes co-selected Done from alternative singleton selections. Native completion can also escape through aliases or dynamic compilation. The existing scoped AST pass marks unshadowed native/global/compiler references, prototype constructors/reflection and unresolved computed prototype/callable access as unproven, refusing these only within the new body scope. Ordinary scripts, known data properties, numeric literal indexes and protected workflow-variable reads remain supported. No evaluator policy, native authoring capability, stored marker or standalone import mode changes.
