# Research: Weaver workflow authoring and inspection journeys

**Date**: 2026-11-03 | **Base**: main `e190ab6ce`

This file records the seams each slice uses, and the decisions that follow from them. Paths are relative to the repository root.

## R1. Activity catalog (slice B)

- **Seam**: `IActivityAuthoringCatalogReader.ListAsync(ListActivityAuthoringCatalog)`, in `Essentials/src/Activities/Design/Api/Services/ActivityAuthoringCatalogReader.cs:176-179`.
- **Request**: `ListActivityAuthoringCatalog(Availability = Addable | All)`.
- **What the designer uses**: Studio's `listActivities()` (`Studio/src/essentials/Elsa.Studio.Workflows/Client/src/api/activityDesign.ts:220-224`) calls `GET /design/activities/catalog`, which requires `ActivityDesignPermissions.Read`.
- **How addability is decided**: from the host-default availability settings (`ActivityAvailabilitySettings.HostDefaultScope`) through `IActivityAvailabilityEvaluator.FilterAddable`. It is not decided per actor.
- **Entry contents**: `ActivityAuthoringDescriptorView` carries the type key, version id, version, display data, `Available` and `AvailabilityReason`, inputs, outputs, ports and container structure. `ActivityInputDescriptorView` carries `IsCredential` and `IsSensitive`.

**Decision**:
- The Weaver catalog calls the same reader with `Availability = All`. Context discloses the bounded entries and their `Available` flag, so an existing non-addable node stays explainable and editable (FR-013) while a new one is refused.
- "Under the current actor and tenant authority" means the designer's own gate: the actor must hold `ActivityDesignPermissions.Read`, checked through the permission service for the request's tenant, before any entry is disclosed. The catalog is not narrowed per actor, because the designer offers no per-actor narrowing.
- The reader is in an `.Api` package, and framework §2.1 lets features couple only through `.Core` contracts. So `Elsa.Activities.Design.Core` gains `IActivityAuthoringCatalog`, which the Api implements over this reader (plan D4). `Elsa.Agent.Workflows` references the Core only.

## R2. Revision identity (slices B, C)

- **Current content revision**: `getDraftRevision(draft) = hashString(getDraftSignature(draft))`, in `workflow-editor/editorHelpers.ts:549-559`. `hashString` is FNV-1a 32-bit (`:648-656`).
- **Where it is used**: `workflowDocument.ts:90` and `:147`, the compare-and-replace.
- **Decision**: slice C adds a SHA-256 content revision over the same canonical signature, for batch binding and receipts. It is computed synchronously by a module-owned implementation tested against the NIST vectors. The compare runs inside a synchronous reducer (`workflowDocument.ts:88-90`), and `crypto.subtle` is undefined on non-secure origins. The FNV hash stays for change detection only.

## R3. Designer history and autosave (slice C)

- **Timing**: `constants.ts` sets `historyCaptureDelayMs = 250` and `autosaveDelayMs = 1200`.
- **History**: `useDraftHistory` returns `flushPendingHistory`, `undo`, `redo`, `canUndoNow` and `canRedoNow`. The capture timer is at `:52`.
- **Autosave**: `useWorkflowPersistence.holdForReview(hold)` skips autosaves while it is held. A covering explicit save clears it.
- **Caveats**:
  - The history is capped at `MAX_HISTORY = 50` (`workflowHistory.ts:28`), so indexes shift.
  - `flushPendingHistory` reads the render-time draft (`useDraftHistory.ts:59-67`).
- **Decision**: the batch applier flushes pending capture from the latest committed draft, read from a ref. It then pushes the batch result as its own entry. Each entry carries the set of batch identities in its line, inherited by later entries. Autosave is held while the current entry's set holds an unsaved batch. Undo before the batch releases the hold, Redo restores it, and an explicit save marks the covered batches as saved.

## R4. Workflow library (slice E)

- **Store**: `IWorkflowLibraryStore`, in `Essentials/src/Workflows/Design/Persistence/Core/Stores/IWorkflowLibraryStore.cs`:
  - `CreateTagAsync(WorkflowTagInput, actorId)` at `:27`;
  - `SaveViewAsync(id?, WorkflowLibraryViewInput, subjectId, canShare)` at `:31`;
  - `ReplaceTagsAsync(definitionId, revision, tags, actorId, operationKey)` at `:21`;
  - `EditTagsAsync(WorkflowTagBulkEdit, actorId)` at `:23`;
  - `MoveDefinitionsAsync(ids, folderId, actorId, operationKey)` at `:14`;
  - `SetFavoriteAsync(definitionId, favorite, subjectId)` at `:33`.
- **EF store**: `EfWorkflowLibraryStore`. The relevant entities are `WorkflowTagEntity` and `WorkflowLibraryViewEntity`.
- **When organization is disabled**: `WorkflowOrganizationOptions(Enabled)`. `WorkflowLibraryAccess.RequireOrganization` throws `WorkflowLibraryUnavailableException`, which maps to 501.
- **Permission flags**: `WorkflowLibraryPermissions(CanAssignTags, CanManageFolders, CanManageTags, CanShareViews)`. They are embedded in `GET definitions/page`, the organization routes and `GET tags`. There is no dedicated route.

**Decision**:
- The list context reads the flags from the page response Studio already holds.
- There are no separate handlers. The authority checks are inline in the endpoint `HandleAsync` methods (`WorkflowLibraryEndpoints.cs:235-245`) and use the internal, `HttpContext`-bound `WorkflowLibraryAccess`. Slice E extracts them into `IWorkflowLibraryCommands` (plan D4). The Weaver tools call the installed contract. The HTTP endpoints run the same built-in logic directly, so a replacement decides only Weaver's writes.
- The operation-key unique index is scoped like the existing tag index (`WorkflowLibraryEntityConfigurations.cs:71`): by the tenant scope key for tags, and by the tenant scope key and subject for views.
- The operation-key overloads are `CreateTagAsync(input, actorId, operationKey)` and `SaveViewAsync(id, input, subjectId, canShare, operationKey)`.

## R5. Templates (slice E)

- **Store**: `IWorkflowTemplateStore`, with `ListAsync`, `FindAsync`, `SaveAsync(operationKey, id?, name, description, content, expectedRevision)` and `DeleteAsync`.
- **Model**: `WorkflowTemplate(Id, Name, Description, Revision, …)`. The wire field is `ExpectedRevision`.
- **Studio**: `workflow-editor/WorkflowTemplatesPage.tsx` hosts both the list and the editor, through `templateDocumentAdapter.ts`.
- **Decision**: template contexts use `Id` and `Revision`. The template editor's adapter supplies the working-state content revision and client binding through the same batch applier as the designer.

## R6. Versions (slice F)

- **List**: `GET design/workflows/definitions/{id}/versions`.
- **Exact version**: `GET design/workflows/versions/{versionId}`, through `IWorkflowVersionDetailsReader.ReadAsync`.
- **Existing diffs**: none for workflow versions. Only activity versions have a diff (`IActivityVersionDiffService`).
- **Decision**: the version tool reads both exact versions through `IWorkflowVersionReader`, a new `Elsa.Workflows.Design.Core` contract that the Api implements over `IWorkflowVersionDetailsReader` (plan D4). It computes a bounded structural comparison by scope path over those two owned reads: activities added, removed and changed, and connections. It records that the comparison is the tool's own. The owning design services remain the only source of the version content.

## R7. Executables and Runs (slices F, G)

- **Executable reads**: `IWorkflowExecutableInspector.GetAsync(GetWorkflowExecutable(ArtifactId, Ref?))` returns `WorkflowExecutableDetailsView`, with `ArtifactHash`, `RootActivity`, `References` and `InputContract`.
  - The lookup is by artifact id. There is no lookup by hash.
  - **Decision**: the context binds the artifact id together with the Artifact Hash. A read whose returned hash differs from the bound hash is stale.
- **Run reads**: `IWorkflowInstanceDetailsService`, `IWorkflowIncidentListService`, `IActivityExecutionInspectionService` and `IActivityExecutionStateViewService`, all in `Elsa.Workflows.Runtime.Api`.
- **Redaction**: execution evidence withholds sensitive values at capture time (`ExecutionEvidenceCheckpointEnricher.cs:150-151`). The Run tool discloses only values those services return. A withheld or unavailable value is reported with its reason.
- **Decision (slice G review): stricter than the run page.** A sensitive value is never released or resolved by `IWorkflowRunInspection`, even when a capture policy captured it and the actor may resolve it on the run page; incident messages and state views of activities with a sensitive input binding are withheld too, because exception text and projections can echo the input. Weaver discloses to a model provider, a wider audience than the actor's own screen. Trade-off: Weaver never sees values the run page can show, and says they are withheld. A retained read is withheld once the actor loses the access it was read under (`CanInspectValues`, `CanResolveValues`), and only disclosed values are resolved and audited (`ResolveEvidenceIds`).
- **Decision**: these services are in `Elsa.Workflows.Runtime.Api`. `Elsa.Workflows.Runtime.Core` gains `IWorkflowExecutableInspection` and `IWorkflowRunInspection`, which the Api implements over them (plan D4). `Elsa.Agent.Workflows.Runtime` references the Core only, as the Specialists packages reference `Elsa.Workflows.Runtime.Core`.

## R8. Deployment shapes (slice F)

- **Combined host**: `Essentials/apps/Elsa.Workbench`. Its Design and Runtime planes are composed separately (`Program.cs:320-321`).
- **Runtime-only precedent**: `RuntimeOnlyArtifactExecutionTests` asserts that no `Elsa.Workflows.Design*` assembly is loaded.
- **Design-only precedent**: `EtlDesignApiTestHost`.
- **Decision**: slice F adds Design-only, Runtime-only and combined composition tests for the two Agent workflow packages, modelled on those precedents, and Runtime-only and Design-only cases in the probe host.

## R9. Dangling Weaver contributions (slice D)

- The `workflows.proposal` renderer in `Extensions/Weaver/studio/Elsa.Studio.Weaver.Workflows/Client/src/module.tsx:131-138` has no producer. It is removed.
- The explain-executable prompt (`:74-78`) names a "get-executable-detail" tool that is never registered. `getExecutableDetail` (`:172-179`) is used only in tests.
  - Slice D removes the prompt and the helper.
  - Slice F restores the prompt against `workflow_read_executable`.
