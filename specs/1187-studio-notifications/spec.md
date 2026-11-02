# Studio notifications: toasts and Notification Center

**Status**: Draft — owner-approved design, awaiting spec review
**Issue**: [#3631](https://github.com/elsa-workflows/elsa-foundation/issues/3631)
**Program bucket**: none/free-flow

## Problem

Studio surfaces have no shared feedback channel. About 97 client files keep a local `status` string
or render their own `role="status"` / `wf-alert` bar, so each page decides placement, lifetime,
dismissal and announcement differently, and action results push page content down (for example
"Moved 6 workflow definitions" above the workflow definitions grid). `StudioActionNotice` already
implements timed, pausable, announced feedback but is used by one other file.
[`Studio/docs/ux-polish-plan.md`](../../Studio/docs/ux-polish-plan.md) records the missing toast
system as a gap.

## Outcome

A host-owned notification service, modeled on the Azure portal: transient toasts for the result of
an action, and a Notification Center behind a header bell that keeps recent notifications, tracks
long-running operations and survives reload. Modules publish through one SDK call. Page-level state
that the user must still see or fix stays inline.

## User scenarios

1. Move, delete, restore or favorite a workflow definition and see a toast that does not shift the
   grid; follow its link or use its action (Restore after delete).
2. Start a long-running operation, leave the page, and get an attention-grabbing toast when it
   completes or fails; the Center entry updates in place instead of adding a new one.
3. Miss a toast, then open the bell to read it later, including after a page reload.
4. Switch backend or sign in as another user and see only that identity's and backend's
   notifications.
5. Operate toasts and the Center by keyboard and screen reader.

## Design

### SDK contract

Exposed to modules as `api.notifications` on `ElsaStudioModuleApi`, next to `api.dialogs`.

```ts
export type StudioNotificationLevel = "info" | "success" | "warning" | "error" | "progress";

export interface StudioNotificationInput {
  level: StudioNotificationLevel;
  title: string;
  message?: string;
  link?: { label: string; path: string };
  action?: { label: string; run(): void | Promise<void> };
  toast?: boolean;   // default true; false records in the Center only
  source?: string;   // reserved for future server-pushed notifications; defaults to "studio"
}

export interface StudioNotificationHandle {
  readonly id: string;
  update(patch: Partial<StudioNotificationInput>): void;
  dismiss(): void;
}

export interface StudioNotificationApi {
  notify(input: StudioNotificationInput): StudioNotificationHandle;
  track<T>(work: Promise<T>, messages: {
    progress: string;
    success: string | ((result: T) => string);
    error: string | ((error: unknown) => string);
  }): Promise<T>;
}
```

- Lifetime is host policy, not a module option: `info` and `success` toasts dismiss after 8 s,
  `warning` after 12 s; `error` and `progress` toasts stay until dismissed or updated.
- `track()` publishes a `progress` notification, resolves it to `success` or `error`, and returns
  the original promise result or rejection unchanged, also when a message formatter throws.
- `update(patch)` leaves absent keys untouched; a key present with value `undefined` clears that
  field.
- An action runs at most once: the host clears it from the entry before running it.
- `link.path` is a Studio route opened through host navigation. External URLs are not supported.
- `action` callbacks are not persisted. After reload the entry keeps its link but loses its action.
- Modules can only publish. Reading, marking read and clearing belong to the host UI.

### Store and persistence

- `createNotificationController()` in the SDK; one host singleton in
  `Elsa.Studio.Hosting/Client/src/app/notifications.ts`, wired into the registry like
  `dialogController`. Components read it through `useSyncExternalStore`.
- Entry fields: `id`, `level`, `title`, `message`, `link`, `source`, `createdAt`, `updatedAt`,
  `read`, plus the non-persisted `action`.
- `update()` mutates in place. A transition to `success`, `error` or `warning` marks the entry
  unread and re-raises its toast.
- Persistence key: `elsa-studio:notifications:v1:<userKey>:<backendKey>`, using a stable identity
  key and the active backend key. No tokens or emails in keys or values.
- Writes are debounced (about 250 ms). On load, keep the newest 50 entries and drop entries older
  than 7 days.
- A persisted `progress` entry is restored as `warning`: "Status unknown — this operation was
  interrupted by a page reload", keeping its link.
- All storage access is wrapped in try/catch; when storage is unavailable the store runs in memory.
- When the identity or backend key changes, the store flushes the current list and loads the list
  for the new key. Sign-out flushes and clears the in-memory list.
- A `storage` event listener merges changes from other Studio tabs (three-way against the last
  known list while a local write is pending); only a fresh load maps `progress` to "status unknown".

### Host UI

All components live in `Elsa.Studio.Hosting/Client/src/app/ui/feedback/`.

- `<ToastHost />` is mounted once inside the main shell frame in `App.tsx`, where host navigation is
  available for links. Toasts appear top-right
  below the header as an overlay that never reflows content, about 360 px wide and full width on
  narrow viewports. At most three are visible, newest on top; the rest queue (they are already in
  the Center). Each toast shows a level icon (spinner for `progress`), title, optional message,
  link, action and dismiss button.
- Timer, hover and focus pause logic is extracted from `StudioActionNotice` into a shared hook used
  by both `StudioActionNotice` and the toast.
- One host live region announces toasts: `assertive` for `error`, `polite` otherwise.
- A bell button in the header, left of the Weaver button, shows the unread count ("9+" above nine).
  The badge uses the danger role when any unread entry is an error. Accessible name:
  "Notifications, N unread".
- The Notification Center is a right-side, non-modal sheet built on `@radix-ui/react-dialog`, about
  400 px wide. Header: "Notifications", "Mark all read", "Dismiss all". Entries are newest first
  with level icon, title, message, relative time (absolute on hover), link, action when still
  available and dismiss. Unread entries carry an accent bar. Opening the Center does not mark
  entries read; clicking an entry or "Mark all read" does. Escape closes it and focus returns to the
  bell. It has an empty state and a Close button. While it is open, toasts are suppressed (the list
  already shows every entry) and new entries are still announced.
- Toasts and the sheet sit below the shell header, measured into `--studio-shell-header-bottom`.
- Styling uses the `--studio-*` token contract in `Studio/docs/design-tokens.md`; no new tokens
  unless review finds a gap.

### Feedback classification

This rule decides, per existing feedback surface, whether it becomes a notification. It is recorded
in `Studio/docs/module-ui.md` under "States and behavior".

| Existing feedback | Becomes |
|---|---|
| Result of a user action (moved, deleted, restored, saved, favorited) | `notify({ level: "success" })` |
| Failed action the user cannot correct on the page | `notify({ level: "error" })` |
| Long-running operation (export, publish, import, test run) | `track()` |
| Reversible action with existing reversal logic | toast with an `action` |
| Load failure, stale data, validation and field errors | stays inline |
| Loading placeholders (`Suspense` fallbacks) | stays inline |

### Reference migration: workflow definitions

In `Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions.tsx`:

- Remove the `status` state and its status line. The seven writers become notifications: "Moved N
  workflow definitions" links to the target folder; "Deleted X" carries a Restore action;
  "Restored X" links to the definition; "Permanently deleted X", "Added to/Removed from favorites"
  and "Workflow organization updated" are `success`; the favorite failure is `error`.
- Keep `staleStatus` and the `currentError` alert inline.
- Keep the `isCurrentGeneration()` guards so a stale async result publishes nothing.

## Requirements

- FR001: Add `StudioNotificationApi` and its types to the Studio SDK and expose it as
  `api.notifications` on every module API.
- FR002: Provide `createNotificationController()` with notify, update, dismiss, mark read, mark all
  read, dismiss all and subscribe, and the lifetime policy above.
- FR003: Persist per identity and backend with the cap, age limit, interrupted-progress restoration,
  storage-failure fallback, key switching and cross-tab merge described above.
- FR004: Render toasts, the header bell and the Notification Center in the host as described, with
  one shared timer hook and one host live region.
- FR005: Migrate the workflow definitions page as specified and record the classification rule in
  `Studio/docs/module-ui.md`.
- FR006: Keep `StudioActionNotice` working on the shared timer hook; leave its existing consumer
  unchanged.
- FR007: Document `api.notifications` in `Elsa.Studio.Hosting/Client/src/app/ui/README.md` beside
  `api.dialogs`.

## Verification

- SC001: Controller unit tests cover notify/update/dismiss, lifetime per level, `track()` success and
  failure with unchanged result or rejection, and re-raise on terminal update.
- SC002: Persistence tests cover key isolation per identity and backend, the 50-entry and 7-day
  limits, interrupted-progress restoration, throwing storage and cross-tab merge.
- SC003: Host component tests cover the three-toast limit and queue, hover/focus pause, unread count
  and error badge, mark read, dismiss all and focus return to the bell.
- SC004: Workflow definitions tests assert notifications through a stub `api.notifications` instead
  of an inline `[role='status']`; the existing browser move test still finds
  "Moved 1 workflow definition" (now in a toast).
- SC005: A browser test moves a workflow, sees the toast, reloads, opens the bell and finds the entry.
- SC006: Toasts and the Center are readable and operable in light and dark themes, at 320 px width
  without page overflow, by keyboard only, and with reduced motion. Screenshots are attached to the
  PR.

## Scope boundaries

In scope: the SDK contract, host store and persistence, toasts, bell, Notification Center and the
workflow definitions reference migration.

Out of scope: server-pushed notifications (the `source` field keeps the contract open), sounds,
browser/OS notifications, notification preferences, and migrating the remaining feedback surfaces.
Those surfaces move in later per-module changes using the classification rule.

## Specification review

Reviewed against the owner-approved design from the 2026-11-01 brainstorming session and the current
host structure (`api.dialogs`, `StudioActionNotice`, the header, the `--studio-*` token contract):
implementable without new runtime or backend contracts; no unresolved scope decision.
