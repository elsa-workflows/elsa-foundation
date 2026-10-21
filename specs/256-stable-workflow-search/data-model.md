# Data Model: Stable workflow search

**Source**: Approved `spec.md`; current source pinned to `7a95893176bbaa75afb4b12fc3d69e3f815320b8`.

## Route-owned browse session

One module-private session, created by the mounted `WorkflowManagementPage`, is the sole authority for the workflow list's navigation state. The planned internal helper is `useWorkflowDefinitionBrowseSession` in `workflowDefinitionBrowseSession.ts`.

| Field | Meaning | Lifetime |
|---|---|---|
| `browseKey` | Current search text, resolved effective folder, and lifecycle filter. The editor `definition` selector is excluded. | Current route visit; current search/folder/lifecycle remain represented by the existing URL. |
| `page` | Selected numeric UI page. | Current route visit; defaults on full remount. |
| `pageSize` | Selected list size. | Current route visit; defaults on full remount. |
| `cursors` | API-issued continuation tokens indexed by the pages they lead to; empty for legacy unpaged mode. | Current route visit; never serialized or replayed from a cold URL. |
| `authorityRevision` | Monotonic invalidation signal for an observed endpoint/session boundary or a current typed 401/403. | Current route visit; child uses it to discard pending and protected display state. |

The route supplies controlled browse values and transition callbacks to `WorkflowDefinitions`. The route helper owns the browse key, page, page size, cursor map, and authority revision. The list owns request status, request generation/controller, current rows, the last-success display snapshot, selection, and folder-capability discovery. The list reports a settled effective folder and typed authority denial to the route helper. It does not keep shadow copies of the canonical route-owned fields. Internal component tests and browser fixtures use the same route owner/session; they do not invent their own paging state.

## Identities kept distinct

- **Authority boundary**: Current endpoint `baseUrl`, public auth-session start/end events, and route remount; a current request denied with typed status 401 or 403 also invalidates. These are the changes available through existing public contracts. A successful silent token refresh is not observable to this module and is not inferred.
- **Browse key**: Search, resolved effective folder, and lifecycle filter. A definition-only URL transition does not change it.
- **Request identity**: Authority boundary, browse key, page, page size, and the actual continuation token when paging capability exists; a local generation distinguishes successive requests with otherwise equal fields.
- **Last-success display snapshot**: Successful rows and their original browse key/page, retained only for eligible search-only refresh. It cannot supply current count, pager, empty state, selection, actions, or the new query's cursor.

## State transitions

| Event | Route-owned transition | Visible-result/request behavior |
|---|---|---|
| Search edit | Update URL and `browseKey` immediately; set target page to 1 and clear new-query cursors. | Invalidate/abort the prior generation immediately, before the 250 ms text-search delay. Keep eligible prior rows labelled by their old query and page, including rows from page 2+. Request only the latest settled query. |
| Same-view refresh | Keep browse key, page, page size, cursors. | Keep current rows and announce refresh; replace them only with the current request result. |
| Folder or lifecycle change | Update browse key; set page to 1 and clear cursors. | Clear prior-view rows and selection before loading. |
| Page-size change | Set the selected size; set page to 1 and clear cursors. | Clear prior-view rows and selection; load the first page for the new size. |
| Explicit page navigation | Set the selected page while preserving the cursor map needed to reach it. | Clear old-page rows while loading; request the selected page with its already observed token. Do not reset to page 1 or erase that token. |
| `popstate`, definition-only or same browse key | Preserve browse key, page, page size, cursors. | Clear selection as appropriate. This includes the synthetic event dispatched before the list unmounts when opening a definition. |
| `popstate`, changed search/folder/lifecycle | Rebase page to 1, clear cursors, update browse key. | Clear prior-view rows and selection, except an eligible search-only transition follows the labelled snapshot rule. |
| Endpoint/auth event or host route remount | Clear page/cursors and increment invalidation revision at the route owner, including while editor is open. | Clear protected rows, selection, and pending results when the list is mounted. A remount starts with defaults. |
| Current request returns typed 401/403 | Route callback clears page/cursors and increments invalidation revision. | Clear protected rows, selection, and pending results. Do not inspect error-message text. |
| Folder capability is temporarily unknown on editor return | Keep saved browse session until effective folder resolves. | Do not treat provisional `all` as a change. Preserve the cursor if resolved folder matches; otherwise clear page/cursors/rows and load page 1. |

## Paging and fallback

The current paged endpoint takes an actual continuation token and page size; numeric page is UI state. The route-owned cursor map is the only source for a token already observed in this visit. Legacy unpaged mode continues to fetch and slice the collection locally. Neither mode adds numeric page or opaque cursor state to the URL, and a full route remount does not guess or replay a prior page.
