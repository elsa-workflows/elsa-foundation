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
**Constraints**: Preserve authorization, backend/module contracts, ADR0022 navigation, exact identity and private evidence/cleanup.
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
