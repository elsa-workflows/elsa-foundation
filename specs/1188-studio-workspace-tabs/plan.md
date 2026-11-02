# Studio Workspace Tabs Implementation Plan

**Goal:** A host-owned workspace tab strip for every Studio screen, with context preservation,
guard-aware closing and per-user, per-backend restoration.

**Spec:** [`spec.md`](spec.md)

Paths are relative to `Studio/src/essentials/` unless they start with `Studio/`, `Extensions/` or
`specs/`. `H` = `Elsa.Studio.Hosting/Client/src`, `W` = `Elsa.Studio.Workflows/Client/src`.

## Constitution Check

Framework 6.1.0 and Elsa 6.0.3 consulted. Client-only change: no Core, domain, persistence,
transport, backend or .NET signature change. Additive TypeScript SDK contracts only:
`StudioRouteContribution.workspace?`, `StudioNavigationRequest.intent?` (absent means `navigate`, so
older guards and hosts keep their meaning), `openStudioWorkspaceTab`, `useStudioWorkspaceTab`,
`useStudioWorkspaceTitle`, `useStudioRouteLocation`, `isStudioWorkspaceTabClick`. No new dependency:
`<Activity>` ships with the React 19.2 already in the lockfile, and the vendor React shim now
re-exports it. Styling uses `--studio-*` tokens (`pnpm lint:css`). Tests are risk-based: the pure
model and storage, the controller with real `<Activity>` panels, Alt-click fall-through and the
probe contract of guards that show UI. No exception required.

## Tasks

- [x] SDK contract — `H/sdk/workspaceTabs.ts`, `H/sdk/index.ts` (route `workspace`, navigation
      `intent`), vendor `Activity` export in `H/vendor/react.ts`.
- [x] Pure model and persistence — `H/app/workspace/model.ts`, `storage.ts`; tests
      `H/__tests__/workspace-model.test.ts`.
- [x] Controller — `H/app/workspace/useWorkspaceTabs.ts` (address/history ownership, capture-phase
      `popstate`, probe/close guards, dedupe, `beforeunload`, persistence, scroll restore) and
      `history.ts` (entry tagging and write observer).
- [x] Panels and strip — `WorkspaceTabPanels.tsx` (`<Activity>`, error boundary, tab handle),
      `WorkspaceTabStrip.tsx`, `workspace.css`, `useWorkspaceShortcuts.ts`.
- [x] Shell integration — `H/app/App.tsx`: per-location `renderScreen`, `ShellFrame` tab-strip slot
      and content ref, Alt-click on navigation and Diagnostics links; `.content` child rules in
      `styles.css`, `themes/workspaceStyles.css` and the Extension Builder see through the active panel.
- [x] Controller tests — `H/__tests__/workspace-tabs.test.tsx`, mutation-checked.
- [x] Module adoption — Workflows resource keys (`W/module.tsx`), titles (`WorkflowEditor`,
      `ActivityDefinitionDraftEditor`, run inspector), `useStudioRouteLocation` in the run inspector,
      `StudioLink` Alt-click; Backends page location and probe-silent guard; Feature Management
      probe-silent guard.
- [x] Docs — SDK README *Workspace tabs* section; Studio extension-point catalog entry.
- [x] Composed qualification in a real browser against a locally started Workbench and Studio,
      recorded on the PR.
