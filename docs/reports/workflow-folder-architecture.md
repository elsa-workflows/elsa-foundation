# Workflow Folder Architecture

Status: proposed free-flow architecture design, 2026-07-19.

Program goal: `none/free-flow`. Workflow-folder product design is not owned by an existing mid-term
bucket. The implementation must still honor the active
[Zero-EF Persistence](../program-goals/zero-ef-persistence.md) constraints. Promote this work into a
named bucket only if it becomes a durable coordination surface beyond one cross-repository work unit.

This report records the recommendations accepted during a `grill-with-docs` pass. Canonical meanings
live in the [Elsa glossary](../glossary/elsa.md); the hard-to-reverse identity and containment choice
lives in [ADR 0046](../adr/0046-workflow-folders-use-stable-single-placement.md). This report is the
architecture and implementation exploration, not an active implementation queue or a substitute for
a Speckit feature specification.

The draft Elsa constitution's §E2.9 supports keeping organization outside
`WorkflowDefinitionState`, but that section is still pending architecture-review ratification. This
design treats the boundary as a provisional quality gate and does not ratify or extend it.

## Goal

Allow users to organize large workflow-definition inventories into a navigable hierarchy shared by
Elsa Server and Elsa Studio without changing workflow authoring, versioning, publication, or runtime
semantics.

## Accepted product model

- The user-facing and domain term is **workflow folder**, not generic group.
- A workflow definition belongs to zero or one workflow folder.
- Folders may contain folders to a bounded depth.
- Definitions without a placement appear in the virtual **Unfiled** view.
- **All workflows** is another virtual view, not a persisted root folder.
- Folder placement is mutable metadata on the logical workflow definition.
- Multiple classification is deferred to tags or collections.
- Folders are organizational in v1. They are not security, tenant, environment, deployment,
  publication, lifecycle, or runtime boundaries.
- Existing coarse workflow-design read/manage permissions govern folder operations.

## Useful use cases

Initial use cases:

- Browse large inventories by business domain, team, product, customer, project, or process.
- Separate reusable workflow libraries from application-specific definitions.
- Create a workflow directly in the current organizational context.
- Move one or many selected definitions as an explicit bulk operation.
- Search within the current folder or globally while preserving folder context in results.
- Restore a soft-deleted definition to its former organizational location.
- Give operators and authors a stable vocabulary for discussing a coherent area of workflows.

Useful follow-ons that require separate decisions:

- Folder-scoped health, validation, incident, or ownership projections.
- Folder-scoped Weaver context and tools.
- Portable export or promotion bundles.
- Tags or collections for multiple classification.
- Inherited folder authorization.
- Source-controlled organization or configurable import placement.

Folders must not duplicate concepts Elsa already owns. In particular, folders named Draft,
Published, Production, or Tenant are presentation conventions only; they do not become lifecycle,
deployment, publication, or isolation facts.

## Domain and persistence model

### Workflow folder

Introduce a Design-owned `WorkflowFolder` entity with:

- `Id`: stable opaque identity.
- `TenantId`: inherited tenant ownership.
- `ParentFolderId`: nullable; `null` means a top-level persisted folder.
- `Name`: trimmed display name.
- `NormalizedName`: server-produced comparison key.
- inherited creation and last-modified timestamps.

Use an adjacency list rather than a materialized name path or closure table. Folder moves then update
one folder record. Paths and breadcrumbs are read projections, never identities.

Recommended invariants:

- A parent folder must exist in the same tenant.
- A folder cannot parent itself or any ancestor.
- Normalized names are unique among siblings in one tenant.
- Maximum depth is 16.
- Folder mutations re-read the affected ancestry inside the committing transaction/session.
- Folder deletion succeeds only when there are no child folders and no contained definitions,
  including soft-deleted definitions.
- Concurrent create, rename, move, placement, and delete operations must fail atomically rather than
  produce duplicate siblings, cycles, cross-tenant placement, or orphaned definitions.

Recommended storage routes/indexes:

- folders by `(TenantId, ParentFolderId, NormalizedName)` for sibling uniqueness;
- folders by `(TenantId, ParentFolderId)` for bounded child listing;
- definitions by `(TenantId, FolderId, DeletedAt, LastModifiedAt, Id)` for stable browsing.

Core owns provider-neutral contracts and invariants. Groundwork owns the first-party durable entity or
document representation, storage manifest, bounded routes, serialization version, concurrency, and
provider-conformance evidence. Per ADR 0042 and the active Zero-EF roadmap, do not add an EF migration
or expand the EF implementation family for this feature.

### Definition placement

Add nullable `FolderId` to the persisted logical `WorkflowDefinition`, but do not add placement to
`WorkflowDefinitionState`, draft state, version state, layout records, publication records,
executables, or runtime state.

The provider/source-facing `IWorkflowDefinition` content contract does not need to expose folder
placement. Reconciliation updates to name, description, or deletion state must preserve an existing
server placement. A newly reconciled definition defaults to Unfiled unless a future explicit
placement policy says otherwise.

Lifecycle rules:

- Creating a definition may supply an optional destination folder.
- Moving a definition changes only its placement and last-modified metadata.
- Moving or renaming a folder preserves contained definition placement.
- Soft deletion retains `FolderId`; restoration returns to the same folder.
- Permanent definition deletion removes the definition normally.
- Folder deletion never cascades into definitions, drafts, versions, layouts, publications, or
  runtime artifacts.
- A folder move or definition move never creates a draft/version, triggers publication, or changes an
  executable hash.

## Server API

Keep folder APIs inside the existing `design/workflows` domain rather than creating a new Foundation
module.

Recommended capability and routes:

- advertise an additive `workflow-folders` relation from the existing workflow-design capability;
- `GET /design/workflows/folders?parentId={id}` lists direct children;
- `GET /design/workflows/folders/{id}` returns folder detail and its ancestor breadcrumb;
- `POST /design/workflows/folders` creates a folder;
- `PATCH /design/workflows/folders/{id}` renames a folder;
- `POST /design/workflows/folders/{id}/move` moves a folder;
- `DELETE /design/workflows/folders/{id}` deletes an empty folder;
- `POST /design/workflows/definitions/move` moves one or more definitions atomically.

These are capability-relative contract sketches. The implementation specification must bind them to
the canonical shell-relative base advertised by Elsa Server rather than hard-code a Studio host
prefix.

Definition creation accepts optional `folderId`. Definition summary/detail views expose nullable
`folderId`; browse/search views may also expose a breadcrumb projection for display. Do not put
placement into the general name/description metadata patch: a distinct move command gives bulk
behavior, authorization, validation, audit, and conflict errors one clear boundary.

Extend definition browsing with mutually validated filters:

- no folder selector: all visible definitions;
- `folderId={id}`: definitions directly in that folder;
- `unfiled=true`: definitions with no placement;
- existing active/deleted/all lifecycle state;
- search term;
- bounded `pageSize` and opaque continuation token.

Initial folder browsing is direct-membership only. Recursive subtree filters and subtree bulk
operations are deferred until there is a concrete workload and a bounded Groundwork query strategy;
v1 must not emulate recursion with an unbounded collection scan.

The current list endpoint materializes all definitions and Studio performs client-side slicing. The
folder work must replace that behavior for new clients with stable server-side keyset paging ordered
by last-modified time and ID. Preserve old-client compatibility by keeping omitted paging parameters
on the legacy all-results behavior until the compatible contract can be retired; do not silently
return only the first page to an older Studio.

Recommended errors:

- unknown or cross-tenant folder: not found without foreign-tenant disclosure;
- normalized sibling collision: conflict;
- cycle or stale hierarchy mutation: conflict;
- non-empty delete: conflict;
- depth violation: validation failure;
- malformed or mutually exclusive filters: bad request.

Reads require the existing workflow-design read permission. Folder and placement mutations require
workflow-design manage. Per-folder grants are explicitly absent from v1.

## Studio experience

Implement the UI inside the existing `Elsa.Studio.Workflows` module.

Desktop layout:

- a left folder tree;
- a right definition table;
- virtual All workflows and Unfiled entries above persisted folders;
- a clickable breadcrumb above the table;
- existing Active/Deleted state, search, refresh, selection, creation, and paging controls;
- Create folder and Move to folder actions;
- Create workflow defaults to the selected folder.

Interaction rules:

- The selected folder is represented in the URL by opaque ID, never by display path.
- Browser back/forward restores folder, lifecycle state, and search context.
- Search in a selected folder is direct-folder scoped; selecting All workflows provides global
  search.
- Global results display a folder breadcrumb.
- Existing multi-selection powers an atomic Move to folder action.
- Folder move targets exclude the folder and its descendants.
- Drag and drop may be added as a convenience but is never the only move mechanism.
- The tree follows the ARIA tree keyboard pattern; folder actions remain reachable without a pointer.
- On narrow screens, the tree becomes a folder-picker drawer.
- Deleted definitions retain and display their folder context.
- When the backend does not advertise `workflow-folders`, Studio renders the current flat list and
  does not probe missing endpoints.

The existing `WorkflowDefinitions.tsx` surface is already large. Extract folder state, tree,
definition table, move dialog, and API query logic into focused components/hooks rather than adding
the feature inline.

## Capability and version compatibility

The Server already advertises workflow-design capability links, and Studio resolves those links.
Adding an optional relation and optional response fields is contract-major compatible. A Studio that
does not understand the link keeps its flat list; a Studio that understands folders falls back when
the link is absent.

Changing list defaults or response envelopes so old clients lose definitions is breaking. Introduce
new optional paging inputs/outputs or a new relation before changing default semantics. Capability
tests must pin additive-link behavior and contract-major compatibility.

## Git reconciliation and interchange

Workflow-folder organization is server/catalog metadata in v1. Do not:

- infer workflow folders from the existing Git definition directory layout;
- add folder placement to immutable version content;
- make a folder path part of content identity;
- let Git reconciliation clear a manually selected server placement.

The existing Git model uses `definition.json` as mutable content-authority metadata and a
definition-ID directory as storage layout. Folder placement can differ by tenant or environment and
has no portable identity contract yet.

If portable organization becomes a requirement, design a separately versioned folder/placement
manifest with collision and identity-remapping rules. Do not overload repository paths or individual
workflow version files.

## Authorization evolution

Inherited folder ACLs are deferred. They would make folder moves security-sensitive subtree
mutations and require:

- a resource-authorization model;
- inheritance and override semantics;
- effective-permission projections;
- audit and approval for subtree moves;
- behavior for Unfiled and All workflows;
- safeguards against accidental access expansion or lockout.

The v1 entity shape keeps stable folder identity so such a feature remains possible, but no API or UI
may imply that organization currently grants access.

## Migration and rollout

1. Land glossary, ADR, report, and a reviewed cross-repository specification.
2. Add provider-neutral folder/placement contracts and a Groundwork storage manifest/version.
3. Add folder reads, bounded definition browsing, capability links, and compatibility tests.
4. Add hierarchy and placement mutations with transactional invariant tests.
5. Add Studio capability fallback, folder tree, breadcrumb, create-in-folder, and bulk move.
6. Add optional projections only after their workloads are proven.

Existing definitions require no data rewrite: absent placement means Unfiled. Deploy Server support
before folder-enabled Studio. Older Studio remains on the flat definition list.

## Verification

Foundation:

- entity and command invariants: sibling normalization, same tenant, depth, cycles, empty delete;
- create/move/restore/permanent-delete lifecycle behavior;
- reconciliation preserves placement;
- placement changes do not touch draft/version/layout/publication/executable state;
- bounded Groundwork folder and definition queries on SQLite, SQL Server, PostgreSQL, and MongoDB;
- storage manifest, serializer/golden fixture, tenancy, restart, and concurrency coverage;
- capability-link and old-client list compatibility;
- architecture guard that folders remain Design-owned and absent from authored/runtime models;
- architecture guard or review check that no EF schema/migration is added.

Studio:

- capability-present and capability-absent behavior;
- loading, empty, error, conflict, and unavailable states;
- URL/back-forward restoration;
- keyboard-accessible tree and actions;
- create-in-current-folder;
- single and bulk move;
- deleted/restore folder preservation;
- folder-scoped and global search;
- cursor paging without duplicate or skipped definitions;
- focused module tests plus the shared Studio build/lint gates when implementation begins.

## Deferred, non-blocking decisions

These are deliberately outside v1 and must not delay the basic folder feature:

- tags and collections;
- subtree counts and health aggregation;
- recursive subtree browsing and bulk operations;
- custom folder ordering;
- folder icons/colors;
- inherited ACLs;
- portable import/export manifests;
- repository-directory mapping;
- folder-scoped Weaver mutations.

No unresolved decision above blocks promotion into `speckit-specify`. The next review point is whether
to promote this free-flow design into one cross-repository work unit or separate Server-contract and
Studio-consumer work units.
