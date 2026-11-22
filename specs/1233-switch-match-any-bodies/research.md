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

The same mismatch is broader than the Break activity. The native Switch recognizes any child completion outcome named Break, while Elsa 3 Switch ignores child outcome names. `Elsa3ExpressionRewriter.cs` accepts literal `setOutcome('Break')` and lowers it to the native returned selection; computed selections and nested Switch/FlowSwitch labels can also surface that name without a BreakSignal. A name-only descendant check would admit changed behavior.

For newly enabled imported MatchAny cases, establish from the effective rewritten body/outcome flow that native Break cannot reach the owning Switch. Analyze Break activities, literal/computed script selections, container propagation and nested case-label boundaries after aliases/replacements are normalized. Refuse reachable or unproven boundary Break with existing `ELS3-ACTIVITY-UNSUPPORTED-MODE` at the originating script/activity/case path and actionable restructuring guidance. An outer loop does not waive the rule. Do not treat every declared Break port as reachable: ordinary If/Sequence bodies without a Break-producing path must remain convertible. A supported case-local loop consuming a genuine source Break may still convert under existing rules when no native Break escapes and its source behavior is preserved; it must leave sibling work alive. This does not assert that every script outcome named Break is a genuine source control signal. MatchFirst and existing exceptions remain unchanged; no new warning exception is added.

Required controls cover direct and nested Break activity, literal/computed script selections, propagated nested Switch/FlowSwitch label Break, aliases/replacements, an outer loop, declared-but-unreachable Break, and a safe case-local-loop sibling wait. Check Finish separately against existing terminal rules. Refusal is chosen over adding context-sensitive rewriting or a new outcome-provenance/runtime mode in this work.

**Owning selection matters too:** Elsa 3's structural Switch finishes with Done after all bodies and does not expose case labels as completion outcomes. Native Switch emits its own labels. An imported owning label Break therefore skips a following Sequence step (`Essentials/src/Activities/Sequence/Activities/Sequence.cs`) even when every case body returns ordinary Done and passes the body-boundary check. Refuse Break and Done labels in newly enabled body-containing imports with localized UnsupportedMode and rename guidance. Done also conflicts with S-3's prohibition on selecting Done with a branch (`ActivityOutcomeSelectionRules.cs`); the current compiler adds structure labels without preventing that combination. Draft/publication/runtime structure validation must reject Done in the newly supported native body-containing MatchAny shape. This does not redesign existing modes or prohibit native intentional Break outcomes. Add own-label Break/Done refusal, enclosing-Sequence successor and ordinary-label positive controls.

## R-5: Publication and dependency analysis

**Decision:** Remove the specific body refusal from compilation/navigation, retain structural validation, and report distinct MatchAny case bodies as Unordered while case/Default pairs stay MutuallyExclusive. Results between sibling cases remain of unknown availability. Completion-after-children remains true for normal success; Break cancels the remainder atomically.

**Evidence:** `SwitchStructureHandler.cs` currently says every distinct pair is MutuallyExclusive. `UnorderedVariableWriteAnalyzer.cs` consumes these facts; retaining exclusivity would suppress legitimate conflicting-write warnings. `ParallelStructureHandler.cs` supplies the existing Unordered shape. `SwitchCaseRules.cs`, the Draft validator, compiler and navigator already protect stable case identities, labels, condition contracts, slot consistency and activation-only binding rules.

**Alternatives considered:** A new warning policy is unnecessary because existing variable analysis already handles unordered children. Claiming case order is a happens-before relation would be unsound once bodies can suspend independently. Guaranteeing sibling result availability would require an ordered execution contract that is deliberately absent.

## R-6: Faithful conversion

**Decision:** Replace the blanket MatchAny-body refusal in `Extensions/Elsa3/src/Mapping/ActivityMap/Rewrites/Elsa3SwitchValueSynthesisRewrite.cs` with recursive child conversion plus R-4's effective boundary-outcome validation in the normalizer after rewrites and before native structure output. Preserve both ItemId and ConditionProperty on the resulting slot. Keep Default, supported literal mode forms and established per-case condition lowering.

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
