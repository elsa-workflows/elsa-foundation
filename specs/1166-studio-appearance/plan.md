# Implementation Plan: Studio appearance

**Branch**: `agent/codex/3447-studio-appearance` | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)

## Summary
Retain the theme store and legacy preset selection. Add nullable browser-local workspace-style and color-scheme overrides. Publish resolved style/palette separately from legacy identity, keep components mounted, and implement the approved hierarchy through shared controls and surface-owned styles.

## Technical Context
TypeScript, React 19, CSS tokens, existing Radix controls and SDK layout contract. Browser-local preferences; unchanged .NET theme-store API and pack schema. No new packages or projects. Scope includes shell, shared controls, workflow/resource lists, authoring, inspector, settings and diagnostics. Validation uses Vitest, rendered browser journeys, types/lint/build/bundle checks, architecture/maps and hosted checks.

## Constitution Check
Framework v6.1.0 and Elsa v6.3.0 gate indexes and the contributor checklist were read. Preserve valid behavior, test risky preference/state paths, preserve published token/module contracts, and retain feature identities. No new dependencies, projects, backend schema, runtime semantics or authorization changes. No reliance on draft/provisional sections.

## Design
- Existing themeId is the base preset. Null workspaceStyle/colorSchemeId overrides inherit it exactly.
- Explicit style selects layout, typography, shape and surface treatment; explicit scheme supplies colors and supported modes only. Normal setters preserve other axes.
- Whole-preset application intentionally clears overrides. Theme Builder preview is transient and temporarily previews its full preset without clearing stored overrides.
- Successful-store-load protection applies independently to custom preset and custom palette IDs. Failed loading never writes fallback IDs.
- data-theme remains the base identity; data-color-scheme and optional data-workspace-style expose resolved presentation. SDK layout values remain compatible.
- Quiet is docked, Focus overlays tools, Open uses editorial authoring. Navigation defaults left for Quiet/Focus and top for Open; explicit placement wins.
- Surface-owned rules and semantic edge roles distinguish decorative separators from interactive controls/focus. High contrast restores required boundaries.
- Floating tools retain accessible resize and canvas clearance. Narrow displays use adaptive panels.
- Main Appearance controls expose workspace style, scheme and mode; existing presets remain accessible.

## Project Structure
Host themes/, components/ThemeProvider.tsx, components/ThemeSwitcher.tsx, settings/AppearancePage.tsx, App.tsx and ui/tokens.css under Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app. Workflows surface styles under Studio/src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/ and existing list/control styles. Tests use existing host/workflow suites and Studio/tests/browser fixtures.

## Review and Execution
Spec reviewed against owner decisions; no clarification needed. Independent research audited preference/store/preview risks. Implement preference behavior, presentation, rendered qualification, then independent review. Prove meaningful behavior red/green through implementation reversion. Preserve valid tests; update only deliberately changed labels/key counts. Record exact commands, failures and screenshots on #3447 and its PR.
