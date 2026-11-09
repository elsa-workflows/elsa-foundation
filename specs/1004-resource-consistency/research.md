# Research: Studio resource-list consistency and Home access

**Source baseline**: selected worktree HEAD `d2968b0f1bc8330fe99dff927cecd885867e68fb` (2026-10-24). This is source inspection only; no tests or builds were run.

## Discovery method

The canonical code graph was checked first for the Secrets, Connections, Home, and browser-fixture surfaces. It returned no matching TSX nodes, so targeted direct source reads and path listings were used for those files. This bounded fallback is sufficient for the narrow UI contract; it does not support claims about unrelated modules.

## Decisions

### Keep the list hierarchy shared and operations page-specific

**Decision**: Apply the accepted Studio resource-list hierarchy to the two page shells using existing shared controls and local styles. Keep their table structure and domain-specific row entry points. Secrets uses Edit as its direct primary row action and moves Rotate, Test, Revoke, and Delete behind the existing accessible overflow menu, with destructive actions separated. Connections keeps Details as its row entry; Test, Revoke, and Delete stay inside the detail surface.

**Rationale**: The current pages already have different domain semantics and action surfaces. The accepted #3070 Lists work supplies the shared hierarchy; duplicating its controls or adding a generic table abstraction would add unnecessary implementation surface.

**Alternatives considered**: Forcing both pages into one identical action menu would erase the Connections Details entry. Replacing both tables with a generic grid would discard meaningful column semantics. Neither is accepted scope.

**Source evidence**: `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretsPage.tsx`, its `styles.css`, `Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionsPage.tsx`, `ConnectionList.tsx`, `ConnectionDetailDialog.tsx`, their `styles.css` files, and existing shared Studio controls.

### Preserve Secrets inventory and query contracts

**Decision**: Keep one 100-record loaded page, server-side name/display-name search and total count, local Type/Store/Scope/Status facets over loaded records, selected-scope qualification when absent from loaded results, and distinct draft/accepted/failed query states. Define one internal page-size constant and use it for both the request limit and coverage explanation. Refresh and successful operations use the accepted query; retry uses the failed attempted query.

**Rationale**: The current page already implements most of this contract. The repair should keep its truthfulness and avoid treating a loaded subset as a full inventory.

**Alternatives considered**: Fetching all records or applying local search would change the endpoint/query model and is outside scope. Showing an unqualified empty selected scope would overstate what the loaded subset proves.

**Source evidence**: `SecretsPage.tsx` keeps `search`, `completedSearch`, and `attemptedSearch`, requests `pageSize: 100`, and receives `totalCount`; local facets use loaded records.

### Keep Connection creation within accepted search context

**Decision**: Preserve the accepted query in state across creation; show the existing exact notice `Connection '${created.name}' created.`; reload the accepted query; and pass the preferred created name only to the existing exact-returned-item selection logic. Do not select an item that is not in `list.items`.

**Rationale**: The current selection path already requires an exact returned name; the regression is that create clears the query and reloads an empty query. Retaining the accepted query preserves operator context without changing API behavior.

**Alternatives considered**: Clearing search to force-select the new item was rejected because it discards context and can claim selection outside the current response. Adding paging, total-count, or create-endpoint behavior is not supported by current contract.

**Source evidence**: `ConnectionsPage.tsx` separates editable `search` from `appliedSearch`, clears search in the create callback, and selects a preferred name only when found in returned `items`.

### Gate only Home's Needs attention destination

**Decision**: Reuse `hasEffectivePermission` for authenticated `workflows/runtime:read` on only the Needs attention destination. Keep the active-runs URL unchanged, preserve the existing authentication-disabled behavior, and leave Continue's complete authorized definition catalog and counts unchanged.

**Rationale**: Home already relies on the local matcher for design-read/manage. The concern is isolated to one task link; changing the authorization SDK or widget model would expand scope without evidence.

**Alternatives considered**: Applying the runtime permission to the whole Home group would restrict unrelated catalog content. Adding global permission metadata or a new helper would duplicate existing behavior.

**Source evidence**: `Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/HomeTaskGroup.tsx` exports `hasEffectivePermission`; Needs attention currently targets `/workflows/instances?incidentHealth=active`; Continue uses complete catalog semantics.

### Make fixture mutation attempts observable without recording payloads

**Decision**: Keep the fixture metadata-only and make its rendered counter update for each attempted POST, PUT, and DELETE, including when validation or the simulated handler throws. Tests assert counts, not request bodies or secret values.

**Rationale**: The current counter is a ref displayed without a state-triggered render; although POST/PUT/DELETE paths increment it, the UI can be stale. A state-backed count provides an observable proof of attempted writes.

**Alternatives considered**: Logging request bodies or credentials would violate fixture privacy. Counting only successful calls would hide rejected write attempts and weaken strict-write evidence.

**Source evidence**: `Studio/tests/browser/secretScopesFixture.tsx` increments `mutationRequestCount` in POST/PUT and a ref in DELETE, then renders `.current` without an update signal.

## Repository and gate findings

- Both constitutions and `docs/contributing/review-checklist.md` were read. Existing UI logic warrants behavioral tests. The framework's greenfield cadence does not apply; no test-first rule is inferred.
- Studio `pnpm typecheck` includes recursive package typechecks and `tests/live/tsconfig.json`. Browser specs remain covered by the browser suite.
- The Workflows published-surface `build:lib` is a distinct declaration-emission check, not included in ordinary typecheck/build. It is required only if the affected published surface is touched; this plan expects no such touch.
- The shared gate set retains three full Workflows shuffle seeds, five existing client/host build groups, architecture/maps, full Chromium, safe private-host proof, exact-head independent reviews and approval, and resulting-main CI/Maps/Studio.
- Previous #3069 host recipes and #3310 results are historical setup/reference only. They are not acceptance proof for this feature.

## Open decisions

No product decision is left unresolved for this bounded scope. The exact command matrix and full-tier policy must be refreshed by the root at implementation time so verification uses the then-current checks and selected exact head.
