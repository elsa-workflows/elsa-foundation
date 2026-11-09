# Studio Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace per-page alert bars with a host-owned notification service: toasts plus a header bell and Notification Center, persisted per identity and backend.

**Architecture:** A pure SDK controller (`createNotificationController`) holds entries and exposes a publish-only `api.notifications` to modules, mirroring `createDialogController`/`api.dialogs`. The host owns one singleton with a localStorage adapter, scopes it to the active identity and backend, and renders `<ToastHost />` and `<NotificationCenter />` from the same store. The workflow definitions page is the reference migration.

**Tech Stack:** TypeScript, React 19 (`useSyncExternalStore`), `@radix-ui/react-dialog`, Vitest + jsdom, Playwright, pnpm workspace in `Studio/`.

**Spec:** [`specs/1187-studio-notifications/spec.md`](spec.md)

Paths below are relative to `Studio/src/essentials/` unless they start with `Studio/` or `specs/`.
`H` = `Elsa.Studio.Hosting/Client/src`, `W` = `Elsa.Studio.Workflows/Client/src`.

## Constitution Check — before implementation

Framework 6.1.0 and Elsa 6.0.3 consulted. Client-only change: no Core, domain, persistence, transport,
backend or .NET public signature change. One additive public TypeScript contract
(`StudioNotificationApi`) on the Studio SDK, declared optional on `ElsaStudioModuleApi` so hosts and
module bundles built against the previous SDK keep compiling and loading (same pattern as
`workflowRunInputEditors?`). No new dependency. Styling follows `Studio/docs/design-tokens.md`
(`--studio-*` only; `pnpm lint:css` enforces). Risk-based tests target store semantics,
persistence isolation and real rendered interaction. No exception required.

## Global Constraints

- Levels: `"info" | "success" | "warning" | "error" | "progress"`.
- Toast lifetime: info/success 8000 ms, warning 12000 ms, error and progress `null` (sticky).
- At most 3 toasts visible; newest on top.
- Storage key: `elsa-studio:notifications:v1:<userKey>:<backendKey>`; no tokens or emails in keys or values (`userKey`/`backendKey` are hashes).
- Persist: newest 50 entries, drop entries older than 7 days (604 800 000 ms), debounce writes 250 ms.
- Restored `progress` entry → `level: "warning"`, `message: "Status unknown — this operation was interrupted by a page reload"`, link kept.
- `action` is never persisted; `link.path` is a Studio route navigated by the host.
- `source` defaults to `"studio"`.
- Bell accessible name: `Notifications, N unread`; badge text `9+` above nine.
- Center copy: heading `Notifications`, buttons `Mark all read`, `Dismiss all`, empty state `No notifications`.
- Live region: `assertive` for error, `polite` otherwise.
- Every `localStorage` access is inside try/catch.

## Review Focus

1. **Notification published before the host sets a scope** (module registers and notifies during startup): entries must be kept in memory and written to the scope once it is set, not dropped. Test in Task 1.
2. **Corrupt or foreign JSON in storage** (older version, manual edit, truncated write): load must return an empty list without throwing. Test in Task 2.
3. **`update()` or `dismiss()` on an entry that was already dismissed or evicted by the cap**: must be a silent no-op, never resurrect the entry. Test in Task 1.
4. **Action callback throws or rejects** (e.g. Restore fails): the toast must still close and the rejection must surface as an error notification, not an unhandled rejection. Test in Task 5.
5. **Async result arriving after a backend switch** (`isCurrentGeneration()` false): must publish nothing into the new scope. Test in Task 7.

---

### Task 1: SDK contract and controller

**Files:**
- Create: `H/sdk/notifications.ts`
- Modify: `H/sdk/index.ts` (add `export * from "./notifications";` with the other re-exports; add `readonly notifications?: StudioNotificationApi;` to `ElsaStudioModuleApi` directly after `dialogs`, doc comment "Host notification service. Optional for hosts built against older SDKs.")
- Test: `H/__tests__/notification-controller.test.ts`

**Interfaces:**
- Produces (all exported from the SDK):
  - `StudioNotificationLevel`, `StudioNotificationInput`, `StudioNotificationHandle`, `StudioNotificationApi` exactly as in the spec.
  - `interface StudioNotificationLink { label: string; path: string }`, `interface StudioNotificationAction { label: string; run(): void | Promise<void> }` (named so the spec's inline shapes are reusable).
  - `interface StudioNotificationEntry { id: string; level; title: string; message?: string; link?: StudioNotificationLink; action?: StudioNotificationAction; source: string; createdAt: number; updatedAt: number; read: boolean; toasting: boolean }`
  - `interface StudioNotificationStorage { load(scope: string): StudioNotificationEntry[]; save(scope: string, entries: StudioNotificationEntry[]): void; subscribe?(scope: string, listener: (entries: StudioNotificationEntry[]) => void): () => void }`
  - `function notificationToastDurationMs(level: StudioNotificationLevel): number | null`
  - `interface StudioNotificationController { readonly api: StudioNotificationApi; getEntries(): readonly StudioNotificationEntry[]; subscribe(listener: () => void): () => void; setScope(scope: string | null): void; replaceEntries(entries: StudioNotificationEntry[]): void; hideToast(id: string): void; markRead(id: string): void; markAllRead(): void; dismiss(id: string): void; dismissAll(): void }`
  - `function createNotificationController(options?: { storage?: StudioNotificationStorage; now?: () => number; createId?: () => string }): StudioNotificationController`

Behavior the tests pin: `getEntries()` returns a new array reference only when something changed (required by `useSyncExternalStore`), newest first. `notify` creates `read: false, toasting: input.toast !== false`. `update` merges the patch and sets `updatedAt`; when the resulting level is success/error/warning and the previous level was `progress`, it also sets `read: false, toasting: true`. `setScope(next)` saves the current list under the old scope (if any), then loads `storage.load(next)`, prepending any entries created while the scope was `null`; `setScope(null)` saves and clears. `replaceEntries` applies another tab's list without saving it back: incoming entries win by `id`, local entries keep their non-persisted `action`. `save` is called after every mutation while a scope is set (debouncing lives in the storage adapter, Task 2).

- [ ] **Step 1: Write the failing tests** in `notification-controller.test.ts`:
  - `notify adds an unread toasting entry newest first` — two notifies; `getEntries().map(e => e.title)` equals `["second", "first"]`; both `read === false`, `toasting === true`, `source === "studio"`.
  - `toast false records in the center only` — `toasting === false`.
  - `lifetime policy per level` — `notificationToastDurationMs` returns 8000, 8000, 12000, null, null for info, success, warning, error, progress.
  - `update from progress to success re-raises` — notify progress, `hideToast`, `markRead`, then `handle.update({ level: "success", title: "Done" })` → `read === false`, `toasting === true`, same `id`, `getEntries().length === 1`.
  - `update and dismiss after dismissal are no-ops` — dismiss, then `handle.update(...)`, `handle.dismiss()` → `getEntries()` stays `[]`.
  - `track resolves to success and returns the result` — `await api.track(Promise.resolve(42), { progress: "Working", success: n => \`Got ${n}\`, error: "Failed" })` returns 42; one entry, level `success`, title `Got 42`.
  - `track rejects with the original error and records error` — rejection with `new Error("boom")` and `error: e => (e as Error).message`; `await expect(...).rejects.toBe(err)`; entry level `error`, title `boom`.
  - `entries published before a scope are kept when the scope is set` — fake storage whose `load("s1")` returns one stored entry; notify with scope null, then `setScope("s1")` → two entries, new one first; `save` last called with `("s1", both)`.
  - `switching scope saves the old list and loads the new one` — `setScope("a")`, notify, `setScope("b")` → `save` called with `("a", [entry])`, `getEntries()` equals `load("b")`.
  - `getEntries is referentially stable without changes` — two calls return the same array.
  - `replaceEntries merges by id and keeps local actions` — local entry with an `action`, incoming list with the same id and a changed title plus one new entry → title updated, `action` still present, new entry included, `save` not called.
  - `subscribe notifies on change and unsubscribes`.
- [ ] **Step 2: Run** `pnpm --filter @elsa-workflows/studio-web exec vitest run src/__tests__/notification-controller.test.ts` from `Studio/` — expect FAIL (module not found).
- [ ] **Step 3: Implement** `H/sdk/notifications.ts` with the signatures above. Default `createId` follows `createAgentRegistry` (`globalThis.crypto?.randomUUID?.()` with a counter fallback).
- [ ] **Step 4: Run** the same command — expect PASS. Also run `pnpm --filter @elsa-workflows/studio-web exec vitest run src/__tests__/sdk-typing-copies.test.ts` — expect PASS (ambient copies are subsets and need no change).
- [ ] **Step 5: Commit** `feat(studio-sdk): add notification contract and controller`.

### Task 2: Browser storage adapter

**Files:**
- Create: `H/app/notificationStorage.ts`
- Test: `H/__tests__/notification-storage.test.ts`

**Interfaces:**
- Consumes: `StudioNotificationStorage`, `StudioNotificationEntry` (Task 1).
- Produces:
  - `const notificationStorageKeyPrefix = "elsa-studio:notifications:v1:"`
  - `function notificationScopeKey(session: { key: string; identity: AuthSession } | null | undefined): string | null` — `null` for no session; otherwise `` `${hash(`${provider?.id ?? ""}|${tenantId ?? ""}|${subject ?? "anonymous"}`)}:${hash(session.key)}` `` where `hash` is a 32-bit FNV-1a rendered as 8 lowercase hex chars.
  - `function createLocalNotificationStorage(options?: { storage?: () => Storage | null; now?: () => number; debounceMs?: number }): StudioNotificationStorage & { flush(): void }`

Behavior: `load` parses `prefix + scope`, keeps only objects with string `id`/`title`, a known `level` and numeric `createdAt`, drops entries with `createdAt < now - 604800000`, keeps the newest 50, maps `progress` to the restored warning from Global Constraints, forces `toasting: false`, and strips `action`. Any thrown error or non-array JSON yields `[]`. `save` strips `action`, caps to 50, and writes after `debounceMs` (default 250); `flush()` writes pending saves immediately and is called on `pagehide`. `subscribe` listens for `window` `storage` events whose `key` matches the scope and passes the parsed list.

- [ ] **Step 1: Write the failing tests:**
  - `scope key hashes identity and backend without raw values` — `notificationScopeKey({ key: "https://a.example", identity: { status: "authenticated", subject: "jane@example.com", roles: [], permissions: [] } })` matches `/^[0-9a-f]{8}:[0-9a-f]{8}$/` and does not contain `jane`; a different `subject` or `key` gives a different result; `null` session gives `null`.
  - `save then load round-trips without actions` (use `debounceMs: 0` + `flush()`).
  - `load drops entries older than seven days and keeps the newest fifty` — 60 entries, 5 older than 7 days.
  - `load restores progress as status unknown warning` — level `warning`, message exactly `Status unknown — this operation was interrupted by a page reload`, link kept, `toasting === false`.
  - `corrupt json loads as empty` — `localStorage.setItem(prefix + "s", "{not json")` → `[]`; `"{\"a\":1}"` → `[]`.
  - `throwing storage degrades silently` — `storage: () => { throw new Error("denied"); }`; `save` + `flush` and `load` do not throw; `load` returns `[]`.
  - `scopes are isolated` — save to `a`, load `b` → `[]`.
  - `storage events from another tab reach subscribers` — dispatch `new StorageEvent("storage", { key: prefix + "s", newValue: JSON.stringify([...]) })`; listener called with parsed entries; event for another key is ignored.
- [ ] **Step 2: Run** `pnpm --filter @elsa-workflows/studio-web exec vitest run src/__tests__/notification-storage.test.ts` — expect FAIL.
- [ ] **Step 3: Implement** `H/app/notificationStorage.ts`.
- [ ] **Step 4: Run** the same command — expect PASS.
- [ ] **Step 5: Commit** `feat(studio): persist notifications per identity and backend`.

### Task 3: Host singleton, registry and scope wiring

**Files:**
- Create: `H/app/notifications.ts` — `export const notificationStorage = createLocalNotificationStorage();` `export const notificationController = createNotificationController({ storage: notificationStorage });` Doc comment mirrors `app/dialogs.ts`. Register `window.addEventListener("pagehide", () => notificationStorage.flush())` once, guarded for non-browser environments.
- Modify: `H/app/registry.ts:90` — add `notifications: notificationController.api,` after `dialogs`.
- Modify: `H/app/App.tsx` — in the component that owns `backendSession` (around line 189, beside `assistantOpen`), add `useEffect(() => { notificationController.setScope(notificationScopeKey(backendSession)); }, [scopeKey])` where `const scopeKey = notificationScopeKey(backendSession);`. In the same effect, when `scopeKey` is non-null, subscribe with `notificationStorage.subscribe(scopeKey, entries => notificationController.replaceEntries(entries))` and return the unsubscribe.
- Test: `H/__tests__/registry.test.ts` (add case)

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces: `notificationController`, `notificationStorage` from `H/app/notifications.ts` (used by Tasks 5–6 and the browser fixture in Task 8).

- [ ] **Step 1: Write the failing test** in `registry.test.ts`: `registry exposes the shared notification api` — `createStudioRegistry(...)` (use the file's existing setup) `.notifications` is `notificationController.api`, and two registries share it.
- [ ] **Step 2: Run** `pnpm --filter @elsa-workflows/studio-web exec vitest run src/__tests__/registry.test.ts` — expect FAIL.
- [ ] **Step 3: Implement** the files above.
- [ ] **Step 4: Run** `registry.test.ts` and `app-routing.test.ts` — expect PASS.
- [ ] **Step 5: Commit** `feat(studio): expose api.notifications and scope it to the active session`.

### Task 4: Shared notice timer

**Files:**
- Create: `H/app/ui/feedback/useNoticeTimer.ts`
- Modify: `H/app/ui/feedback/ActionNotice.tsx` (replace its inline timer/pause refs with the hook; public props and DOM unchanged)
- Test: `H/app/ui/__tests__/notice-timer.test.tsx`; existing `H/app/ui/__tests__/kit.test.tsx` must stay green

**Interfaces:**
- Produces: `function useNoticeTimer(options: { durationMs: number | null; onElapse(): void; resetKey?: unknown }): { onMouseEnter(): void; onMouseLeave(): void; onFocusCapture(): void; onBlurCapture(event: React.FocusEvent<HTMLElement>): void }` — `durationMs: null` never elapses; changing `resetKey` or `durationMs` restarts with the full duration; hover or focus inside pauses and resumes with the remaining time (the logic currently in `ActionNotice`).

- [ ] **Step 1: Write the failing tests** (fake timers): `elapses after duration`, `null duration never elapses` (advance 60 000 ms), `hover pauses and resumes with remaining time` (advance 5000, hover, advance 10 000 → not elapsed, leave, advance 2999 → not elapsed, advance 1 → elapsed), `resetKey restarts the full duration`.
- [ ] **Step 2: Run** `pnpm --filter @elsa-workflows/studio-web exec vitest run src/app/ui/__tests__/notice-timer.test.tsx` — expect FAIL.
- [ ] **Step 3: Implement** the hook and refactor `ActionNotice` onto it.
- [ ] **Step 4: Run** `notice-timer.test.tsx` and `kit.test.tsx` — expect PASS.
- [ ] **Step 5: Commit** `refactor(studio): extract shared notice timer`.

### Task 5: Toasts and live region

**Files:**
- Create: `H/app/ui/feedback/ToastHost.tsx`
- Modify: `H/app/ui/components.css` (add `.studio-toast-host`, `.studio-toast`, level modifiers via `data-level`, beside `.studio-action-notice`; `position: fixed`, top offset below the header, right 16px, width `min(360px, calc(100vw - 32px))`; motion only under `prefers-reduced-motion: no-preference`; `--studio-*` tokens only)
- Modify: `H/app/App.tsx` — render `<ToastHost navigate={navigateTo} />` as the first child inside the main `<ShellFrame>` (line ~476, before the `"notice"` `ShellContributions`)
- Test: `H/app/ui/__tests__/toast-host.test.tsx`

**Interfaces:**
- Consumes: `notificationController` (Task 3), `notificationToastDurationMs` (Task 1), `useNoticeTimer` (Task 4).
- Produces: `function ToastHost(props: { navigate(path: string): void; controller?: StudioNotificationController }): JSX.Element` — `controller` defaults to the host singleton (tests and the browser fixture pass their own).

Rendering: entries with `toasting === true`, first 3, newest first, each an `<div className="studio-toast" data-level={level}>` with icon (`lucide-react`: `Info`, `CheckCircle2`, `AlertTriangle`, `XCircle`, `Loader2` spinning for progress), title, message, link button (calls `navigate(link.path)`, `markRead`, `hideToast`), action button (runs `action.run()`, then `hideToast`; a rejection becomes `api.notify({ level: "error", title: \`${action.label} failed\`, message })`), dismiss button `aria-label="Dismiss notification"` (calls `hideToast`, entry stays in the Center). Each toast uses `useNoticeTimer({ durationMs: notificationToastDurationMs(level), onElapse: () => hideToast(id), resetKey: \`${level}:${updatedAt}\` })`. The host renders two visually hidden regions (`role="status" aria-live="polite"` and `role="alert" aria-live="assertive"`) and writes `title` (plus message) of the newest toast that changed into the one matching its level.

- [ ] **Step 1: Write the failing tests** (own controller per test, fake timers):
  - `shows at most three toasts newest first` — 4 notifies → 3 `.studio-toast`, first has the 4th title; after hiding one, the oldest queued appears.
  - `success toast hides after 8 seconds but stays in the center` — advance 8000 → no toast; `controller.getEntries().length === 1`.
  - `error toast is sticky` — advance 60 000 → still visible.
  - `progress toast updates in place` — notify progress, `update({ level: "success" })` → one toast, success icon, timer now 8000.
  - `link navigates and marks read`.
  - `rejected action closes the toast and reports an error` — action rejects `new Error("nope")`; toast gone; a new entry with title `Restore failed` and message `nope`; no unhandled rejection.
  - `errors are announced assertively` — error title appears in `[aria-live="assertive"]`, success title in `[aria-live="polite"]`.
- [ ] **Step 2: Run** `pnpm --filter @elsa-workflows/studio-web exec vitest run src/app/ui/__tests__/toast-host.test.tsx` — expect FAIL.
- [ ] **Step 3: Implement** `ToastHost.tsx`, CSS and the `App.tsx` mount.
- [ ] **Step 4: Run** the test file, then `pnpm lint:css` from `Studio/` — expect PASS.
- [ ] **Step 5: Commit** `feat(studio): add notification toasts`.

### Task 6: Bell and Notification Center

**Files:**
- Create: `H/app/ui/feedback/NotificationCenter.tsx`
- Modify: `H/app/ui/components.css` (`.studio-notification-bell`, `.studio-notification-badge` with `data-tone="danger"`, `.studio-notification-center` sheet: fixed right, `width: min(400px, 100vw)`, full height below header, `.studio-notification-entry[data-unread]` accent bar)
- Modify: `H/app/App.tsx` — add `notificationAction?: React.ReactNode` to `ShellFrame` props (beside `assistantAction`, ~line 544), render `{notificationAction}` immediately before `{assistantAction}` (~line 856); pass `notificationAction={<NotificationCenter navigate={navigateTo} />}` in the main `<ShellFrame>` (~line 472). Loading/startup-failed/connection frames get no bell.
- Test: `H/app/ui/__tests__/notification-center.test.tsx`

**Interfaces:**
- Consumes: `notificationController` (Task 3).
- Produces: `function NotificationCenter(props: { navigate(path: string): void; controller?: StudioNotificationController; now?: () => number }): JSX.Element` — bell trigger plus sheet.

Rendering: `Dialog.Root modal={false}` with `Dialog.Trigger asChild` on the bell button (`Bell` icon, accessible name from Global Constraints, badge hidden at 0). `Dialog.Content` is the sheet with `Dialog.Title` `Notifications`, `Mark all read`, `Dismiss all`, a list of entries (icon, title, message, relative time via `Intl.RelativeTimeFormat` with `title` attribute holding the absolute `toLocaleString()`, link, action if present, dismiss `aria-label="Dismiss notification"`). Clicking an entry's body or link calls `markRead`; link also navigates and closes the sheet. Opening does not mark read. Empty state `No notifications`.

- [ ] **Step 1: Write the failing tests:**
  - `bell shows unread count and caps at 9+` — 3 unread → name `Notifications, 3 unread`, badge `3`; 12 unread → badge `9+`; 0 → no badge.
  - `badge uses danger tone when an unread error exists`.
  - `opening the center does not mark entries read`.
  - `clicking an entry marks it read`; `Mark all read` marks all; `Dismiss all` empties and shows `No notifications`.
  - `link navigates, marks read and closes the center`.
  - `escape closes and returns focus to the bell`.
  - `entries persisted without an action show no action button` — entry with `link` and no `action`.
- [ ] **Step 2: Run** `pnpm --filter @elsa-workflows/studio-web exec vitest run src/app/ui/__tests__/notification-center.test.tsx` — expect FAIL.
- [ ] **Step 3: Implement** the component, CSS and `ShellFrame` slot.
- [ ] **Step 4: Run** the test file plus `src/__tests__/topNavigationOverlay.test.tsx` and `src/__tests__/a11y.test.tsx` — expect PASS.
- [ ] **Step 5: Commit** `feat(studio): add notification bell and center`.

### Task 7: Workflow definitions reference migration

**Files:**
- Create: `W/workflow-editor/notifications.ts` — `setNotifications(api: StudioNotificationApi | undefined): void` and `getNotifications(): StudioNotificationApi`, same shape and comment style as `workflow-editor/dialogs.ts`; `getNotifications()` returns a no-op api (`notify` returns a handle whose methods do nothing; `track` returns the promise) when the host provided none.
- Modify: `W/module.tsx:55` — `setNotifications(api.notifications);` after `setDialogs`.
- Modify: `W/workflow-editor/WorkflowDefinitions.tsx` — remove `status` state (line 118), every `setStatus(...)` call and the status line (line 642). Keep `staleStatus` (628) and `currentError` (641). Replace the seven writers:
  - `movedDefinitions` (434): `success`, title `Moved 1 workflow definition` / `Moved N workflow definitions`, link `{ label: "Open folder", path }` where `path` is `/workflows/definitions?folderId=<id>` or `/workflows/definitions?unfiled=true` for `null`. Change `MoveWorkflowDefinitionsDialog`'s `onMoved()` to `onMoved(folderId: string | null)` and pass `destinationId` (line 53).
  - `organizationSaved` (479): `success`, `Workflow organization updated`.
  - `changeFavorite` (486): `success`, `Added to favorites` / `Removed from favorites`; catch (487): `error`, same message expression as today.
  - `softDelete` (498): `success`, `Deleted <name>`, `action: { label: "Restore", run: () => restore(definition) }`, `link: { label: "View deleted", path: "/workflows/definitions?state=deleted" }`.
  - `restore` (513): `success`, `Restored <name>`, link `{ label: "Open", path }` with `path` from `updateWorkflowDefinitionIdInUrl(new URL("/workflows/definitions", window.location.origin), definition.id)` (pathname + search).
  - `permanentDelete` (529): `success`, `Permanently deleted <name>`.
  - All calls stay behind the existing `isCurrentGeneration()` checks.
- Modify: `W/__tests__/module.test.tsx` (4161–4162, 4258), `W/__tests__/workflowDefinitionRoute.test.tsx:150`, `W/__tests__/routeRenderingHelpers.tsx` (`testApi()` gains `notifications: { notify: vi.fn(...), track: vi.fn(...) }` returning a stub handle).
- Modify: `Studio/docs/module-ui.md` "States and behavior" — add the classification table from the spec and one line: "Publish action results through the injected `api.notifications`; never render a local status bar for them."
- Modify: `H/app/ui/README.md` near line 93 — document `api.notifications.notify` / `track` beside `api.dialogs.confirm`.

**Interfaces:**
- Consumes: `StudioNotificationApi` (Task 1).

- [ ] **Step 1: Update the tests first:** replace the `[role='status']` assertions with expectations on the stub — `expect(notify).toHaveBeenCalledWith(expect.objectContaining({ level: "success", title: "Moved 2 workflow definitions", link: { label: "Open folder", path: "/workflows/definitions?folderId=folder-root" } }))`; line 4258 analog for `Moved 1 workflow definition`; `workflowDefinitionRoute.test.tsx:150` becomes `expect(notify).not.toHaveBeenCalled()` (the stale-generation guard, Review Focus 5). Add `soft delete offers restore` (calling the captured `action.run()` issues the restore request) and `favorite failure publishes an error`.
- [ ] **Step 2: Run** `pnpm --filter @elsa-workflows/studio-workflows exec vitest run src/__tests__/module.test.tsx src/__tests__/workflowDefinitionRoute.test.tsx` — expect FAIL.
- [ ] **Step 3: Implement** the module changes and docs.
- [ ] **Step 4: Run** `pnpm --filter @elsa-workflows/studio-workflows test` — expect PASS; `grep -n "setStatus" W/workflow-editor/WorkflowDefinitions.tsx` — expect no output.
- [ ] **Step 5: Commit** `feat(studio-workflows): publish workflow definition results as notifications`.

### Task 8: Browser verification and qualification

**Files:**
- Modify: `Studio/tests/browser/src.tsx` — in the `move-definitions` mode, create a fixture controller with `createNotificationController({ storage: createLocalNotificationStorage() })`, `setScope("fixture")`, call `setNotifications(controller.api)`, and render `<ToastHost controller={controller} navigate={...} />` and `<NotificationCenter controller={controller} navigate={...} />` around the page.
- Modify: `Studio/tests/browser/workflow-definition-move.spec.ts` — line 24: assert the text is inside `.studio-toast` instead of `role="status"`; add test `move result survives reload in the notification center`: move, see toast, `page.reload()`, click `Notifications, 1 unread`, see `Moved 1 workflow definition`.
- Modify: `specs/1187-studio-notifications/spec.md` — `**Status**: Implemented — <PR link>` in the implementing PR.

- [ ] **Step 1: Write the browser changes** above.
- [ ] **Step 2: Run** `pnpm test:browser -- workflow-definition-move.spec.ts` from `Studio/` — expect PASS.
- [ ] **Step 3: Run** the full gate from `Studio/`: `pnpm typecheck`, `pnpm lint`, `pnpm test`, `pnpm build` — expect all PASS.
- [ ] **Step 4: Visual check (SC006)** in the real Studio: light and dark, 320 px width (no page overflow), keyboard-only through bell → center → entry → Escape, reduced motion. Save screenshots for the PR.
- [ ] **Step 5: Commit** `test(studio): cover notification persistence in the browser`; set spec status in the PR's final commit.
