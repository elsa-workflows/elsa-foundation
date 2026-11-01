# Implementation plan

## Context and design

Issue #3609; spec reviewed before planning. Studio already separates built-in palette definitions, workspace typography/shape, host CSS and module CSS. Add Sketchbook through those seams. Existing ThemeProvider and preference serialization remain authoritative.

- Add a coordinated built-in `sketchbook` definition with all four modes and a `sketchbook` workspace default.
- Extend the recognized workspace styles with readable sans/mono fonts and a bundled serif display face. Geometry is gently irregular through static CSS radii, with precise hit boxes.
- Host workspace CSS supplies subtle paper/wash recipes through public roles. Modules consume public tokens under the allowed workspace-style guard; no module targets palette identity or host-private variables.
- Preserve semantic runtime/error backgrounds, selected states and high-contrast focus. Remove decoration for High contrast and forced-colors.
- Reuse the approved hand-drawn Elsa icon only for the default host brand in Sketchbook; configured host logos retain precedence.

## Constitution Check — before implementation

Framework 6.1.0 and Elsa 6.3.0 consulted. No Core, domain, persistence, transport, dependency, public signature or execution changes. No new service or framework abstraction. Studio module token ownership follows Studio/docs/design-tokens.md. Risk-based tests target preference/selection behavior and real rendered interfaces. Existing runtime and authoring invariants remain unchanged. No exception required.

## Files and seams

Host: themes/workspaceStyles.ts, themes/foundationThemes.ts (or its domain-local definition), themes/workspaceStyles.css, ThemeProvider/appearance controls only where necessary, App.tsx default-brand selection and bundled image.
Workflow module: existing workspaceAppearance.css and graph-authoring/workspaceAppearance.css, preserving deferred authoring CSS boundaries. Shared semantic roles are documented in Studio/docs/design-tokens.md and branding.md.

Login scope amendment: extend the existing Identity:Login layout/palette/artwork options with Sketchbook. The identity host still owns authentication and its configuration independently of browser preferences. Reuse the approved illustration and drawn logo as embedded resources, retaining the explicit asset allowlist, antiforgery, encoding, safe return URLs and custom renderer. Use flex layout so configured story text cannot overlap the illustration. High contrast removes decoration.

## Validation

Focused Vitest red/green for selection, persistence, custom-copy and palette independence; whole affected frontend unit projects; typecheck, CSS/ESLint, production build. Existing browser appearance matrix extended to Sketchbook and dedicated rendered cases for grain/high contrast, authoring/runtime and narrow layouts. Actual browser inspection uses the real Studio component fixture and, when available, local application. Architecture/map gates per repository PR rules. Review the exact head and refresh main before final qualification.

## Plan review

No new dependency or generated graph geometry. Keep decoration bounded to existing paint surfaces and exclude semantic-state overrides. A custom brand must never be replaced. Rechecked Constitution Check after design: passes without exception.
