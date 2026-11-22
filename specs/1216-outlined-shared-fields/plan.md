# Implementation Plan: Outlined shared Studio fields

**Branch**: agent/codex/4219-outlined-shared-fields | **Date**:2026-11-12 | **Spec**:[spec.md](spec.md)

## Summary
Promote the accepted native field treatment into shared Studio UI, preserve existing callers, and migrate host/optional module scalar fields through that contract. Complete the actual source census and preserve semantically distinct controls. Qualify theme/state geometry and real workflows before protected delivery.

## Technical Context
Existing TypeScript/React19, Studio UI import-map package, semantic CSS tokens, Vitest and Playwright; .NET10 composed Workbench/Studio hosts. No new libraries, backend persistence or package identities. Shared UI is a public module-author surface; native controls and caller events/state remain authoritative.

## Constitution Check
Loaded framework6.1.0 and Elsa gate indexes plus contributor review checklist. Existing Studio-owned shared surface and module imports preserve dependency boundaries. No .NET API, provider, project, feature identity or test-project addition. Added optional field properties preserve public callers. Behaviour/accessible-name/ID/ref/description tests protect the risky composition paths; actual theme/host screenshots protect rendering. Retain all meaningful previous tests; any selector update names its old markup assumption. No draft/provisional architectural decision is used. Post-design check: no exception needed.

## Structure
- Shared forms/Field.tsx plus scoped forms stylesheet and semantic tokens.
- Existing host/module TSX call sites and local field helpers; Secret field compatibility re-export and removal of duplicate styling.
- Shared UI and affected-module tests; browser field-state gallery and affected existing journeys.
- Existing module-author docs/examples; spec inventory and evidence references.

## Validation
1. Native behaviour controls for unique labels/IDs, descriptions, errors, refs/events, output, disabled versus readonly, affix focus, value retention and custom/group compatibility.
2. All theme/appearance field states, workspace geometry,320/360px,200% text,RTL,forced colours/reduced motion and long multiline labels.
3. Affected complete frontend suites/typecheck/lint/build and current composed host journeys; real create/edit/validation/recovery/persistence on changed hosts. Optional modules use real registered components where composed routes are unavailable; identify fixture evidence.
4. Three independent source lenses to convergence, independently opened current images/ARIA/traces, exact-head required CI, normal protected merge, containing-main verification. Preserve failures, retries and skips explicitly.

## Risks and mitigations
Native fieldset width/legend layout -> min-inline-size0 and real narrow/enlarged captures. Existing module input rules -> scoped reset and Agent/UserTasks composed checks. Contributor editors -> no recursive mutation; adopt at owned boundaries and preserve individual labels/actions. Large migration -> mechanical attribute preservation plus source inventory, typechecks and whole affected behavioural suites.

## Complexity Tracking
No new architectural layer or library. One shared native-field contract replaces module-local duplication.
