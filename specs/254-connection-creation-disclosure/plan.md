# Implementation Plan: Connection Creation Disclosure

**Branch**: `agent/codex/3065-connection-creation` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from specs/254-connection-creation-disclosure/spec.md; selected issue #3065; source-grounded implementation handoff at /Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3065-implementation-handoff.md.

**Delivery State**: Root approved 2026-10-20 after source, requirements, and constitution review. Implementation in progress; no execution gate has passed yet.

## Summary

Make the built-in HTTP API-key connection path present the required credential before optional settings, explain and enforce the existing Base URL-or-Allowed-hosts rule when its descriptor supports both fields, and progressively disclose only relevant optional controls while retaining authored values and safe defaults. Keep unknown types and contributed schemes descriptor-driven. Preserve the existing metadata-only Secret boundary. Retain an automatic reveal through error correction until the operator manually collapses it, without persisting that automatic reveal.

The client will use the existing exported AdvancedSection from @elsa-workflows/studio-ui. Add that internal workspace dependency to the Connections client and update Studio/pnpm-lock.yaml. No backend, endpoint, DTO, descriptor, public SDK, Secret policy, or persistence change is planned.

## Technical Context

**Language/Version**: TypeScript, React 19, pnpm workspace.

**Primary Dependencies**: Existing Connections client, @elsa-workflows/studio-ui AdvancedSection, descriptor and credential components; no new third-party package.

**Storage**: No new persisted data. Authored form settings remain in the existing create request. Automatic error reveal is transient and must not update persisted disclosure preference.

**Testing**: Vitest package suites, Playwright Connections browser test, TypeScript checks, Studio ESLint/stylelint/module-layout checks, and the existing architecture and generated-maps gates.

**Target Platform**: Elsa Studio web browser.

**Project Type**: Existing Studio web client packages across Extensions/Connections and Studio.

**Performance Goals**: No measurable performance change; preserve current dialog behavior and mounted-child identity.

**Constraints**: Exact type/scheme specialization only; preserve descriptor requiredness and generic fallback; do not expose stored Secret values; no backend/API/policy changes; no whole-solution local build. The test-first red→green sequence is explicitly requested for this work unit. Framework §2.21.2 leaves the durable Studio greenfield test cadence to the application owner, so this plan does not infer a general TDD policy from that request.

**Scale/Scope**: One create-dialog journey; likely four production files plus tests, an internal package dependency, and Studio lockfile update.

## Constitution Check

### Before Phase 0

- Both constitutions and docs/contributing/review-checklist.md were reviewed. No new backend feature, domain contract, extension point, persistence model, or public package surface is proposed.
- Framework §2.17 favors avoiding a new shared helper/dependency for a small local behavior. The plan reuses the existing shared disclosure component and adds only the needed internal workspace package dependency; it does not extract a new helper.
- Framework §2.21.1 requires preserving existing valid behaviors and tests. Existing credential-source, denial, endpoint-change, cancellation, failure-recovery, and duplicate-submit coverage remains in force.
- Framework §2.21.2 does not set an application-wide test cadence. Root has explicitly required red→green tests for this unit; record the durable Studio cadence gap for the application owner rather than generalizing this task’s order.
- Framework §2.23.2 warrants behavior tests for address/placement decisions, failure recovery, and the metadata-only Secret boundary. No feature registration or new contract test is applicable.
- Framework §2.24 is draft pending ratification; this work uses existing React disclosure and descriptor-driven form patterns and introduces no new architecture pattern.
- Elsa §E2.8 concerns the activity catalog picker, not the Connections picker. No Elsa-specific architecture gate is changed.

### After Phase 1 design

- **Root approved 2026-10-20**: use private transient AdvancedSection reveal state through recovery; automatic reveals must not persist as user preference. No new public prop or persisted error-reveal preference is proposed.
- No constitution violation or exception has been identified. Recheck the review checklist after implementation, particularly metadata compatibility, focus/child preservation, dependency cohesion, and the unchanged Secret boundary.

## Phase 0: Research

Completed as planning research in [research.md](research.md). Key decisions are:

- Specialize only exact built-in HTTP plus API-key metadata; keep other types and schemes generic.
- Apply the address one-of guidance/gate only when the recognized HTTP descriptor contains both address fields. Base URL remains direct; Allowed hosts is optional More options. If metadata cannot satisfy the rule, leave generic validation and server-error behavior intact.
- Header and Query are verified runtime contract values, not descriptor-advertised enum values. Missing or whitespace-only placement acts as Header; supported matching is case-insensitive without trimming; emit canonical lowercase values and reject unsupported nonblank values.
- Retain both authored header and query names through placement changes. Required metadata overrides optional relevance grouping. Placeholder/default values are not authored, while explicit user entries equal to runtime defaults remain configured.
- Keep automatic AdvancedSection error reveal and manual preference distinct. Root approved this private local refinement; a failing shared regression is required before implementation.
- Keep ConnectionCredentialField and the Secret API contract unchanged.

## Phase 1: Design

### Data model

See [data-model.md](data-model.md). No persisted entity or public request shape is added. The logical state is the existing connection draft, authored settings versus effective defaults, relevant name fields, transient disclosure recovery, and a metadata-only Secret reference or explicit new value.

### Contracts

No contracts/ directory is needed: the work changes no public API, endpoint, DTO, descriptor shape, or extension point. The only package relation is an internal workspace dependency on the existing shared UI package.

### Source structure

    Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionCreateDialog.tsx
    Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/styles.css
    Extensions/Connections/studio/Elsa.Studio.Connections/Client/package.json
    Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/__tests__/connectionCreateDialog.test.tsx
    Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/disclosure/AdvancedSection.tsx
    Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/disclosure/__tests__/disclosure.test.tsx
    Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx
    Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPropertyGroups.test.tsx
    Studio/pnpm-lock.yaml
    Studio/tests/browser/connections-management.spec.ts
    Studio/tests/browser/connectionsFixture.tsx

**Structure Decision**: Reuse the Connections dialog and browser harness, and the existing Studio UI disclosure primitive and test seam. Keep shared disclosure recovery private to the existing component. No new project, endpoint, backend module, or test harness.

### Test strategy

Add failing regression coverage before implementation for the API-key create path and shared disclosure recovery. The browser case is root-owned and must also be authored before implementation. Prove error→focused correction→recovery→manual collapse, preserving mounted child identity, controlled value, and useful focus without changing the stored user disclosure preference. Then implement and show the same regressions passing. Keep all existing Secret and generic-scheme tests passing.

### Quickstart

See [quickstart.md](quickstart.md). Commands are planned only; no tests, builds, browser run, architecture check, or map check was run in this specification preparation.

## Verification and Delivery Plan

All implementation tasks remain unchecked until executed and evidenced. Root runs machine-intensive cross-package/architecture/map checks serially.

1. Focused Connections dialog regression, then full Connections client suite and build.
2. Focused and full Studio Web disclosure tests/build, StudioUi public typecheck build, and full Studio Workflows client suite/build because it consumes the shared component.
3. Canonical Studio typecheck and lint, including CSS and module-layout checks.
4. Direct Playwright Connections browser run with metadata-only stubs; verify keyboard operation, expanded-state accessibility, narrow and desktop layout, address gating, relevant fields, and preserved values.
5. Restore the architecture CI project graph and run the full architecture test project, reconciling executed test count with the reported result. No whole-solution local build.
6. Run generated-maps freshness check and assess the generated findings. Root already verified that the base f7c417d map job failed before this feature because docs/maps/manifest.json and docs/reports/maps-v1-findings.md were stale. Preserve this baseline evidence; do not claim maps were initially fresh or attribute that failure to #3065. Root owns any baseline or feature-caused narrow refresh, findings review, and explicit staging of changed generated files, including the manifest if changed.
7. Perform DRY and full diff review, confirm issue ownership/no competing PR before commit, and resolve all findings.
8. Commit as the Elsa Workflows Codex GitHub App; open a ready PR with Fixes #3065, comment the PR link/state on the issue, and attach the PR in Codex.
9. Complete three independent review axes (acceptance/accessibility, Secret/privacy boundary, code/tests/architecture), exact-head required CI, and a no-failed-check/no-hold review audit. Merge only under the standing Oct 18 authorized gated-merge procedure, recording its audit evidence.
10. Verify resulting-main gates, synchronize #3065, epic #3057, qualification #3075, and GitHub Project 61 state, then release the claim. Update any other project only if its relevance is verified in the live issue/board.

## Complexity Tracking

No constitution exception or new architectural layer is proposed. The internal workspace dependency is required only to use the existing exported shared disclosure component.

## Integration refinement (root, 2026-10-20)

Full Web suite passed 564 tests. Full Workflows suite exposed one valid cross-activity identity regression: its existing stale-host-error test expected a new node’s optional section to start closed, while the new private reveal latch retained the prior node’s state. Root authorized a narrow consumer identity key on AdvancedSection, scoped to draft/activity identity, and adapting the existing test to inspect the current section after the legitimate identity remount. Same-activity corrections retain focus/value/child identity. No public disclosure prop or changed error policy is added. Re-run the focused stale-activity case and full Workflows suite after the correction; the first full run is recorded as 2058 passed / 1 failed, not a green result.

Root visual inspection found the generic Connections browser mode omitted the actual host stylesheet, leaving required spacing tokens undefined; its light palette was also generic fixture fallback. The Connections-only lazy fixture now loads the real host CSS, and only that mode uses the existing theme-palette setup for both modes. The browser proof explicitly checks >=16px dialog insets and is rerun after this fixture correction. This changes test composition, not production styles or host requirements.

Root browser proof with actual host styles reproduced a concrete 320px credential-source defect: focusing the native New value radio changed its width from 13px to 37.375px under generic text-input padding, wrapping the radio onto another line between pointerdown and pointerup and losing the click. Captured pointer/focus/geometry events in external 3065-radio-debug-width.log. Scoped native-radio sizing/padding reset plus the existing label hit height keeps the source choice stable; the actual pointer-driven creation test must pass without force clicks, retry clicks, or keyboard substitution. More-options trigger alignment also preserves the shared left-aligned disclosure composition inside the modal.
