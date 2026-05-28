# Domain Events — `Elsa.Workflows.Design.Core`

Catalog per framework §2.22.1 (Unit C Phase-5 sub-rule) + Unit C FR-030. Lists every
`IDomainEvent` this `.Core` publishes. Heading convention per research item R4:
`### <EventClassName>` (exact class name, no decoration). The catalog-parity test in
`tests/Elsa.Workflows.Design.Tests/Unit/CatalogParityTests.cs` (Unit C FR-031) asserts
bidirectional alignment between this file and the assembly's `IDomainEvent` types.

**Pipeline behaviour for every event** (framework §2.6.1 + Unit C FR-027c):
- Default dispatcher: `Iterator → ExceptionShielding → Invoker`.
- Per-handler exceptions caught + logged + swallowed; dispatch always completes.
- Subscribers MUST NEVER break the publisher.

---

## Lifecycle (origination + cloning + disposal)

### OnDraftCreated

**Semantic.** A freshly-created `WorkflowDefinitionDraft` has been persisted.
**Payload.** `DraftId : string`, `WorkflowDefinitionId : string`.
**Publication site.** `ICreateDraftCommand` implementation, after the Draft row is persisted but before the command returns.
**Expected handlers.** event-sourcing subscriber (if enabled), audit features, telemetry.
**Ordering.** Fires once per Draft creation, after the per-Draft lock is released.

### OnDraftClonedFromVersion

**Semantic.** A new `WorkflowDefinitionDraft` was cloned from an existing `WorkflowDefinitionVersion` (deep copy of State + Layout sibling).
**Payload.** `NewDraftId : string`, `SourceVersionId : string`, `TargetDefinitionId : string`.
**Publication site.** `ICloneDraftFromVersionCommand` (Unit C FR-028), after the new Draft + its layout sibling are persisted.
**Expected handlers.** event-sourcing (records the cloning origin), audit, copy-trail telemetry.

### OnDraftDiscarded

**Semantic.** A `WorkflowDefinitionDraft` was atomically deleted along with its layout + validation siblings. Terminal entry on the Draft's event stream.
**Payload.** `DraftId : string`, `WorkflowDefinitionId : string`.
**Publication site.** `IDiscardDraftCommand` (Unit C FR-029).
**Expected handlers.** event-sourcing (terminal marker), audit.
**Idempotency.** A second Discard on the same Draft id is a no-op (the load returns null; the command exits cleanly without re-publishing).

---

## Activities (graph)

### OnActivityAddedToDraft

**Semantic.** An activity has been placed on the canvas.
**Payload.** `DraftId : string`, `Activity : ActivityNode` (with derived projections `NodeId`, `ActivityVersionId`).
**Publication site.** `IAddActivityToDraftCommand`, after snapshot mutation, before `OnDraftValidating`.
**Expected handlers.** event-sourcing subscriber. Validators react via `OnDraftValidating`, not this event directly.

### OnActivityRemovedFromDraft

**Semantic.** An activity has been removed from the canvas.
**Payload.** `DraftId : string`, `NodeId : string`.
**Publication site.** `IRemoveActivityFromDraftCommand`.

### OnActivityMovedInDraft

**Semantic.** Layout-position / size change for a placed activity. Folds into the same Draft event stream per Unit C FR-017 (single stream, single replay). The command mutates `WorkflowDefinitionDraftLayout.Records`, NOT `WorkflowDefinitionState`.
**Payload.** `DraftId : string`, `NodeId : string`, `NewX : double`, `NewY : double`, `NewWidth : double?`, `NewHeight : double?`.
**Publication site.** `IMoveActivityInDraftCommand`.

---

## Per-activity inputs (full CRUD)

Per-activity binding state — entries on `ActivityNode.Inputs` typed as `ArgumentState`. Distinct from `OnWorkflowInput*` (workflow-definition-level declarations). The CRUD trio mirrors workflow-input CRUD per Joey 2026-05-28 — collection mutations on the activity follow the same shape as collection mutations at workflow level. Generic per-activity property events were rejected in favour of these specialized CRUD events.

### OnActivityInputAddedToDraft

**Payload.** `DraftId : string`, `NodeId : string`, `Input : ArgumentState` (with derived `InputReferenceKey` projection).
**Publication site.** `IAddActivityInputToDraftCommand`.

### OnActivityInputUpdatedInDraft

**Payload.** `DraftId : string`, `NodeId : string`, `InputReferenceKey : string`, `OldValue : ArgumentState`, `NewValue : ArgumentState`. `InputReferenceKey` is the stable identity per Unit C FR-010.
**Publication site.** `IUpdateActivityInputInDraftCommand`.

### OnActivityInputRemovedFromDraft

**Payload.** `DraftId : string`, `NodeId : string`, `InputReferenceKey : string`.
**Publication site.** `IRemoveActivityInputFromDraftCommand`.

---

## Per-activity outputs (full CRUD)

Same rationale as Per-activity inputs above — full CRUD on the activity's `Outputs` bag, symmetric with workflow-output CRUD.

### OnActivityOutputAddedToDraft

**Payload.** `DraftId : string`, `NodeId : string`, `Output : ArgumentState` (with derived `OutputReferenceKey` projection).
**Publication site.** `IAddActivityOutputToDraftCommand`.

### OnActivityOutputUpdatedInDraft

**Payload.** `DraftId : string`, `NodeId : string`, `OutputReferenceKey : string`, `OldValue : ArgumentState`, `NewValue : ArgumentState`.
**Publication site.** `IUpdateActivityOutputInDraftCommand`.

### OnActivityOutputRemovedFromDraft

**Payload.** `DraftId : string`, `NodeId : string`, `OutputReferenceKey : string`.
**Publication site.** `IRemoveActivityOutputFromDraftCommand`.

---

## Connections (graph)

### OnConnectionAddedToDraft

**Semantic.** An edge has been added to the activity graph.
**Payload.** `DraftId : string`, `Connection : ActivityConnection` (source + target endpoints).
**Publication site.** `IAddConnectionToDraftCommand`.

### OnConnectionRemovedFromDraft

**Semantic.** An edge has been removed from the activity graph.
**Payload.** `DraftId : string`, `Connection : ActivityConnection` (the removed edge — connections have no separate id; source+target IS the identity).
**Publication site.** `IRemoveConnectionFromDraftCommand`.

---

## Variables (definition-bag, full CRUD)

### OnVariableDeclaredInDraft

**Semantic.** A workflow variable has been declared on the Draft.
**Payload.** `DraftId : string`, `Variable : VariableDefinition`.
**Publication site.** `IDeclareVariableInDraftCommand`.

### OnVariableUpdatedInDraft

**Semantic.** A workflow variable's definition has been updated.
**Payload.** `DraftId : string`, `VariableReferenceKey : string`, `OldValue : VariableDefinition`, `NewValue : VariableDefinition`. `VariableReferenceKey` is the stable identity per Unit C FR-010.
**Publication site.** `IUpdateVariableInDraftCommand`.

### OnVariableRemovedFromDraft

**Semantic.** A workflow variable has been removed.
**Payload.** `DraftId : string`, `VariableReferenceKey : string`.
**Publication site.** `IRemoveVariableFromDraftCommand`.

---

## Workflow inputs (definition-bag, full CRUD)

These are **workflow-definition-level** input declarations (`WorkflowDefinitionState.Inputs`), distinct from per-activity inputs (which mutate via `OnActivityPropertyChangedInDraft`). Workflow-level inputs get bound *as* activity-shaped inputs at compile time when the workflow is composed as an activity inside another workflow. The `WorkflowInput` prefix is deliberate per Unit C FR-018 naming discipline.

### OnWorkflowInputAddedToDraft

**Payload.** `DraftId : string`, `Input : InputDefinition`.
**Publication site.** `IAddWorkflowInputToDraftCommand`.

### OnWorkflowInputUpdatedInDraft

**Payload.** `DraftId : string`, `InputReferenceKey : string`, `OldValue : InputDefinition`, `NewValue : InputDefinition`.
**Publication site.** `IUpdateWorkflowInputInDraftCommand`.

### OnWorkflowInputRemovedFromDraft

**Payload.** `DraftId : string`, `InputReferenceKey : string`.
**Publication site.** `IRemoveWorkflowInputFromDraftCommand`.

---

## Workflow outputs (definition-bag, full CRUD)

Same `WorkflowOutput`-prefix rationale as the workflow-inputs section above.

### OnWorkflowOutputAddedToDraft

**Payload.** `DraftId : string`, `Output : OutputDefinition`.
**Publication site.** `IAddWorkflowOutputToDraftCommand`.

### OnWorkflowOutputUpdatedInDraft

**Payload.** `DraftId : string`, `OutputReferenceKey : string`, `OldValue : OutputDefinition`, `NewValue : OutputDefinition`.
**Publication site.** `IUpdateWorkflowOutputInDraftCommand`.

### OnWorkflowOutputRemovedFromDraft

**Payload.** `DraftId : string`, `OutputReferenceKey : string`.
**Publication site.** `IRemoveWorkflowOutputFromDraftCommand`.

---

## Cross-references

- Validation event lives in [`Elsa.Workflows.Design.Validations.Core/DOMAIN_EVENTS.md`](../Elsa.Workflows.Design.Validations.Core/DOMAIN_EVENTS.md): `OnDraftValidating` fires after every event listed here.
- Activity-feature validators (per Unit C FR-034) subscribe to `OnDraftValidating`, not to these granular events.
- Event-sourcing subscribers (Unit C FR-017's opt-in feature, deferred to a follow-on unit) subscribe to all 16 mutation events + the 2 lifecycle events listed here.
