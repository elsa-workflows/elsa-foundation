# Implementation Plan: Shell Feature Management

**Branch**: `codex/shell-feature-management` | **Date**: 2026-06-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/072-shell-feature-management/spec.md`

## Summary

Add backend feature-management APIs under `Elsa.Modularity` and a Studio `FeatureManagement` module. The backend owns shell inference, catalog merging, JSON-backed shell configuration mutation, feature catalog refresh, and shell reload. Studio contributes a route and modular setting-editor registry, stages edits locally, then applies the desired state in one request.

## Technical Context

**Language/Version**: C# net10.0 and React/TypeScript.

**Primary Dependencies**: CShells, FastEndpoints, Nuplane admin APIs, Elsa Studio SDK, Vite.

**Storage**: JSON shell configuration (`shells.json`) for v1.

**Testing**: xUnit for backend; Vitest/jsdom for Studio module and SDK tests.

**Target Platform**: Elsa server shell features and browser-based Elsa Studio.

**Project Type**: Paired backend feature modules plus frontend Studio module.

**Constraints**: Studio must not know or send shell IDs; configuration/settings constitution sections remain deferred.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Three-layer separation | PASS | Contracts in `Elsa.Modularity.Core`; Nuplane-specific discovery in `Elsa.Modularity.Nuplane`; HTTP API in `Elsa.Modularity.Api`. |
| Feature identity | PASS | Uses stable CShells feature names and existing manifest metadata. |
| Provider decomposition | PASS | Nuplane/package discovery stays outside Core. |
| Unit tests | PASS | Registration tests and behavior tests are required for new feature classes and services. |
| Configuration classification deferred | PASS | Consumes current manifest metadata; does not ratify new setting taxonomy. |

## Project Structure

```text
src/elsa/Modularity/Core/
src/elsa/Modularity/Nuplane/
src/elsa/Modularity/Api/
tests/Elsa/Modularity/Tests/

/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.FeatureManagement/
/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Web/Client/src/sdk/
/Users/sipke/Projects/Elsa/elsa-foundation-studio/tests/Elsa.Studio.Tests/
```

**Structure Decision**: Use the Elsa `Modularity` domain named in the constitution and the existing Studio module manifest pattern.

## Implementation Steps

1. Create Speckit artifacts and branch metadata.
2. Add backend Modularity contracts, DTOs, service interfaces, and feature options.
3. Add Nuplane-backed feature catalog discovery and manifest parsing.
4. Add JSON shell configuration store, revision generation, and apply behavior.
5. Add Modularity API endpoints and feature registration.
6. Add backend tests and extension-point documentation.
7. Wire the Elsa server host and refresh generated maps.
8. Add Studio SDK setting-editor registry and built-in editor helpers.
9. Add Studio FeatureManagement module, manifest contribution, client UI, styles, and tests.
10. Validate focused backend and Studio build/test targets.

## Risks

- CShells currently exposes runtime feature catalog refresh through an implementation type; isolate any reflection fallback behind a Modularity service so the API surface remains replaceable.
- JSON shell mutation is v1-specific; keep it behind an interface so future non-file-backed shell stores can replace it.
- Frontend editor extensibility should be generic enough for later modules without overfitting to this page.

## Category navigation refinement plan (#4212)

Replace the module-local CategorySelect and its obsolete popover styles with a visible CategoryRail. Use shared Studio search/button controls and native exclusive-filter buttons (`aria-pressed`). Keep All categories outside the category-search results so it is always reachable. Keep category search in the existing per-host state; browsing changes no feature configuration or network behavior.

Use the existing category derivation after feature search, including the current unavailable-category reset. Preserve feature list, inspector, bulk selection, settings dialog and review/apply handlers. A module container query presents rail/list/inspector columns only when all three fit; otherwise the category actions occupy a bounded, scrollable normal-flow section above the list. The existing narrow list/inspector stacking stays. Labels wrap, with shared theme/focus tokens and coarse-pointer target sizes.

Current paths are `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/{module.tsx,styles.css,__tests__/module.test.ts}`, the composed Features journey, and its isolated browser fixture/layout test. The browser test writes category screenshots to output files; a scoped step in `.github/workflows/ci.yml` retains only those fixture images on successful shards for independent review. Workflow permissions stay unchanged. No backend/project references, new dependencies or extension points change. Framework §2.21/§2.23 apply through retained behavioral coverage and regression bite-proof; FR-008/FR-010 stay unchanged. Deferred configuration classification is not ratified by this UI refinement.

Validate the complete affected module suite, TypeScript and lint with light local checks. Extend the actual composed Features journey for category visibility/search/keyboard/reset and containment in every configured viewport/theme, retaining settings/impact assertions. The normal 1440px host reference can place categories above the list because settings navigation reduces the module width; add explicit 1920px light/dark captures and geometry checks for the three-column side rail and keyboard reach to its final category, then restore the project viewport. Keep the 320px composed reference and isolated 320/360/286px-container/1280px long-label fixtures. The isolated vertical guard uses deliberate clipping as its negative control and restores styles and scroll before captures; it does not assume that changing flex-shrink alone reproduces clipping. Use hosted CI for heavy builds/browser/composed proof under the current machine constraint. Final independent correctness, standards and UX/spec reviews require exact-head downloadable composed captures and the clearly identified isolated stress images before merge and resulting-main checks. Pending or failed checks remain explicit.
