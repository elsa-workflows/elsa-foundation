# Research: named embedded blocks

Source baseline: `f06480a783fab5f669a7af96ae35bc5be50ccf18`, 3 November 2026. This is a source inventory, not execution qualification.

## Composition choice

**Decision:** use existing native Sequence, Flowchart and BPMN process activities. A block is an authoring/refactoring experience over a named occurrence, not a new runtime primitive or private definition.

**Rationale:** each structure already owns its children and scheduling. Sequence propagates Break and accepts rescheduled faulted children; Flowchart owns entry, connections, policy/join state and fault handling. Ordinary container variables follow ancestor scopes until a closed reusable boundary. Existing scope navigation and run inspection can present the structure.

**Alternatives:** a thin Body wrapper adds a scheduling layer without solving data-result or interchange hazards. Compiler-flattened decorative grouping requires synthetic inspection and does not provide the requested real embedded-composite experience. Neither is selected.

Evidence:

- `Essentials/src/Activities/Sequence/Activities/Sequence.cs`: activity-owned structure, ordering, Break propagation and rescheduled-child support.
- `Essentials/src/Activities/Flowchart/Activities/Flowchart.cs` and `Internal/FlowchartExecutionEngine.cs`: normal completion is Done; child faults also fault the Flowchart composite.
- `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimeContainerScopeService.cs`: ancestor variable visibility and closed-boundary termination.

## Value and recovery proof obligations

**Finding:** stable node identities alone do not prove preservation of child results consumed outside a block. `CausalActivityResultResolver` follows scheduling and parent predecessor IDs; `StructuralParentEvaluationSupport.CompleteParentActivity` completes a parent without obviously adding its last completed child to that causal relation. A successor outside a block may therefore lose the producer's causal lineage.

**Decision:** the prototype must reproduce and compare outside-producer/inside-consumer and inside-producer/outside-consumer cases, including repeated occurrences. If confirmed, design and prove a narrow completion-causality bridge through existing runtime-owned records. Never substitute a newest-result fallback or weaken branch, iteration, ambiguity or closed-boundary checks. Production extraction cannot advertise the affected profile until it passes.

**S001 result:** the committed paired baseline reproduced inside-to-outside result loss. PR #3767 records the direct completed-child edge using existing runtime metadata, guarded by committed matching completions and exact parent ownership. Existing visibility/branch/iteration/ambiguity filtering remains intact. The [prototype receipts](evidence/prototype/README.md) include actual composed-host crossings, distinct occurrences, strict restart/resume, cancellation, supported native rescheduling and nearest control targets. This is bounded linear proof, not general fork/join visibility.

**Current-main reconciliation (12 November):** mandatory shared source analysis now runs in workflow publication and Activity Graph compilation. It rejects a plain child result read from outside its new structured parent, even with an explicit legacy producer scope. The historical submit/publish runtime proof did not pass through this newer admission path and remains historical; the causal bridge is not publication acceptance. T002 integrates through an explicit ordinary Set/ancestor-variable rewrite under the plan's typed-reference direction, without changing scope authority. The [data boundary contract](data-model.md#refactoring-preview) records its bounded profile and inverse behavior. Shared golden planner documents feed actual publication/runtime regressions; generating or comparing them is not a runtime pass. Member-result crossings remain pre-mutation refusals.

**Finding:** nesting Flowchart adds an ordinary composite incident on child failure; Sequence leaves its parent waiting on the faulted child. The additional inspection node is expected, but successful recovery and downstream behavior are unproved. Grouping must not add an independent retry/transaction/compensation policy.

**Decision:** prove waits/resumption, cancellation, child fault/recovery and nearest-loop Break/nearest-graph Complete behavior in paired baselines. Promotion refuses external Break targets and Complete targets that would change at the new reusable boundary.

## Studio inventory

**Finding:** shared graph adapters edit root and layout; Activity Definition implementation state has payload and layout but no typed node-presentation collection. Activity Graph duplication currently supplies empty presentation. Workflow naming already uses a typed sidecar frozen with executable-source presentation.

**Decision:** extend the existing presentation model through Activity Definition graph lifecycle, editor state, history and source inspection. Keep presentation outside executable material and behavior hashes, following Studio ADR 0012 on activity presentation and Foundation ADR 0050. Do not hide block names in opaque layout properties.

**Finding:** React Flow already supports multi-selection; Sequence's public selection contract is singular.

**S001 result:** the real Workflow Editor also exposed phantom history entries when saving normalized optional layout fields. PR #3767 makes an accepted live save rebase the current captured position without creating another author edit; queued duplicate acknowledgements are idempotent, stale responses cannot rebase newer edits, and Undo's autosave preserves Redo. Working-state signature/revision checks are unchanged. Equivalent Activity Definition lifecycle/history remains T001/T003 work.

**Decision:** add accessible contiguous/range selection to Sequence and consume actual canvas selections elsewhere. Shared pure transformations feed each resource host through its existing persistence controls. A graph edit never acquires workflow lifecycle responsibilities (Studio ADRs 0015 and 0018).

## BPMN inventory

**Current-main reconciliation (4 November):** upstream #3795 / commit `6bb6969e5` supplies the existing exact-version/portable-literal vendor binding profile. T004 will extend and qualify that boundary rather than introduce a second format. Nested-process DI, variable types/defaults, structured references and Block-specific no-loss/refusal behavior still require proof. See the [interchange contract](../../Essentials/src/Activities/Bpmn/Interchange/README.md#exact-task-bindings) and [T004 evidence](https://github.com/elsa-workflows/elsa-foundation/issues/3753#issuecomment-6617962817). The original inventory below describes the earlier baseline.

**Decision:** extract eligible native bound tasks/ordinary subprocesses into a subprocess bound to a new native BPMN process. Preserve task ActivityNodes, elements and internal flows; add plain internal start/end events. Refuse unsupported events, gateways, listeners, special boundaries, direct-model bindings and cross-lane selections.

**Finding:** `BpmnDocumentExporter` recursively emits a subprocess body only for an `elsa.bpmn.structure` child. Arbitrary native task versions/arguments are not fully serialized by the current XML exporter; `BpmnDocumentImporter` rematerializes work through its binder and may classify it as unbound.

**Decision:** T004 owns explicit binding-retention work for the supported extraction profile and existing vendor interchange boundary, with refusal diagnostics for unsupported exported bindings. A nested diagram/export is not round-trip proof. No dependency on optional bpmn-js adoption or an off-limits repository is introduced.

## Promotion inventory

**Finding:** `GraphActivityProvider.ProposeContractAsync` seeds schema-1 Done only. `GraphBodyReferenceValidator` rejects undeclared inputs, external producers and captures into undeclared graph variables; ambient script dependencies are not statically analyzed.

**Decision:** a separate shared Studio promotion planner analyzes explicit references, produces reviewed candidate graph/public-contract/call-site transformations and invokes existing server validation/publication. Supported expression parameters are rewritten structurally, never by editing arbitrary script strings. Refuse opaque dependencies, unsafe external writes and unpreservable read timing. Existing server validation remains authoritative.

**Lifecycle:** create a normal reusable draft only after review. Do not publish automatically. Keep the source block until an exact published target is selected and source/target snapshots are rechecked. Resume by selecting that target and recomputing the preview. Undo affects the source edit, not reusable-resource existence.

## Elsa 3 inventory

The current built-in mapping table has no FlowNode/Container entry. Resolver fallback checks configured aliases/catalog identities and otherwise returns `UnmappedType`. This work does not assert Elsa 3 Container import compatibility and does not expand importer scope. Evidence: `Extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs` and `Resolution/Elsa3ActivityResolver.cs`.

## Delivery structure

Native issue types are Program, Epic, Feature and Task; this organization's available native types do not include Spike, so S001 uses native Task plus `type:spike`. Bug issues will be created only for reproduced defects. No invented checkpoint issues. The native hierarchy and Project are scheduling surfaces; spec/plan/tasks remain canonical requirements/design and issue/PR comments hold receipts.

## Current-main integration: expression observation when adding variables

Introducing even a freshly named variable can change JavaScript ambient-snapshot selection: the runtime can match getter markers in inert strings/comments before executing source. A token blacklist is not an absence proof. Following Studio ADR 0002, Workflows now asks a language-neutral, revision-bound preservation contribution and refuses if absent. The JavaScript expression module owns a deliberately finite direct `args.<declared parameter>`/numeric-operand admission profile; it does not evaluate source or claim general JavaScript safety. T003 host composition and real-host behavioral qualification remain required.
