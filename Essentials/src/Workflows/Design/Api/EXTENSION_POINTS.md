# Extension points — Workflows.Design domain

## Supported management-client API boundary

`WorkflowsDesignApiFeature` composes the canonical Workflow Design management-client slice described in [README.md](README.md). Its endpoints are host-independent and have no `Elsa.Workbench` dependency. This catalog covers the seams behind those endpoints; it does not make mediator handlers or API response projections extension points.

The authoring endpoints resolve `IActivityInputOptionsProvider` contributions and traverse authored structures through `IActivityStructureService`. Definition and draft endpoints depend on the provider-neutral stores and commands listed below. Replace a single-owner store/command deliberately; add keyed providers and structure handlers as contributors. Canonical ownership and terminology remain in the [domain-owned API spec](../../../../../specs/220-domain-owned-apis/spec.md) and [Elsa glossary](../../../../../docs/glossary/elsa.md).

The per-domain catalog (framework §2.22.1) of everything you can implement or override in the Workflows.Design domain, plus the events it publishes. Anchored at `Elsa.Workflows.Design.Api` — the composition root where `WorkflowsDesignApiFeature` wires the default implementations and aggregating handlers. Three sections:

- **Overridable contracts** — interfaces with a default implementation you can *replace* (`services.Replace(...)` / register-your-own). Bring one implementation and the built-in one steps aside.
- **Implementable contributor interfaces** — *add-don't-replace* seams aggregated by a single handler (framework §2.6.1, §2.24.2).
- **Events** — the FR-018/FR-018a mutation + lifecycle events this domain publishes.

> **Domain spans several projects.** Contracts live in `Elsa.Workflows.Design.Core` and `Elsa.Workflows.Design.Persistence.Core`; the provider-neutral default services (`WorkflowDefinitionLookup`, `DraftStateDiffEngine`) also live in `Elsa.Workflows.Design.Persistence.Core`, while the concrete store/command provider implementations are in `Elsa.Workflows.Design.Persistence.EntityFrameworkCore`. Per three-layer rule (framework §2.1): contracts in `.Core`, defaults in the persistence layer, composition in this Api feature. The EF Core provider persists design records through provider-neutral read/write ports (no EF `EntitySaving` / `EntityLoading` hydration seams); see [`Elsa.Workflows.Design.Persistence.EntityFrameworkCore/EXTENSION_POINTS.md`](../Persistence/EntityFrameworkCore/EXTENSION_POINTS.md).

---

## Overridable contracts

| Contract | Layer | Default impl | Override when |
|---|---|---|---|
| `IWorkflowDefinitionLookup` | Core — `Elsa.Workflows.Design.Core` | `WorkflowDefinitionLookup` (`.Persistence.Core`) | You want a different read strategy (caching, projection store) while keeping the write path. |
| `IWorkflowDefinitionAuthorizer` | Core — `Elsa.Workflows.Design.Core` | `HttpWorkflowDefinitionAuthorizer` (`.Api`) | A host replaces current per-definition read/manage authority. Select one scoped implementation; duplicates fail startup and missing/ambiguous authority refuses resource access. |
| Command interfaces (`IUpdateDraftCommand` + 5 lifecycle) | Core — `Elsa.Workflows.Design.Persistence.Core` | EF Core command implementations (`.Persistence.EntityFrameworkCore`) | You want different mutation/lifecycle behaviour while keeping the built-in `IWorkflowDefinitionLookup`. |
| `IDraftStateDiffEngine` | Contract — `Elsa.Workflows.Design.Persistence.Core` | `DraftStateDiffEngine` (`.Persistence.Core`) | You want to change which mutation events are emitted or the match-key semantics. |
| `IWorkflowDefinitionPublicationStateLookup` | Contract — `Elsa.Workflows.Design.Persistence.Core` | `NullWorkflowDefinitionPublicationStateLookup` (`Elsa.Workflows.Design.Api.Services`); Publishing replaces with `WorkflowDefinitionPublicationStateLookup` | Publishing is composed and definition list/detail views should distinguish Retracted and Retired from Published. Design-only hosts keep the null-object and leave `publicationState` unset. |
| `IWorkflowLibraryStore` | Contract — `Elsa.Workflows.Design.Persistence.Core` | `EfWorkflowLibraryStore` (`Elsa.Workflows.Design.Persistence.EntityFrameworkCore`) | A host replaces the durable, tenant-scoped organization and bounded workflow-library read/write backend. |
| `IWorkflowLibraryCommands` | Contract — `Elsa.Workflows.Design.Core` | `WorkflowLibraryCommands` (internal, this package); duplicates fail configuration and shell startup | A host replaces the library organization commands other domains (such as Weaver) call through Workflows Design Core. A replacement decides Weaver's library writes; the HTTP endpoints keep the built-in decisions. |
| `IWorkflowVersionReader` | Contract — `Elsa.Workflows.Design.Core` | `WorkflowVersionReader` (internal, this package); duplicates fail configuration and shell startup | A host replaces the exact version reads other domains (such as Weaver) make through Workflows Design Core. |
| `IWorkflowLibraryVisibilityPolicy` | Contract — `Elsa.Workflows.Design.Persistence.Core` | Host-supplied optional replacement; at most one implementation, duplicates fail shell startup | A host registers any permission resource handlers and must provide their complete workflow query visibility before counts and paging. |

### `IWorkflowDefinitionAuthorizer` *(Contract — `Elsa.Workflows.Design.Core`)*

`CanReadAsync(definitionId, cancellationToken)` and `CanManageAsync(definitionId, cancellationToken)`
return current owner decisions without accepting a principal, permission string, client revision or
Weaver model. The default uses the normalized HTTP principal, typed `WorkflowLibraryResource`
permission checks, tenant-scoped `IWorkflowDefinitionStore.FindByIdAsync` existence and the existing
`IWorkflowDefinitionDetailsReader.AuthorizeReadAsync` preflight for definition/folder/ancestor visibility.
Management includes read visibility plus the typed management permission. Infrastructure failures
from permission, metadata-store or reader operations become
`WorkflowDefinitionAuthorizationException` from Workflow Design Core, with the definition identity,
requested read/manage operation and original `InnerException`. Its outer message excludes infrastructure
details. An existing authorization exception is preserved without another wrapper. Known native
authorization refusals remain false, cancellation propagates, and failures never become permission grants.

The startup validator captures the configuring collection's metadata only. Copied child-shell
registrations rebind it to the child's collection, and repeated configuration retains one validator;
an inherited application-wide `IServiceCollection` registration cannot hide a child duplicate.

The built-in reader's preflight reads organization metadata only. A replacement details reader's
compatibility default may call its complete `ReadAsync`. The definition GET honors the selected
authorizer before returning details and preserves the existing reader's not-found response. This
contract does not replace the bounded query visibility policy used before counts and paging.

**Failure semantics (spec 1193).** Any authorizer, built-in or replacement, that throws
`WorkflowDefinitionAuthorizationException` makes every Design endpoint answer fail-closed as
unavailable: HTTP 503 in the design error body with `errorCode`
`workflow.authorization.unavailable` (`WorkflowDefinitionAuthorizationException.UnavailableCode`)
and the exception's fixed message in `errors.generalErrors`. The inner cause is logged, never
written. A `false` decision stays the existing 403, whose body, like every other design body, has
no `errorCode` property. The Design owner's keyed `IEndpointFaultRenderer` writes this ahead of
the codeless `WorkflowDesignExceptionTranslator`. Weaver's workflow tools and context report the
same code, distinct from denied.

**Read evidence (built-in only).** At the end of a successful `ReadAsync`, the built-in
`WorkflowDefinitionDetailsReader` records internal, single-use evidence in the request's
`HttpContext.Items`: its own instance, its definition store, the principal, the definition id and
the stored tenant, and, when a library store is registered, the scoped permission service its Read
permission and organization checks used. The built-in authorizer's next decision in that request
takes the evidence and, only when the registered reader is that same instance, the store and
principal are the same, and the id and the principal's tenant match, skips its repeated Read
permission check (only when the reader made one), `FindByIdAsync` and `AuthorizeReadAsync`. A
Manage decision still checks Manage. Any mismatch, including any replacement reader, even one that
delegates to the built-in reader, gets the complete decision. A replacement authorizer never sees
the evidence. A failed read discards earlier evidence, so a built-in GET makes three Read
decisions on the definition, one per breadcrumb folder, one definition store read and one
organization lookup, where it made six, two per folder, two and two.

### `IWorkflowLibraryCommands` *(Contract — `Elsa.Workflows.Design.Core`, spec 1193 D4)*

The library's organization commands for an explicit `WorkflowLibraryActor(actorId, tenantId)`: `GetPermissionsAsync`,
`GetOrganizationAsync(definitionId)`, `ListTagsAsync(search)`, `CreateTagAsync(input, operationKey)`,
`FindTagByOperationKeyAsync`, `EditTagsAsync(edit)`, `SetFavoritesAsync(ids, favorite)`,
`MoveDefinitionsAsync(ids, folderId, operationKey)`, `ListViewsAsync`, `SaveViewAsync(viewId, input, operationKey)` and
`FindViewByOperationKeyAsync`. Each returns a `WorkflowLibraryCommandResult<T>` whose status is one of
`WorkflowLibraryCommandStatus` (`applied`, `denied`, `unavailable`, `conflict`, `not-found`, `invalid`, `unsupported`)
with a cause-free message; multi-target commands report one `WorkflowLibraryTargetOutcome` per target, and a folder move,
which the store applies atomically, reports the same outcome for every target. Delete and permanent delete are not offered.

The default, `WorkflowLibraryCommands`, is the library endpoints' own logic, extracted: the endpoints run its request-bound
methods over their injected store, exactly as before the extraction, and the Core contract runs the same methods. A
replacement `IWorkflowLibraryCommands` decides Weaver's library writes; the HTTP endpoints keep the built-in decisions.
For an explicit actor the default serves only the
current request's own authenticated subject and tenant (any other is `denied`), applies the permission the matching
endpoint's route gate applies (`ManageTags` for tag creation, `AssignTags` for tag edits, `Manage` for folder moves,
`Read` otherwise), and reports workflow organization disabled or an omitted store as `unavailable` with the backend's
reason before any actor or permission decision. Infrastructure failures and cancellation propagate. The Core create
shapes carry everything the library's own create accepts (a tag's and its values' deprecation and identities, which the
store validates as on its own create). A view's schema version is output only: the store creates version 1 and
`WorkflowLibraryViewSnapshot.SchemaVersion` reports the stored version; a view save never supplies it. A create's operation key is optional (none is the
library's ordinary non-idempotent create; Weaver always passes one); a blank key, or a key on a view update, is `invalid`.
One registration per shell: a second one fails configuration and shell startup (`WorkflowLibraryCommandsStartupTask`).

### `IWorkflowVersionReader` *(Contract — `Elsa.Workflows.Design.Core`, spec 1193 D4)*

Exact, read-only reads of persisted workflow definition versions: `ReadAsync(versionId)` returns
`WorkflowVersionDetails(VersionId, DefinitionId, Version, DefinitionName, State)` or `null` for an unknown version (a
synthetic `draft:` identifier is never looked up), `FindDefinitionIdAsync(versionId)` returns only the definition a version
belongs to (through the version store, without reading the version), so a consumer can ask the definition's owner first, and
`ListAsync(definitionId)` returns the definition's
`WorkflowDefinitionVersionSummary` rows in the store's order. The default, `WorkflowVersionReader`, reads through the same
`IWorkflowVersionDetailsReader` composition as `GET versions/{versionId}` (without the layout and presentation records) and
the same store list as `GET definitions/{definitionId}/versions`. It decides no authority: a consumer requires the host's
`workflows/design:read` and the owning `IWorkflowDefinitionAuthorizer.CanReadAsync` decision for the version's definition
before it discloses anything it read (Weaver's version context and `workflow_read_version` do both, owner first, so an
unknown version, another definition's version and a withheld definition are the same unavailable outcome). Infrastructure
failures and cancellation propagate. One registration per shell: a second one fails configuration and shell startup
(`WorkflowVersionReaderStartupTask`).

### `IWorkflowFolderDeletionStore` *(Contract — `Elsa.Workflows.Design.Persistence.Core`)*

Optional scoped single-implementation port for safe folder removal:
`PreviewFolderDeletionAsync(folderId, cancellationToken)` and
`DeleteFolderAsync(folderId, previewRevision, deleteWorkflows, actorId, operationKey, cancellationToken)`.
EF implements it on the library store; omission leaves the existing empty-only DELETE available.
Only a registered port advertises `workflow-folder-deletion-preview` (GET) and
`workflow-folder-delete` (POST), and both disappear when organization is disabled.

Preview returns authoritative bounded direct workflow targets, affected descendant folders,
destination and an opaque revision. The API checks Read/Manage on canonical returned resource identities, ancestor
visibility and the selected `IWorkflowDefinitionAuthorizer` before returning count-only inventory.
Commit repeats authorization and the provider must validate the preview again inside its atomic
hierarchy transaction. Never trust client-supplied counts or browser filters. EF limits direct
workflow targets to 10,000 and hierarchy targets to the existing folder bound.

Default removal moves direct workflow membership (including deleted workflows) and child folders
to the surviving parent/top level. Descendant paths/depths change, while hierarchy and workflow
identities remain intact. Name collisions reject the entire operation. The opt-in marks only
active, directly assigned workflows soft-deleted; already deleted facts, tags, favorites, authored
content, immutable versions, publication and runtime are preserved. Every move/deletion audit fact
commits with the mutation. Stale previews and provider failures cannot leave partial changes.

### `IWorkflowLibraryStore` *(Contract — `Elsa.Workflows.Design.Persistence.Core`)*

The scoped optional port owns catalog organization independently of authored workflow state.
Startup permits an omitted library port for existing backends and rejects multiple registrations;
backend ownership still rejects an implicit second owner of a selected port.
Consumers are the capability-discovered folder, paged-definition, organization, tag, saved-view and
favorite endpoints. `WorkflowLibraryCapabilitySource` advertises those relations only when the port
is registered. Omission retains legacy flat browsing and yields a truthful unsupported response on
direct requests to optional routes.

**Signatures:** `ListFoldersAsync(parentId, pageSize, cursor)`, `GetFolderAsync(id)`,
`CreateFolderAsync(name, parentId, actorId)`, `RenameFolderAsync(id, name, revision, actorId)`,
`MoveFolderAsync(id, parentId, revision, actorId)`, `DeleteEmptyFolderAsync(id, revision, actorId)`,
`MoveDefinitionsAsync(ids, folderId, actorId, operationKey)`,
`ListDefinitionsAsync(query, subjectId, readScope)`, `GetVersionLabelsAsync(versionIds)`, `GetOrganizationAsync(definitionId)`,
`ReplaceTagsAsync(definitionId, revision, tags, actorId, operationKey)`,
`EditTagsAsync(edit, actorId)`, `ListTagsAsync(search, pageSize, cursor)`, `GetTagAsync(id)`,
`CreateTagAsync(input, actorId)`, `CreateTagAsync(input, actorId, operationKey)`, `FindTagByOperationKeyAsync(operationKey)`,
`UpdateTagAsync(id, input, actorId)`, `ListViewsAsync(subjectId)`, `SaveViewAsync(id, input, subjectId, canShare)`,
`SaveViewAsync(id, input, subjectId, canShare, operationKey)`, `FindViewByOperationKeyAsync(operationKey, subjectId)`,
`DeleteViewAsync(id, revision, subjectId, canShare)` and `SetFavoriteAsync(definitionId, favorite, subjectId)`;
all operations are asynchronous and accept cancellation.

**Operation keys (spec 1193 FR-021, Minor, additive):** the keyed `CreateTagAsync` and `SaveViewAsync` overloads and the two
`Find…ByOperationKeyAsync` rereads are default interface members whose default throws
`WorkflowLibraryOperationKeyUnsupportedException` (a `NotSupportedException` with a fixed message), so an existing
replacement keeps compiling and a keyed create against it is unavailable (501 on the API), never silently non-idempotent.
A store that implements them keeps the key unique per tenant scope for tags and per tenant scope and owning subject for
views: the same key with the same input returns the resource that key created and writes nothing; the same key with
different input is a `WorkflowLibraryConflictException`; the reread returns the key's resource or null. The client key is
never the store writer's own operation key. A keyed view save creates only (`id` null); an update stays bound by its revision.

**Override obligations:** replace one scoped implementation deliberately. Enforce the immutable
ordinary tenant persistence scope for every identity and query; bind cursors to scope, actor and
query; apply every membership predicate before bounded paging/counting; preserve hierarchy,
revision, retry, lifecycle and source-reconciliation guarantees. Folder moves are atomic across
the explicit selection; bulk tag outcomes are atomic per target. Views/favorites are isolated by
server-derived subject. Folder placement during creation participates in
`IAddWorkflowDefinitionCommand.Execute(..., folderId, actorId, cancellationToken)` in the same
committing operation as definition/draft creation. No organization operation changes authored
state or runtime behavior.

`POST tags` and `POST views` accept an optional body `operationKey` (1–256 characters) and then call the keyed overloads:
a repeat with the same key and body returns the existing tag or view (200), the same key with a different body is 409,
and a store without key support answers 501. A request without a key is unchanged.

The API derives actors through `IAuthSessionService` and evaluates permissions through
`IPermissionAuthorizationService`, including per-target host resource handlers. Host handlers receive
`WorkflowLibraryResource` with a definition or folder identity. Before querying the library, the API
obtains `WorkflowLibraryReadScope` from the host's `IWorkflowLibraryVisibilityPolicy.GetScopeAsync(
ClaimsPrincipal, CancellationToken)`. The scope has a nonblank revision (at most 256 characters), an
optional allow-list and an optional deny-list (each at most 10,000 bounded definition identities).
The persistence query applies this trusted scope before counts, ordering, page selection and cursor
generation. The provider binds the policy revision and identity membership into continuation tokens.
Clients and saved views cannot supply or replace the scope.

The host policy must cover all current resource visibility rules for the normalized principal and
change revision when its meaning or result changes. If any `IPermissionResourceHandler` is registered
and no query visibility policy is composed, the API refuses the page before invoking the store and
returns neither rows, counts nor cursors. Hosts with only tenant/action authorization and no resource
handlers use an unrestricted tenant query scope. Per-item authorization remains a second defense;
filtering an already completed page is not a supported substitute for query visibility.

Paged rows retain the library query's membership/count and enrich only that bounded page through
`IWorkflowDefinitionPublicationStateLookup`. `GetVersionLabelsAsync` resolves at most 100 explicitly
referenced version identities to labels without loading authored payloads or complete history.
Publication membership indexes are not a substitute for current lifecycle evidence; absent lookup
evidence leaves publication state unknown. Soft-deleted organization remains readable, with
assignment and placement action hints disabled.

The permission contributor owns separate `workflows/design/tag-assignments:manage`,
`workflows/design/tags:manage` and `workflows/design/views:share` catalog entries. Ordinary workflow
read permission supports private views and personal favorites. Shared-view permission grants no
workflow visibility. Permission hints in library responses are advisory; each endpoint and target
is authorized again on the server. No new event contributor or periodic task is introduced here.

### `IWorkflowDefinitionLookup` *(Core — `Elsa.Workflows.Design.Core`)*
- **Signature:** `GetDefinition(id)`, `ListDefinitions(searchTerm?)`, `GetVersion(versionId)`, `FindLatestVersion(definitionId)`, `ListVersions(definitionId)` — all `Task`-returning reads.
- **Default impl:** `WorkflowDefinitionLookup` (`Elsa.Workflows.Design.Persistence.Core`), backed by the named `IWorkflowDefinitionVersionStore` + `IWorkflowDefinitionStore` read ports.
- **Override:** `services.Replace(ServiceDescriptor.Scoped<IWorkflowDefinitionLookup, MyLookup>())`. Or override one of the underlying named read ports (see [`Elsa.Workflows.Design.Persistence.EntityFrameworkCore/EXTENSION_POINTS.md`](../Persistence/EntityFrameworkCore/EXTENSION_POINTS.md)) — two granularities of the same *override* axis.

### Commands — `IUpdateDraftCommand` + 5 lifecycle *(Core — `Elsa.Workflows.Design.Persistence.Core`)*
Full detail in the persistence catalog: [`Elsa.Workflows.Design.Persistence.EntityFrameworkCore/EXTENSION_POINTS.md`](../Persistence/EntityFrameworkCore/EXTENSION_POINTS.md).

Summary: `IUpdateDraftCommand`, `ICreateDraftCommand`, `ICloneDraftFromVersionCommand`, `IDiscardDraftCommand`, `IPromoteDraftToVersionCommand`, `IAddWorkflowDefinitionCommand` — each backed by an EF Core default; `services.Replace(...)` to swap individual commands while keeping the rest (the canonical *swap-commands-keep-queries* example per Joey's framing). A replacement `IPromoteDraftToVersionCommand` keeps its content precondition: it takes the `WorkflowDraftStateHash` of the draft the Drafts/Promote route admitted and refuses with `WorkflowDraftChangedException` (409) when the draft it promotes no longer has that content (spec 188, research R7).

### `IDraftStateDiffEngine` *(Contract — `Elsa.Workflows.Design.Persistence.Core`)*
- **Signature:** `IReadOnlyList<IEvent> Evaluate(string draftId, WorkflowDefinitionState stored, IReadOnlyCollection<DesignMetadataRecord> storedLayout, WorkflowDefinitionState desired, IReadOnlyCollection<DesignMetadataRecord> desiredLayout)`
- **Default impl:** `DraftStateDiffEngine`. Override to change emission / matching.

### `IWorkflowDefinitionPublicationStateLookup` *(Contract — `Elsa.Workflows.Design.Persistence.Core`)*
- **Signature:** `ListByDefinitionIdsAsync(workflowDefinitionIds)` returns a dictionary of `WorkflowDefinitionPublicationSummary` values: `State` (`Draft`, `Published`, `Retracted`, `Retired`) and optional `PublishedVersionId`, the active publication's immutable source version ID. An absent entry means the provider cannot establish publication state.
- **Default impl:** `NullWorkflowDefinitionPublicationStateLookup` in `Elsa.Workflows.Design.Api.Services`, so MinimalEndpoints `GetRequiredService` succeeds on a design-only host and list/detail views leave `publicationState` unset. Publishing `Replace`s it with `WorkflowDefinitionPublicationStateLookup`. Live and empty slots cover Published and Retracted on every store. The source version comes from the active configured primary slot, or a unique version shared by all valid active publications when no primary is active. Conflicting or unavailable records leave the source version unset. Without a slot, Retired needs journal evidence and Draft needs a complete definition-keyed journal lookup; unsupported lookup leaves the state unset. The in-memory journal supports this lookup; the EF ledger lacks a definition-id hash — tracked by [#2801](https://github.com/elsa-workflows/elsa-foundation/issues/2801).
- **Override:** `services.Replace(ServiceDescriptor.Scoped<IWorkflowDefinitionPublicationStateLookup, MyLookup>())`. Do not add a Design.Api → Publishing project reference; the lookup is the bridge.

### Domain-model abstractions
`IWorkflowDefinition`, `IWorkflowDefinitionVersion`, `IWorkflowDefinitionDraft`, `IWorkflowDefinitionLayout`, `IWorkflowGraph` are read-model abstractions (framework §2.1). A custom persistence provider realises them; application code does not replace them piecemeal.

---

## Implementable contributor interfaces

The Draft-validation contributor (`IDraftValidator`) lives in [`Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md`](../Validations/Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md). Subscribers also extend this domain by handling the Background events below.

### `IExpressionAuthoringContextSource` *(Core — `Elsa.Workflows.Design.Core`)*

- **Kind:** Authoritative location-to-metadata contributor.
- **Registration:** add implementations as `IExpressionAuthoringContextSource`; the scoped
  `IExpressionAuthoringContextService` selects the first owning source and bounds/paginates its
  output.
- **Boundary:** validate the requested draft/node/property against authored Design state, apply
  authorization and host policy before returning metadata, and never evaluate expressions or
  disclose runtime values.

### `IExpressionAuthoringContextFilter` *(Core — `Elsa.Workflows.Design.Core`)*

- **Kind:** Permission and Host Policy location/metadata filter (add-don't-replace).
- **Registration:** add filters as `IExpressionAuthoringContextFilter`; the persisted context source
  invokes them after location authority and before symbol enumeration.
- **Boundary:** deny the location by returning `null`, never add live values, and **must** stamp an
  opaque `PolicyFingerprint` whenever the filter depends on Host Policy.
  The persisted source incorporates that fingerprint into the context/catalog revision, so a policy
  change makes in-flight requests stale and invalidates client metadata caches.

### `IExpressionAuthoringSymbolFilter` *(Core — `Elsa.Workflows.Design.Core`)*

- **Kind:** Streaming Permission and Host Policy symbol filter (add-don't-replace).
- **Registration:** add filters as `IExpressionAuthoringSymbolFilter`; each candidate is passed
  through the chain before it enters the bounded context catalog.
- **Boundary:** return `null` to omit a restricted symbol, never add runtime values, and pair
  policy-dependent symbol filtering with an `IExpressionAuthoringContextFilter` that stamps the
  corresponding opaque `PolicyFingerprint`.
- **Scale:** filtering is applied while candidates are enumerated, so the source retains at most
  the 5,500 authorized symbols required by the provider boundary and can stop work at that bound.

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
  - `ProjectChildren(activity)` / `ReplaceChildren(activity, projections)` — expose and rewrite the activity's child slots. Each non-persisted `ActivityChildProjection` can also carry `OutcomeProjections`: per-child `ActivityChildOutcomeProjection` declarations with the expected activity type, additional outcome names and an optional literal-outcome input key. Publishing checks the resolved child type before pinning those names; the structure handler owns payload interpretation.
  - `CompileExecutableStructure(activity)` — materialize the authored structure into the executable structure the runtime consumes.
  - `SupportsScopedVariables` *(default `false`)* — whether this activity is a **container scope** that can own container-scoped variable declarations (ADR 0027). Container activities (`Sequence`, `Flowchart`) return `true`.
  - `ProjectScopedVariables(activity)` *(default empty)* — the container-scoped `VariableDefinition`s the activity declares, visible to its descendant activities. These flow through publishing into the compiled executable structure's `variables`, so the runtime materializes the visible variable scope chain (`RuntimeVariableScopeFactory` / `RuntimeContainerScopeService` in `Elsa.Workflows.Runtime.Core`) without re-reading the design document.
  - `ChildrenCompletedBefore(activity, childNodeId)` *(default `null`)* — legacy completion declarations, most relevant first. The default richer assessment adapts an explicitly listed producer to Definite; an absent declaration promises no order. The shared analyzer retains this ranking for expression/source discovery, including Compensation's own and prior forward results.
  - `AssessChildResult(activity, producerChildNodeId, consumerChildNodeId)` — owner-supplied `ActivityChildResultFacts`: availability, cyclic-backedge, possible concurrency and mutual exclusion. Its conservative default adapts `ChildrenCompletedBefore`; undeclared order stays Unknown and absent nullable flags supply no safety proof. `IActivityStructureService` delegates to the installed owner. `ActivityDataSelectionAnalyzer` uses these facts for direct/portable admission and discovery; `ValueFlowValidator` separately uses ordering/concurrency/exclusion to assess colliding writes. Unknown owner facts do not certify write safety.
  - `AuthoredPayloadType` *(default `null`)* — the CLR model type of the handler's *authored* structure payload. The structure-kind registry endpoint (`GET design/workflows/structures`) publishes a generated JSON Schema per kind from this type so headless clients can compose structure payloads without reading source; `null` keeps the payload opaque/undocumented.
- **Aggregation:** `IActivityStructureService` (`DefaultActivityStructureService`) resolves the registered handlers by `Kind`. `ScopedVariableResolver` walks the authored tree through this contract to compute per-node scoped-variable visibility.
- **Registration:** register an implementation with DI as `IActivityStructureHandler` from the activity's own module.

Known implementations:
- `SequenceStructureHandler` (`Elsa.Activities.Sequence`) *(cross-domain — scoped variables and document-order child-result assessment)*.
- `FlowchartStructureHandler` (`Elsa.Activities.Flowchart`) *(cross-domain — scoped variables; parsed, runtime-normalized entry/dominance/backedge/concurrency/exclusion facts over all legal entries)*.
- `BpmnStructureHandler` (`Elsa.Activities.Bpmn.Design`) *(cross-domain — authored BPMN children/outcomes, scoped variables and executable graph lowering)*.
- `CompensationScopeStructureHandler` (`Elsa.Activities.Compensation`) *(cross-domain — implements `ChildrenCompletedBefore`: a handler sees its own step's result first)*.

---

## Events

Every event the Workflows.Design domain publishes is an `IEvent` (framework §2.6.1), grouped by **delivery strategy** (§2.6.6).

**Sequential / contribution** — none in this domain's Core. The contribution event (`DraftValidating`, Sequential) lives in [`Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md`](../Validations/Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md).

Heading convention: `### <EventClassName>`. Every published `IEvent` type of the `Elsa.Workflows.Design.Core` design namespace has a heading here, and every such heading names one; review keeps the two in step.

**Background / notification** (§2.6.6): published through `IDeferredEventPublisher`, which queues the event and returns before handlers run; the Events domain's background worker drains the queue asynchronously. Subscriber exceptions are caught + logged; one flaky subscriber cannot break the publish.

**Lifecycle — origination + disposal.**

### DraftCreated

**Semantic.** A freshly-created `WorkflowDefinitionDraft` has been persisted — the single origination marker regardless of how the Draft was born. A cloned Draft emits this same event; `SourceVersionId` distinguishes the two origins.
**Payload.** `DraftId : string`, `WorkflowDefinitionId : string`, `SourceVersionId : string?` — the `WorkflowDefinitionVersion` the Draft was cloned from, or `null` for a fresh Draft.
**Publication site.** `ICreateDraftCommand` implementation, after the draft is committed + lock release. `ICloneDraftFromVersionCommand` reaches it by delegation.

### DraftDiscarded

**Semantic.** A `WorkflowDefinitionDraft` was atomically deleted along with its layout sibling. Terminal entry on the Draft's event stream.
**Payload.** `DraftId : string`, `WorkflowDefinitionId : string`.
**Publication site.** `IDiscardDraftCommand`.

**Mutation events — declared as tested contract; publication currently retired pending an event-sourcing consumer.** The event types below are the per-diff mutation-event surface computed by `IDraftStateDiffEngine` from the desired `WorkflowDefinitionState` (+ layout sibling) versus the stored snapshot. They have no subscriber today, so `IUpdateDraftCommand` no longer computes or publishes them and the diff engine is no longer registered in DI; the event records and the engine remain in place as the tested contract, to be re-wired when the FR-017/FR-018 event-sourcing consumer is built. The "Publication site" notes below describe the *intended* per-diff emission for that future wiring. Match keys: activities by `NodeId`, I/O by (`NodeId`,`ReferenceKey`), variables/inputs/outputs by `ReferenceKey`, layout by `NodeId`. Flowchart graph connection events belong to the future Flowchart activity module, not to Workflows.Design Core.

**Activities.**

### ActivityAddedToDraft

**Payload.** `DraftId : string`, `Activity : ActivityNode` (with derived `NodeId`, `ActivityVersionId` projections).
**Publication site.** `IUpdateDraftCommand` — emitted when a desired activity `NodeId` is absent from stored state.

### ActivityRemovedFromDraft

**Payload.** `DraftId : string`, `NodeId : string`.
**Publication site.** `IUpdateDraftCommand` — emitted when a stored activity `NodeId` is absent from desired state.

### ActivityMovedInDraft

**Semantic.** Layout-position / size change for a placed activity.
**Payload.** `DraftId : string`, `NodeId : string`, `NewX : double`, `NewY : double`, `NewWidth : double?`, `NewHeight : double?`.
**Publication site.** `IUpdateDraftCommand` — emitted when desired layout differs from stored.

**Per-activity inputs — full CRUD.**

### ActivityInputAddedToDraft

**Payload.** `DraftId : string`, `NodeId : string`, `Input : ArgumentState`.
**Publication site.** `IUpdateDraftCommand` — desired (`NodeId`,`ReferenceKey`) input absent from stored.

### ActivityInputUpdatedInDraft

**Payload.** `DraftId : string`, `NodeId : string`, `InputReferenceKey : string`, `OldValue : ArgumentState`, `NewValue : ArgumentState`.
**Publication site.** `IUpdateDraftCommand` — matched input payload changed.

### ActivityInputRemovedFromDraft

**Payload.** `DraftId : string`, `NodeId : string`, `InputReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — stored (`NodeId`,`ReferenceKey`) input absent from desired.

**Per-activity outputs — full CRUD.**

### ActivityOutputAddedToDraft

**Payload.** `DraftId : string`, `NodeId : string`, `Output : ArgumentState`.
**Publication site.** `IUpdateDraftCommand` — desired (`NodeId`,`ReferenceKey`) output absent from stored.

### ActivityOutputUpdatedInDraft

**Payload.** `DraftId : string`, `NodeId : string`, `OutputReferenceKey : string`, `OldValue : ArgumentState`, `NewValue : ArgumentState`.
**Publication site.** `IUpdateDraftCommand` — matched output payload changed.

### ActivityOutputRemovedFromDraft

**Payload.** `DraftId : string`, `NodeId : string`, `OutputReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — stored (`NodeId`,`ReferenceKey`) output absent from desired.

**Variables — full CRUD.**

### VariableDeclaredInDraft

**Payload.** `DraftId : string`, `Variable : VariableDefinition`.
**Publication site.** `IUpdateDraftCommand` — desired variable `ReferenceKey` absent from stored.

### VariableUpdatedInDraft

**Payload.** `DraftId : string`, `VariableReferenceKey : string`, `OldValue : VariableDefinition`, `NewValue : VariableDefinition`.
**Publication site.** `IUpdateDraftCommand` — matched variable payload changed.

### VariableRemovedFromDraft

**Payload.** `DraftId : string`, `VariableReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — stored variable `ReferenceKey` absent from desired.

**Workflow inputs — full CRUD.**

### WorkflowInputAddedToDraft

**Payload.** `DraftId : string`, `Input : InputDefinition`.
**Publication site.** `IUpdateDraftCommand` — desired workflow-input `ReferenceKey` absent from stored.

### WorkflowInputUpdatedInDraft

**Payload.** `DraftId : string`, `InputReferenceKey : string`, `OldValue : InputDefinition`, `NewValue : InputDefinition`.
**Publication site.** `IUpdateDraftCommand` — matched workflow-input payload changed.

### WorkflowInputRemovedFromDraft

**Payload.** `DraftId : string`, `InputReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — stored workflow-input `ReferenceKey` absent from desired.

**Workflow outputs — full CRUD.**

### WorkflowOutputAddedToDraft

**Payload.** `DraftId : string`, `Output : OutputDefinition`.
**Publication site.** `IUpdateDraftCommand` — desired workflow-output `ReferenceKey` absent from stored.

### WorkflowOutputUpdatedInDraft

**Payload.** `DraftId : string`, `OutputReferenceKey : string`, `OldValue : OutputDefinition`, `NewValue : OutputDefinition`.
**Publication site.** `IUpdateDraftCommand` — matched workflow-output payload changed.

### WorkflowOutputRemovedFromDraft

**Payload.** `DraftId : string`, `OutputReferenceKey : string`.
**Publication site.** `IUpdateDraftCommand` — stored workflow-output `ReferenceKey` absent from desired.

---

## Cross-references

- Validation events (`DraftValidating` Sequential gate + `DraftValidated` Background outcome): [`Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md`](../Validations/Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md).
- Command + diff-engine overridables: [`Elsa.Workflows.Design.Persistence.EntityFrameworkCore/EXTENSION_POINTS.md`](../Persistence/EntityFrameworkCore/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.6 + §2.22.1.


## Workflow authoring templates

`IWorkflowTemplateStore` (Design Persistence Core) is an optional scoped port. EF registers
`EfWorkflowTemplateStore` by default in `AddWorkflowsDesignEntityFrameworkCore`; it stores
`WorkflowTemplateContent` (authored state, layout and activity presentation) in the tenant-scoped
`elsa_workflow_templates` table. Consumers are the Design template endpoints and Studio catalog.
`ListAsync` / `FindAsync` read snapshots; `SaveAsync(DesignOperationKey, id?, name, description,
content, expectedRevision)` and `DeleteAsync(DesignOperationKey, id, expectedRevision)` use the
Design atomic writer. Updates/deletes require the last read revision; conflicts never overwrite.

Alternative Design backends may omit the port, or register exactly one implementation with the
same scope, snapshot, atomic-replay and revision guarantees. Explicit backend replacement removes
EF-owned descriptors, including this port. EF refuses a pre-existing template port and startup
refuses duplicate template registrations. The optional port is not a mandatory workflow backend
replacement contract. No template mutation creates workflow definitions, versions or executables.

`WorkflowTemplateCapabilitySource` advertises `workflow-templates`, `workflow-template` and
`workflow-template-delete` relations only when this port is present. Studio discovers these
relations instead of constructing routes; code-owned Blank/HTTP recipes remain client starters.

### `IActivityStructureHandler.ProjectInputs` *(Core — `Elsa.Workflows.Design.Core`)*

- **Kind:** Contribution keyed by structure kind and schema version; `IActivityStructureService` routes to the owning handler.
- **Signature:** `ActivityStructureInputProjection ProjectInputs(ActivityNode activity)`, defaulting to an empty projection for existing handlers.
- **Obligations:** project stable owner-derived reference keys, normal `InputDefinition` declarations and optional `ArgumentState` values. Declare consumed ordinary catalog keys explicitly; a null projected definition uses that catalog declaration. Publishing normalizes state keys, rejects duplicate declarations and compiles through the ordinary typed binding compiler. Expression authoring and draft validation use the same projection. Keep child traversal/replacement lossless for structure-owned arguments and unknown members.
- **Known implementation:** Switch schema 2.0.0 projects Boolean case predicates, consumes Value and reintroduces it only when legacy matching remains. No Runtime project reads the Design projection.
