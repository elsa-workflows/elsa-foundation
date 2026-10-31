# Workflow library API

All operations are authenticated, tenant scoped and capability-discovered. Links advertise support, never permission.

## Existing folder relations

- `workflow-folders`: GET bounded parentId/pageSize/continuationToken -> `{items,nextContinuationToken}`; POST `{name,parentId}` -> folder.
- GET folder ID -> `{folder,ancestors}`; folder keeps current id/parentId/name/normalizedName/timestamps with additive revision.
- `workflow-folder-rename`: POST `{name,revision?}`; `workflow-folder-move`: POST `{parentId,revision?}`; `workflow-folder-delete-empty`: DELETE with optional revision precondition.
- `workflow-definition-folder-move`: POST `{definitionIds,folderId,operationKey?}`; <=100 explicit targets, atomic all-or-none.
- Definition creation accepts folderId within the same committing write as definition/draft creation.

## New relations

- `workflow-definitions-page`: GET searchTerm/state/folderId/unfiled/includeDescendants/favoritesOnly/sort/pageSize/continuationToken and JSON `tagFilters`. Response `{items,nextContinuationToken,totalCount}`; existing definition summary plus organization/favorite.
- Clauses `{tagId,operator,valueIds}` support exists/missing/any/all/none. Clauses AND, alternatives OR; all only for multiple controlled values. Invalid/missing references never silently drop a predicate.
- `workflow-organization`: GET/PUT templated definitionId; organization has folder/breadcrumb, assignments, revision and allowed-action hints. PUT replaces explicit assignments under expected revision.
- `workflow-tags`: bounded GET and POST catalog creation. `workflow-tag`: GET exact stable identity for conflict recovery and PUT display/lifecycle/values under revision. Catalog administration and assignment are distinct permissions.
- `workflow-tags-bulk`: POST explicit IDs/revisions, add/remove assignments and stable operation key; per-target atomic outcomes. Studio previews first.
- `workflow-views`: GET visible private/shared views and POST name/query/columns/shared. `workflow-view`: PUT/DELETE under revision and owner/share authorization.
- `workflow-favorites`: PUT/DELETE templated definitionId; personal actor derived server-side, query uses favoritesOnly.

Errors: invalid 400, anonymous 401, denied 403, absent/foreign 404, stale/invariant conflict 409, unsupported optional store 501. Personal subject never comes from request payloads. Cursors bind canonical query, actor and persistence scope. View documents are bounded and schema-versioned. Ordinary organization changes no runtime/access/authored state.
