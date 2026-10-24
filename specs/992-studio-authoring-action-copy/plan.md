# Implementation Plan: Studio Authoring Action Copy and Toolbar Consistency

**Branch**: agent/codex/3072-authoring-action-copy | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Approved specification for #3072, “make Studio authoring actions truthful and consistent across workflow and activity authoring while preserving existing action eligibility and lifecycle behavior.”

## Summary

Update the copy and presentation of existing authoring actions in the Elsa Studio Workflows client, with the bounded unknown-head shortcut correction recorded in the R1 refinement below. Use the approved labels for transient workflow Test Run setup, activity draft choices, and review-before-publication entries; retain module-supplied labels, exact activity IDs, operation callbacks, lifecycle behavior, status meaning, and direct Test Run result access. Measure the existing toolbar behavior with real conditional status components before selecting any layout repair. No backend, SDK, persistence, or external interface changes are planned.

## Technical Context

**Language/Version**: TypeScript/TSX and CSS; Studio workspace uses Node tooling with pnpm 11.9.0 and TypeScript 5.6.

**Primary Dependencies**: React 19, @tanstack/react-query, lucide-react, Vitest 2.1, Playwright 1.61, and the existing Studio SDK and UI workspace packages.

**Storage**: No new storage. Continue using existing workflow draft/run and Activity Definition authoring views and existing client/server operations.

**Testing**: Vitest unit/component tests in the Workflows client; Playwright Chromium tests using the Studio browser fixture and routed Activity Definition API responses. Typecheck and the Workflows client bundle build verify the affected module.

**Target Platform**: Elsa Studio web client, Chromium browser coverage at fixed widths 320, 768, 1024, and 1440 CSS pixels, with light and dark themes. Primary actions must remain usable throughout 320–1440 pixels.

**Project Type**: Existing modular web client package; this change remains inside the Elsa.Studio.Workflows client.

**Performance Goals**: No performance change is intended.

**Constraints**: Preserve confirmed-state action eligibility, exact draft/version identities, callback and request semantics, publication modes and steps, status meaning and complete accessible status text, field labels and associations, the connected-environment/side-effects warning, and direct access to Test Run details. For the same definition at the same viewport, primary action positions must vary by at most 1 CSS pixel while statuses change. Do not choose a layout mechanism until the real-component baseline is measured.

**Scale/Scope**: One Studio client package, its component tests, and the targeted browser fixtures. No new package, shared product abstraction, public API, SDK contract, backend contract, persisted entity, or dependency. Reuse local typed fixtures and extract setup only when it reduces repetition and improves clarity.

## Constitution Check

*GATE: Re-check after Phase 1 design.*

- **Framework §2.21.1**: Pass. Preserve existing behavior and exact command semantics; retain current behavior tests and add regressions only for the approved user-visible behavior.
- **Framework §2.23.2**: Pass. Test risky draft eligibility and callback behavior at the existing component seams. Assert accessible user-visible UI behavior, not documentation or source text.
- **Framework §2.17**: Pass. Keep the copy and layout work local to existing Workflows consumers. Do not introduce a shared helper or dependency for this bounded change.
- **Framework §2.23.1, §2.23.3, §2.23.4–§2.23.5**: Not triggered. No feature registration, public surface, cross-feature implementation dependency, or infrastructure boundary changes are expected.
- **Elsa §E2.2**: Pass. No Runtime-to-Design dependency or deployment-shape change; the work stays in the Studio client.
- **Elsa §E2.8 and §E2.9**: No scope change. Activity versioning (§E2.8) and workflow definition state (§E2.9) are draft/provisional and are not redefined here. The UI consumes existing projections and preserves identity, eligibility, lifecycle commands, and the separation of mutable authoring records, read views, and immutable versions.
- **Review checklist**: No package, API, dependency, project-layout, or extension-point changes. Keep tests at the existing component and browser seams.

No constitution violation or unresolved product decision is known. After the design artifacts, re-check both constitutions and the review checklist; the same no-contract/no-lifecycle-change result is expected.

## Design and Execution Approach

1. **Baseline gate before production edits.** Root owns the implementation baseline approval and final QA. The pre-change Workflows client unit suite was run by root at source revision 3255701dcd9ed231d718aea0945dc3a6332c3866: 2124 tests passed across 167 files, with no skips, in 56.74 seconds; evidence is recorded in /Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3072-unit-baseline.log. Do not treat that as geometry, browser, or new copy-assertion evidence. Capture the live-component browser geometry baseline for each definition and fixed viewport before changing production copy or styles; root must approve this baseline first.
2. **RED regressions first.** Add user-visible assertions to the existing Workflows tests for the generic “Start Test Run” fallback, supplied submit label, trigger/input associations, Test Run loading copy, warning visibility, review-entry wording, and draft-action choices. Use exact request payloads/IDs and existing authorization states for activity scenarios. Run affected tests and record the expected failures before production edits.
3. **Minimal presentation changes.** Update only the specified existing entry labels and setup loading copy, and align the existing toolbar outcome prefix with the approved Test Run capitalization. Preserve the complete result details, stale cue, and module-provided submit and field labels. Preserve distinct workflow Publish mode and remembered choice, Activity “Continue publication review” after preflight, and the immediate flush promise of “Save now.” Keep draft eligibility selection and callbacks unchanged except for the bounded unknown-head correction recorded below.
4. **Measure, then repair only if needed.** The current workflow toolbar fixture has a real WorkflowEditorToolbar but no conditional runStatus. Extend it to render the actual TestRunStatus component with typed run-view states and its direct-details callback. Exercise the same workflow definition at identical viewport dimensions with save, publication, and conditional Test Run status/results. For the Activity Definition editor, exercise the real authoring toolbar with actual publication status states. Compare each primary action’s on-screen x/y position within 1 CSS pixel. Choose the smallest bounded presentation adjustment only after these measurements; preserve the full accessible Test Run status and direct result-details affordance. Do not prescribe a CSS architecture before measuring.
5. **Verify the complete affected surface.** Run targeted unit tests, the complete Workflows client unit suite, typecheck, client bundle build, and targeted Playwright cases. Exercise 320, 768, 1024, and 1440 pixel widths in light and dark themes, keyboard focus/navigation, no hidden or overflowing primary actions, exact callback/request destinations, and status geometry. Browser readiness waits must observe expected semantic status/result state rather than a fixed sleep.
6. **Root integration and QA gate.** Root retains final integration and QA. Run workspace typecheck/lint, the affected module build and complete unit project, targeted browser checks, locked architecture restore and the complete architecture project, and the dependency-map check. The required repository gates are not waived for presentation changes. Before final review, set the spec to Implemented in the implementation diff and remove the completed specification-quality checklist per the lifecycle rule. Obtain three independent non-author review axes and qualifying exact-head hosted checks before a normal merge; verify resulting-main gates afterward. Publish no ignored documentation maps or manifests from this worktree. Root owns all GitHub actions.

## Project Structure

### Documentation (this feature)

    specs/992-studio-authoring-action-copy/
    ├── plan.md
    ├── research.md
    ├── data-model.md
    ├── quickstart.md
    └── tasks.md (created only by the separate Speckit tasks step)

### Source and tests

    Studio/src/essentials/Elsa.Studio.Workflows/Client/src/
    ├── workflow-editor/
    │   ├── WorkflowEditorToolbar.tsx
    │   ├── WorkflowEditorToolbar.css
    │   ├── PublishActionButton.tsx
    │   ├── WorkflowTestDrivePanel.tsx
    │   ├── lazyTestDriveUi.tsx
    │   └── editorPanels.tsx
    ├── ActivityDefinitionWorkbench.tsx
    ├── ActivityDefinitionDraftEditor.tsx
    ├── ActivityDefinitionPublicationReview.tsx
    ├── activityDefinitionAuthoring.css
    └── __tests__/
        ├── workflowEditorToolbar.test.tsx
        ├── publishActionButton.test.tsx
        ├── workflowTestDrive.test.tsx
        ├── lazyTestDriveAnnouncement.test.tsx
        ├── activityDefinitions.test.tsx
        ├── activityDefinitionAuthoring.test.tsx
        └── activityDefinitionPublicationReview.test.tsx

    Studio/tests/browser/
    ├── src.tsx
    ├── workflow-toolbar.spec.ts
    └── activity-definitions.spec.ts

**Structure Decision**: Keep production changes in the existing Workflows client package. Reuse its current component and routed browser fixtures; update only the tests that prove this feature’s UI and existing operation semantics. No contracts directory is needed because no external interface changes.

## Complexity Tracking

No constitution violations or added architectural complexity.


## R1 review refinement — 2026-10-20

Root adopts the independent R1 findings on head 13d1866750a20d35b4948f70da85682d08ee6b9e. The original copy-only constraint has one conservative exception: require explicit `headVersionId === null` before offering the blank-create shortcut. The optional client field also permits omission; omission is unknown evidence and must retain the existing drafts chooser under FR-008/009. Explicit-null creation, sole editable-draft precedence, exact clone source, permissions and all commands remain unchanged. This changes the shortcut for an unconfirmed field rather than redefining the backend contract. Add a click-through omitted-field regression and record its RED result before the production guard changes.

Extend Test Run loading copy to exactly two existing accessible fallbacks: Runtime controls and module action-result loading. Keep their chunks, module-provided component text, callbacks and runtime behavior unchanged. Add deferred-loading regression coverage and establish RED before the two string changes. Correct the stale quickstart qualification sentence. Root reviews the patch, repeats affected qualification, and requires a fresh three-axis R2 on the pushed head; earlier review or CI does not qualify a new head.
