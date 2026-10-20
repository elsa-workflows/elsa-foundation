# Research: Stable workflow search

**Pinned source**: `7a95893176bbaa75afb4b12fc3d69e3f815320b8` in the assigned worktree `/Users/sipke/.codex/worktrees/studio-search-3068/elsa-foundation-main`.

The canonical code graph was queried first for workflow-definition browse/search symbols. Its `Users-sipke-Projects-Elsa-elsa-foundation-main` index returned old C# layout results, not the current Studio TypeScript implementation. I then read the known source files with `git show HEAD:<path>` from the assigned worktree at the pinned HEAD. The worktree was not indexed. Root’s previous source review records these same relevant WorkflowDefinitions, route, list-request, and host-auth contracts as unchanged from `4c933f768f4cb11b16249d0f5a971312cd293f56` through `7a95893176bbaa75afb4b12fc3d69e3f815320b8`; the facts and line references below are checked against 7a.

## Source findings

| Concern | Source at 7a | Finding |
|---|---|---|
| List state and effective folder | `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions.tsx:31-61` | Page, page size, result state, rows, cursor map, selection, and folder-capability state are local to the list. Until capability discovery settles, the effective folder is provisionally `all`. |
| Supersession and current loading | `WorkflowDefinitions.tsx:86-91,112-178` | Browse rebasing invalidates the current generation and clears page/cursors. A new load increments the generation, sets `loading`, then commits only if its generation remains current. There is no debounce and an older request can still commit after a new input is entered but before a delayed new load begins. |
| Search/history behavior | `WorkflowDefinitions.tsx:93-106,312-328`; `workflowDefinitionBrowseLocation.ts:4-49` | Search updates the current URL immediately using replace; folder/lifecycle changes use push. Page and page size are not URL fields. Page-size changes reset to page one and clear cursors. |
| Loading/empty/error and row controls | `WorkflowDefinitions.tsx:403-432,451-509` | A refresh replaces the list with a skeleton; failure and ready-empty are distinct branches. Rows have selection and actions, so retained rows for another search must be display-only and must not keep the new view’s count, pager, selection, or actions. |
| Cursor and legacy paging | `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/api/workflowDesign.ts:30-39,213-249`; `DefinitionPager.tsx:5-40`; `constants.ts:5-6` | The paged endpoint uses the actual returned continuation token and page size; numeric page is UI state, not sent to that endpoint. Without the page capability, the client fetches the list and slices locally. Existing sizes are 10, 25, and 50; default is 10. Both paths must remain supported. |
| Editor/list lifetime and event ordering | `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/pages.tsx:14-64` | The route parent remains mounted while it conditionally replaces the list with the editor. Opening a definition pushes only the `definition` selector and synchronously dispatches `popstate`; the child list may receive that event before unmount. The parent currently keeps only `definitionId`, while the list listener unconditionally rebases. The route owner must handle browse-key comparison before the list unmounts and provide the list controlled page/cursor state; avoid a second independently rebasing list listener. |
| Route-session ownership and direct-list callers | `pages.tsx:14-64`; `Studio/tests/browser/src.tsx:978,1126`; exact-head `git grep -n -E '<WorkflowDefinitions|WorkflowDefinitions context=' HEAD -- Studio` | Production has one route-level list mount. Two browser fixtures mount `WorkflowDefinitions` directly: the browse fixture uses a no-op `onOpen`, and the folder-restructure fixture also bypasses the route owner. The route hook must be the sole page/page-size/cursor/browse-key/invalidation owner, with controlled list values/actions. Adapt both internal fixtures to use the same route-owned session; use the real `WorkflowManagementPage` editor/list path in the return-context scenario. Utility/API tests that do not render the component are not direct list callers. |
| Host route lifetime | `Studio/src/apps/Elsa.Studio.Web/Client/src/app/App.tsx:160-180,395-438`; `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/module.tsx:116-120` | The host selects/render routes by path and maps `/workflows/definitions` to the workflow-management route. The editor selector remains inside that route. Same-route return can use the existing route owner; full remount has no in-memory session. |
| Request init and error status | `Studio/src/apps/Elsa.Studio.Web/Client/src/sdk/index.ts:132-180,2295-2368,2396-2399,2542-2551` | The current public `getJson` accepts `StudioRequestInit`, which extends `RequestInit`; the timeout wrapper combines the caller’s signal. Non-OK JSON responses throw public `StudioHttpError` with a numeric `status`. Detect 401/403 by the typed status, not by parsing message text and not by changing the SDK. |
| Auth/session observation | `Studio/src/apps/Elsa.Studio.Web/Client/src/auth/types.ts:107-108`; `Studio/src/apps/Elsa.Studio.Web/Client/src/auth/transport/http.ts:41-64,75-104`; `Studio/src/apps/Elsa.Studio.Web/Client/src/app/auth/studioAuth.tsx:35-60` | Existing public session-start/end events and host auth boundary are available. The HTTP transport retries a 401 after refresh and emits session-ended when refresh fails. A successful silent refresh is not surfaced as a module-visible start event, so the feature cannot promise immediate detection of an access change not exposed by the host or a failed request. |
| Current browser fixture | `Studio/tests/browser/src.tsx:28-29,976-979`; `Studio/tests/browser/workflow-definition-browse-context.spec.ts:1-46` | The current browser fixture renders the real list but passes `onOpen={() => undefined}`. It verifies current search/folder/history/reload behavior, not actual editor return. Extend the fixture to render the actual `WorkflowManagementPage` and the editor callback. |
| Existing package commands | `Studio/src/essentials/Elsa.Studio.Workflows/Client/package.json:19-25`; `Studio/package.json:7-18` | The module has `test`, `typecheck`, and `build` scripts. The module build is Vite plus bundle-size check; Studio exposes recursive typecheck/build and lint, plus Playwright. Commands in quickstart are copied from those scripts and remain unrun. |

## Decisions and alternatives

### Debounce requests, not input or URL

**Decision**: Keep input and current `search` URL state immediate. When an effective search is entered, invalidate its predecessor at entry, then wait 250 ms before making the list request. If more edits occur, only the latest settled search is requested. Abort the older request through the existing request-init signal when useful; generation/identity validation remains the correctness mechanism.

**Rationale**: The accepted spec requires immediate input/address updates and stable results during refresh. Current search updates immediately, and loads start from the dependent effect. Delaying the input itself would change navigation behavior; delaying invalidation would allow an older in-flight response to replace the just-entered search before the newer request starts.

**Alternative considered**: Debounce the input or URL. Rejected because it delays the user-visible query and changes current browsing/history behavior.

### Separate the current request from last-success display rows

**Decision**: Keep a last-success view with its original search/page separately from the current request. Same-request refresh keeps the current result visible and announces refresh. A search-only change may keep the prior rows when the access boundary, effective folder, and lifecycle remain unchanged. Label the old search and old page; hide its count, pager, empty state, selection, actions, and cursor from the new request. If the search starts on a later page, reset the target request to page one but keep the old page’s rows only as the labelled display snapshot until new rows arrive.

**Rationale**: Page and page size participate in current browse state, while the requested filter set changes only on search. The approved spec explicitly allows showing the previous successful result during a search refresh; the old page must be named so it cannot be mistaken for results from the new query.

**Alternative considered**: Clear rows for every changed query or keep old rows with their pager/actions. The first loses the requested stable view; the second can present stale definitions as current or actionable. Neither meets the accepted behavior.

### Distinguish browse-key changes from page navigation

**Decision**: Search, folder, lifecycle, and page-size changes begin a new page-one browse and clear old cursor state. Folder/lifecycle/page-size changes also clear visible prior rows. Search-only changes may keep old rows under the display-only rule above. An explicit next/previous page request selects that page, clears visible rows from the old page while it loads, and uses the already observed cursor for the selected page. It preserves the cursor map needed to make that request and does not reset to page one. Keep current cursor pruning/update behavior after success.

**Rationale**: `DefinitionPager` calls the selected page, while the paged endpoint has no numeric page lookup. Resetting to page one on an explicit page click or clearing the map first would either ignore the author’s selection or erase the only valid cursor for it.

**Alternative considered**: Treat every request change as a page-one rebase. Rejected because normal page navigation is a user choice, not a filter change, and the existing API only reaches a later page with its observed cursor.

### Give the route one canonical browse-session owner

**Decision**: `WorkflowManagementPage` remains the owner, through one module-private `useWorkflowDefinitionBrowseSession` helper (or an equally cohesive private hook/reducer in the same implementation boundary). That session is the only source of browse key, page, page size, actual cursor history, and authority-invalidation revision. It passes controlled state and transitions to `WorkflowDefinitions`; the list owns rows/request status/selection/capability discovery but does not shadow those paging fields or install a competing rebasing listener. The route owner parses and compares the browse key on `popstate`, excluding the editor’s `definition` selector. Definition-only or same-browse transitions preserve page/cursors and tell the list to clear selection as appropriate. A changed search/folder/lifecycle key resets page/cursors. A full route remount starts with defaults; do not add page/cursor URL state or cursor replay.

**Rationale**: The actual route parent survives list/editor replacement and the literal acceptance asks Back to restore context. The current list receives the synthetic `popstate` before it unmounts, so the route owner must handle that transition at the source and prevent the old unconditional child rebase. One controlled session avoids parent/child shadow page state and ensures route Back, page transitions, and authority invalidation update the same cursor map. A numeric page cannot be requested directly from the cursor endpoint, so restoring cold links would require a larger replay/URL contract than the issue asks for.

**Alternative considered**: Keep the list mounted invisibly, add URL page state and replay cursor pages, or add an arbitrary replay limit. These all change lifetime, add unrequested behavior, or invent cursor navigation policy. Rejected.

**Internal-caller adaptation**: `git grep` at exact HEAD 7a finds direct `WorkflowDefinitions` mounts in the existing browse and folder-restructure browser fixtures (`Studio/tests/browser/src.tsx:978,1126`) in addition to the production route (`pages.tsx:60`). Both fixtures must compose the route-owned session so no second paging owner is introduced. The new browser return-context scenario must use the real `WorkflowManagementPage` path; the browse fixture’s current no-op `onOpen` cannot prove editor replacement/return. This is a private module integration detail, not a new SDK surface.

### Do not invalidate on temporary unknown folder capability

**Decision**: On list remount, keep the route-owned page/cursor session while child folder-capability discovery is `unknown`; do not interpret the child’s provisional `all` value as a real effective-folder change. Wait until capability discovery settles before loading. If the resolved effective folder matches the saved browse key, preserve the page and use its stored cursor. If it differs, clear old rows/page/cursors and start at page one.

**Rationale**: The current list intentionally gates loading while capability is unknown, but computes a provisional `all` selection during that interval. Treating the provisional value as final on editor return would lose valid cursor state before the child knows which folder behavior is available.

### Clear only on public authority boundaries

**Decision**: At the route owner, clear route page/cursor state on a change to `context.baseUrl`, the existing public auth-session start/end events, or the host auth-boundary remount. Clear mounted result rows, selection, and pending generations with it. A current `StudioHttpError` with status 401 or 403 clears protected state as well. Do not derive a key from a bearer token, raw headers, token factory, or private `AuthSession` fields.

**Rationale**: The public SDK already supplies the endpoint context, lifecycle events, cancellation signal, and typed HTTP status. Root confirmed these are the intended bounded contract. The current transport does not report a successful silent refresh to module consumers; no new event/key can be inferred or promised here.

**Alternative considered**: Add a token/session-version API or inspect token/header contents. Rejected as an unrequested auth protocol and privacy expansion. A request that succeeds after an invisible auth change remains outside the observable contract until the host reports a boundary or a request is denied.

## Verification baselines and limits

Source confirms the generation guard already prevents an old completion after a later `load()` has started; that alone is not new proof. The high-value RED case is resolving the older request after the author enters a newer search but before the newer debounced request begins. Source also confirms that failure and successful-empty are already distinct render branches, while refresh currently replaces rows with a skeleton. Same-route return is not proved by current browser evidence because its fixture has a no-op `onOpen`.

All tests, builds, lint, architecture checks, map freshness checks, and browser scenarios in this feature plan are **UNRUN**. No user-visible behavior, RED/GREEN result, production latency, or passing gate is claimed here.
