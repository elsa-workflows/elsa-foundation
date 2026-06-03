# Extension points — Workflows.Design domain

The per-domain catalog (framework §2.22.1) of everything you can implement or override in the Workflows.Design domain, plus the events it publishes. Anchored at `Elsa.Workflows.Design.Api` — the composition root where `WorkflowsDesignApiFeature` wires the default implementations and aggregating handlers. Three sections:

- **Overridable contracts** — interfaces with a default implementation you can *replace* (`services.Replace(...)` / register-your-own). Bring one implementation and the built-in one steps aside.
- **Implementable contributor interfaces** — *add-don't-replace* seams aggregated by a single handler (framework §2.6.1, §2.24.2).
- **Events** — the FR-018/FR-018a mutation + lifecycle events this domain publishes.

> **Domain spans several projects.** Contracts live in `Elsa.Workflows.Design.Core` and `Elsa.Workflows.Design.Persistence.Core`; default implementations + aggregators are in `Elsa.Workflows.Design.Persistence.EFCore`. Per three-layer rule (framework §2.1): contracts in `.Core`, defaults in the persistence feature, composition in this Api feature. The persistence-lifecycle seams (`OnEntitySaving` / `OnEntityLoading`) are in [`Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Persistence.EFCore/EXTENSION_POINTS.md).

---

## Overridable contracts

| Contract | Layer | Default impl | Override when |
|---|---|---|---|
| `IWorkflowDefinitionLookup` | Core — `Elsa.Workflows.Design.Core` | `WorkflowDefinitionLookup` (`.Persistence.EFCore`) | You want a different read strategy (caching, projection store) while keeping the write path. |
| `IWorkflowDesignContextFactory` | Core — `Elsa.Workflows.Design.Core` | *(persistence-supplied)* | You need a custom ambient design context (multi-tenant scoping, alternate graph source). |
| Command interfaces (`IUpdateDraftCommand` + 5 lifecycle) | Core — `Elsa.Workflows.Design.Persistence.Core` | EF Core command impls (`.Persistence.EFCore`) | You want different mutation/lifecycle behaviour while keeping the built-in `IWorkflowDefinitionLookup`. |
| `IDraftStateDiffEngine` | Feature contract — `Elsa.Workflows.Design.Persistence.EFCore` | `DraftStateDiffEngine` | You want to change which mutation events are emitted or the match-key semantics. |

### `IWorkflowDefinitionLookup` *(Core — `Elsa.Workflows.Design.Core`)*
- **Signature:** `GetDefinition(id)`, `ListDefinitions(searchTerm?)`, `GetVersion(versionId)`, `FindLatestVersion(definitionId)`, `ListVersions(definitionId)` — all `Task`-returning reads.
- **Default impl:** `WorkflowDefinitionLookup` (`Elsa.Workflows.Design.Persistence.EFCore`), backed by `IQueries<WorkflowDefinitionVersion>` + `IQueries<WorkflowDefinition>`.
- **Override:** `services.Replace(ServiceDescriptor.Scoped<IWorkflowDefinitionLookup, MyLookup>())`. Or override the underlying `IQueries<>` (see [`Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Persistence.EFCore/EXTENSION_POINTS.md)) — two granularities of the same *override* axis.

### `IWorkflowDesignContextFactory` *(Core — `Elsa.Workflows.Design.Core`)*
- **Signature:** `ValueTask<IWorkflowDesignContext> Create(CancellationToken ct)`
- **Override:** `services.Replace(...)` when you need a custom ambient context.

### Commands — `IUpdateDraftCommand` + 5 lifecycle *(Core — `Elsa.Workflows.Design.Persistence.Core`)*
Full detail in the persistence catalog: [`Elsa.Workflows.Design.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Persistence.EFCore/EXTENSION_POINTS.md).

Summary: `IUpdateDraftCommand`, `ICreateDraftCommand`, `ICloneDraftFromVersionCommand`, `IDiscardDraftCommand`, `IPromoteDraftToVersionCommand`, `IAddWorkflowDefinitionCommand` — each backed by an EF Core default; `services.Replace(...)` to swap individual commands while keeping the rest (the canonical *swap-commands-keep-queries* example per Joey's framing).

### `IDraftStateDiffEngine` *(Feature contract — `Elsa.Workflows.Design.Persistence.EFCore`)*
- **Signature:** `IReadOnlyList<IEvent> Evaluate(string draftId, WorkflowDefinitionState stored, IReadOnlyCollection<DesignMetadataRecord> storedLayout, WorkflowDefinitionState desired, IReadOnlyCollection<DesignMetadataRecord> desiredLayout)`
- **Default impl:** `DraftStateDiffEngine`. Override to change emission / matching.

### Domain-model abstractions
`IWorkflowDefinition`, `IWorkflowDefinitionVersion`, `IWorkflowDefinitionDraft`, `IWorkflowDefinitionLayout`, `IWorkflowGraph`, `IWorkflowDesignContext` are read-model abstractions (framework §2.1). A custom persistence provider realises them; application code does not replace them piecemeal.

---

## Implementable contributor interfaces

This domain owns no add-don't-replace contributor interface of its own. The Draft-validation contributor (`IDraftValidator`) lives in [`Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md). Subscribers extend this domain by handling the Background events below.

---

## Events

Every event the Workflows.Design domain publishes is an `IEvent` (framework §2.6.1), grouped by **delivery strategy** (§2.6.6).

**Sequential / contribution** — none in this domain's Core. The contribution event (`OnDraftValidating`, Sequential) lives in [`Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md).

Heading convention: `### <EventClassName>`. The `CatalogParityTests` in `tests/Elsa.Workflows.Design.Tests` asserts bidirectional alignment between the `### On…` headings here and the assembly's published `IEvent` types (anchored on the `Elsa.Workflows.Design.Core` assembly).

**Background / notification** (§2.6.6) — published via `EventPublishingStrategy.Background`. Queued on `IEventChannel`; `BackgroundEventPublisher` drains asynchronously. Subscriber exceptions are caught + logged; one flaky subscriber cannot break the publish.

**Lifecycle — origination + disposal.**

### OnDraftCreated

**Semantic.** A freshly-created `WorkflowDefinitionDraft` has been persisted — the single origination marker regardless of how the Draft was born. A cloned Draft emits this same event; `SourceVersionId` distinguishes the two origins.
**Payload.** `DraftId : string`, `WorkflowDefinitionId : string`, `SourceVersionId : string?` — the `WorkflowDefinitionVersion` the Draft was cloned from, or `null` for a fresh Draft.
**Publication site.** `ICreateDraftCommand` implementation, after `SaveChangesAsync` + lock release. `ICloneDraftFromVersionCommand` reaches it by delegation.

### OnDraftDiscarded

**Semantic.** A `WorkflowDefinitionDraft` was atomically deleted along with its layout + validation siblings. Terminal entry on the Draft's event stream.
**Payload.** `DraftId : string`, `WorkflowDefinitionId : string`.
**Publication site.** `IDiscardDraftCommand`.

**Mutation events — emitted by `IUpdateDraftCommand`.** Every event below is a per-diff emission of the single coarse `IUpdateDraftCommand`: the command diffs the desired `WorkflowDefinitionState` (+ layout sibling) against the stored snapshot under the per-Draft lock and emits one event per detected difference, published Background after `SaveChangesAsync` + lock release. Match keys: activities by `NodeId`, I/O by (`NodeId`,`ReferenceKey`), connections by endpoint tuple, variables/inputs/outputs by `ReferenceKey`, layout by `NodeId`.

**Activities — graph.**

### OnActivityAddedToDraft

**Payload.** `DraftId : string`, `Activity : ActivityNode` (with derived `NodeId`, `ActivityVersionId` projections).
**Publication site.** `IUpdateDraftCommand` — emitted when a desired activity `NodeId` is absent from stored state.

### OnActivityRemovedFromDraft

**Payload.** `DraftId : string`, `NodeId : string`.
**Publication site.** `IUpdateDraftCommand` — emitted when a stored activity `NodeId` is absent from desired state.

### OnActivityMovedInDraft

**Semantic.** Layout-position / size change for a placed activity.
**Payload.** `DraftId : string`, `NodeId : string`, `NewX : double`, `NewY : double`, `NewWidth : double?`, `NewHeight : double?`.
**Publication site.** `IUpdateDraftCommand` — emitted when desired layout differs from stored.

**Per-activity inputs — full CRUD.**

### OnActivityInputAddedToDraft

**Payload.** `DraftId : string`, `NodeId : string`, `Input : ArgumentState`.
**Publication site.** `IUpdateDraftCommand` — desired (`NodeId`,`ReferenceKey`) input absent from stored.

### OnActivityInputUpdatedInDraft

**Payload.** `DraftId : string`, `NodeId : string`, `InputReferenceKey : string`, `OldValue : ArgumentState`, `NewValue : ArgumentState`.
**Publication site.** `IUpdateDraftCommand` — matched input payload changed.

### OnActivityInputRemovedFromDraft

**Payload.** `DraftId : string`, `NodeId : string`, `InputReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — stored (`NodeId`,`ReferenceKey`) input absent from desired.

**Per-activity outputs — full CRUD.**

### OnActivityOutputAddedToDraft

**Payload.** `DraftId : string`, `NodeId : string`, `Output : ArgumentState`.
**Publication site.** `IUpdateDraftCommand` — desired (`NodeId`,`ReferenceKey`) output absent from stored.

### OnActivityOutputUpdatedInDraft

**Payload.** `DraftId : string`, `NodeId : string`, `OutputReferenceKey : string`, `OldValue : ArgumentState`, `NewValue : ArgumentState`.
**Publication site.** `IUpdateDraftCommand` — matched output payload changed.

### OnActivityOutputRemovedFromDraft

**Payload.** `DraftId : string`, `NodeId : string`, `OutputReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — stored (`NodeId`,`ReferenceKey`) output absent from desired.

**Connections — graph.**

### OnConnectionAddedToDraft

**Semantic.** An edge has been added to the activity graph.
**Payload.** `DraftId : string`, `Connection : ActivityConnection`.
**Publication site.** `IUpdateDraftCommand` — desired endpoint-tuple connection absent from stored.

### OnConnectionRemovedFromDraft

**Semantic.** An edge has been removed from the activity graph.
**Payload.** `DraftId : string`, `Connection : ActivityConnection`.
**Publication site.** `IUpdateDraftCommand` — stored endpoint-tuple connection absent from desired.

**Variables — full CRUD.**

### OnVariableDeclaredInDraft

**Payload.** `DraftId : string`, `Variable : VariableDefinition`.
**Publication site.** `IUpdateDraftCommand` — desired variable `ReferenceKey` absent from stored.

### OnVariableUpdatedInDraft

**Payload.** `DraftId : string`, `VariableReferenceKey : string`, `OldValue : VariableDefinition`, `NewValue : VariableDefinition`.
**Publication site.** `IUpdateDraftCommand` — matched variable payload changed.

### OnVariableRemovedFromDraft

**Payload.** `DraftId : string`, `VariableReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — stored variable `ReferenceKey` absent from desired.

**Workflow inputs — full CRUD.**

### OnWorkflowInputAddedToDraft

**Payload.** `DraftId : string`, `Input : InputDefinition`.
**Publication site.** `IUpdateDraftCommand` — desired workflow-input `ReferenceKey` absent from stored.

### OnWorkflowInputUpdatedInDraft

**Payload.** `DraftId : string`, `InputReferenceKey : string`, `OldValue : InputDefinition`, `NewValue : InputDefinition`.
**Publication site.** `IUpdateDraftCommand` — matched workflow-input payload changed.

### OnWorkflowInputRemovedFromDraft

**Payload.** `DraftId : string`, `InputReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — stored workflow-input `ReferenceKey` absent from desired.

**Workflow outputs — full CRUD.**

### OnWorkflowOutputAddedToDraft

**Payload.** `DraftId : string`, `Output : OutputDefinition`.
**Publication site.** `IUpdateDraftCommand` — desired workflow-output `ReferenceKey` absent from stored.

### OnWorkflowOutputUpdatedInDraft

**Payload.** `DraftId : string`, `OutputReferenceKey : string`, `OldValue : OutputDefinition`, `NewValue : OutputDefinition`.
**Publication site.** `IUpdateDraftCommand` — matched workflow-output payload changed.

### OnWorkflowOutputRemovedFromDraft

**Payload.** `DraftId : string`, `OutputReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — stored workflow-output `ReferenceKey` absent from desired.

---

## Cross-references

- Validation events (`OnDraftValidating` Sequential gate + `OnDraftValidated` Background outcome): [`Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md).
- Command + diff-engine overridables: [`Elsa.Workflows.Design.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Persistence.EFCore/EXTENSION_POINTS.md).
- Persistence-lifecycle seams (`OnEntitySaving` / `OnEntityLoading`): [`Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Persistence.EFCore/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.6 + §2.22.1.
