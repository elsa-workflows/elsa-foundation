# Extension points — `Elsa.Workflows.Design.Core`

The per-domain catalog (framework §2.22.1) of everything you can implement or override in the Workflows.Design domain, plus the events it publishes. Three sections:

- **Overridable contracts** — interfaces with a default implementation you can *replace*. Bring one implementation and the built-in one steps aside. This is the *override* axis ("I'll bring my own commands but keep the built-in queries").
- **Implementable contributor interfaces** — *add-don't-replace* seams aggregated by a single handler (framework §2.6.1, §2.24.2). This is the *extend* axis.
- **Events** — the FR-018/FR-018a mutation + lifecycle events this `.Core` publishes.

> **Domain spans several projects.** The Workflows.Design *domain model* lives in this `.Core`, but its behavioural seams are split across sibling projects per the three-layer rule (framework §2.1): the **commands** live in `Elsa.Workflows.Design.Persistence.Core` and their EF Core implementations + the diff engine live in `Elsa.Workflows.Design.Persistence.EFCore`. Those seams are catalogued here (with their owning project noted) because they publish *this* `.Core`'s events; their persistence-lifecycle seams (`OnEntitySaving`/`OnEntityLoading`) are catalogued in [`Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Persistence.EFCore/EXTENSION_POINTS.md).

This is the repo-wide [`EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md) index's entry for this domain; the index links here for detail.

---

## Overridable contracts

| Contract | Owning project | Default impl | Override when |
|---|---|---|---|
| `IWorkflowDefinitionLookup` | `Elsa.Workflows.Design.Core` | `WorkflowDefinitionLookup` (`.Persistence.EFCore`) | You want a different read strategy for definitions/versions (caching, projection store) while keeping the write path. The *keep-queries-swap-commands* counterpart. |
| `IWorkflowDesignContextFactory` | `Elsa.Workflows.Design.Core` | *(persistence-supplied)* | You need a custom ambient design context (multi-tenant scoping, alternate graph source). |
| `IUpdateDraftCommand` and the 5 lifecycle commands | `Elsa.Workflows.Design.Persistence.Core` | EF Core command impls (`.Persistence.EFCore`) | You want different mutation/lifecycle behaviour (alternate diff, custom locking, non-EF store) while keeping the built-in `IWorkflowDefinitionLookup`. The canonical *swap-commands-keep-queries* example. |
| `IDraftStateDiffEngine` | `Elsa.Workflows.Design.Persistence.EFCore` | `DraftStateDiffEngine` | You want to change *which* mutation events are emitted or the match-key semantics that decide add/update/remove. |

### `IWorkflowDefinitionLookup`
- **Signature:** `GetDefinition(id)`, `ListDefinitions(searchTerm?)`, `GetVersion(versionId)`, `FindLatestVersion(definitionId)`, `ListVersions(definitionId)` — all `Task`-returning reads.
- **Default impl:** `WorkflowDefinitionLookup` (in `.Persistence.EFCore`), backed by `IQueries<WorkflowDefinitionVersion>` + `IQueries<WorkflowDefinition>`. Override the lookup, or override the underlying `IQueries<>` (see [`Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Persistence.EFCore/EXTENSION_POINTS.md)) — two granularities of the same *override* axis.

### The commands — `IUpdateDraftCommand` + 5 lifecycle commands
- **Live in:** `Elsa.Workflows.Design.Persistence.Core` (`Contracts/`). **Default impls:** EF Core commands in `.Persistence.EFCore` (`Commands/`).
- `IUpdateDraftCommand.Execute(UpdateDraftRequest, ct)` — the single coarse Draft-mutation command (Unit 2). Under the per-Draft lock it diffs desired-vs-stored state (via `IDraftStateDiffEngine`), emits one mutation event per difference (the Events section below), runs the validation gate, and persists.
- Lifecycle (not mutations, kept distinct per FR-003): `ICreateDraftCommand`, `ICloneDraftFromVersionCommand`, `IDiscardDraftCommand`, `IPromoteDraftToVersionCommand`, plus `IAddWorkflowDefinitionCommand`.
- **Override** any of these to change behaviour while leaving the read seam (`IWorkflowDefinitionLookup`) and the events intact — this is exactly Joey's "I don't like the commands, I'll implement those myself, but I still want the built-in Queries" scenario.

### `IDraftStateDiffEngine`
- **Lives in:** `Elsa.Workflows.Design.Persistence.EFCore` (`Contracts/`). **Default impl:** `DraftStateDiffEngine`.
- **Signature:** `IReadOnlyList<IEvent> Evaluate(string draftId, WorkflowDefinitionState stored, IReadOnlyCollection<DesignMetadataRecord> storedLayout, WorkflowDefinitionState desired, IReadOnlyCollection<DesignMetadataRecord> desiredLayout);`
- Returns the mutation events for the detected differences. Match keys (FR-023): activities by `NodeId`, activity I/O by (`NodeId`,`ReferenceKey`), connections by endpoint tuple, variables/inputs/outputs by `ReferenceKey`, layout by `NodeId`. Override to change emission/matching.

### Domain-model abstractions
`IWorkflowDefinition`, `IWorkflowDefinitionVersion`, `IWorkflowDefinitionDraft`, `IWorkflowDefinitionLayout`, `IWorkflowGraph`, `IWorkflowDesignContext` are read-model abstractions (framework §2.1). A custom persistence provider realises them; application code does not replace them piecemeal.

---

## Implementable contributor interfaces

This `.Core` owns no add-don't-replace contributor interface of its own. The Draft-validation contributor (`IDraftValidator`) that runs against this domain's Drafts lives in [`Elsa.Workflows.Design.Validations.Core/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Validations.Core/EXTENSION_POINTS.md). Subscribers extend this domain by handling the Background events below.

---

## Events

Every event the `Workflows.Design.Core` library publishes is an `IEvent` (framework §2.6.1), grouped by **delivery strategy** (§2.6.6).

**Sequential / contribution** (§2.6.6) — publisher awaits the dispatch and reads handler contributions back. *None in this `.Core` — the FR-018/FR-018a mutation events are all notification-shaped. The contribution event (`OnDraftValidating`, published Sequential) lives in [`Elsa.Workflows.Design.Validations.Core/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Validations.Core/EXTENSION_POINTS.md).*

Heading convention per research item R4: `### <EventClassName>`. The catalog-parity test in `tests/Elsa.Workflows.Design.Tests/Unit/CatalogParityTests.cs` (Unit C FR-031) asserts bidirectional alignment between the `### On…` headings here and the assembly's published `IEvent` types.

**Background / notification** (§2.6.6) — publisher fires and returns; subscribers (audit, event-sourcing stream, UI push, telemetry) observe but don't feed back. Published via `EventPublishingStrategy.Background`.

**Pipeline behaviour for every event in this section.**

- Published via `IEventPublisher.Publish(..., EventPublishingStrategy.Background, ...)`.
- Dispatch: queued on `IEventChannel`; the `BackgroundEventPublisher` hosted task drains the channel and runs each subscriber. The publisher's call returns before subscribers run.
- Subscriber exception isolation: caught + logged by the Background strategy + worker; one flaky subscriber cannot stall the queue or break the publisher.
- Order: FIFO at enqueue, preserved at dispatch.
- Crash semantics: queued events are in-memory; a process crash drops them. Subscribers that need durability persist their own log.

**Lifecycle — origination + disposal.**

### OnDraftCreated

**Semantic.** A freshly-created `WorkflowDefinitionDraft` has been persisted — the single origination marker regardless of how the Draft was born. A cloned Draft emits this same event (clone delegates to `ICreateDraftCommand`); `SourceVersionId` distinguishes the two origins.
**Payload.** `DraftId : string`, `WorkflowDefinitionId : string`, `SourceVersionId : string?` — the `WorkflowDefinitionVersion` the Draft was cloned from, or `null` for a fresh Draft (no source version). Mirrors the immutable `WorkflowDefinitionDraft.SourceVersionId` column.
**Publication site.** `ICreateDraftCommand` implementation, after `SaveChangesAsync` + lock release. `ICloneDraftFromVersionCommand` (Unit C FR-028) reaches it by delegation.
**Expected handlers.** Event-sourcing subscriber (records the Draft's stream origination, including clone provenance); audit; telemetry.

### OnDraftDiscarded

**Semantic.** A `WorkflowDefinitionDraft` was atomically deleted along with its layout + validation siblings. Terminal entry on the Draft's event stream.
**Payload.** `DraftId : string`, `WorkflowDefinitionId : string`.
**Publication site.** `IDiscardDraftCommand` (Unit C FR-029).
**Expected handlers.** Event-sourcing (terminal marker); audit.
**Idempotency.** A second Discard on the same Draft id is a no-op (the load returns null; the command exits cleanly without re-publishing).

**Mutation events — re-homed onto `IUpdateDraftCommand` (Unit 2).** Every event in the remaining sections is a **per-diff emission** of the single coarse `IUpdateDraftCommand`: the command diffs the desired `WorkflowDefinitionState` (+ layout sibling) against the stored snapshot under the per-Draft lock and emits one of these events per detected difference, published Background after `SaveChangesAsync` + lock release. The 20 former granular mutation commands no longer exist (FR-002); the event *types* and the catalog headings are unchanged (FR-011/FR-012) — only the publication site moved. Match keys (FR-023): activities by `NodeId`, activity I/O by (`NodeId`,`ReferenceKey`), connections by endpoint tuple, variables/inputs/outputs by `ReferenceKey`, layout by `NodeId`.

**Activities — graph.**

### OnActivityAddedToDraft

**Semantic.** An activity has been placed on the canvas.
**Payload.** `DraftId : string`, `Activity : ActivityNode` (with derived `NodeId`, `ActivityVersionId` projections).
**Publication site.** `IUpdateDraftCommand` — emitted when a desired activity `NodeId` is absent from the stored state.
**Expected handlers.** Event-sourcing subscriber.

### OnActivityRemovedFromDraft

**Semantic.** An activity has been removed from the canvas.
**Payload.** `DraftId : string`, `NodeId : string`.
**Publication site.** `IUpdateDraftCommand` — emitted when a stored activity `NodeId` is absent from the desired state (its connections cascade as connection removals, FR-013f).

### OnActivityMovedInDraft

**Semantic.** Layout-position / size change for a placed activity. Mutates `WorkflowDefinitionDraftLayout.Records`; State is layout-free per Elsa §E2.9.2.
**Payload.** `DraftId : string`, `NodeId : string`, `NewX : double`, `NewY : double`, `NewWidth : double?`, `NewHeight : double?`.
**Publication site.** `IUpdateDraftCommand` — emitted when the desired layout `DesignMetadataRecord` (X/Y/W/H) for a `NodeId` differs from the stored layout sibling.

**Per-activity inputs — full CRUD.** Per-activity binding state — entries on `ActivityNode.Inputs` typed as `ArgumentState`. Distinct from workflow-input events (which mutate workflow-definition-level input declarations).

### OnActivityInputAddedToDraft

**Payload.** `DraftId : string`, `NodeId : string`, `Input : ArgumentState` (with derived `InputReferenceKey` projection).
**Publication site.** `IUpdateDraftCommand` — emitted when a desired (`NodeId`,`ReferenceKey`) input is absent from stored.

### OnActivityInputUpdatedInDraft

**Payload.** `DraftId : string`, `NodeId : string`, `InputReferenceKey : string`, `OldValue : ArgumentState`, `NewValue : ArgumentState`.
**Publication site.** `IUpdateDraftCommand` — emitted when a matched (`NodeId`,`ReferenceKey`) input's payload changed.

### OnActivityInputRemovedFromDraft

**Payload.** `DraftId : string`, `NodeId : string`, `InputReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — emitted when a stored (`NodeId`,`ReferenceKey`) input is absent from desired.

**Per-activity outputs — full CRUD.** Same rationale as Per-activity inputs above — full CRUD on the activity's `Outputs` bag.

### OnActivityOutputAddedToDraft

**Payload.** `DraftId : string`, `NodeId : string`, `Output : ArgumentState`.
**Publication site.** `IUpdateDraftCommand` — emitted when a desired (`NodeId`,`ReferenceKey`) output is absent from stored.

### OnActivityOutputUpdatedInDraft

**Payload.** `DraftId : string`, `NodeId : string`, `OutputReferenceKey : string`, `OldValue : ArgumentState`, `NewValue : ArgumentState`.
**Publication site.** `IUpdateDraftCommand` — emitted when a matched (`NodeId`,`ReferenceKey`) output's payload changed.

### OnActivityOutputRemovedFromDraft

**Payload.** `DraftId : string`, `NodeId : string`, `OutputReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — emitted when a stored (`NodeId`,`ReferenceKey`) output is absent from desired.

**Connections — graph.**

### OnConnectionAddedToDraft

**Semantic.** An edge has been added to the activity graph.
**Payload.** `DraftId : string`, `Connection : ActivityConnection`.
**Publication site.** `IUpdateDraftCommand` — emitted when a desired endpoint-tuple connection is absent from stored (a retargeted edge diffs as remove+add, FR-013e).

### OnConnectionRemovedFromDraft

**Semantic.** An edge has been removed from the activity graph.
**Payload.** `DraftId : string`, `Connection : ActivityConnection`. Identity is the source+target pair; connections carry no separate id.
**Publication site.** `IUpdateDraftCommand` — emitted when a stored endpoint-tuple connection is absent from desired.

**Variables — full CRUD.**

### OnVariableDeclaredInDraft

**Payload.** `DraftId : string`, `Variable : VariableDefinition`.
**Publication site.** `IUpdateDraftCommand` — emitted when a desired variable `ReferenceKey` is absent from stored.

### OnVariableUpdatedInDraft

**Payload.** `DraftId : string`, `VariableReferenceKey : string`, `OldValue : VariableDefinition`, `NewValue : VariableDefinition`.
**Publication site.** `IUpdateDraftCommand` — emitted when a matched variable `ReferenceKey`'s payload changed.

### OnVariableRemovedFromDraft

**Payload.** `DraftId : string`, `VariableReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — emitted when a stored variable `ReferenceKey` is absent from desired.

**Workflow inputs — full CRUD.** Workflow-definition-level input declarations (`WorkflowDefinitionState.Inputs`), distinct from per-activity inputs.

### OnWorkflowInputAddedToDraft

**Payload.** `DraftId : string`, `Input : InputDefinition`.
**Publication site.** `IUpdateDraftCommand` — emitted when a desired workflow-input `ReferenceKey` is absent from stored.

### OnWorkflowInputUpdatedInDraft

**Payload.** `DraftId : string`, `InputReferenceKey : string`, `OldValue : InputDefinition`, `NewValue : InputDefinition`.
**Publication site.** `IUpdateDraftCommand` — emitted when a matched workflow-input `ReferenceKey`'s payload changed.

### OnWorkflowInputRemovedFromDraft

**Payload.** `DraftId : string`, `InputReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — emitted when a stored workflow-input `ReferenceKey` is absent from desired.

**Workflow outputs — full CRUD.** Workflow-definition-level output declarations.

### OnWorkflowOutputAddedToDraft

**Payload.** `DraftId : string`, `Output : OutputDefinition`.
**Publication site.** `IUpdateDraftCommand` — emitted when a desired workflow-output `ReferenceKey` is absent from stored.

### OnWorkflowOutputUpdatedInDraft

**Payload.** `DraftId : string`, `OutputReferenceKey : string`, `OldValue : OutputDefinition`, `NewValue : OutputDefinition`.
**Publication site.** `IUpdateDraftCommand` — emitted when a matched workflow-output `ReferenceKey`'s payload changed.

### OnWorkflowOutputRemovedFromDraft

**Payload.** `DraftId : string`, `OutputReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — emitted when a stored workflow-output `ReferenceKey` is absent from desired.

---

## Cross-references

- The validation events for the same transition live in [`Elsa.Workflows.Design.Validations.Core/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Validations.Core/EXTENSION_POINTS.md): `OnDraftValidating` (Sequential, the gate) and `OnDraftValidated` (Background, the outcome).
- The persistence-lifecycle seams (`OnEntitySaving` / `OnEntityLoading`) the commands flow through live in [`Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Persistence.EFCore/EXTENSION_POINTS.md).
- Event-sourcing subscribers (Unit C FR-017's opt-in feature, deferred to a follow-on unit) subscribe to every Background event listed above to materialise the Draft's event stream.
- Audit / telemetry subscribers may subscribe selectively per strategy.
- Repo-wide interface index: [`EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.6 + §2.22.1.
