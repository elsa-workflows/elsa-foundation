# Events — `Elsa.Workflows.Design.Core`

Catalog per framework §2.22.1. Every event the `Workflows.Design.Core` `.Core` library publishes is documented here, grouped by category.

**Domain events** (§2.6.1) — contribution mechanism. Publisher awaits the dispatch and reads handler contributions back. *None in this `.Core` after the 2026-05-29 lifecycle/domain split — the FR-018/FR-018a events are all notifications-shaped. The validation domain event (`OnDraftValidating`) lives in [`Elsa.Workflows.Design.Validations.Core/EVENTS.md`](../Elsa.Workflows.Design.Validations.Core/EVENTS.md).*

**Lifecycle events** (§2.6.6) — state-transition notifications. Publisher fires; subscribers (audit, event-sourcing stream, UI push, telemetry) observe but don't feed back. Default dispatch strategy: `NotificationStrategy.Background` — the publisher returns before subscribers run.

Heading convention per research item R4: `### <EventClassName>`. The catalog-parity test in `tests/Elsa.Workflows.Design.Tests/Unit/CatalogParityTests.cs` (Unit C FR-031) asserts bidirectional alignment between the `### On…` headings here and the assembly's published-event types (both `IDomainEvent` and `ILifecycleEvent`).

---

## Lifecycle events (`ILifecycleEvent`)

**Pipeline behaviour for every event in this section.**

- Sender: `ILifecycleEventSender.SendAsync` (delegates to `INotificationSender` with the Background strategy by default).
- Dispatch: queued on `INotificationsChannel`; the `BackgroundEventPublisher` hosted task drains the channel and runs each subscriber via `SequentialProcessingStrategy`. The publisher's call returns before subscribers run.
- Subscriber exception isolation: caught + logged + swallowed; one flaky subscriber cannot stall the queue.
- Order: FIFO at enqueue, preserved at dispatch.
- Crash semantics: queued events are in-memory; a process crash drops them. Subscribers that need durability persist their own log.

**Lifecycle — origination + cloning + disposal.**

### OnDraftCreated

**Semantic.** A freshly-created `WorkflowDefinitionDraft` has been persisted.
**Payload.** `DraftId : string`, `WorkflowDefinitionId : string`.
**Publication site.** `ICreateDraftCommand` implementation, after `SaveChangesAsync` + lock release.
**Expected handlers.** Event-sourcing subscriber (records the Draft's stream origination); audit; telemetry.

### OnDraftClonedFromVersion

**Semantic.** A new `WorkflowDefinitionDraft` was cloned from an existing `WorkflowDefinitionVersion` (deep copy of State + Layout sibling).
**Payload.** `NewDraftId : string`, `SourceVersionId : string`, `TargetDefinitionId : string`.
**Publication site.** `ICloneDraftFromVersionCommand` (Unit C FR-028), after the new Draft + its layout sibling are persisted.
**Expected handlers.** Event-sourcing (records the cloning origin); audit; copy-trail telemetry.

### OnDraftDiscarded

**Semantic.** A `WorkflowDefinitionDraft` was atomically deleted along with its layout + validation siblings. Terminal entry on the Draft's event stream.
**Payload.** `DraftId : string`, `WorkflowDefinitionId : string`.
**Publication site.** `IDiscardDraftCommand` (Unit C FR-029).
**Expected handlers.** Event-sourcing (terminal marker); audit.
**Idempotency.** A second Discard on the same Draft id is a no-op (the load returns null; the command exits cleanly without re-publishing).

**Activities — graph.**

### OnActivityAddedToDraft

**Semantic.** An activity has been placed on the canvas.
**Payload.** `DraftId : string`, `Activity : ActivityNode` (with derived `NodeId`, `ActivityVersionId` projections).
**Publication site.** `IAddActivityToDraftCommand`, after `SaveChangesAsync` + lock release.
**Expected handlers.** Event-sourcing subscriber.

### OnActivityRemovedFromDraft

**Semantic.** An activity has been removed from the canvas.
**Payload.** `DraftId : string`, `NodeId : string`.
**Publication site.** `IRemoveActivityFromDraftCommand`.

### OnActivityMovedInDraft

**Semantic.** Layout-position / size change for a placed activity. Mutates `WorkflowDefinitionDraftLayout.Records`; State is layout-free per Elsa §E2.9.2.
**Payload.** `DraftId : string`, `NodeId : string`, `NewX : double`, `NewY : double`, `NewWidth : double?`, `NewHeight : double?`.
**Publication site.** `IMoveActivityInDraftCommand`.

**Per-activity inputs — full CRUD.** Per-activity binding state — entries on `ActivityNode.Inputs` typed as `ArgumentState`. Distinct from workflow-input events (which mutate workflow-definition-level input declarations).

### OnActivityInputAddedToDraft

**Payload.** `DraftId : string`, `NodeId : string`, `Input : ArgumentState` (with derived `InputReferenceKey` projection).
**Publication site.** `IAddActivityInputToDraftCommand`.

### OnActivityInputUpdatedInDraft

**Payload.** `DraftId : string`, `NodeId : string`, `InputReferenceKey : string`, `OldValue : ArgumentState`, `NewValue : ArgumentState`.
**Publication site.** `IUpdateActivityInputInDraftCommand`.

### OnActivityInputRemovedFromDraft

**Payload.** `DraftId : string`, `NodeId : string`, `InputReferenceKey : string`.
**Publication site.** `IRemoveActivityInputFromDraftCommand`.

**Per-activity outputs — full CRUD.** Same rationale as Per-activity inputs above — full CRUD on the activity's `Outputs` bag.

### OnActivityOutputAddedToDraft

**Payload.** `DraftId : string`, `NodeId : string`, `Output : ArgumentState`.
**Publication site.** `IAddActivityOutputToDraftCommand`.

### OnActivityOutputUpdatedInDraft

**Payload.** `DraftId : string`, `NodeId : string`, `OutputReferenceKey : string`, `OldValue : ArgumentState`, `NewValue : ArgumentState`.
**Publication site.** `IUpdateActivityOutputInDraftCommand`.

### OnActivityOutputRemovedFromDraft

**Payload.** `DraftId : string`, `NodeId : string`, `OutputReferenceKey : string`.
**Publication site.** `IRemoveActivityOutputFromDraftCommand`.

**Connections — graph.**

### OnConnectionAddedToDraft

**Semantic.** An edge has been added to the activity graph.
**Payload.** `DraftId : string`, `Connection : ActivityConnection`.
**Publication site.** `IAddConnectionToDraftCommand`.

### OnConnectionRemovedFromDraft

**Semantic.** An edge has been removed from the activity graph.
**Payload.** `DraftId : string`, `Connection : ActivityConnection`. Identity is the source+target pair; connections carry no separate id.
**Publication site.** `IRemoveConnectionFromDraftCommand`.

**Variables — full CRUD.**

### OnVariableDeclaredInDraft

**Payload.** `DraftId : string`, `Variable : VariableDefinition`.
**Publication site.** `IDeclareVariableInDraftCommand`.

### OnVariableUpdatedInDraft

**Payload.** `DraftId : string`, `VariableReferenceKey : string`, `OldValue : VariableDefinition`, `NewValue : VariableDefinition`.
**Publication site.** `IUpdateVariableInDraftCommand`.

### OnVariableRemovedFromDraft

**Payload.** `DraftId : string`, `VariableReferenceKey : string`.
**Publication site.** `IRemoveVariableFromDraftCommand`.

**Workflow inputs — full CRUD.** Workflow-definition-level input declarations (`WorkflowDefinitionState.Inputs`), distinct from per-activity inputs.

### OnWorkflowInputAddedToDraft

**Payload.** `DraftId : string`, `Input : InputDefinition`.
**Publication site.** `IAddWorkflowInputToDraftCommand`.

### OnWorkflowInputUpdatedInDraft

**Payload.** `DraftId : string`, `InputReferenceKey : string`, `OldValue : InputDefinition`, `NewValue : InputDefinition`.
**Publication site.** `IUpdateWorkflowInputInDraftCommand`.

### OnWorkflowInputRemovedFromDraft

**Payload.** `DraftId : string`, `InputReferenceKey : string`.
**Publication site.** `IRemoveWorkflowInputFromDraftCommand`.

**Workflow outputs — full CRUD.** Workflow-definition-level output declarations.

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

- The validation domain event + lifecycle event for the same transition live in [`Elsa.Workflows.Design.Validations.Core/EVENTS.md`](../Elsa.Workflows.Design.Validations.Core/EVENTS.md): `OnDraftValidating` (domain event, the gate) and `OnDraftValidated` (lifecycle event, the outcome).
- Event-sourcing subscribers (Unit C FR-017's opt-in feature, deferred to a follow-on unit) subscribe to every lifecycle event listed above to materialise the Draft's event stream.
- Audit / telemetry subscribers may subscribe selectively per category.
