# Contracts — Domain Events

> **Supersession note (2026-07-05):** the per-diff **mutation** events below remain *declared* as the tested contract but are **no longer published** — commands no longer compute or publish them and the diff engine is unregistered from DI, until an event-sourcing consumer (FR-017) exists. The "publishing command reads `event.Errors` … flushes to `WorkflowDefinitionDraftValidation`" step is superseded (entity deleted; errors derived, not persisted — spec.md FR-021). **Lifecycle** events (`DraftCreated`, `DraftDiscarded`, `DraftValidating`, `DraftValidated`) remain published. Reinstatable when a consumer exists.

19 events in total. All events are `sealed class` `IDomainEvent` per framework §2.6.1's intent-revealing-methods sub-rule. Events that gather handler contributions expose `Add*(...)` methods + `public IReadOnlyList<T>` read accessors (private backing list).

Pipeline behaviour for every event (framework §2.6.1 + clarify s2 Q1):
- Default dispatcher: `Iterator → ExceptionShielding → Invoker`.
- Per-handler exception caught + logged + swallowed; dispatch always completes.
- Subscribers can NEVER break the publisher.

---

## FR-018 mutation events (16) — `Elsa.Workflows.Design.Core`

### `DraftCreated`
**Lifecycle origination** event for a freshly-created Draft.
**Payload:** `DraftId : string`, `WorkflowDefinitionId : string`.
**Published by:** `ICreateDraftCommand` implementation, after the Draft is persisted but before the command returns.
**Expected handlers:** event-sourcing subscriber (if enabled), audit features, telemetry.
**Ordering:** fires once per Draft creation, after the lock is released by the create-command.

### `ActivityAddedToDraft`
**Mutation** event for an activity placed on the canvas.
**Payload:** `DraftId : string`, `NodeId : string`, `ActivityVersionId : string`, `Activity : IActivityNodeView` (read-only view of the placed node).
**Published by:** `IAddActivityToDraftCommand`.
**Expected handlers:** event-sourcing subscriber, baseline graph-integrity validators (via `DraftValidating` chain, not directly).
**Ordering:** after snapshot mutation, before `DraftValidating`.

### `ActivityRemovedFromDraft`
**Mutation** event for an activity removed from the canvas.
**Payload:** `DraftId : string`, `NodeId : string`.
**Published by:** `IRemoveActivityFromDraftCommand`.

### `ActivityPropertyChangedInDraft`
**Mutation** event for a configured-property or argument-state mutation.
**Payload:** `DraftId : string`, `NodeId : string`, `PropertyPath : string`, `OldValue : object?`, `NewValue : object?`.
**Published by:** `IUpdateActivityPropertyInDraftCommand`.

### `ActivityMovedInDraft` (layout event)
**Mutation** event for a layout-position change. Folds into the same Draft event stream per FR-017 (single stream, single replay).
**Payload:** `DraftId : string`, `NodeId : string`, `NewX : double`, `NewY : double`, `NewWidth : double?`, `NewHeight : double?`.
**Published by:** `IMoveActivityInDraftCommand`.

### `ConnectionAddedToDraft`
**Payload:** `DraftId : string`, `Connection : IActivityPortConnectionView`.
**Published by:** `IAddConnectionToDraftCommand`.

### `ConnectionRemovedFromDraft`
**Payload:** `DraftId : string`, `ConnectionId : string`.
**Published by:** `IRemoveConnectionFromDraftCommand`.

### `VariableDeclaredInDraft`
**Payload:** `DraftId : string`, `Variable : IVariableDefinitionView`.
**Published by:** `IDeclareVariableInDraftCommand`.

### `VariableUpdatedInDraft`
**Payload:** `DraftId : string`, `VariableReferenceKey : string`, `OldValue : IVariableDefinitionView`, `NewValue : IVariableDefinitionView`.
**Published by:** `IUpdateVariableInDraftCommand`.

### `VariableRemovedFromDraft`
**Payload:** `DraftId : string`, `VariableReferenceKey : string`.
**Published by:** `IRemoveVariableFromDraftCommand`.

### `WorkflowInputAddedToDraft`
**Payload:** `DraftId : string`, `Input : IInputDefinitionView`.
**Published by:** `IAddWorkflowInputToDraftCommand`.

### `WorkflowInputUpdatedInDraft`
**Payload:** `DraftId : string`, `InputReferenceKey : string`, `OldValue : IInputDefinitionView`, `NewValue : IInputDefinitionView`.
**Published by:** `IUpdateWorkflowInputInDraftCommand`.

### `WorkflowInputRemovedFromDraft`
**Payload:** `DraftId : string`, `InputReferenceKey : string`.
**Published by:** `IRemoveWorkflowInputFromDraftCommand`.

### `WorkflowOutputAddedToDraft`
**Payload:** `DraftId : string`, `Output : IOutputDefinitionView`.
**Published by:** `IAddWorkflowOutputToDraftCommand`.

### `WorkflowOutputUpdatedInDraft`
**Payload:** `DraftId : string`, `OutputReferenceKey : string`, `OldValue : IOutputDefinitionView`, `NewValue : IOutputDefinitionView`.
**Published by:** `IUpdateWorkflowOutputInDraftCommand`.

### `WorkflowOutputRemovedFromDraft`
**Payload:** `DraftId : string`, `OutputReferenceKey : string`.
**Published by:** `IRemoveWorkflowOutputFromDraftCommand`.

---

## FR-018a lifecycle events (2) — `Elsa.Workflows.Design.Core`

### `DraftClonedFromVersion`
**Lifecycle** event for a Draft cloned from a Version.
**Payload:** `NewDraftId : string`, `SourceVersionId : string`, `TargetDefinitionId : string`.
**Published by:** `ICloneDraftFromVersionCommand`.
**Expected handlers:** event-sourcing (records the cloning origin), audit, copy-trail telemetry.

### `DraftDiscarded`
**Lifecycle** event for a discarded Draft.
**Payload:** `DraftId : string`, `WorkflowDefinitionId : string`.
**Published by:** `IDiscardDraftCommand`.
**Expected handlers:** event-sourcing (terminal entry on the Draft's event stream), audit.

---

## FR-025 validation event (1) — `Elsa.Workflows.Design.Validations.Core`

### `DraftValidating`
**Coarse** event fired after every granular FR-018 event. Validators subscribe to this and contribute errors.

**Payload:**
- `Draft : IWorkflowDefinitionDraft` — the post-mutation Draft (from Workflows.Design.Core; cross-`.Core` reference per §2.1).
- Private backing: `_errors : List<ValidationError>`.
- **Contribution API:** `void AddValidationError(ValidationError error)`.
- **Read accessor:** `public IReadOnlyList<ValidationError> Errors` (non-mutating by type per framework §2.6.1).

**Class shape:**
```csharp
public sealed class DraftValidating(IWorkflowDefinitionDraft draft) : IDomainEvent
{
    private readonly List<ValidationError> _errors = new();
    public IWorkflowDefinitionDraft Draft { get; } = draft;
    public void AddValidationError(ValidationError error) => _errors.Add(error);
    public IReadOnlyList<ValidationError> Errors => _errors;
}
```

**Published by:** every FR-019 mutation command, after the granular FR-018 event.
**Expected handlers:**
- Baseline validators in `Elsa.Workflows.Design.Validations` (5 handlers per FR-033).
- Activity-feature-co-located validators per FR-034 (each activity feature ships its own handler that recognises its activity types).

**Ordering guarantees:**
- Fires after the granular FR-018 event for the same mutation.
- Validators run in DI-resolution order (no guaranteed inter-validator ordering — independent per framework §2.6.1).
- The publishing command reads `event.Errors` *after* the handler chain completes, then flushes to `WorkflowDefinitionDraftValidation`.

**Cross-references:** baseline validators in `Elsa.Workflows.Design.Validations` cite this event in their documentation; activity-feature validators document the same per framework §2.22.

---

## Catalog requirement (FR-030)

Each `.Core` ships a `DOMAIN_EVENTS.md` at its project root documenting every event with: class name (markdown heading `### <EventClassName>` per R4), one-line semantic, payload signature, publication site, expected handler audiences, ordering guarantees.

- `src/Elsa.Workflows.Design.Core/DOMAIN_EVENTS.md` — 18 events (16 FR-018 + 2 FR-018a).
- `src/Elsa.Workflows.Design.Validations.Core/DOMAIN_EVENTS.md` — 1 event (FR-025 `DraftValidating`).

The catalog parity test (FR-031) asserts bidirectional alignment per R4.
