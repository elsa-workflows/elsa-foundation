# Feature Specification: Workflow Library Organization

**Feature Branch**: `agent/codex/3500-workflow-library`
**Created**: 2026-10-31
**Status**: In progress
**Input**: Approved folders, tags and saved-views proposal; owner instruction to create a program and deliver end to end in a separate worktree. Program #3499; delivery #3500.

## User Scenarios & Testing

### User Story 1 - Find and organize workflows by folder (Priority: P1)

An author organizes a growing library into a shallow, familiar hierarchy and returns to the same browsing context after editing.

**Independent Test**: In the normal host create Finance / Receivables, create a workflow there, find it through All workflows and the folder, move it, rename its parent and revisit its original definition link.

**Acceptance Scenarios**:
1. Given an existing library, All workflows shows all visible definitions and Unfiled shows only definitions without placement.
2. Creating a workflow in a folder places it there. Renaming or moving a folder preserves its identity and all workflow links.
3. Selecting multiple definitions and moving them commits the complete selection or none; stale, denied and invalid targets preserve an actionable selection.
4. A folder displays direct workflows by default; Include subfolders explicitly searches descendants. Search scope and active filters remain visible.
5. Empty-folder deletion never cascades; child folders and soft-deleted workflow placements prevent deletion.

### User Story 2 - Classify overlapping groups (Priority: P1)

An author applies reusable labels and controlled classifications without copying workflows or changing their execution.

**Independent Test**: Assign critical and customer-facing markers plus a single owning-team value, filter by their intersection, bulk remove one marker and observe authoritative results.

**Acceptance Scenarios**:
1. Existing labels are suggested, surrounding whitespace/case do not create accidental duplicates, and controlled values use a picker.
2. A workflow has many marker tags and controlled dimensions; single-valued dimensions reject incompatible assignments.
3. Organization edits affect the logical definition, with no new draft/version/publication or runtime change.
4. Retired vocabulary remains visible/filterable on existing assignments but cannot be newly assigned. Renames preserve references.
5. Bulk preview names selected targets and intended add/remove operations; retries are safe and per-target failures/conflicts are visible.

### User Story 3 - Revisit useful views (Priority: P2)

A reader saves a combined query privately, shares an agreed view when permitted, and pins personal workflow favorites.

**Independent Test**: Save Published finance workflows, share it, open it as another reader with a smaller visible inventory and verify its query and authorized results; pin/unpin a favorite independently.

**Acceptance Scenarios**:
1. A view stores folder/subtree, search, lifecycle, tag filters, sort and column choices by stable identities; matching workflows enter/leave automatically.
2. Private views and favorites are isolated by user, tenant and active host scope. Shared views require sharing permission and grant no workflow access.
3. URLs, browser Back and editor return preserve the intended scope, filters, page size and cursor history; scope changes restart paging and selection.
4. Missing or retired view references remain diagnostic rather than silently widening the query.

### User Story 4 - Work with a large durable library (Priority: P1)

An organization uses thousands of workflows without downloading its complete inventory to organize it.

**Independent Test**: Seed 10,000 definitions with folders and tags, browse/search/filter across pages, restart the host, and repeat through the same advertised public surface.

**Acceptance Scenarios**:
1. Filtering and paging operate across the complete authorized result set; counts/facets do not describe only the current page.
2. Tree children, tag suggestions, query clauses and bulk selections are bounded, with deterministic database-work budgets independent of total history.
3. Supported durable providers preserve the same tenant, concurrency, hierarchy and lifecycle behavior after restart.

### Edge Cases

Concurrent sibling creation or hierarchy moves; cycles and depth overflow; a folder containing deleted definitions; stale tag revisions; mixed bulk outcomes; hidden foreign identities; a saved view referencing retired/missing vocabulary; source reconciliation after manual organization; unsupported capabilities; rapid query changes and expired cursors; narrow layouts and keyboard-only operation.

## Requirements

### Functional Requirements

- **FR-001**: A definition has zero or one home folder with stable identity. Folders are tenant-scoped, sibling names normalize consistently, hierarchy depth is bounded to 16, and cycles/orphans are rejected atomically.
- **FR-002**: All workflows, Unfiled, direct-folder and explicit subtree scopes compose with search, lifecycle and tags before bounded server paging.
- **FR-003**: Users can create workflows in context, create/rename/move folders, delete empty folders and move bounded explicit selections atomically.
- **FR-004**: Reusable marker and controlled tags support stable identities, consistent normalization, single/multiple cardinality, rename and retirement. Ordinary tags never imply execution, access, deployment or publication effects.
- **FR-005**: Organization can be inspected/edited at definition details and from selection actions, with optimistic concurrency, safe retry and attributable durable change records.
- **FR-006**: Bulk tag add/remove requires bounded explicit target identities and a preview; each target is atomic and outcomes identify success, conflict or refusal without leaking hidden target existence.
- **FR-007**: Saved views are private by default, optionally tenant-shared with explicit permission, and store query/presentation choices by stable identity. Sharing never grants target access.
- **FR-008**: Personal favorites are isolated to the reader's tenant and user and can be filtered without changing organization for other users.
- **FR-009**: Folder/tag changes are outside authored state and immutable versions. Soft deletion retains organization; restore retains it; permanent deletion removes target-owned organization. Source reconciliation preserves manual placement and tags.
- **FR-010**: Capability discovery determines available organization operations; unsupported hosts keep truthful existing browsing behavior and never issue guessed routes.
- **FR-011**: Every result, count, suggestion, saved view, favorite, audit fact and mutation enforces current tenant and action authorization. Organization introduces no folder-based security inheritance.
- **FR-012**: Studio preserves compact names and exact-copyable IDs, keyboard navigation/focus, narrow-screen access, URL/history and current-result selection semantics; failures remain actionable.
- **FR-013**: Counts and suggestions describe authorized full matching sets with explicit result bounds. Stable sort has an identity tie-breaker and cursors bind to the full query and access scope.
- **FR-014**: User/integrator documentation explains folder/tag/view roles, actual host support, permissions, organization ownership, import/export expectations and limits.

### Key Entities

- Workflow folder: stable home in a tenant hierarchy.
- Workflow definition organization: placement, effective tags and a revision for one logical definition.
- Workflow tag and controlled value: stable reusable vocabulary with lifecycle and cardinality.
- Workflow definition view: private/shared query and presentation preset.
- Workflow favorite: personal reference to a definition.
- Organization change record: attributable committed before/after organization fact.

## Success Criteria

### Measurable Outcomes

- **SC-001**: The normal composed host completes all four user journeys with real persisted data and after restart.
- **SC-002**: A 10,000-workflow fixture yields complete, duplicate-free paged folder/tag searches and stable full-result counts without loading the whole inventory into Studio.
- **SC-003**: Every supported durable provider passes the same hierarchy, placement, query, concurrency and lifecycle scenarios.
- **SC-004**: Cross-user and cross-tenant refusal scenarios expose no private organization data; keyboard-only and narrow-screen journeys remain usable.
- **SC-005**: Delivery evidence covers the exact reviewed head, required checks, merged result and resulting main; pending, skipped or quarantined evidence is reported distinctly.

## Assumptions

- Existing workflow read/manage authorization and the current durable persistence family remain authoritative.
- Marker and controlled tags cover the initial classification requirement. Free-text values, source-produced assignments, manually ordered collections and mutable-query bulk jobs are later extensions.
- Explicit-selection operations are bounded to 100 definitions initially; future reviewed jobs can extend that limit without changing ordinary selection semantics.
- Views default to personal; sharing permission is separate. Organization never acts as an access boundary.
- The repository's retired timing benchmark program remains retired; scale is qualified with bounded work and deterministic correctness evidence.
