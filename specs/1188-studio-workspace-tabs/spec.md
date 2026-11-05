# Studio workspace tabs

**Status**: Implemented — [#3639](https://github.com/elsa-workflows/elsa-foundation/pull/3639)
**Issue**: [#3620](https://github.com/elsa-workflows/elsa-foundation/issues/3620)
**Program bucket**: none/free-flow

## Problem

Studio renders one routed screen at a time. Moving from a workflow draft to a failed run and back
replaces the screen, so the user loses the selected activity, graph position, list filters and any
unsaved in-memory edits. Several related screens cannot stay at hand.

## Outcome

One host-owned tab strip shared by every full Studio screen, including screens from optional
modules. Ordinary navigation reuses the active tab; the user explicitly opens another tab. Switching
tabs keeps each screen's working state, closing respects existing save and guard behaviour, and the
same user returning to the same backend gets their tabs back.

## Owner-selected behaviour (from the issue)

- Ordinary navigation reuses the active tab; nothing accumulates tabs implicitly.
- An explicit **New tab** action, an **Open in tab** gesture and discoverable shortcuts.
- Open tabs and supported view state are restored for the same user and backend. Restoring a tab is
  never evidence that its edits were saved; draft recovery stays with the owning screen.

## Design

### Lifecycle: retained tabs live inside React `<Activity>`

Each retained tab renders its route screen inside its own `<Activity>` boundary and a
`role="tabpanel"` wrapper. The active panel uses `display: contents`, so route pages keep the
existing `.content > …` layout rules (those selectors now also match through the active panel).

A background tab is `<Activity mode="hidden">`: React keeps its state (drafts, undo history,
selection, graph viewport, list filters held in state) but tears down its effects. A background
tab therefore stops polling, drops subscriptions and SignalR listeners, and stops listening to
`popstate` and the navigation-guard event until it returns, when its effects mount again against
its own restored address. This is the inactive-tab lifecycle the issue asks for.

Resource bounds:

- At most five background tabs stay retained; older clean ones are released (unmounted) and remount
  from their location when visited again. A tab that holds unsaved or pending work is never released.
- At most 30 tabs are open; opening more closes the oldest clean, unpinned background tab.
- Restored tabs are not mounted until visited.
- Each panel has its own error boundary: a failing screen offers **Reload this tab** and **Close
  tab**, and the strip and other tabs keep working.

Because effects mount again when a tab returns, screens that seed editable state from a load-on-mount
effect must load once per resource. The workflow editor's data hook, Feature Management's catalog
refresh, Theme Builder's store load and draft seeding, the Activity Definition upgrade workbench's plan
load, and the workflow definition list's scope reset (which closed an open create dialog and dropped an
in-flight create) now do.
Without that, returning to a tab replaced the unsaved draft and its undo history with the server copy
(found during live qualification). Audited and unaffected: the Activity Definitions pages (TanStack
queries; drafts seed `useState` once), the Backends form (local state only), and Appearance, Modules
and Package feeds (no editable server draft). React Flow also rebuilds its pan-zoom from
`defaultViewport` and resets its store on effect teardown, so every Workflows canvas renders
`RetainFlowViewport`, which records each settled pan/zoom and restores it when the canvas returns.

### Address and history

The address bar always shows the active tab. The shell records which tab wrote each history entry
(a `elsa.studio.workspaceTab` key in `history.state`, added by a thin observer around
`pushState`/`replaceState`, because screens write history directly). The observer also keeps the
active tab's location current when a screen updates filters with `replaceState`.

- Switching tabs pushes the target tab's address, so **Back** returns to the previous tab.
- **Back/Forward** onto another open tab's entry returns to that tab as it is now; onto a closed
  tab's entry it reopens that tab at that address; an untagged entry navigates the active tab.
- The shell's `popstate` listener runs in the capture phase and stops propagation when an entry
  belongs to another tab, so the visible screen never reacts to an address that is not its own.
- A direct link wins over the restored active tab: it focuses the tab already showing it, or opens
  beside the restored tabs. Opening Studio at `/` restores the last active tab.

### Navigation, guards and closing

Existing editors guard navigation by cancelling `elsa:studio-navigation-requested`.
`StudioNavigationRequest` gains an optional `intent`: `navigate` (default, also what older hosts
mean), `close`, or `probe`.

- Navigation inside the active tab asks the guards with `navigate`, exactly as before. Background
  guards are not mounted, so they cannot block the active tab.
- Switching tabs is not navigation. The shell sends a `probe` on the way out; a guard that cancels it
  marks the tab with an unsaved-work dot. A probe must not prompt or change UI (Backends' discard
  confirmation and Feature Management's error message now skip probes).
- Closing the active tab asks its guards with `close`; a blocked close keeps the tab and shows a
  notice. Closing a background tab that was marked keeps it and brings it to the front. **Close
  other tabs** and **Close all tabs** keep pinned tabs and tabs with unsaved work, and say how many
  stayed open.
- Theme Builder reports an unsaved custom theme or a Theme Store write in progress to probes and closes
  (not to ordinary navigation, which keeps its earlier behaviour), so a background tab holding one is
  never released or closed silently.
- While a background tab is marked, the shell's own `beforeunload` warns before the document unloads
  (backend switch, sign-out redirect, module reload, browser close).

### Identity and dedupe

A route may declare `workspace.resourceKey(location)`. Two tabs never show the same resource:
navigating to one already open elsewhere focuses that tab, so a hidden editor and a visible one never
write the same draft. Lists return no key and may be open several times. An explicit open of a list
focuses an identical open list (path and sorted query).

Tabs are restored before routes are registered, so a deep link or the restored set can briefly hold two
tabs for one resource. As soon as routes can name resources the shell folds them: the active tab (the
deep link) survives in the earliest duplicate's place and pin, and a mounted duplicate, which may hold
edits, is never dropped. Back onto a closed tab whose resource is open elsewhere focuses that tab instead
of reopening a second editor.

Workflows declares keys for workflow definitions (`definition`), workflow templates (`template`),
Activity Definition drafts/versions (`definition` + `draft`/`version`) and upgrade plans (`plan`).

### Titles and icons

The shell names a tab from its settings section, navigation entry or route label, adding a short id
for parameterized routes, and uses the navigation icon. A screen can name its tab with
`useStudioWorkspaceTitle(title, detail)`: the workflow editor (`<name> · Draft` / `Template`),
Activity Definition drafts (`Draft` / `Published`) and runs (`Run <id> · Execution`). A module title
holds until the tab moves to another screen.

### Background rendering

A hidden tab can still render (a request resolves, the registry refreshes) while another tab owns
the address bar. `useStudioRouteLocation()` returns the tab's own location for that case; the run
inspector and Backends page now read their location through it instead of `window.location`.

A hidden tab can also finish async work that navigates: a create that opens what it created, or an
upgrade plan that records itself in the address. History belongs to the active tab, so such a write
would move the active tab to another screen without asking its guards. `useStudioWorkspaceWhenActive()`
returns a function that runs navigation at once while the tab is active and otherwise holds it until
the tab returns (the latest held navigation wins), after the screen's own listeners are back. Holding
marks the tab as holding pending work, so it is not released meanwhile. The handle's `isActive()`
answers for the current moment, inside async callbacks too. Activity Definitions
navigation, the upgrade workbench's plan address and the workflow definition create flow use it.

### Opening tabs

- **New tab** button and `Alt+Shift+T` open Home in a new tab.
- **Alt-click** (Option-click) on sidebar links, Diagnostics links and `StudioLink` opens the link in
  a workspace tab, focusing the tab that already shows it. Ctrl/Cmd-click, Shift-click, middle-click,
  `target`, `download` and external links keep their browser behaviour (#3243).
- Modules call `openStudioWorkspaceTab(path)`; it returns false on hosts without workspace tabs.

### Strip interaction and accessibility

`role="tablist"` labelled *Open screens*, roving focus with manual activation (arrows, Home/End;
Enter/Space activates), `Delete` closes, `Shift+F10`/context-menu key or right-click opens the tab
menu (Pin/Unpin, Move left/right, Close, Close other tabs, Close all tabs), `Alt+Shift+←/→` moves the
focused tab, drag and drop reorders within the pinned or unpinned group, middle-click closes. The
**All tabs** menu lists every tab for overflow. The strip scrolls horizontally and keeps the active
tab in view. Shortcuts (`Alt+Shift+T/W/P/[/]/1–9`) use physical key codes and are ignored while
typing. Pinned tabs lead the strip and have no close button.

### Persistence and isolation

`localStorage` key `elsa.studio.workspace-tabs.v1:[origin, backend id, backend URL, subject,
tenant]`. Stored per tab: id, sanitized location, pinned, module title and content scroll offset.
Never stored: `history.state`, drafts, payloads, the backend qualifier, credentials, sign-in callback
parameters or one-shot intents (`homeAction`, `backendConnect`). Sign-out drops stored titles.
Another backend, user or tenant reads a different key; backend and identity changes already reload
the document, so no tab is reinterpreted against another backend.

## Known limits

- A screen that writes history from an async callback without `useStudioWorkspaceWhenActive()` still
  writes the active tab's address if its tab went to the background first. The flows above that
  navigate after a request use it; other history writes come from user actions or effects. A tab
  released before its request even completes (five other tabs visited during one request) remounts
  from its address and drops the held navigation.
- Across browser sessions only the location, pin, title and scroll offset are restored; graph
  viewport, selection and undo history live only in memory, as the issue scopes.
- Split views, docking, tab groups, preview tabs and cross-device sync are out of scope.

## Acceptance mapping

| Issue criterion | Evidence |
|---|---|
| 1. Open list, editor, execution and settings/module screens in tabs; navigation reuses the active tab | `workspace-tabs.test.tsx` (navigation reuses the active tab, explicit opens, module requests); live: workflow editor, Runs list, Diagnostics, Settings and Feature Management opened with Alt-click and the strip |
| 2. Switching keeps working state and edits; close honours save/recovery guards | `workspace-tabs.test.tsx` (state kept while effects are torn down, close and probe guards, `beforeunload`); `useWorkflowEditorData.test.tsx` (no reload on return); `backends.test.tsx` (probe never prompts); live: selection, inspector tab, graph viewport, unsaved Delay, undo/redo, Runs filters and an unapplied feature draft survive switching; a guarded tab refuses to close with a notice |
| 3. Identify, focus, close, reorder, pin, keyboard, overflow, themes | `workspace-strip.test.tsx` (names, roving focus with manual activation, Delete, tab menu, All tabs menu, axe WCAG A/AA); `workspace-model.test.ts` (order, pins); live: Shift+F10 menu, pin, Alt+Shift+W, dark and light themes, 375 px width |
| 4. Restore tabs and view state per user and backend | `workspace-model.test.ts` (scope key, sanitizing, deep-link precedence); `workspace-tabs.test.tsx` (restore, lazy mount); live: reload and `/` restore the pinned set and the last active tab, mounting only that tab |
| 5. URLs, history, native links, backend/identity isolation | `workspace-tabs.test.tsx` (Back across tabs, owner redirect, resource dedupe); `studioLink.test.tsx` (Alt-click, browser fall-through); live: Back returns to the previous tab |
| 6. Composed qualification with heavy screens | Live: a following Test Run polled 120 requests per 20 s with its editor active, 0 while the tab was hidden, and resumed on return with its state intact. Recorded on the PR |
