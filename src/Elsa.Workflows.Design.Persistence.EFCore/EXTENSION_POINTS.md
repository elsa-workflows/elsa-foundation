# Extension points — Workflows.Design.Persistence domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Workflows.Design.Persistence.EFCore` — the EF Core persistence feature that provides default implementations of all workflow persistence commands and the diff engine.

---

## Overridable contracts

All command contracts are defined in `Elsa.Workflows.Design.Persistence.Core`. Replace individually to swap one command while keeping the rest (the canonical *swap-commands-keep-queries* example).

### `IUpdateDraftCommand` *(Core — `Elsa.Workflows.Design.Persistence.Core`)*
- **Signature:** `Task Execute(UpdateDraftRequest request, CancellationToken ct)`
- **Default impl:** EF Core `UpdateDraft` command (this feature) — loads the draft, diffs, emits mutation events, validates, persists.
- **Override:** `services.Replace(ServiceDescriptor.Scoped<IUpdateDraftCommand, MyCommand>())`.

### `ICreateDraftCommand` *(Core — `Elsa.Workflows.Design.Persistence.Core`)*
- **Signature:** `Task<string> Execute(string workflowDefinitionId, WorkflowDefinitionState? initialState, IReadOnlyCollection<DesignMetadataRecord>? initialLayout, string? sourceVersionId, CancellationToken ct)`
- **Default impl:** EF Core `CreateDraft`.

### `ICloneDraftFromVersionCommand` *(Core — `Elsa.Workflows.Design.Persistence.Core`)*
- **Signature:** `Task<string> Execute(string sourceVersionId, CancellationToken ct)`
- **Default impl:** EF Core `CloneDraftFromVersion` — delegates to `ICreateDraftCommand`.

### `IDiscardDraftCommand` *(Core — `Elsa.Workflows.Design.Persistence.Core`)*
- **Signature:** `Task Execute(string draftId, CancellationToken ct)`
- **Default impl:** EF Core `DiscardDraft`.

### `IPromoteDraftToVersionCommand` *(Core — `Elsa.Workflows.Design.Persistence.Core`)*
- **Signature:** `Task<string> Execute(string draftId, CancellationToken ct)`
- **Default impl:** EF Core `PromoteDraftToVersion`.

### `IAddWorkflowDefinitionCommand` *(Core — `Elsa.Workflows.Design.Persistence.Core`)*
- **Signature:** `Task Execute(WorkflowDefinition workflowDefinition, WorkflowDefinitionDraft draft, CancellationToken ct)`
- **Default impl:** EF Core transactional insert.

### `IDraftStateDiffEngine` *(Feature contract — `Elsa.Workflows.Design.Persistence.EFCore`)*
- **Signature:** `IReadOnlyList<IEvent> Evaluate(string draftId, WorkflowDefinitionState stored, IReadOnlyCollection<DesignMetadataRecord> storedLayout, WorkflowDefinitionState desired, IReadOnlyCollection<DesignMetadataRecord> desiredLayout)`
- **Default impl:** `DraftStateDiffEngine` (this feature).
- **Override:** `services.Replace(...)` to change which mutation events are emitted or the match-key semantics.

---

## Entity persistence contributors

This feature ships `IEntitySavingHandler` + `IEntityLoadingHandler` implementations for workflow entities. See [`Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Persistence.EFCore/EXTENSION_POINTS.md) for those interfaces and aggregators.

- **`WorkflowDefinitionVersionSavingHandler`** — serialises workflow version `State` + payloads into `*Source` columns.
- **`WorkflowDefinitionVersionLoadingHandler`** — hydrates the version `State` from `StateSource`.
- **`WorkflowDefinitionDraftSavingHandler`** — serialises draft `State`.
- **`WorkflowDefinitionDraftLoadingHandler`** — hydrates draft `State`.

---

## Cross-references

- Domain-level events (draft mutation events): [`Elsa.Workflows.Design.Api/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Api/EXTENSION_POINTS.md).
- Validation extension points: [`Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md).
- Persistence-lifecycle seams: [`Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Persistence.EFCore/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.2 + §2.22.1.
