# Implementation Plan: Studio UX integrated qualification

**Branch**: `agent/codex/3075-final-qualification` | **Date**: 2026-10-25 | **Spec**: [spec.md](spec.md)
**Input**: #3075 under epic #3057, Elsa 4 Simplification #2559.

## Summary

Qualify the existing implementation on normal Workbench/Studio, repair only reproduced in-scope defects, and reconcile the existing module-author guidance. The finite issue matrix owns acceptance and is not replaced by source preparation.

## Technical Context

**Language/Version**: Existing TypeScript/React frontend and .NET10 hosts; external private proof runners.
**Primary Dependencies**: Existing Studio SDK, UI primitives, modular features and Playwright tooling.
**Storage**: Existing disposable SQLite/private configuration only; no schema changes.
**Testing**: Actual composed browser journeys; affected suites, all workflow shuffles, architecture/maps, exact-head and resulting-main CI/Studio/security.
**Target Platform**: Existing browser Workbench/Studio composition, laptop and narrow layouts.
**Project Type**: Integration qualification and existing documentation; focused frontend correction only if reproduced.
**Performance Goals**: None; no benchmark program.
**Constraints**: Preserve authorization, backend/module contracts, [Studio ADR0022 navigation placement](../../Studio/docs/adr/0022-let-the-user-choose-where-the-navigation-sits.md), exact identity and private evidence/cleanup.
**Scale/Scope**: Nine composed rows, UX-01–18 and the existing three guidance documents.

## Constitution Check

Loaded framework v6.1.0 and Elsa v6.3.0 Gate indexes plus the review-only checklist. No project, dependency, public API, feature identity, persistence schema or domain seam changes are planned. No new extension-point catalog or package-version exception is needed.

Framework §2.21.1 preserves existing behavior/tests; §2.23.2 requires meaningful behavior regressions if a reproduced correction changes behavior, never tests asserting markdown/source text. Architecture/map/tier checks remain required. Existing review rules and all owner-retained delivery gates apply.

Draft/provisional sections are not ratified by this work. No reliance on their unresolved material is introduced. Post-design check: no constitutional exception is requested.

## Project Structure

- `Studio/docs/ux/design-spec.md`: normative existing UX rules and audit/example links.
- `Studio/docs/ux-polish-plan.md`: accepted resource-slice wording.
- `Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/README.md`: source-faithful host examples and module boundary.
- Existing frontend/component/browser paths only for a reproduced in-scope defect.
- This spec's plan/research/contracts/quickstart/tasks record work intent; #3075 records actual acceptance.

## Execution and Review

Review spec → plan → tasks before implementation. Preserve one claimed issue and one PR. Execute private journeys serially under an owned lifecycle; keep safe proof metadata and root visual review. Do not edit proof helpers during a run. Three independent review axes and root integration precede separate application review/merge. Revalidate exact head after any source change and qualify resulting main before closure.

## Complexity Tracking

No new product abstraction, project, provider or governance exception.

## T009 refinement: Connection clear

The normal-host authoring journey reproduced a compile failure after clearing an optional Connection. Repair the properties-panel clear path in the existing Workflows frontend, with a behavioral regression through canonical serialization. Retain malformed-reference compiler rejection and all other expression semantics. Root verifies the test fails without the fix, inspects the full diff and repeats the composed authoring/service journey on rebuilt exact-head hosts before final acceptance. This remains the sole #3075 work unit and PR.

## Resource contrast refinement

The rebuilt normal-host picker error resolves to near-white text on an opaque pale modal surface. Correct the existing standalone Connections error foreground tokens and add rendered Classic Light/Dark regression coverage for reachable error states, with a failing baseline control. Keep the defensive placement error style consistent without claiming it was reached through normal UI. Repeat affected browser/private-host checks and every final gate; no backend or provider changes are needed.

## Features checkbox refinement

The normal-host320px selection trace reproduced a same-node Clear miss: releasing checkbox focus expands its inherited padding and the first grid row by18px between pointerdown and pointerup. Stabilize only the bulk-selection checkbox with explicit padding and dimensions, preserving native appearance/focus. Add a rendered320/360 regression for checkbox focus followed by one native Clear click, prove red without the CSS correction and green with it, then repeat both-target private review/Apply and all final gates. The failed preliminary trace and configuration-delta gate remain failed; no exception is introduced.

## Feature settings draft refinement

The normal-host Server review exposed unrelated setting changes after editing only Workflow executable cache capacity: Save draft materialized untouched default/empty values. Preserve untouched configuration membership and values by staging only settings whose displayed values actually changed. Reuse the existing stable value comparison; cover no-op Save, changed-field-only review/payload, deliberate clear and reset using the mounted component. Prove red without the correction and green with it, then rebuild/repeat both-target private Apply and every final gate. No backend persistence contract changes.

## Structured Logs stream authorization refinement

The actual separate-origin browser EventSource uses the interactive cookie and receives a302 redirect when its authenticated identity lacks the Structured Logs read permission. The adjacent SDK reads use Bearer authentication and correctly return403. Restore the stream's already documented401/403 contract with the framework's endpoint-local `DisableCookieRedirect` metadata. Keep the permission policy, authentication selector, grants, ordinary browser redirects and all other endpoints unchanged; the withdrawn runtime-settings cookie proposal remains withdrawn.

Add real-cookie HTTP regressions against the production stream mapping for anonymous401, denied403 and authorized200/SSE, plus ordinary browser redirect controls. Record the baseline failures before the correction, run the complete affected backend suite, then rebuild and repeat exact-head composed qualification, independent review and all delivery gates. This narrowly extends T009 to an observed diagnostics transport defect without changing a backend payload, persistence or module contract.

## Appearance reset persistence refinement

The additive Drift error proof reaches both real409 states, then fails native Reset's existing eight-key clearing guard: resetting the viewport-clamped panel height immediately persists its default again. The same mounted-shell effect pattern can recreate the other chrome defaults. Represent default chrome preferences by absent keys, preserve explicit nondefault choices, and retain an unset active panel as the implicit first panel instead of materializing a selection. Verify the actual Appearance button with the mounted shell and changed chrome settings: all eight keys remain absent after effects settle, defaults render correctly, and subsequent explicit resize/panel/expand choices still persist. Persist theme selections when chosen or resolved from an existing stored id, so a pending DOM effect cannot restore a cleared selection after Reset. Record causal red/green, repeat the complete Web suite, rebuild, and repeat every final gate on the new candidate; the original failed resource credential-retention proof stays unchanged.
