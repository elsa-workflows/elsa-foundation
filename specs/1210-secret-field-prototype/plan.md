# Implementation Plan: Compact Secret field prototype

**Branch**: `agent/codex/3851-secret-field-prototype` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

## Summary

Introduce a module-internal compact field shell in Secrets, opt it into Create secret and its scope picker, and exercise the real dialog through an interactive isolated preview and an owned-host journey. Floating labels use native controls and existing semantic tokens. No shared UI API is frozen during the prototype.

## Technical Context

**Language/Version**: TypeScript, React 19, CSS; existing pnpm workspace.
**Primary Dependencies**: Existing Studio SDK/UI, ThemeProvider and lucide-react; no new dependencies.
**Storage**: Existing Secrets APIs; composed proof owns disposable host copies and stores.
**Testing**: Existing Secrets Vitest project, targeted Playwright browser suites, composed Studio UX acceptance runner.
**Target Platform**: Studio browsers, keyboard, 360px narrow layouts, enlarged text, forced colors, reduced motion.
**Project Type**: Existing UI module.
**Constraints**: Semantic colors only; no existing listener/store reuse; secret values stay masked until explicitly revealed.
**Scale/Scope**: Create secret plus opt-in scope picker; all built-in themes and supported modes enumerated from the canonical theme registry.

## Constitution Check

Loaded framework v6.1.0 and Elsa v6.3.0. Applies framework §2.17 (module-local extraction), §2.21.1 (preserve existing behavior), §2.23.2 (test decisions and failure paths), ADR 0080 D1/D6 (risk-based tests/internal surface) and Studio token/UX acceptance contracts. No C# types, features, package references, persistence invariants or domain boundaries change. No provisional constitutional sections are relied on. Elsa declares no greenfield TDD cadence; this existing-form change uses regression tests and a focused mutation check. No additional greenfield application cadence is inferred.

Pre-design gate: passed. Post-design gate: passed; helper stays internal to the module, not the shared package.

## Project Structure

- `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretFormField.tsx` — compact native-control shell, label/help/error association.
- `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretDialogs.tsx` — opt-in Create secret integration.
- `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretScopePicker.tsx` — optional compact treatment.
- `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/styles.css` — semantic token styling and accessibility modes.
- `Studio/tests/browser/secretFieldPrototype*` / `secret-field-prototype*` — interactive actual-component preview and theme/state captures.
- `Studio/tests/acceptance/journeys.spec.ts` and `tools/ux-acceptance/run.py` — feature-specific composed journey, inventory and required evidence.
- This spec directory — design decisions, contracts, tasks, validation and findings.

## Validation and review

Run affected Secrets unit suite whole, module typecheck/build, CSS/JS lint for touched files, browser scope regressions and theme/state matrix. Compare before/after dialog geometry at the same viewport/theme. Commit before composed proof. Independent acceptance reviewer inspects screenshots of the exact source revision; fixture passes cannot substitute for composed proof. Open the prototype PR as draft, complete independent review and required checks, then use the normal merge process; shared extraction remains a future work unit.

## Filled / Outlined comparison follow-up

Scope: preview-only controls and styles in `Studio/tests/browser/secretFieldPrototypeFixture.tsx` and `.css`; product/module/shared-UI sources are unchanged. The toolbar and an in-dialog portal expose native radio groups with one tab stop each. A preview preference and URL parameter persist the presentation independently of theme choice. Live switching changes only the ancestor style attribute, preserving control instances, draft values, validation and reveal state.

Outlined uses an in-flow wrapping label crossing the outer border and a separate native-control row. The label paints with the containing surface role, including More options and state sheets. Textarea scrolling remains in its own row. Parameterize the existing theme/state suite over both styles, add a live-switch/persistence/focus journey, and inspect captures independently. No new public field API, product setting or module dependency is introduced; composed-host product sources and prior proof remain unchanged.
