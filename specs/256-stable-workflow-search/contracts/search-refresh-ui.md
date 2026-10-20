# Search refresh UI contract

This internal contract describes the visible workflow-definition list behavior required by `spec.md`. It adds no route, endpoint, permission, or SDK contract. Existing URL fields for search, folder, and lifecycle remain the browser-facing browse state.

## Visible states

| State | Required user-visible behavior |
|---|---|
| First load, no successful result | Show the existing first-load waiting state. Do not imply that there are no matches. |
| Same-view refresh | Keep current rows and announce that they are refreshing. |
| Search-only refresh with an eligible prior result | Keep the previous rows visible and label them with their originating search and page. This applies when the previous page was later than page one and the new query targets page one. Do not present the old count, pager, empty message, selection, row actions, or cursor as belonging to the new request. |
| Changed folder, lifecycle, or page size | Clear rows from the prior view before loading the new view. |
| Explicit page navigation | Clear prior-page rows while the selected page loads. Keep the selected page and use its already-observed cursor; normal page navigation does not rebase to page one. |
| Current successful result with no rows | Identify the current search and filters and show the existing no-match state. |
| Current request failure | Distinguish the failure from no matches, identify the current search/filters, and offer a retry of that current request. If old search rows remain under the eligible retention rule, mark them as previous/out of date. |
| Reported authority change or current 401/403 | Clear protected rows, pending result state, selection, and remembered page context before presenting results under a new access context. |

All status information must remain understandable without color alone and available to assistive technology. Use existing Studio status patterns and tokens.

## Browse and return behavior

- Search input and existing search URL update immediately. Only list requests for text search wait briefly; supersession occurs at entry, before the delay.
- Search-only refresh may show the old search/page snapshot while requesting the new search at page one. The new query's current status and retry describe the new query; the snapshot remains display-only.
- Folder, lifecycle, and page-size transitions start page one and clear previous rows and page context.
- Explicit next/previous navigation opens the selected page using its saved, API-issued cursor. It clears prior-page rows but does not delete the cursor required for the requested page.
- Editor Back and browser Back during the same mounted workflow-management visit restore the current search, folder, lifecycle, page size, and page. A definition-only or same-browse `popstate` preserves the route-owned session and may clear selection. A changed browse key follows the rules above.
- A full reload or route remount starts with the current default page and page size. There is no cold-link page/cursor replay or URL cursor representation.
- While folder capability is unresolved on editor return, do not interpret a temporary provisional folder as a resolved change. Preserve the session until the actual effective folder is known, then retain it only if that folder matches.

## Boundaries

Only the workflow-definition list changes. Search and list calls keep the existing folder, lifecycle, permission, endpoint, paged-cursor, and legacy-unpaged behavior. The module may react to public endpoint/session lifecycle changes, host remounts, and a current typed 401/403. It cannot promise instant detection of an access change that Studio does not report and that has not produced a denied current request. No bearer-token-derived key, new auth event, or new public API is introduced.
