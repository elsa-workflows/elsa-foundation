# Research: Switch MatchAny case activities

## Evidence baseline

The design was checked against initiative tree `18dd3aefb98bb0d3c8916ad6b71be69a9fc3699f`. Paths below are repository-relative. The existing [S-3 contract](../1208-explicit-outcomes/spec.md) deliberately leaves case bodies refused until S-12; the outcome algebra and default downstream join remain its responsibility.

Elsa 3 reference tree: `be796b65e89a83941af78351582f988ed391d9f4` in `elsa-workflows/elsa-core`. The source was read at `main` and its blob identities were recorded: Switch `ff0ae71a4cc2765e11efbb090b395eaa3b62c6c2`, FlowSwitch `e2be725af456c0c94f2031b5307c10b3d3443915`.

- [Elsa 3 Switch](https://github.com/elsa-workflows/elsa-core/blob/be796b65e89a83941af78351582f988ed391d9f4/core/src/modules/Elsa.Workflows.Core/Activities/Switch.cs): evaluates the case list first, schedules each selected non-null body, uses Default only when no condition matches, and tracks scheduled child IDs until all complete. A matching empty case suppresses Default. This source also evaluates all cases in MatchFirst; S-12 does not change Elsa 4's already established native MatchFirst short circuit or claim to repair that separate conversion difference.
- [Elsa 3 FlowSwitch](https://github.com/elsa-workflows/elsa-core/blob/be796b65e89a83941af78351582f988ed391d9f4/core/src/modules/Elsa.Workflows.Core/Activities/Flowchart/Activities/FlowSwitch.cs): selects all matching labels or Default and completes immediately, because the bodies are outgoing graph nodes. It is the outcome-only regression control, not proof of nested body behavior.

## R-1: Selection and ordering

**Decision:** Reuse the existing pinned structure inputs to derive the ordered selected case set. Schedule every selected populated body in that order; do not promise body completion order.

**Evidence:** `Essentials/src/Activities/ControlFlow/Switch/Internal/SwitchStructureInputPolicy.cs` materializes every MatchAny condition before activation and stops only for MatchFirst. `SwitchNavigator.cs` reads typed Boolean snapshots or retained ordinal Value matching. Native cleared predicates are false. `Switch.cs` currently handles outcome-only MatchAny directly and defers for a single populated branch. Conditions are not re-evaluated in child callbacks: the runtime reconstructs their activation from `InputSnapshot` in `WorkflowParentActivityCompletionSchedulerWorkHandler.cs`.

**Alternatives considered:** Sequential body execution would let a suspended first body prevent a later matching body from starting and would not preserve the Elsa 3 scheduling shape. Re-evaluating predicates after children commit could change the selection and violate recorded-input semantics. Persisting a second selection copy is unnecessary while the original immutable snapshot is sufficient.

## R-2: All selected bodies and durable progress

**Decision:** Reconstruct completion from selected direct-child execution records scoped to the current parent invocation. Empty selected cases contribute labels but require no record. Switch completes with the full ordered selection only after every selected body has succeeded.

**Evidence:** `Parallel/Activities/Parallel.cs` demonstrates `IActivityExecutionStateStore.ListAllByParentAsync`, grouping owned direct children by executable node. Each completion callback is a reconstructed activation. `WorkflowParentActivityCompletionSchedulerWorkHandler.cs` ignores processed children and non-Running parents and commits the processed-child marker with a deferred parent evaluation. Child records are committed before parent completion work. Selection must be recomputed from the committed snapshot, not live variable values.

**Alternatives considered:** A mutable activation counter is lost on reconstruction. Counting all children can accept Default, an unselected body or another invocation. Copying Parallel's configurable threshold changes the user contract and introduces a join policy outside this scope. A new runtime-wide join component is unnecessary.

## R-3: Faults, cancellation and recovery

**Decision:** Keep Switch's current child-fault policy: the parent remains Running with the child's blocking incident and cannot route successfully. A suspended or individually cancelled selected body does not satisfy success. Parent/workflow cancellation uses existing subtree cancellation. Process recovery runs forward from committed inputs and child state.

**Evidence:** Switch declares no `IRuntimeActivityChildFaultHandler`; the corresponding parent-handler path retains the blocking incident. Parallel explicitly faults its join when its threshold becomes unreachable, which is a different policy. `RescheduleActivityAlterationHandler.cs` requires `IRuntimeAcceptsRescheduledFaultedChild` on a processed fault's immediate parent. Switch currently does not opt in. This feature preserves that refusal and does not borrow Parallel's reschedule capability. No condition-rematerialization marker is added.

**Alternatives considered:** Automatically faulting the Switch, swallowing the child's incident, treating cancellation as success, or newly admitting operator reschedule would change existing policy. These are not needed to add normal MatchAny bodies or recover persisted waits. Operator new-work admission is distinct from process recovery and delivery retry.

## R-4: Break with multiple selected bodies

**Decision proposed:** Preserve immediate outward `Break`, with cancellation of all remaining live selected child subtrees in the same completion evaluation. Ordinary labels are not emitted on that control exit. Final coordinator approval explicitly covers this rule.

**Evidence:** `Switch.cs` currently propagates a selected child's Break immediately instead of its label. The previously supported case has no sibling. `IRuntimeActivityExecutionContext.RequestLiveChildrenSubtreeCancellation` supports atomic cancellation with Complete and suppression of queued starts. `BreakPropagationExecutionTests.cs` already exercises this behavior for Flowchart. Merely returning Complete(Break) would leave running siblings alive.

**Alternatives considered:** Waiting for all bodies before forwarding Break would allow an unrelated wait to block an intended early exit. Completing immediately without cancellation can strand bookmarks and allow later sibling effects. Refusing all MatchAny bodies capable of Break would add a broad restriction that native Switch does not otherwise impose. No new join/cancellation mode or runtime contract is required for the proposed rule.

**Imported Break disposition:** Elsa 3's [Break](https://github.com/elsa-workflows/elsa-core/blob/be796b65e89a83941af78351582f988ed391d9f4/core/src/modules/Elsa.Workflows.Core/Activities/Break.cs) sends a BreakSignal; CodeActivity installs [AutoCompleteBehavior](https://github.com/elsa-workflows/elsa-core/blob/be796b65e89a83941af78351582f988ed391d9f4/core/src/modules/Elsa.Workflows.Core/Behaviors/AutoCompleteBehavior.cs), which then completes the child normally. Switch has no Break signal handler: it removes that child's scheduled ID and still waits for its other bodies. A root MatchAny Switch with Break plus a bookmarked sibling therefore retains the wait in Elsa 3, whereas native FR-010 cancels it. The current converter accepts Break and warns only under Parallel, so removing the body refusal alone is not faithful.

Refuse a Break that can escape a newly enabled imported MatchAny case: without a supported signal-consuming loop strictly between that Break and the owning case boundary, report the existing `ELS3-ACTIVITY-UNSUPPORTED-MODE` at the original Break/case path with control-flow restructuring guidance. An outer loop does not waive this bounded refusal. A supported loop wholly inside the case consumes its own Break, may still convert under existing rules, and must leave other selected cases alive. Aliases/replacements must not bypass semantic validation; unproven control boundaries remain refused. MatchFirst and existing conversion exceptions are not changed. This is faithful-or-refuse, not a new behavior-changing warning. Add both the refused root counterexample and the accepted case-local-loop sibling-wait control; check Finish with existing terminal rules separately.

## R-5: Publication and dependency analysis

**Decision:** Remove the specific body refusal from compilation/navigation, retain structural validation, and report distinct MatchAny case bodies as Unordered while case/Default pairs stay MutuallyExclusive. Results between sibling cases remain of unknown availability. Completion-after-children remains true for normal success; Break cancels the remainder atomically.

**Evidence:** `SwitchStructureHandler.cs` currently says every distinct pair is MutuallyExclusive. `UnorderedVariableWriteAnalyzer.cs` consumes these facts; retaining exclusivity would suppress legitimate conflicting-write warnings. `ParallelStructureHandler.cs` supplies the existing Unordered shape. `SwitchCaseRules.cs`, the Draft validator, compiler and navigator already protect stable case identities, labels, condition contracts, slot consistency and activation-only binding rules.

**Alternatives considered:** A new warning policy is unnecessary because existing variable analysis already handles unordered children. Claiming case order is a happens-before relation would be unsound once bodies can suspend independently. Guaranteeing sibling result availability would require an ordered execution contract that is deliberately absent.

## R-6: Faithful conversion

**Decision:** Replace the blanket MatchAny-body refusal in `Extensions/Elsa3/src/Mapping/ActivityMap/Rewrites/Elsa3SwitchValueSynthesisRewrite.cs` with recursive child conversion plus R-4's explicit escaping-Break guard in the normalizer's control-placement validation. Preserve both ItemId and ConditionProperty on the resulting slot. Keep Default, supported literal mode forms and established per-case condition lowering.

**Evidence:** `Elsa3NativeStructureBuilder.cs` already writes activity, stable case identity and condition together. `Elsa3BuiltInActivityMap.Entries.cs` maps Elsa 3 Switch Done onto case labels and Default. `Elsa3NodeRewriteTests.cs` currently tests the intentional refusal. `ExplicitOutcomesRunTests.cs` currently proves FlowSwitch only. `UnsupportedMode` is shared with dynamic/unknown mode and other unsupported constructs and must not be deleted.

**Alternatives considered:** Flattening bodies into outgoing graph nodes changes structural ownership, scope, completion timing and authoring. Dropping a body or weakening unsupported-child diagnostics is not faithful conversion. Synthesizing a hidden Parallel adds a concept and complicates stable identity without necessity.

## R-7: Studio authoring

**Decision:** Expose a Switch-owned visual mode choice in the existing Slots experience and correct its help text. Retain the current child slots and outcome ports.

**Evidence:** `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/CollectionSlotEditor.tsx` always describes the first match. `InspectorPanel.tsx` hosts it. Existing `collectionSlots.ts` mutations preserve payload members and stable case identities; `switchOutcomePorts.ts` handles labels and wire reconciliation independent of mode. There is no visual mode control. No composed acceptance journey currently qualifies Switch mode/body authoring.

**Alternatives considered:** Requiring Code view would leave the capability inaccessible in the intended authoring flow. A separate Switch editor or graph mode would duplicate existing collection-slot navigation, expressions and controls. Mode changes need no new outcome-port concept.

## R-8: Validation and boundaries

**Decision:** Use affected project tests, targeted failure controls and a new composed Switch journey. Keep exact-head downloadable visual evidence and independent UX inspection. Runtime builds/tests begin only after spec agreement and merge, on a separate branch.

Existing solution projects provide the test seams; no project, package, provider, external service, expression engine or new generic runtime state is planned. CI is a separate delivery gate; the session's explicit dispatch defers waits and does not disable or claim CI. Windows policy failures must be recorded; use an established permitted Linux execution path where necessary.

The ADR alternatives applied here remain those in ADR 0045, ADR 0090 and ADR 0092. The proposed activity-level Break rule is surfaced for spec agreement; any review finding that contradicts an agreed ADR requires a separate amendment rather than silent divergence.
