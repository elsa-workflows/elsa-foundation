# Implementation plan

**Issue**: #3863 · **Branch**: agent/codex/3863-grouped-studio-navigation

Spec review: the owner's approved hierarchy and theme request are encoded without new domain behavior. All existing modules, authorization and route ownership remain intact.

## Implementation

1. Extend the existing navigation placement contract with Build, Operate, Integrations, Workflow authoring and Runtime settings. Use explicit placements and inherited child placements; undeclared top-level workspace entries are unplaced. Add optional parent metadata for local route views, forwarded by feature-area registration.
2. Replace the single More tools disclosure with reusable named groups, initially open in sidebar mode. Persist explicit collapse choices, reveal active/search results, preserve rail and accessible dismissible top-navigation menus. Home stays direct; Settings moves beside the environment/account footer.
3. Use module-owned routes as local views rather than duplicating screens: Library (activities/templates), Workflows (definitions/executables), Diagnostics (overview/diagnostic children). Register existing children under their parent surfaces; derive Library's entry from its accessible children. Move configuration entries into named Settings groups and add contextual links.
4. Apply shared token-based spacing and restrained navigation/icon/surface styling across the existing theme/layout axes. Preserve high contrast and coarse-pointer targets. Update first-party/sample navigation producers, fixtures and module-author guidance.

## Constitution check

Framework v6.1.0 and Elsa v6.5.0 apply. This is Studio-owned navigation composition and presentation; no new project, provider, dependency or admitted domain, persistence schema, backend lifecycle or draft constitutional assumption. Existing Studio slot, module feature and permission boundaries remain authoritative. Public TypeScript metadata changes require SDK/module documentation and composition/availability regressions. Follow §2.21 behavior preservation and §2.23 risk-based testing; do not assert on source text.

## Validation and review

Prove a representative changed navigation expectation red before implementation, then green. Run affected complete frontend suites, type/lint/build checks and rendered browser journeys at laptop/narrow widths and relevant appearance axes. Extend the owned-host UX runner for the moved-destination journey and retain source-head screenshots/traces. Commit before final composed proof. Run required architecture/maps checks through the existing build-slot wrapper. Use the repository's independent correctness, standards and acceptance/UX reviewers on the exact committed diff, fixing/retesting findings before requesting app review. Keep failed/incomplete gates visible.

Plan review: implementation remains within the approved hierarchy; links preserve qualified backend addresses and workspace intent; parent-derived Library availability avoids dead navigation in partial compositions. Theme work uses shared tokens, without duplicating module CSS.
