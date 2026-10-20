# Implementation Plan: Stable workflow search

**Branch**: `agent/codex/3068-workflow-search` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Approved specification for Foundation #3068.

## Summary

Make workflow-definition search stable while a new result is requested, distinguish current results from previous results and failures, and preserve the current browse page when the author returns from an opened definition. Keep current URL and request behavior, both paged and legacy unpaged list support, and existing access permissions. Store temporary browse state in one module-private session owned by the already mounted workflow-management route. The route passes that state and its transition callbacks to the list as controlled paging; the list does not keep a second page, page-size, browse-key, or cursor-map copy. A full reload or route remount starts at defaults.

The design has two separate identities: the request currently being entered or loaded, and the last successful rows that may remain visible. Entering a new search immediately supersedes older work, then waits 250 ms before requesting the latest trimmed search. If the author was on a later page, the new search starts at page one, while rows from the previous page may remain as clearly labelled display-only results until the new result arrives. Folder, lifecycle, explicit page, and page-size transitions clear those previous rows. Explicit page navigation uses the cursor already observed for the selected page; it does not reset to page one or discard the cursor needed for that request.

The route owner observes the existing access-session lifecycle and endpoint changes even while the list is unmounted in the editor. A current 401/403 is detected through the existing public `StudioHttpError.status`. These signals clear retained rows and route-owned page/cursor state. The current host does not expose a successful silent token refresh to module consumers; the feature will not add a credential-derived identity or promise detection of an unreported access change.

## Technical Context

- **Language/version**: TypeScript; Studio Workflows module supports React 18 and 19 peers, and the current Studio workspace uses React 19 (`Studio/src/essentials/Elsa.Studio.Workflows/Client/package.json:32-36`).
- **Primary dependencies**: Existing Studio SDK HTTP client, Studio UI, React, Vitest, Playwright, and workspace `pnpm`; no new dependency is proposed.
- **Storage**: No persistence. Last-successful rows and request status are temporary list state. Current search, folder, and lifecycle remain in the existing URL. One route-owned session holds the parsed browse key, page, page size, actual continuation-token history, and authority-invalidation revision while the editor is open; controlled props/actions connect it to the list without shadow paging state.
- **Testing**: Workflows Client Vitest suite and the Studio Playwright workflow-definition browser fixture, extended to exercise the actual `WorkflowManagementPage` editor/list path. Studio-wide type checking, lint, and build remain integration gates.
- **Target platform**: Elsa Foundation Studio in a browser.
- **Project type**: Existing Studio Workflows client module.
- **Performance goals**: None. The candidate 250 ms delay is request pacing after typing, not a production response-time target or benchmark claim.
- **Constraints**: No server endpoint or DTO change; no new SDK/authentication contract, permission, configuration, credential-derived key, persistent storage, URL page/cursor state, cursor replay for cold links, or unrelated list changes. Preserve current trimmed `searchTerm`, folder/state behavior, endpoint capability discovery, and legacy unpaged fallback.
- **Scope**: Workflow-definition search/refresh state, route-owned return context, and focused tests/browser fixture only.

## Constitution Check

### Before research and design

Reviewed both constitutions and `docs/contributing/review-checklist.md` at pinned source `7a95893176bbaa75afb4b12fc3d69e3f815320b8`:

- Framework Gate index: `.specify/memory/constitution-framework.md#gate-index` (§2.1–§4.2).
- Elsa Gate index: `.specify/memory/constitution.md#gate-index` (§E2.2–§E5).
- Review-only gates: `docs/contributing/review-checklist.md`.

This is confined to an existing Studio client feature and its tests. It adds no domain, package, feature registration, service, endpoint, persisted entity, public SDK surface, or backend dependency. No architecture boundary gate or Elsa runtime/design gate is changed. The relevant review rule is framework §2.23.2: behavior tests must cover the risky user-visible decisions, failures, and request races; tests must not assert on source or documentation text. The explicit RED/GREEN plan below is required by the issue’s delivery request, not a claim that this repository has a general TypeScript test-first cadence.

### After design

The proposed design remains inside the same client module, uses only existing public SDK contracts, and introduces no new package or public application contract. Retest the touched behavior with the affected Workflows suite and real route browser scenario. No constitution violation or complexity exception is identified.

## Source-backed design decisions

### Search lifecycle and retained results

`WorkflowDefinitions.tsx:31-60` currently owns page, page size, list status, rows, cursor history, selection, and folder-capability state. `:128-178` starts a load, switches the whole list to `loading`, and uses a generation counter to ignore responses after a later load has started. `:316-318` updates the URL/input immediately; the resulting search change currently starts a load immediately. `:403-432` renders a skeleton instead of rows while loading and distinguishes a failed card from a successful empty state. The current route parent `pages.tsx:14-64` owns only the selected definition. The implementation will move the canonical browse session into one module-private route-owner hook and make page/page-size/cursors controlled by that session.

Keep the visible input and existing URL update immediate. On a search entry that changes the effective request, increment the generation immediately and abort the previous request where possible, before scheduling the 250 ms list-request delay. Debounce only the text search list request; folder/lifecycle changes, explicit page navigation, page-size changes, manual refresh, and route/history changes remain immediate. Keep the generation/identity check as the correctness guard even when an abort signal is used.

Track the latest successful view separately from current loading state. A same-request refresh keeps that result current and announces refreshing. A search-only change within the same endpoint/access boundary and same effective folder/lifecycle can keep the previous rows visible with the old search and old page identified. If that search began on page two or later, reset the target request to page one but preserve the old page’s rows only as a display snapshot; do not show its count, page controls, empty claim, selection, actions, or cursor as belonging to the new search. A folder/lifecycle change, page-size change, or explicit page change clears those display rows. A successful current response replaces the snapshot; a failed current response is distinct from no matches and retries the current request.

### Browse identity and page transitions

`workflowDefinitionBrowseLocation.ts:4-49` currently defines the URL-backed search, lifecycle, and folder fields and preserves unrelated parameters. `workflowDesign.ts:213-249` keeps the current trimmed `searchTerm` contract. When the paged capability is present, the endpoint accepts an actual continuation token and returns the next token; a numeric page is client metadata only. When the capability is absent, the client fetches the collection and slices it locally. Keep both paths.

Keep these concepts separate:

- **Browse key**: effective search term, effective folder, and lifecycle state. It excludes the editor’s `definition` selector.
- **Current request**: authority boundary, browse key, selected page, page size, and the actual continuation token (when the paged endpoint is used).
- **Last successful display snapshot**: rows plus their originating search/page, used only while an eligible search-only refresh is pending or failed.

Use one internal `useWorkflowDefinitionBrowseSession` (or an equivalently cohesive private route-owner helper) as the sole owner of the browse key, page, page size, cursor map, and authority-invalidation revision. `WorkflowManagementPage` creates it once and passes controlled session values and transition callbacks to `WorkflowDefinitions`. The list continues to own its request lifecycle, result rows, previous-result snapshot, selection, and folder-capability discovery; it reports settled effective-folder changes and typed authority denials to the route owner. It must not duplicate page, page-size, cursor-map, or canonical browse-key state. This keeps route restoration, `popstate`, and invalidation transitions in one place while avoiding a new public SDK or application-wide cache contract.

Search/folder/lifecycle changes and page-size changes start at page one and clear cursors for the new request. Search-only changes may retain old rows as described above, including when the previous page was greater than one, but they do not reuse the old count, pager, selection, or cursor for the new query. A folder or lifecycle change clears those rows. Explicit page navigation clears the displayed rows for the previous page, requests the selected page using its already observed cursor, and preserves the cursor map needed for that request. It does not call the page-one rebase. After a successful page response, keep the existing cursor pruning/update behavior.

### Route return, history, and capability discovery

`pages.tsx:14-64` keeps `WorkflowManagementPage` mounted while replacing `WorkflowDefinitions` with `WorkflowEditor`. `openDefinition` pushes the `definition` selector and dispatches `popstate` at `:45-48`; that event reaches the list before the editor replaces it. The page number, page size, and continuation-token map currently live only in the child list, so they are lost on return. Move the canonical browse session to one module-private route-owner hook, with the route as the only page/page-size/cursor/browse-key owner and the list consuming controlled values/actions. The route owner compares parsed browse keys on `popstate`: a definition-only or same-browse transition preserves route page/page-size/cursors and tells the list to clear selection as appropriate; a changed search/folder/lifecycle key rebases to page one and clears cursors/selection. Do not keep a second list-side paging state or a second listener that independently rebases. This handles the event before list unmount and the later Back event without requiring a new navigation contract.

On return, the list initially has unknown folder capability (`WorkflowDefinitions.tsx:51-61, 108-110, 128-130`). Do not interpret that temporary `unknown` state as an effective “all folders” change and erase the route’s saved cursor. Defer the list request until capability discovery settles. Then compare the resolved effective folder with the preserved browse key: keep page/cursors if it is the same; clear rows/page/cursors and start at page one only if it actually changed.

The Studio host chooses the active route from `pathname` and renders it in the same route position (`Studio/src/apps/Elsa.Studio.Web/Client/src/app/App.tsx:160-180, 395-438`); module registration maps `/workflows/definitions` to this route (`module.tsx:116-120`). The same-route editor return therefore has an existing mounted owner. A full remount has no saved state and starts at defaults. Do not add numeric page URL fields, cursor serialization, cold-link page recovery, replay, scroll, or focus behavior.

### Access invalidation and cancellation

The route owner observes changes to the existing endpoint `baseUrl`, public auth-session start/end events, and the host auth-boundary remount while the child list is absent. Clear route page/cursor state immediately on those observed boundaries; invalidate pending child generations and clear retained rows/selection when mounted. A current query that finishes with `StudioHttpError.status` 401 or 403 clears protected rows and route-owned cursors as well. The type exposes the status from `StudioHttpError` (`sdk/index.ts:2396-2399, 2542-2551`); do not parse an error string. The SDK accepts optional `RequestInit` for `getJson` (`sdk/index.ts:132-180`) and carries request signals through its timeout handling (`:2314-2347`), so a normal `AbortSignal` can be threaded through the existing list calls if needed. An abort is an optimization; it does not replace generation invalidation.

The authenticated transport retries an initial 401 after its existing refresh (`auth/transport/http.ts:41-64`) and reports session end when refresh fails (`:75-104`). A successful silent refresh is not exposed as a module-visible session-change event. Do not invent an auth key or new SDK event to fill that gap; do not promise immediate detection of a server-side or credential change the public host contract does not report. The route-owned session handles public endpoint/session/remount events even while the editor is mounted; a current typed 401/403 from the child clears that same session through a callback.

### Accessible status

Use the existing Studio list/status and token patterns. Expose current refresh and previous-search status to assistive technology; identify the old search and old page when old rows remain; distinguish current failure from successful no matches. Do not rely on color alone. If the UI needs new CSS, use existing Studio tokens. Test state/roles/row behavior rather than implementation or documentation text.

## Red/GREEN proof plan

The following are expected baseline results inferred from exact source, not executed evidence. The implementation worker must run the tests first and record actual RED/GREEN outcomes; if a baseline assertion is already GREEN, report that honestly and add the missing behavioral condition instead of claiming a regression was newly fixed.

| Behavior | Exact-source baseline signal | New proof and expected result |
|---|---|---|
| Rapid typing | Search URL state changes immediately (`WorkflowDefinitions.tsx:316-318`) and its load effect is not debounced (`:176-178`). | Fake-timer Vitest case fails before the change because requests start for each edit; passes with one request for the latest settled search while input/URL change immediately. |
| Latest result after newer search entry | Generation check at `:146,170` protects only after a later load starts; no delayed interval currently exists. | Deferred-response test starts an old request, enters a new search, resolves the old request before the debounce fires, and proves it cannot become current. This pre-change case should expose the missing immediate invalidation. |
| Useful rows/status during refresh and failure | Each load sets `loading` (`:133-135`), which renders a skeleton (`:423`); current failures render an error card (`:403-412`). | Component tests fail before the change because eligible prior rows do not remain visible or clearly labelled. Verify same-view refresh, changed-search success, and changed-search failure separately. |
| Empty versus failed result | The source currently has distinct ready-empty and failed branches (`:403-430`). | Preserve and test this baseline distinction, then cover latest-query copy/retry and retained stale rows; do not report the pre-existing empty/error distinction itself as newly fixed. |
| Explicit page navigation | The pager selects its requested page (`:513-520`); `load` uses the selected page and its saved token (`:128-145`). | Test page two requests the already observed token, clears the old page’s visible rows, and stays on page two. This guards against an incorrect page-one reset or clearing the cursor needed for the target page. |
| Editor return and synthetic `popstate` | Page state is child-owned (`:34-42`); parent only tracks the selected definition (`pages.tsx:37-63`); the synthetic event is dispatched at `:45-48`, while the list listener unconditionally rebases (`WorkflowDefinitions.tsx:119-126`). | A real route browser test must fail before the change: open a definition from a later page, prove the definition-only event preserves paging before list unmount, then prove editor Back and browser Back restore the page and use its real observed cursor. |
| Access and capability changes | The list loads only after its folder capability settles (`WorkflowDefinitions.tsx:51-61,128-130`); current child has no route-owned access-boundary state. | Test route-owner invalidation while the editor is open for endpoint change and both public session events; test a current typed 401/403; and test that temporary unknown folder capability on editor return preserves saved cursors until the effective folder is known. |

## Project Structure

```text
specs/256-stable-workflow-search/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── search-refresh-ui.md
└── tasks.md                         # Future /speckit-tasks output; not created in this phase

Studio/src/essentials/Elsa.Studio.Workflows/Client/
├── src/workflow-editor/WorkflowDefinitions.tsx
├── src/workflow-editor/pages.tsx
├── src/workflow-editor/workflowDefinitionBrowseSession.ts
├── src/workflow-editor/workflowDefinitionBrowseLocation.ts
├── src/api/workflowDesign.ts
└── src/__tests__/
    ├── module.test.tsx
    ├── workflowDefinitionBrowseSession.test.ts
    └── workflowDefinitionBrowseLocation.test.ts

Studio/tests/browser/
├── src.tsx
└── workflow-definition-browse-context.spec.ts
```

**Structure Decision**: Keep code in the current Workflows client and browser fixture. `pages.tsx` is a core implementation file because it owns route lifetime and the editor/list conditional. `workflowDefinitionBrowseSession.ts` contains the private session transitions/hook used by that route owner and its focused tests. Adapt internal direct-list fixtures to compose the same route-owned controlled session rather than create their own paging state; keep at least one browser path on the real `WorkflowManagementPage` editor/list route so its return behavior is exercised. The current browse fixture mounts `WorkflowDefinitions` with `onOpen={() => undefined}` (`Studio/tests/browser/src.tsx:976-979`) and cannot prove same-route editor return. Keep the UI state contract in this feature’s `contracts/` directory; no public API or server contract is added.
