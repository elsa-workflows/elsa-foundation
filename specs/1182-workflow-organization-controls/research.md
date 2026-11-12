# Research: Workflow organization controls

Two independent source-only reviews inspected backend and Studio behavior. Findings were checked against the current source on main `077ef2c9a`; no research builds or runtime claims were made.

## Setting and composition

- **Decision**: Add default-true `WorkflowOrganizationEnabled` to the existing `WorkflowsDesignApiFeature`, with a Boolean manifest descriptor and an internal immutable snapshot. Reuse Features settings review/Apply, source ownership and shell reload.
- **Rationale**: The existing selected-backend admin flow already stages changed fields, pins revision/target and refreshes only that backend session. A new personal preference or feature identity would duplicate authority or introduce a dependency that disables ordinary workflow APIs.
- **Alternatives considered**: Unregistering the library store would remove paging/authorization semantics; hiding only UI would leave stale server writes active; a separate feature would complicate backward-compatible default activation. Retain the store and observers.

## Availability, authorization and mutation paths

- **Decision**: Keep `workflow-definitions-page` and `workflow-definitions-query` whenever a library store exists. The latter explicitly signals extended query grammar; base paging predates those options. Gate organization relations and their handlers. Preserve trusted query visibility, counts/cursors, tenant scope and per-definition/folder authorization, even when response organization metadata is suppressed.
- **Source**: `WorkflowLibraryCapabilitySource`, `WorkflowLibraryAccess`, `DefinitionPageEndpoint`, `WorkflowLibraryProjection`, ordinary details readers and create endpoint.
- **Important bypass**: Ordinary create accepts `FolderId`; refusal must happen before `addCommand.Execute`. Existing organization endpoints funnel through the access helper.
- **Decision**: Refuse active organization predicates on the paged route while disabled. The legacy flat list never supports these predicates; reject recognized organization query keys there while disabled rather than allowing a stale caller to interpret a broad response as filtered. Default-enabled legacy behavior remains unchanged.
- **Failure contract**: Reuse structured unavailable HTTP 501 for disabled organization operations and structured HTTP 400 for invalid disabled browse predicates. Native permission enforcement remains authoritative.

## Studio recovery and independent operations

- **Decision**: Use relation-based operation facts, not a palette ID or a global frontend flag. Keep paging, sort and publication-state transport support independent of organization availability. Extended options require `workflow-definitions-query`, with `workflow-organization` retained only as the existing library-server compatibility signal; a base paged host can ignore newer query keys. Keep supported folder selection when only descendants/options are unavailable.
- **Source**: `workflowOrganizationCapabilities.ts`, `workflowDesign.ts`, `WorkflowDefinitions.tsx`, `WorkflowLibraryControls.tsx` and `workflowDefinitionBrowseLocation.ts`.
- **Decision**: Hold organization-scoped browse while required facts are unresolved or unavailable. Explain the unavailable constraints and clear only those constraints plus the selected-view association through the existing location/session transition. Preserve search, lifecycle, backend, unrelated URL values and stored column choices.
- **Decision**: Intersect visible organization columns and metadata/actions with their owning operations. Re-enable restores the saved choices; do not overwrite them merely because a backend cannot offer them.
- **Decision**: Reuse #3536's session-keyed capability refresh and route/dialog unmount. Existing local refresh/context guards must also retire move/organization targets and stale reads for standalone module use. No competing refresh listener/cache is added.
- **Sources**: Features `module.tsx` emits `studioBackendCapabilitiesChangedEvent`; Hosting `App.tsx` refreshes only the matching immutable backend session and rebuilds the module registry.

## Validation

- Extend existing API contract probes for default/on/off/store-absent, every mutation route, ordinary paging and resource visibility, metadata suppression and explicit folder placement before create.
- Extend mounted Workflows browse/route tests for each unavailable predicate, partial support, delayed old reads/dialogs and retained column choices. Extend the existing Features Boolean descriptor/review fixture, preserving its target/revision tests.
- Extend browser fixtures for desktop/320px keyboard recovery and all supported appearance treatments; retain valid existing folder full-width/narrow contracts.
- Reuse `Studio/tests/live/workflow-library-journey.spec.ts` and the real two-backend setup in `specs/1178-studio-backends/quickstart.md` for reviewed setting Apply and retained identities. Existing tests do not yet prove this new switch.
- Run an existing published artifact through `workflow-execute`; a transient Test Run is not substituted for published execution.

No unresolved research question remains. A request already executing in an older shell can finish with that shell's captured setting; current-session generation guards must retire its UI result after availability refresh.
