# Implementation Plan: Direct Home authoring paths

**Branch**: `agent/codex/3063-home-golden-path` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)

**Input**: #3063, sole isolated implementation of #3057 / #2559; implementation base is qualified `45f9a3b9b226ad7a94e9c176503729ebadfea9f5`. Prerequisites #3061 and #3062 are accepted on this base; Core run `39762400251` and Studio run `39762399813` succeeded. The research pin `5d1df7097db3a731330735a94c2b6a5fb7f9a476` remains the reviewed historical snapshot because the Home target paths are unchanged since it. The [scheduling refinement](https://github.com/elsa-workflows/elsa-foundation/issues/3057#issuecomment-6328158365) permitted implementation during Runs main Core verification but did not waive qualification; stop/resume reconciliation still applies if a required base gate fails.

## Summary

Add a compact durable workflow task group before the configurable Home grid. Reuse the existing create dialog through consumed `homeAction=create`, discover current drafts with the existing complete authorized definitions list, and preserve exact selected definition identity through stale/error recovery. Needs attention links to `/workflows/instances?incidentHealth=active`. Preserve widgets, existing navigation guards, backend authorization and module boundaries.

## Technical Context

**Language/Version**: Existing TypeScript/React Studio pnpm workspace.
**Primary Dependencies**: Existing public studio-sdk/studio-ui, Dashboard and Workflows packages; no new dependency.
**Storage**: Existing Home preferences unchanged; no new persisted state. Creation intent is consumed from the current URL; draft-discovery state is ephemeral and authority scoped.
**Testing**: Vitest/Testing Library, real module/router tests, focused Playwright fixtures, rebuilt disposable normal Workbench/Studio proof and existing backend workflow-flow e2e where applicable.
**Target Platform**: Studio browser client on the current normal composition.
**Project Type**: Existing modular web application, not a greenfield app.
**Performance Goals**: No performance benchmark or invented response-time target.
**Constraints**: Preserve current draft semantics, exact identities, authorization, catalog/readiness, saved layouts and dirty-navigation guards. No backend API, permission-policy or module-composition change.
**Scale/Scope**: Home task surface plus the existing Workflows route/create/editor recovery boundary, focused tests and existing UX guidance.

## Constitution Check

Root loaded framework6.1.0 and Elsa6.1.0 in full, both Gate indexes and docs/contributing/review-checklist.md before design. Rechecked after the design below; no exception.

- Framework §2.6/§2.23.4: Dashboard consumes public SDK/UI and existing backend/URL contracts, never Workflows implementation types or host-only DialogHost. No cross-module side-effect dependency.
- §2.17: keep small domain-local UI permission/candidate helpers; no global authorization or generic navigation framework. Extract actual repeated setup within each test consumer.
- §2.21.1/§2.23.2: preserve valid tests and prove changed routes, late responses, identity, permission affordances and recovery through observable behavior. Include revert/mutation red→green evidence. No markdown/source-text tests. This is brownfield work; no greenfield cadence decision is required.
- §2.23.3/§4.2 and Elsa §E5: no C# public API, dependency or hand-authored package version change is intended. Build affected clients and consumer host assets; preserve public SDK/UI boundaries.
- Elsa §E2.2.3/§E2.6: no Design/Runtime deployment or pinned-artifact behavior changes. Creation reuses existing API/catalog readiness and checks. Draft existence is a read projection, not permission to mutate or invent a lifecycle command.
- No new C# feature/project/event/catalog entry. Run the required architecture restore/test and generated solution-filter check; do not restore deleted documentation snapshots. Draft/provisional constitution sections are not used as a new policy gate.

## Design and Implementation

1. **Home group**: a focused Dashboard-owned component before `.dashboard-grid`, using current tokens and one primary Create treatment, Continue and Needs attention plus Browse workflows. Preserve all widget content/layout. Gate on the existing Workflows feature match. Use current navigation guard/helper and native modified-link behavior for links.
2. **Discovery**: retain nonempty `draftId` in the Home summary. The current server ignores `take=8`; use its complete authorized list, filter actual current drafts before truncation, sort by existing updated/created convention, and determine uniqueness before an eight-entry recent chooser. Keep idle/loading/ready-empty/ready-many/error distinct; cancel/invalidate on authority change and dismiss. Do not use the boolean onboarding helper that swallows errors.
3. **Affordances**: authenticated known-denial checks must reflect current server resource/verb wildcard, subtree, case normalization and manage→read semantics, plus existing auth-off anonymous posture. Keep this narrow to Home's existing workflow-design requirements. Do not alter global usePermissions or claim the client is the security authority. Preserve backend 401/403 and catalog/capability errors as recoverable failures.
4. **One-shot create**: Workflows owns recognizing/consuming `homeAction=create`. Replace only that recognized parameter before invoking the existing openCreateDialog; preserve unrelated query/hash/history state. Hand the lazy list an acknowledged one-use request so rerenders, remounts, Cancel and history do not replay it. If an exact definition is already selected, consume the recognized intent but preserve that editor instead of discarding its target. Unknown intents do not trigger creation. Reuse current catalog loading/retry/readiness and createDefinition success navigation.
5. **Exact editor recovery**: distinguish loading, error and successfully loaded no-current-draft, with Retry/Browse recovery and the chosen definition ID. Verify rapid identity changes/out-of-order loads cannot present another definition's draft; prefer the narrow route/component identity boundary. Preserve old-target pending saves and do not silently clone/create a draft.
6. **Chooser/focus**: public UI has no module-safe DialogHost. Reuse Dashboard's existing settings-dialog composition/useHomeDialogFocus, stable close callback and semantic links/buttons; no new focus trap. Readable name + stable ID, explicit recent window and Browse all.
7. **Guidance**: update the existing Home section of Studio/docs/ux/design-spec.md and its paired backlog if an existing target is fulfilled. Mark actual delivered behavior, not unrun final3075 coverage.

## Project Structure

Feature documents live in `specs/1000-studio-home-golden-path/`: spec, plan, research, data-model, contracts/home-navigation.md, quickstart and tasks. The requirements checklist is temporary process residue removed at terminal spec status.

Expected code boundaries:

- `Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/`: HomePage.tsx, a focused Home task component, styles.css, widgets/homeData.ts and corresponding existing/new tests.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/`: workflow-editor/pages.tsx, WorkflowDefinitions.tsx, WorkflowEditor.tsx/useWorkflowEditorData.ts only as necessary for one-shot creation and exact recovery; existing/new route tests.
- `Studio/tests/browser/home.spec.ts`, homeFixture.tsx and a focused Home/Workflows navigation fixture if needed.
- `Studio/docs/ux/design-spec.md` / `Studio/docs/ux-polish-plan.md` only relevant Home entries.

No new project, dependency or generated map snapshot. Preserve unrelated AGENTS993 pointer and ignored local `.specify/feature.json`; current plan skill explicitly disables agent-context updates.

## Validation and Integration

While implementation overlaps Runs verification, any failed required base gate stops implementation immediately until root reconciles it. Keep the source/evidence checkpoint, investigate or recover the canonical gate, and resume only after that condition is cleared; do not merely hold merge while continuing code work.

Cost order: inspect real route/API/auth contracts → focused regressions and root diff review → whole affected Dashboard/Workflows suites and typechecks/lint/builds → coherent exact-head independent reviews → final affected host builds, normal-host proof and backend workflow-flow → canonical PR and resulting-main gates. Serialize builds through current slots. Use the audited isolated host lifecycle patterns and bind served assets to current output; inspect screenshots before presenting them. Fixtures do not substitute for normal-host evidence.

Record exact suite inventories/executed counts, skips and red/green behavior. Browser cases cover one-shot intent and conflict, duplicate and beyond-eight candidates, unavailable/stale/late identity, known denied and wildcard grants, default/reordered/all-hidden layouts, both laptop themes, keyboard and narrow fallback. Normal proof demonstrates Home Create/cancel/one-use and success, exact draft continuation/chooser and filtered Needs attention without changing layout, using disposable identity/data/config and owned process cleanup.

Before merge: three independent clean axes on the exact head, required/relevant CI green, no human hold/unresolved adopted findings, standing audit, and Runs resulting-main qualification. After merge: verify resulting-main CI and reconcile issue/board before acceptance. Do not claim final3075's nine composed rows ran from this leaf.
