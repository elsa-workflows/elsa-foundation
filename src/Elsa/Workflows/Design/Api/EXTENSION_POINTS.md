# Extension points — Workflows.Design domain

## Supported management-client API boundary

`WorkflowsDesignApiFeature` composes the canonical Workflow Design management-client slice described in [README.md](README.md). Its endpoints are host-independent and have no `Elsa.Server` dependency. This catalog covers the seams behind those endpoints; it does not make mediator handlers or API response projections extension points.

The authoring endpoints resolve `IActivityInputOptionsProvider` contributions and traverse authored structures through `IActivityStructureService`. Definition and draft endpoints depend on the provider-neutral stores and commands listed below. Replace a single-owner store/command deliberately; add keyed providers and structure handlers as contributors. Canonical ownership and terminology remain in the [domain-owned API spec](../../../../../specs/092-domain-owned-apis/spec.md) and [Elsa glossary](../../../../../docs/glossary/elsa.md).

The per-domain catalog (framework §2.22.1) of everything you can implement or override in the Workflows.Design domain, plus the events it publishes. Anchored at `Elsa.Workflows.Design.Api` — the composition root where `WorkflowsDesignApiFeature` wires the default implementations and aggregating handlers. Three sections:

- **Overridable contracts** — interfaces with a default implementation you can *replace* (`services.Replace(...)` / register-your-own). Bring one implementation and the built-in one steps aside.
- **Implementable contributor interfaces** — *add-don't-replace* seams aggregated by a single handler (framework §2.6.1, §2.24.2).
- **Events** — the FR-018/FR-018a mutation + lifecycle events this domain publishes.

> **Domain spans several projects.** Contracts live in `Elsa.Workflows.Design.Core` and `Elsa.Workflows.Design.Persistence.Core`; the provider-neutral default services (`WorkflowDefinitionLookup`, `DraftStateDiffEngine`) also live in `Elsa.Workflows.Design.Persistence.Core`, while the concrete store/command provider implementations are in `Elsa.Workflows.Design.Persistence.Groundwork`. Per three-layer rule (framework §2.1): contracts in `.Core`, defaults in the persistence layer, composition in this Api feature. The Groundwork provider persists design documents through provider-neutral read/write ports (no EF `OnEntitySaving` / `OnEntityLoading` hydration seams); see [`Elsa.Workflows.Design.Persistence.Groundwork/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Persistence.Groundwork/EXTENSION_POINTS.md).

---

## Overridable contracts

| Contract | Layer | Default impl | Override when |
|---|---|---|---|
| `IWorkflowDefinitionLookup` | Core — `Elsa.Workflows.Design.Core` | `WorkflowDefinitionLookup` (`.Persistence.Core`) | You want a different read strategy (caching, projection store) while keeping the write path. |
| `IWorkflowDesignContextFactory` | Core — `Elsa.Workflows.Design.Core` | *(persistence-supplied)* | You need a custom ambient design context (multi-tenant scoping, alternate graph source). |
| Command interfaces (`IUpdateDraftCommand` + 5 lifecycle) | Core — `Elsa.Workflows.Design.Persistence.Core` | Groundwork command impls (`.Persistence.Groundwork`) | You want different mutation/lifecycle behaviour while keeping the built-in `IWorkflowDefinitionLookup`. |
| `IDraftStateDiffEngine` | Contract — `Elsa.Workflows.Design.Persistence.Core` | `DraftStateDiffEngine` (`.Persistence.Core`) | You want to change which mutation events are emitted or the match-key semantics. |

### `IWorkflowDefinitionLookup` *(Core — `Elsa.Workflows.Design.Core`)*
- **Signature:** `GetDefinition(id)`, `ListDefinitions(searchTerm?)`, `GetVersion(versionId)`, `FindLatestVersion(definitionId)`, `ListVersions(definitionId)` — all `Task`-returning reads.
- **Default impl:** `WorkflowDefinitionLookup` (`Elsa.Workflows.Design.Persistence.Core`), backed by the named `IWorkflowDefinitionVersionStore` + `IWorkflowDefinitionStore` read ports.
- **Override:** `services.Replace(ServiceDescriptor.Scoped<IWorkflowDefinitionLookup, MyLookup>())`. Or override one of the underlying named read ports (see [`Elsa.Workflows.Design.Persistence.Groundwork/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Persistence.Groundwork/EXTENSION_POINTS.md)) — two granularities of the same *override* axis.

### `IWorkflowDesignContextFactory` *(Core — `Elsa.Workflows.Design.Core`)*
- **Signature:** `ValueTask<IWorkflowDesignContext> Create(CancellationToken ct)`
- **Override:** `services.Replace(...)` when you need a custom ambient context.

### Commands — `IUpdateDraftCommand` + 5 lifecycle *(Core — `Elsa.Workflows.Design.Persistence.Core`)*
Full detail in the persistence catalog: [`Elsa.Workflows.Design.Persistence.Groundwork/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Persistence.Groundwork/EXTENSION_POINTS.md).

Summary: `IUpdateDraftCommand`, `ICreateDraftCommand`, `ICloneDraftFromVersionCommand`, `IDiscardDraftCommand`, `IPromoteDraftToVersionCommand`, `IAddWorkflowDefinitionCommand` — each backed by a Groundwork default; `services.Replace(...)` to swap individual commands while keeping the rest (the canonical *swap-commands-keep-queries* example per Joey's framing).

### `IDraftStateDiffEngine` *(Contract — `Elsa.Workflows.Design.Persistence.Core`)*
- **Signature:** `IReadOnlyList<IEvent> Evaluate(string draftId, WorkflowDefinitionState stored, IReadOnlyCollection<DesignMetadataRecord> storedLayout, WorkflowDefinitionState desired, IReadOnlyCollection<DesignMetadataRecord> desiredLayout)`
- **Default impl:** `DraftStateDiffEngine`. Override to change emission / matching.

### Domain-model abstractions
`IWorkflowDefinition`, `IWorkflowDefinitionVersion`, `IWorkflowDefinitionDraft`, `IWorkflowDefinitionLayout`, `IWorkflowGraph`, `IWorkflowDesignContext` are read-model abstractions (framework §2.1). A custom persistence provider realises them; application code does not replace them piecemeal.

---

## Implementable contributor interfaces

The Draft-validation contributor (`IDraftValidator`) lives in [`Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md). Subscribers also extend this domain by handling the Background events below.

### `IActivityInputOptionsProvider` *(Core — `Elsa.Workflows.Design.Core`)*

- **Kind:** Keyed Source (add-don't-replace). Each provider contributes allowable design-time values under one stable, case-sensitive `Key`.
- **Contract:** `Elsa.Workflows.Design.Core.Contracts.IActivityInputOptionsProvider`.
- **Input:** `ActivityInputOptionsContext` containing the current authored `WorkflowDefinitionState`, selected `ActivityNode`, and cataloged activity `InputDefinition`.
- **Output:** An ordered list of `ActivityInputOption` values. Labels are nonblank and values are JSON strings, numbers, booleans, or enum names.
- **Aggregation:** `ActivityInputOptionsProviderResolver` resolves the one provider named by the cataloged input metadata. Duplicate keys fail shell startup; the client cannot choose a provider key in its request.
- **Registration:** Register the provider as `IActivityInputOptionsProvider` from a design-side module. Runtime activity libraries declare only the stable key through `ActivityInputAttribute.OptionsProvider`, preserving the Runtime → Design boundary.
- **Authoring contract:** [`specs/090-activity-input-editor-options/contracts/activity-input-authoring.md`](../../../../../specs/090-activity-input-editor-options/contracts/activity-input-authoring.md).

Known implementations: none in the foundation host; feature modules opt in by registering providers.

### `IActivityStructureHandler` *(Core — `Elsa.Workflows.Design.Core`)*

- **Kind:** Contributor (add-don't-replace, keyed by structure `Kind`). One handler per composite/container activity structure kind; generic design and publishing code dispatch to the matching handler without interpreting activity-specific structure payloads.
- **Contract:** `Elsa.Workflows.Design.Core.Contracts.IActivityStructureHandler`.
- **Members:**
  - `Kind` / `SchemaVersion` — identify the structure shape this handler owns.
  - `ProjectChildren(activity)` / `ReplaceChildren(activity, projections)` — expose and rewrite the activity's child slots.
  - `CompileExecutableStructure(activity)` — materialize the authored structure into the executable structure the runtime consumes.
  - `SupportsScopedVariables` *(default `false`)* — whether this activity is a **container scope** that can own container-scoped variable declarations (ADR 0027). Container activities (`Sequence`, `Flowchart`) return `true`.
  - `ProjectScopedVariables(activity)` *(default empty)* — the container-scoped `VariableDefinition`s the activity declares, visible to its descendant activities. These flow through publishing into the compiled executable structure's `variables`, so the runtime materializes the visible variable scope chain (`RuntimeVariableScopeFactory` / `RuntimeContainerScopeService` in `Elsa.Workflows.Runtime.Core`) without re-reading the design document.
- **Aggregation:** `IActivityStructureService` (`DefaultActivityStructureService`) resolves the registered handlers by `Kind`. `ScopedVariableResolver` walks the authored tree through this contract to compute per-node scoped-variable visibility.
- **Registration:** register an implementation with DI as `IActivityStructureHandler` from the activity's own module.

Known implementations:
- `SequenceStructureHandler` (`Elsa.Activities.Sequence`) *(cross-domain — `SupportsScopedVariables = true`)*.
- `FlowchartStructureHandler` (`Elsa.Activities.Flowchart`) *(cross-domain — `SupportsScopedVariables = true`)*.

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
**Publication site.** `ICreateDraftCommand` implementation, after the draft is committed + lock release. `ICloneDraftFromVersionCommand` reaches it by delegation.

### OnDraftDiscarded

**Semantic.** A `WorkflowDefinitionDraft` was atomically deleted along with its layout sibling. Terminal entry on the Draft's event stream.
**Payload.** `DraftId : string`, `WorkflowDefinitionId : string`.
**Publication site.** `IDiscardDraftCommand`.

**Mutation events — declared as tested contract; publication currently retired pending an event-sourcing consumer.** The event types below are the per-diff mutation-event surface computed by `IDraftStateDiffEngine` from the desired `WorkflowDefinitionState` (+ layout sibling) versus the stored snapshot. They have no subscriber today, so `IUpdateDraftCommand` no longer computes or publishes them and the diff engine is no longer registered in DI; the event records and the engine remain in place as the tested contract, to be re-wired when the FR-017/FR-018 event-sourcing consumer is built. The "Publication site" notes below describe the *intended* per-diff emission for that future wiring. Match keys: activities by `NodeId`, I/O by (`NodeId`,`ReferenceKey`), variables/inputs/outputs by `ReferenceKey`, layout by `NodeId`. Flowchart graph connection events belong to the future Flowchart activity module, not to Workflows.Design Core.

**Activities.**

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
- Command + diff-engine overridables: [`Elsa.Workflows.Design.Persistence.Groundwork/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Persistence.Groundwork/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.6 + §2.22.1.
