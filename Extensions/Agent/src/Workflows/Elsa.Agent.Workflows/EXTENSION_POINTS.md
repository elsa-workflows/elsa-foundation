# Workflow Agent extension points

Owner project: `Elsa.Agent.Workflows`
Domain: `Elsa.Agent.Workflows`

## Cross-domain contributions

The module contributes to [Agent foundation](../../Elsa.Agent/EXTENSION_POINTS.md) through contracts in `Elsa.Agent.Core`.

- `IAgentContextProvider`: `DefaultWorkflowAgentContextProvider` (`internal sealed`) admits `workflow.definition` schema `1` for exact `workflow-definition` targets. It whitelists bounded activity identities, connections and positions, stamps caller material as `ClientSnapshot`, and emits installed guidance separately. Caller permission claims, arbitrary fields and every property value outside the selected activity's bounded property context are excluded; the designer's diagnostics and that property context are admitted strictly, as described under [Explain and troubleshoot](#explain-and-troubleshoot-spec-1193-slice-d). A separate `workflow.request` schema `1` admits only bounded intent/name/description/reference metadata with no target, client binding or tool scopes; it never supplies graph authority. Other workflow kinds and versions produce explicit diagnostics. With admitted workflow context it also contributes two server-produced kinds, described under [Truth seams](#truth-seams-spec-1193); a caller cannot supply either.
- `IAgentCapabilityProvider`: `WorkflowAgentCapabilityProvider` (`internal sealed`) declares explanation of the admitted graph and a reviewed change journey (a position change or one Operation Batch, below). It does not advertise runtime investigation, save or publish.
- `IAgentTool`: `WorkflowReadGraphTool` returns a typed `workflows.graph` result: the admitted snapshot plus each activity's scope-path address (slice D). `WorkflowSetDesignerPositionTool` prepares exact `activityId`, finite `x`/`y`, target revision and originating client generation; it requires current `workflows/design:manage` permission and explicit review even under FullAuto.
- `IAgentClientTool`: the same layout tool admits the owning editor's observed result as `workflows.layout-change` schema `1`, renderer `workflows.agent.layout-result`. The receipt proves unsaved local state only. Native editor application and whole-batch undo remain module-owned; save and publish remain explicit.
- `IAgentToolAuthorization`: both installed tools also implement this optional companion. The graph tool asks the owner `IWorkflowDefinitionAuthorizer.CanReadAsync`; layout asks `CanManageAsync`. Generic orchestration combines this with host authority during advertisement, prepared invocation admission, duplicate delivery and reviewed dispatch/completion. No second tool registry is installed. When the owner throws `WorkflowDefinitionAuthorizationException`, the tools and the context provider fail closed as unavailable with the Design API's code `workflow.authorization.unavailable`, distinct from `agent.workflow.permission_denied`, and disclose no cause. The tools report an owner-unavailable violation, so dispatch reports 503. The context provider reports an owner-unavailable required diagnostic, so collection and turn admission, including the re-admission before each tool call, fail with that code and 503 instead of `agent.context.incomplete`. A denial keeps `agent.context.incomplete` with 400. `agent.workflow.authority_unavailable` keeps its meaning: zero or several authorizers are composed.

The named context/capability/tool fan-in and generic review lifecycle are owned by Agent foundation. This module registers contributions, not additional aggregating handlers or provider loops.

This is the accepted M1 workflow subset. The [76ec independent package witness](../../../../../specs/1174-weaver-package-witness/evidence/qualification-76ec58d00.json) executes the original and restored position/validation/whole-Undo/explicit-Save journeys through both actual provider adapters. [Final-head qualification/CI, independent review, merge and resulting-main verification](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6539679143) completed through [#3497](https://github.com/elsa-workflows/elsa-foundation/pull/3497); Program M2–M5 and durable recovery remain open. Graph batches and activity creation are delivered by [draft authoring](#draft-authoring-spec-1193-slice-c), and explanation and troubleshooting of the draft by [slice D](#explain-and-troubleshoot-spec-1193-slice-d); expression authoring belongs to Expression Modules, none of which registers Weaver tools yet; a position-change receipt does not establish those journeys. Preview package/API migration is documented in the [canonical contribution contract](../../../../../specs/1018-weaver-module-contributions/contracts/contributions.md#preview-migration).

## Truth seams (spec 1193)

The module reads two owning domains through their `.Core` contracts only, and registers no fallback for either:

- **Activity catalog.** `workflow.catalog` schema `1` (origin `AuthoritativeRead`, no target, no tool scopes) is the
  [`IActivityAuthoringCatalog`](../../../../../Essentials/src/Activities/Design/Api/EXTENSION_POINTS.md#iactivityauthoringcatalog-contract--elsaactivitiesdesigncore)
  answer for the request actor and tenant: every entry the designer lists, addable or not, with its `available` flag and
  reason, activity type and exact version identity, and declared inputs with `isRequired`, `isSensitive`,
  `isCredential`, `uiHint` and `isMasked` (what the designer masks: sensitive, a credential or hinted `password`; slice D,
  so a password-hinted input reads as masked here as it is withheld in the snapshot). It is bounded to 28 KiB of entries in the owner's order; when entries do not fit it says so with
  `truncated`, `totalCount` and `includedCount`, and reports `budgetBytes`, `includedBytes` and `nextEntryBytes`, so a
  reader can check that the prefix stopped only because the next entry would not fit. `status` is `available`, `denied` (the actor lacks
  `activities/design:read`; no entries) or `unavailable`. A missing, ambiguous or failing catalog owner is
  `unavailable` with the optional diagnostic `agent.workflow.catalog_unavailable`, whose message names the missing
  capability; explanation still works. An owner failure is logged at warning with its cause; the model sees only the fixed
  text.
- **Permissions.** `workflow.permissions` schema `1` (origin `AuthoritativeRead`) is contributed for each admitted exact
  `workflow-definition` target, with the snapshot's classification. `read` is `allowed` (the context was admitted on the
  owner's read decision); `manage` is `allowed`, `denied` or `unavailable` (the owner threw
  `WorkflowDefinitionAuthorizationException`), from the host `IAgentToolAuthorizer` for the layout tool and the owning
  `IWorkflowDefinitionAuthorizer.CanManageAsync`, the same decisions the engine uses to advertise tools. `tools` and
  `capabilities` follow: manage allowed adds `workflow_set_designer_position` and `workflow.propose-change`; otherwise
  the actor gets explanation only. `tools` follows the engine's advertisement rule for each tool: its own descriptor
  through the host `IAgentToolAuthorizer`, its owner decision, and the session's capability policy for its name
  (`IAgentPolicyEvaluator.EvaluateCapabilityAsync`, which applies `DeniedCapabilityIds`), so it names exactly what the
  engine will offer. `capabilities` reports each capability by its own id under that policy, independent of the tools, as the Agent API
  admits a requested capability; `workflow.propose-change` is reported only when `manage` is `allowed`, which is stricter
  than the API's admission. `manage` combines the host decision for the layout tool with the owner's answer: when the host
  `IAgentToolAuthorizer` refuses the layout tool the owner is not asked and `manage` is `denied`, so `denied` does not by
  itself mean the owner refused. A host that refuses `workflow_read_graph` withholds the whole workflow context with the
  required `agent.workflow.permission_denied` (disclosure is admitted on the read tool's descriptor); a host cannot keep the
  workflow context while withholding only the read tool.

Both server reads respect the session's host context policy. When `IAgentPolicyEvaluator.EvaluateContextAsync` does not
admit a kind (for example a policy whose `AllowedContextKinds` lists only the older workflow kinds), the module does not
contribute it: no read is made, no required failure follows, and the optional diagnostic
`agent.workflow.server_read_withheld` says which kind was left out. Explanation and the reviewed layout change keep
working exactly as before the server reads existed, and re-admission of a disclosure recorded before them adds none.

Both kinds are in the descriptor's `ContextKinds`. `AdmitDisclosureAsync` accepts a retained one only with the module's
own origin. For the current turn it never trusts a retained read: it reads the catalog and each target's permissions
again, under the same attachment identities and in the same positions (redacted items stay where they were), so an unchanged answer is the identical projection (no new disclosure
snapshot) and a changed one is a new snapshot under the session's disclosure limits. Historical admission keeps what was
disclosed and re-checks only the catalog's read gate (`IActivityAuthoringCatalog.CanReadAsync`) when entries were
disclosed: a lost read is the required `agent.workflow.permission_denied`, an owner outage the required
`activities.catalog.unavailable`, marked owner-unavailable. Receipt continuations are unchanged: guidance only.

Revision identity follows FR-005: `workflow.definition` already carries the exact target, the working-state content
revision and the client binding. No persisted revision is read in its place.

## Draft authoring (spec 1193 slice C)

- `IAgentTool` / `IAgentClientTool` / `IAgentToolAuthorization`: `WorkflowApplyOperationBatchTool` (`internal sealed`),
  `workflow_apply_operation_batch` contract `1`, execution location `ClientWorkingState`, result `workflows.batch-result`
  schema `1`, renderer `weaver.workflows.batch-result`, required permission `workflows/design:manage`, owner check
  `IWorkflowDefinitionAuthorizer.CanManageAsync` (unavailable stays `workflow.authorization.unavailable`, 503).
  - **Strict schema.** Eight operation kinds, each an object that refuses unknown fields: `add` (a new `ref`, the
    catalog `activityTypeKey` and exact `version`, an explicit `scopePath` of node ids from the root to the container and
    a `slot` the container discloses), `set-root` (installs a root candidate added in the batch), `position`,
    `set-property` (an explicit declared input; a `literal` or a visible `secretReference`; no default property),
    `update` (only `displayName`, `description` and `canStartWorkflow`; type, version and children are never rewritten),
    `remove`, `connect` and `disconnect`. Activities are addressed by `id`, by an earlier `ref`, or by display `name`.
    At most 64 operations; above the declared limit of 16 the review labels the batch high-complexity. A batch is never
    split or partly applied. The plan's `name` field is not offered: the designer's `ActivityNode` has no name, and its
    visible name is the `displayName` presentation record.
  - **Preparation.** Requires one admitted `workflow.definition` snapshot with structure (each activity's exact version,
    parent, slot and slots) and `workingState: "draft"`. Manage authority is decided before validation, so a reader
    gets the shared `agent.workflow.permission_denied` (explanation stays available) and learns nothing else. The
    catalog is read fresh from `IActivityAuthoringCatalog` when an operation needs it. Outcomes, each naming the failing
    operation, with nothing changed: `agent.workflow.batch.schema_invalid` (400), `agent.workflow.batch.reference_invalid`
    (dangling, reused or removed references, a new id the document still holds in a layout or presentation record, a
    root-scope add without `set-root`, a second `set-root`, an add into or a connection within an activity the same
    batch adds, an add into or a removal from a BPMN scope, cross-scope connections; 422),
    `agent.workflow.batch.clarification_required` (an absent, hidden or non-addable activity, an undeclared input, an
    ambiguous name with candidates by scope path, a Secret Reference that is not visible, an unreadable catalog; 422),
    `agent.workflow.batch.credential_literal` (with an offer to bind a visible Secret Reference; 422),
    `agent.workflow.batch.expression_unsupported` (expression edits belong to the Expression Module; 422),
    `agent.workflow.batch.target_not_editable` (a published or historical working state; 409),
    `agent.workflow.batch.structure_required` (409) and `activities.catalog.unavailable` (503). Existing non-addable
    activities stay editable.
  - **Risk and review.** Each operation is `ReviewRequired`, or `Destructive` for `remove`, `disconnect` and a
    `set-root` that replaces an existing root. The batch takes the highest. Preparation always returns
    `RequiresApproval`, so the batch is reviewed in every autonomy mode, including `full-auto`. The frozen arguments carry
    every resolved node id, the catalog `activityVersionId` of each add and each operation's risk, so Studio can recheck
    the classification against live working state and fail closed.
  - **Completion.** `CompleteAsync` admits only an exact observation: applied, a new working-state revision, the
    originating binding and approval digest, `appliedCount` equal to the frozen operations, the frozen added node ids
    in order, `baseRevision` equal to the prepared revision and `historyEntry: true`. The admitted result carries the
    server's own risk labels from the preview, never the client's. The receipt is `ClientWorkingState`: the batch is
    applied locally and not saved or published.
- **Snapshot additions.** `workflow.definition` schema `1` admits, when present, a compact `structure`: `versions` groups
  activity indexes (into `activities`) by exact `activityVersionId`, and `containers` lists the root and every activity
  that declares slots, each with its `parentId`, its `slot` in the parent and its `slots` (`id`, `mode`, `cardinality`,
  member `nodes` by index). Every activity has exactly one version and, except the root, exactly one slot; the result
  must be one tree. Optional `reservedIds` lists ids the document holds only in its layout or presentation records, so
  preparation never reuses one. It also admits the snapshot's `workingState` (`draft` or `historical`) and up to 64 visible
  `secretReferences` (name, type and scope only). The additions are copied only when sent, so an older snapshot admits
  unchanged, and activities keep their pre-existing `id`, `type` and `displayName` shape.
  - **Size.** The compact form keeps the 64 KiB content bound: measured on a chained flowchart with GUID catalog
    version ids, structure adds 946 bytes at 100 activities (24,716 to 25,662), 1,346 at 150 and 1,746 at 200
    (49,116 to 50,862). A per-activity encoding added about 150 bytes each and pushed 200 activities to 78,743. Studio
    leaves the structure out when the content would not fit the effective bound (the host's 32 KiB per attachment, see
    [slice D's size rule](#explain-and-troubleshoot-spec-1193-slice-d)), so a draft that fits without structure always
    still fits; explanation keeps working and a batch is then refused with `agent.workflow.batch.structure_required`
    (`workflowContextSize.test.tsx`).
  - **Visible Secret References** are, in this slice, the Secret References the draft already binds, which the user
    sees in the designer. No Secrets contract is consulted; a broader picker-backed list is later work.
- `workflow.permissions` adds `workflow_apply_operation_batch` to `tools` when manage is allowed, the host authorizes the
  batch tool's own descriptor and the session's capability policy allows its name.

- **Studio side** (not catalogued elsewhere; recorded here with the contract it serves). `Elsa.Studio.Workflows`
  registers the `workflow_apply_operation_batch` client tool contract with `clientTargetAvailability`, applies the frozen
  batch through the strict applier in `workflowGraphOperationBatch.ts` (the same field allowlists; unknown fields, a
  missing property name, structural property names and credential literals refused), rechecks each operation's risk
  against the live draft and fails closed on any disagreement, and compares definition, SHA-256 content revision
  (`sha256:` plus 64 hex digits, from a module-owned synchronous SHA-256), editor generation, selection and scope frame
  before replacing the draft. Designer history records the batch as exactly one entry after flushing pending capture
  from the latest committed draft; each entry carries its line's batch ids, and autosave is held while the current
  line holds a batch the user has not explicitly saved. The `workflow.definition` snapshot's revision is now the
  SHA-256 content revision; the 32-bit hash remains for change detection only. `Elsa.Studio.Weaver.Workflows` renders
  `workflows.batch-result` (`weaver.workflows.batch-result`) with each operation's risk label, registered under its own
  module identity with `resourceModuleIds: ["Elsa.Studio.Workflows"]`. The designer-history Undo and the live saved
  state ("Persisted by your explicit save") are Studio Workflows' result control `workflows.agent.batch-undo`
  (`api.agent.resultControls`), which the host renders after the renderer. Preparation and the applier share one case
  file, `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowBatchConformance.json`, run by
  `WorkflowBatchConformanceTests` and `workflowBatchConformance.test.ts`.

**SemVer: Minor.** A new tool contract, result kind and optional snapshot fields; nothing existing changes meaning.
The working-state revision string changes format (a 32-bit hex hash becomes `sha256:<64 hex>`); it is an opaque
identity that consumers compare for equality only, so this is Minor.

## Explain and troubleshoot (spec 1193 slice D)

`workflow.definition` schema `1` admits three further optional additions from the open editor, each read strictly by
`WorkflowAgentExplainContext`: an unknown field, a wrong type or a bound exceeded refuses the whole snapshot with
`agent.workflow.context_invalid`. All three are resource content (untrusted data, never guidance); the installed guidance
says so.

- **Diagnostics** (FR-018). `diagnostics`: at most 64, in the designer's order, each `severity` (`error` or `warning`),
  `message` (at most 1,024 characters) and optional `type`, `nodeId` and `property` (an input reference key; only with a
  `nodeId`). `diagnosticsOmitted` counts the rest. Studio sends exactly what its validation panel shows (server draft
  validations, the draft's reconciled errors and the designer's structural checks, humanized); Weaver runs no validation.
- **Selected activity property context** (FR-016, FR-017). `selectedActivity`: the selected node (it must equal
  `selectedNodeId`) and its exact `activityVersionId` (it must equal the structure's), `truncated`, and at most 64
  declared properties, each `name`, `referenceKey`, `typeName`, `isCredential`, `isSensitive` and a `value` that is one of
  `unset`; `expression` (`expressionType`, `content` of at most 2,048 characters, `truncated` exactly when `length`
  exceeds the content); `withheld` (`expressionType` only: a sensitive or credential value is present and never
  disclosed, as is a Secret syntax that names no Secret); or `secretReference` (name, type and scope). A sensitive input
  never carries content, a credential input is always sensitive, and the properties carry at most 16 KiB of content
  together. On admission the server stamps `interpretation: "unavailable"` on every non-literal expression: interpretation,
  semantic diagnostics and edits belong to the owning Expression Module's Weaver Tools (Studio ADR 0002), and they are
  unconditionally unavailable here: no contract maps an expression type to an Expression Module's tools, so routing is
  deferred to [#3711](https://github.com/elsa-workflows/elsa-foundation/issues/3711); an editor cannot claim another status. During collection the property context is checked against
  the owning catalog's declared inputs for that exact version (read once per collection and shared with the
  `workflow.catalog` attachment): an undeclared input, a different credential flag, an entry of another activity type than the activity's own, or a catalog-masked input (`IActivityAuthoringCatalog` input `IsMasked`: sensitive, a credential or hinted `password`) reported
  as not sensitive refuses the snapshot; an editor may mask more, never less. A property context requires the structure,
  which binds the selected node to its exact version; without it the snapshot is refused (Studio drops the property
  context before the structure). Studio discloses it only from the descriptor of the node's exact catalog version. When the catalog cannot
  answer for the version (denied, unavailable or not listed) the property context is left out and the optional
  `agent.workflow.property_context_unverified` diagnostic says so; the draft is still explained.
- **Graph activities** (FR-014). `graphActivities`: at most 64 references, each the `activityDefinitionId`, exact
  `activityVersionId` and `version`, and the activity indexes that use it (each index at most once, and in the
  structure's version). A graph activity's body belongs to its definition and is never inlined. Studio lists only
  design-authored versions (catalog provenance `ActivityDefinitionDraft`): the native witness on 999942f18 showed that
  recommended definitions also give CLR rows (Flowchart, WriteLine) a definition identity, which must not make them graph
  activities. A Json-sourced provider-defined composite is not listed either: the catalog view carries no provider identity
  to tell it from a plain Json activity (labelling it would need the catalog to disclose the provider). The server cannot
  confirm provenance (the Core catalog entry carries none), so this rule is Studio's.
- **Scope paths** (FR-014). `workflow_read_graph` returns the admitted snapshot plus `addresses`: for each activity, its
  `scopePath` (the container ids from the root to the container that holds it, as an Operation Batch names a scope) and
  its `slot`; the root's path is empty. The addresses are bounded to 24 KiB, and `addressesTruncated` says when some were
  left out.
- **Size.** Two bounds apply: this module's 64 KiB on the content the editor sends, and the host's 32 KiB on each
  attachment's serialized admitted projection (`AgentContextServices`: `JsonSerializer.Serialize(content).Length`), which
  is the effective one. Studio's single snapshot builder (`buildWorkflowSnapshotContent`) estimates the host's measure of
  the admitted projection (`admittedSnapshotLength`: admitted fields only, the default encoder's escapes, the stamped
  interpretation) and, while it exceeds 32 KiB less 256 characters, leaves out the property context, then the diagnostics
  (their count stays in `diagnosticsOmitted`), then the structure. The estimate equals the host's measure on the shared
  fixture (`workflowExplainSnapshot.json` records it; `WorkflowExplainContextTests` holds the server to it). On a chained
  flowchart with GUID catalog versions the structure is kept up to 120 activities (32,496 characters admitted), left out
  from 121 (31,695 without it), and from 126 activities (32,995 even without structure) the snapshot does not fit the
  host budget at all and is refused (`agent.context.budget_exceeded`); `workflowContextSize.test.tsx` pins these. Lifting
  that limit is [#3710](https://github.com/elsa-workflows/elsa-foundation/issues/3710).
- **Expression edits** stay refused at preparation (`agent.workflow.batch.expression_unsupported`), consistent with the
  unavailable interpretation.
- **Guidance** names the revision rule (an explanation names the revision it read and never attributes an answer to
  another), takes validation findings only from the designer's diagnostics, describes addresses, graph activity
  references, withheld values and unavailable interpretation, and marks workflow content as untrusted.
- **Registered references** (FR-019, SC-007). The composed server registrations (context source and kinds, capabilities,
  tools with their result kinds, renderers and execution locations) are recorded in
  `Extensions/Weaver/studio/Elsa.Studio.Weaver.Workflows/Client/src/__tests__/workflowAgentRegistrations.json`.
  `WorkflowExplainContextTests` holds it equal to the composed host and checks that the guidance, tool and capability
  texts name only registered tools and kinds; the Weaver Workflows Vitest `registeredReferences.test.tsx` composes both
  Workflows Studio packages and checks every prompt, starter, renderer, result control and client tool contract against
  it, and each contribution's module identity. Studio's snapshot and the server's admission share
  `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowExplainSnapshot.json`.
- **Studio side.** `Elsa.Studio.Weaver.Workflows` no longer registers the `workflows.proposal` renderer (no tool
  produced that kind, and it was registered under the Workflows module's identity), the explain-executable prompt (it
  named a `get-executable-detail` tool that was never registered; slice F restores it against `workflow_read_executable`)
  or the test-only `getExecutableDetail` helper, nor the explain-instance prompt (it named instance state, incidents and
  bookmarks with no instance context or tool; slice G restored it against the Runtime-side `workflow_read_run` and the
  run page's `workflows.run` context). Its propose-update prompt now asks for one reviewed Operation Batch.

**SemVer.** The snapshot additions, the read result's `addresses` and the new diagnostic code are **Minor** (optional,
additive). The removed `workflows.proposal` renderer and the explain-executable and explain-instance prompts are
**Major** for any host that dispatched them (pre-GA cleanup, constitution §4.2). The removed `ai-context-providers` and
`ai-tools` manifest tags are a Patch correction (no such contribution ever existed).

## Library, create dialog and templates (spec 1193 slice E)

**Context source `workflows.library-context`** (`WorkflowLibraryContextProvider`, `internal sealed`, Minor): admits three client
kinds, schema `1`, each strictly (every field declared; an unknown field, an over-long value or a list beyond its bound
refuses it) and within 16 KiB, well under the host's 32 KiB per-attachment budget:

| Kind | Target (`ModuleId` `Elsa.Studio.Workflows`) | Content |
| --- | --- | --- |
| `workflows.library` | `workflow-library` / `library` / list revision | the visible query (search, folder, tag and value filters, favorites-only, sort, publication filter), the applied view's identity, owner kind (`private` or `shared`) and revision, the columns, the selection (visible rows only), up to 50 visible rows and the native permission flags the page response carries |
| `workflows.create-dialog` | `workflow-create-dialog` / `create-dialog` / dialog revision | the open dialog's name, description, root activity or template, and the templates it offers |
| `workflows.template-list` | `workflow-template-list` / `templates` / list revision | the visible filter, the selected template and each listed template's identity and persisted revision; never a workflow definition |

Each is bound to its page's client binding. With any of them it adds `workflows.library-organization` (`AuthoritativeRead`):
the library's own current answer from `IWorkflowLibraryCommands.GetPermissionsAsync`, read again every turn, with
`status` `available`, `denied` or `unavailable` and, when unavailable (organization disabled, or no or several
installed commands), the backend's reason; ordinary list context remains. Its installed guidance
(`workflows.library-guidance`) names the tools below and refuses deletion with a pointer to the library's Delete actions.

**Library server writes** (`Mutating`, `ReviewRequired`, `ExecutionLocation.Server`, result `workflows.library-result`,
renderer `weaver.workflows.library-result`, disclosed by `workflows.library-context`): `workflow_library_assign_tags`
(`workflows/design/tag-assignments:manage`), `workflow_library_set_favorite` (`workflows/design:read`),
`workflow_library_move_to_folder` (`workflows/design:manage`) and `workflow_library_save_view` (`workflows/design:read`).
Each goes through `IWorkflowLibraryCommands` (Workflows Design Core, D4) for the session's own actor and tenant, returns
`RequiresApproval` from preparation so review holds in every mode including `full-auto`, and names only rows of the admitted
list. Tag edits bind each target by its current organization revision; favorites and folder moves are idempotent sets (a
move applies to all targets or none). Each named tag is looked up by its own name (the library's
normalized-prefix search, matched exactly), so a tag past the catalog's first page is found; a tag named twice in any
letter case is one change, and one both added and removed is a clarification. A tag that does not exist is created first,
only as a marker tag, only with the library's tag-management flag, and the review shows the creation. Tag creation and view creation carry a client operation
key frozen at preparation; when the create's response is lost, the outcome is an authoritative reread by that key, so a
retry resolves to exactly one tag or view (SC-008). A shared view needs the view-sharing flag; an update is the applied view
bound by its revision. Results carry one outcome per target and one overall `status`: `applied` only when every target applied
and every named tag was created, `partial` when something persisted but not everything (for instance a tag created
whose next creation then failed, when every target reports `not-applied`), and a change that altered nothing is a refusal with no receipt. A partial result keeps its
`ServerPersisted` receipt (something did persist, so the engine must not call it uncertain or refused), and its payload
and summary say partial and which targets were not changed; it is never presented as applied. Delete and permanent delete are not offered.

**Page working-state tools** (`Mutating`, `ReviewRequired`, `ExecutionLocation.ClientWorkingState`, reviewed in every mode):
`workflow_library_set_list_state` (result `workflows.list-state`) changes only the visible list's search, folder, tag filters,
favorites-only, sort, publication filter or columns (folder, tag and favorite narrowing are unavailable with organization
disabled); `workflow_create_dialog_prefill` (result `workflows.create-dialog-prefill`) fills the open dialog's name,
description and either a root activity that is an available entry of the actor's current `IActivityAuthoringCatalog` read or
a template the dialog lists. Each completes only on a receipt that repeats exactly the approved change; the page keeps it
revertible, and creating the workflow remains the user's Create action.

**Templates.** `workflows.context` also admits `workflows.template-editor` schema `1`: the template editor's working state in
the active-draft shape, targeted `workflow-template` / template id / content revision, with `templateId` and the persisted
`templateRevision` (the expected revision of the user's explicit save) instead of a definition and draft. Its authority is
the host's design read and manage decision; `IWorkflowDefinitionAuthorizer` is never asked about a template.
`workflow_apply_operation_batch` accepts it: the preview names the template and its persisted revision and no definition,
and the template editor's adapter applies it with the designer's history and autosave rules.

## Versions (spec 1193 slice F)

**Context source `workflows.version-context`** (`WorkflowVersionContextProvider`, `internal sealed`, Minor): admits the
page's reference `workflows.version` schema `1`, which names exactly a `definitionId` and a `versionId` (each at most 128
characters, no other field) and is targeted `workflow-version` / version id / version id (FR-005: a persisted version's
revision identity is its version identity). The server then reads that exact version itself, in this order: the host's
`workflows/design:read` (the gate is a read descriptor through `IAgentToolAuthorizer`), the owning
`IWorkflowDefinitionAuthorizer.CanReadAsync` for the named definition, and `IWorkflowVersionReader.ReadAsync` (Workflows
Design Core, D4); the version must belong to that definition. It admits `workflows.version` (`AuthoritativeRead`) with the
definition identity and name, the version identity and label, its activity count (`activitiesTruncated` when the walk stopped at its
bound, `readingIncomplete` when a container's children could not be read) and the definition's newest versions (at most 50, `versionsTruncated`), plus
`workflows.version-guidance`. Its target is the version identity only (FR-005); a later authority decision for a version
target (a retained result, the tool's target) asks `IWorkflowVersionReader.FindDefinitionIdAsync` for the version's
definition, which reads nothing else, and then that definition's owner, before the version is read. Required refusals: `agent.workflow.version_context_invalid`;
`agent.workflow.permission_denied` ("Current workflow design read permission is required for the workflow version.");
`agent.workflow.version_not_found` ("The workflow version is unavailable.") for an unknown version, another definition's
version and a withheld definition alike, so existence is disclosed no more than the owner discloses it;
`workflow.authorization.unavailable` (owner threw; for a reference that names its definition, an unknown and an existing
version then answer alike and nothing is read) and the composition faults `workflow.authority.composition_unavailable` (no or several definition
authorizers), `workflow.version.reader_unavailable` and `workflow.version.structure_unavailable` (no or several readers or
activity structure services), each owner-namespaced so the engine reports it as unavailable (503); slice A's
`agent.workflow.authority_unavailable` keeps its established meaning for the draft tools and is not used here. The order is
always the host's permission, then (for a version named only by its id) the definition lookup, then the owner, then one read;
for a version named only by its id (a retained result, a tool target), an unknown id has no definition to ask about, so
during an owner outage it answers not found (404) while a known id answers unavailable (503). With no version reference it contributes nothing. `AdmitDisclosureAsync` accepts only its own
retained read and reads it again for the current turn (an immutable version reads back identically), and re-authorizes a
retained `workflow_read_version` result by its version's definition.

**`workflow_read_version`** (contract `1`, `ReadOnly`, `ExecutionLocation.Server`, `workflows/design:read`, result
`workflows.version` schema `1`, no renderer, disclosed by `workflows.version-context`): arguments `{ versionId?,
compareToVersionId? }`, strict. The version is the only admitted version context, or the one `versionId` names; a caller's
own copy is never the server's read. It returns the version's activities by scope path (container ids from the root, and
the slot the owning activity module's structure handler projects, through `IActivityStructureService`), each with its
exact catalog version and its input names (never values), and its connections (each container structure's conventional
`connections` list, by container, source, port, target and port). With `compareToVersionId`, another version of the same
definition (read once, under the decision already made for that definition), it adds `comparedTo` and `comparison`:
Weaver's own structural comparison computed over the two reads (`computedBy: workflow_read_version`, with a note saying
so), listing activities added (only in the version), removed (only in the compared version) and changed, with the aspects
`activityVersion`, `inputs` (naming the differing inputs), `outputs`, `scope`, `intrinsic`, `placement`,
`containerMetadata` (its entry in its container's `nodeMetadata`, such as a flowchart join policy), `structure` (the
container's structure other than its children, connections and their per-element metadata, such as a flowchart's start
node or a switch's cases, compared normalized) and `childOrder` (`reorderedSlots`; only for an ordered container: a
container that wires its children with connections, such as a flowchart, runs them by its connections, so its list order is
not compared), and connections added, removed and changed. A connection is identified as the flowchart identifies it: its
`id`, or without one `{source}:{sourcePort}->{target}` (FlowchartGraph's key), which is also the key of its
`connectionMetadata` entry. Connections are read as the runtime deserializes them: property names in any letter case, the
last of several matching properties (and of exact duplicate dictionary keys) winning, a
blank or padded id is none or trimmed (as `FlowchartConnection` does), a blank port is `Done` and a port is trimmed (as
`FlowchartEndpoint.NormalizePort` does), and node ids are kept as written; reported ports are the normalized ones. A connection list the runtime would refuse (not an
array, a connection without a source or target object, a blank or non-string node id, a non-string id or port) is not read
as valid: none of that container's connections are reported, and the reading is `incomplete` with the reason; `connectionsChanged` names each by `connectionId` with the aspects `endpoints` (rewired) and
`metadata` (label, default flag, policy or configuration). Two connections with the same endpoints under different ids are
two connections. A metadata entry no connection or child claims stays part of the container's `structure`. A container whose structure no
installed handler projects is never read as empty: the reading and the comparison say `incomplete` with
`incompleteReasons`. Input and output values are compared without being disclosed. Lists are bounded (at most 100 each,
smaller when the result would exceed 24 KiB, well under the host's 32 KiB budget, #3710) and say when they were cut; a read
that does not fit even its smallest bound is refused as `agent.workflow.version_too_large` (413). Its
`IAgentToolAuthorization` decides a concrete version target by its definition's owner read alone (the definition looked
up by id, nothing else read). Refusals:
`agent.workflow.version_context_required` (409), `agent.workflow.version_arguments_invalid` (400),
`agent.workflow.version_not_found` (404), `agent.workflow.permission_denied` (403), `workflow.authorization.unavailable`
and `workflow.version.reader_unavailable` (503).

**Read-only.** A version is immutable. The guidance tells the model to refuse any edit, save, publish or delete of a version
with zero effect and to offer the current draft instead; no tool targets a version for a change, and
`workflow_apply_operation_batch` binds only an admitted working-state snapshot, so it refuses a request that holds only a
version (`agent.workflow.snapshot_required`, FR-015). The capability `workflow.version-explain` (Answer, ReadOnly,
`workflows/design:read`, kind `workflows.version`, mode `explain`) is declared by `WorkflowAgentCapabilityProvider`.

**Executables and Runs** live in the Runtime-side package [`Elsa.Agent.Workflows.Runtime`](../Runtime/EXTENSION_POINTS.md),
so this package keeps referencing Workflows Design only (FR-027, D2). In a Design-only host an Executable reference is
refused by the engine as `agent.context.kind_unsupported` ("No installed module admits the supplied context kind and
owner.") and no Executable tool is offered; in a Runtime-only host the same holds for every kind and tool above.

**SemVer: Minor, additive**: the context source and its two kinds, the tool and its result kind, the capability, and
`IWorkflowVersionReader` with `WorkflowVersionDetails` in Workflows Design Core.

## Removed legacy seams

The seven public legacy contracts, their default implementations, their registrations and the models only they used are
removed (spec 1193 D3): `IWorkflowAgentContextProvider`, `IWorkflowActivityCatalogProvider`
(`DefaultWorkflowActivityCatalogProvider`, empty), `IWorkflowRevisionProvider` (`DefaultWorkflowRevisionProvider`,
`unknown`), `IWorkflowChangePermissionEvaluator` (`DenyAllWorkflowChangePermissionEvaluator`),
`IWorkflowChangeProposalService` (`DefaultWorkflowChangeProposalService`), `IWorkflowGraphOperationBatchRiskClassifier`
(`DefaultWorkflowGraphOperationBatchRiskClassifier`, direct apply) and `IWorkflowAuthoringAuditService`
(`DefaultWorkflowAuthoringAuditService`), with `WorkflowAgentContext`, its permission, constraint, selection, diagnostic
and catalog-item records, the operation-batch, risk, clarification, audit and proposal-request records. None had a
production consumer. Their replacements: the catalog and permissions above; Agent's own tool-invocation review for
proposals; Agent's tool-invocation audit for every workflow tool invocation and outcome; and, from slice C, an internal
classifier that never grants direct apply. `WorkflowAgentGraph` and its activity and connection summaries stay.

Placeholder absence (SC-003) is asserted in two places: `WorkflowAgentNativeAuthorityTests` checks the source-composed
host (no legacy type in this assembly or its registrations, the real `HttpActivityAuthoringCatalog` and
`HttpWorkflowDefinitionAuthorizer`, and catalog entries equal to the designer's catalog route), and the conformance
probe's `workflow-truth-seams` case checks the packaged consumer through its generation observation (the loaded package's
type names and the one composed catalog).

**SemVer: Major**, classed as pre-GA cleanup (constitution §4.2, ADR 0080 D4). `DefaultWorkflowAgentContextProvider`
and `WorkflowAgentCapabilityProvider` are now `internal sealed` (ADR 0080 D3), also Major for any Preview consumer that
named them; hosts compose the feature instead. The new context kinds are Minor.


## Current disclosure ownership (#3439)

`DefaultWorkflowAgentContextProvider` implements `AdmitDisclosureAsync` on the existing source
`workflows.context`. Initial collection and retained disclosure use the current native actor/tenant
and the owning read descriptor's `workflows/design:read` permission against the exact target.
An unsaved working-state revision is not compared with a persisted database revision. Both graph
and position tools declare `DisclosureSourceId=workflows.context`; result-only admission still
checks the exact owning workflow target. Historical/receipt authority does not imply a fresh editor
read. Receipt continuation omits the obsolete snapshot while retaining the truthful accepted result;
no save, publish or effect replay is introduced. Generic failure/deadline/provenance rules belong to
[Agent foundation](../../Elsa.Agent/EXTENSION_POINTS.md#context-and-result-disclosure-3439-under-qualification).

Concrete context/history/results additionally require the owner domain's
[`IWorkflowDefinitionAuthorizer`](../../../../../Essentials/src/Workflows/Design/Api/EXTENSION_POINTS.md#iworkflowdefinitionauthorizer-contract--elsaworkflowsdesigncore).
The workflow contribution references its Core contract; native API resource and identity implementation
types remain in Workflow Design's API adapter. Missing or multiple owner implementations refuse access.
Null-target supplemental caller metadata and public guidance retain their existing generic actor/read
checks and acquire no concrete tool authority. Tool advertisement cannot infer resource authority
from guidance alone.

Preview consumers must rebuild for the added optional owner-authorizer constructor parameter on
`WorkflowReadGraphTool`; `DefaultWorkflowAgentContextProvider` is no longer public. A metadata-only
`new WorkflowReadGraphTool().Descriptor` remains source-compatible, while actual tool authorization
fails closed without a concrete target and one current owner implementation. Workflow Design Core
adds the two-method public owner contract; Workflow Design API supplies its default HTTP adapter.
