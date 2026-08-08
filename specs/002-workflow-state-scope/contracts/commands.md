# Contracts — Commands

> Supersession note (2026-06-11): command effects that mutate workflow-level
> `State.Activities` or `State.ActivityConnections` are superseded by
> [070-workflow-root-activity-contract](../../070-workflow-root-activity-contract/spec.md).
> Draft mutation now operates over `WorkflowDefinitionState.RootActivity` and activity-owned
> composition state.

> **Supersession note (2026-07-05):** the command pipeline no longer publishes per-diff mutation events (event record declarations stand; diff engine unregistered) nor rebuilds `WorkflowDefinitionDraftValidation.Errors` — that entity is deleted and errors are derived state recomputed in-lock (spec.md FR-018/FR-021). The "rebuilds `WorkflowDefinitionDraftValidation.Errors`" step below is superseded. Reinstatable when a consumer exists.

19 commands in total. All command contracts live in `Elsa.Workflows.Design.Persistence.Core.Contracts`; implementations live in `Elsa.Workflows.Design.Persistence.EFCore` (per FR-019a). All mutation/lifecycle commands take the per-Draft distributed lock per FR-027 / FR-027a.

Naming convention: `I<Verb><Subject>InDraftCommand` (mutations) / `I<Verb><Subject>Command` (lifecycle).

---

## Mutation commands (FR-019)

Each mutation command:
1. Acquires lock `workflow-draft:{DraftId}` via `IDistributedLockProvider` (FR-027).
2. Loads the Draft + applies the mutation in memory.
3. Publishes the corresponding FR-018 granular event via `IDomainEventSender.Send`.
4. Publishes `DraftValidating` (FR-025).
5. Rebuilds `WorkflowDefinitionDraftValidation.Errors` from `event.Errors`.
6. Transactional flush.
7. Releases lock.

Handler exceptions never propagate to the caller per FR-027c + framework §2.6.1 default exception-shielding.

### Activities (graph)

#### `IAddActivityToDraftCommand`
**Payload:** `DraftId : string`, `Activity : ActivityNodeRecord` (the activity to add — exact record shape pin in tasks-stage).
**Publishes:** `ActivityAddedToDraft`.
**Effect:** appends to `WorkflowDefinitionState.Activities`; assigns `NodeId` if not pre-set; updates `WorkflowDefinitionDraftLayout.Records` with a default `DesignMetadataRecord` for placement (or expects layout to be supplied separately — plan-stage detail in tasks).

#### `IRemoveActivityFromDraftCommand`
**Payload:** `DraftId : string`, `NodeId : string`.
**Publishes:** `ActivityRemovedFromDraft`.
**Effect:** removes the activity from `State.Activities` AND removes all `ActivityConnections` referencing the NodeId (no dangling connections); removes the corresponding `DesignMetadataRecord` from the Draft's layout.

#### `IUpdateActivityPropertyInDraftCommand`
**Payload:** `DraftId : string`, `NodeId : string`, `PropertyPath : string`, `NewValue : object?`.
**Publishes:** `ActivityPropertyChangedInDraft`.
**Effect:** mutates the named property on the targeted `ActivityNode`. Property path may target configured-properties, argument state, etc. (tasks-stage pins the exact path resolution mechanism).

#### `IMoveActivityInDraftCommand`
**Payload:** `DraftId : string`, `NodeId : string`, `NewX : double`, `NewY : double`, `NewWidth : double?`, `NewHeight : double?`.
**Publishes:** `ActivityMovedInDraft` (the layout event per FR-018).
**Effect:** updates the matching `DesignMetadataRecord` in `WorkflowDefinitionDraftLayout.Records`. Does NOT touch `WorkflowDefinitionState` — layout is the sibling, not nested in State.

### Connections (graph)

#### `IAddConnectionToDraftCommand`
**Payload:** `DraftId : string`, `Connection : ActivityPortConnectionRecord`.
**Publishes:** `ConnectionAddedToDraft`.
**Effect:** appends to `WorkflowDefinitionState.ActivityConnections`.

#### `IRemoveConnectionFromDraftCommand`
**Payload:** `DraftId : string`, `ConnectionId : string` (or composite key — plan-stage decides).
**Publishes:** `ConnectionRemovedFromDraft`.
**Effect:** removes from `State.ActivityConnections`.

### Variables

#### `IDeclareVariableInDraftCommand`
**Payload:** `DraftId : string`, `Variable : VariableDefinitionRecord`.
**Publishes:** `VariableDeclaredInDraft`.
**Effect:** appends to `State.Variables`.

#### `IUpdateVariableInDraftCommand`
**Payload:** `DraftId : string`, `VariableReferenceKey : string`, `NewValue : VariableDefinitionRecord` (or property-path style — plan-stage decides).
**Publishes:** `VariableUpdatedInDraft`.
**Effect:** mutates the matching variable in `State.Variables`.

#### `IRemoveVariableFromDraftCommand`
**Payload:** `DraftId : string`, `VariableReferenceKey : string`.
**Publishes:** `VariableRemovedFromDraft`.
**Effect:** removes from `State.Variables`.

### Workflow inputs (workflow-definition-level declarations)

#### `IAddWorkflowInputToDraftCommand`
**Payload:** `DraftId : string`, `Input : InputDefinition` (carries `IsRequired` per FR-036).
**Publishes:** `WorkflowInputAddedToDraft`.
**Effect:** appends to `State.Inputs`.

#### `IUpdateWorkflowInputInDraftCommand`
**Payload:** `DraftId : string`, `InputReferenceKey : string`, `NewValue : InputDefinition`.
**Publishes:** `WorkflowInputUpdatedInDraft`.
**Effect:** mutates the matching input in `State.Inputs`.

#### `IRemoveWorkflowInputFromDraftCommand`
**Payload:** `DraftId : string`, `InputReferenceKey : string`.
**Publishes:** `WorkflowInputRemovedFromDraft`.
**Effect:** removes from `State.Inputs`.

### Workflow outputs (workflow-definition-level declarations)

#### `IAddWorkflowOutputToDraftCommand`
**Payload:** `DraftId : string`, `Output : OutputDefinition` (carries `IsRequired` per FR-036).
**Publishes:** `WorkflowOutputAddedToDraft`.
**Effect:** appends to `State.Outputs`.

#### `IUpdateWorkflowOutputInDraftCommand`
**Payload:** `DraftId : string`, `OutputReferenceKey : string`, `NewValue : OutputDefinition`.
**Publishes:** `WorkflowOutputUpdatedInDraft`.
**Effect:** mutates the matching output in `State.Outputs`.

#### `IRemoveWorkflowOutputFromDraftCommand`
**Payload:** `DraftId : string`, `OutputReferenceKey : string`.
**Publishes:** `WorkflowOutputRemovedFromDraft`.
**Effect:** removes from `State.Outputs`.

---

## Lifecycle commands (FR-019 + FR-028 + FR-029)

### `ICreateDraftCommand` (FR-019 lifecycle origination)
**Payload:** `WorkflowDefinitionId : string`, `InitialState : WorkflowDefinitionState?` (optional — defaults to an empty State).
**Publishes:** `DraftCreated`.
**Effect:** creates a new `WorkflowDefinitionDraft` with the supplied (or empty) State; creates a corresponding empty `WorkflowDefinitionDraftLayout`. The Draft is now ready for mutation commands. Lock acquired on the new DraftId after generation.

### `ICloneDraftFromVersionCommand` (FR-028)
**Payload:** `SourceVersionId : string`, `TargetDefinitionId : string`.
**Publishes:** `DraftClonedFromVersion`.
**Effect:** per data-model.md §4.3 lifecycle. Cardinality interaction with pre-existing Drafts of the same Definition — Unit D's call per FR-028.

### `IDiscardDraftCommand` (FR-029)
**Payload:** `DraftId : string`.
**Publishes:** `DraftDiscarded`.
**Effect:** per data-model.md §4.4 lifecycle. Idempotent — second discard on same DraftId is a no-op. NEVER touches any `WorkflowDefinitionVersion`.

---

## Provisional (Unit D's allocation)

### `IPromoteDraftToVersionCommand` (provisional name per R8)
**Owned by:** Unit D. Unit C references the name only (FR-024 + FR-027b).
**Payload:** TBD — Unit D.
**Publishes:** TBD (presumably `OnDraftPromotedToVersion`) — Unit D.
**Effect:** per data-model.md §4.2 lifecycle. Throws `DraftHasValidationErrorsException` if the validation row is non-empty.

---

## Cross-references

- All command contracts: `Elsa.Workflows.Design.Persistence.Core/Contracts/`.
- All command implementations: `Elsa.Workflows.Design.Persistence.EFCore/Commands/`.
- Per-command tests: `tests/Elsa.Workflows.Design.Tests/Unit/DraftMutationCommandTests/`.
