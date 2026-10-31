# Implementation Plan: Minimal Studio presets

**Branch**: `agent/codex/3515-minimal-studio` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)
**Issue**: #3515 | **Program**: Elsa 4 Simplification #2559

## Summary

Three new palettes (`builtin:porcelain`, `builtin:atelier`, `builtin:graphite`) and one shared `minimal` workspace treatment build on #3512. Public UI tokens/primitives own the common rules; chrome and specialist module decoration stay with their owners. Complete-preset cards explicitly select the canonical Light/Light/Dark combinations, while existing one-axis actions retain their semantics. The older Atelier ID and palette remain valid.

## Technical Context

Existing TypeScript/React/CSS, public Studio UI package and .NET 10 theme store. No new project, dependency or migration. Existing local preference keys remain authoritative. Optional theme workspace-default metadata must roundtrip through typed server storage and client cloning/normalization, including previews. Add it as an init property without changing the public record constructor. A theme's own layout/type/shape remain its defaults; only explicit workspace overrides replace them.

Refactor cadence: preserve valid behavior tests; add focused selection/persistence/preview regressions and rendered UI checks. Elsa declares no mandatory greenfield cadence. Demonstrate a red/green bite proof for the new behavior. Preserve bundle and deferred-loading budgets; no benchmark program.

## Constitution Check

Read framework v6.1.0 and Elsa v6.3.0 gate indexes plus the review checklist. No new domain/provider dependency, service, event, contributor, permission, workflow model or schema. Framework §2.21.1 preserves behavior; §2.23.2 requires behavior tests and forbids source/markdown assertions. Optional serialized metadata is a compatible public expansion; retain the existing constructor. Architecture/maps and consumer checks apply. Update the extension catalog only if a real extension seam changes. No reliance on draft/provisional sections and no exception requested.

## Project Structure

- Hosting Client themes/provider/settings: definitions, treatment, precedence, previews and explicit preset application.
- Hosting Client public UI tokens/components/page/disclosure CSS: shared neutral controls, boundaries and rhythm. StudioUi ships these sources for independent consumers.
- Hosting.Themes: new built-in reservations and validated, roundtripped optional workspace metadata.
- Workflows owner: lists/status/inspection, template grouping and deferred designer decoration.
- Structured Logs and Connections owners: use shared visual roles in their own stylesheets.
- Studio docs: canonical token/module/branding guidance; this spec retains visual references and acceptance evidence.

## Design and Verification

See [research](research.md), [appearance data](data-model.md), [visual contract](contracts/visual-contract.md) and [validation guide](quickstart.md). Cover all three preset choices, all four modes, host fallbacks, legacy Atelier, custom copies/previews, reload and independent axes. Exercise six actual screen types: definitions, runs, Appearance, run inspection, templates and Structured Logs. Check 1440px and 320px, keyboard/focus, semantic states, source-owned/read-only surfaces, retained editor/occurrence state and a standalone public UI consumer. Actual populated/empty states stay mutually exclusive; template previews must be truthful.

Run focused tests then whole affected frontend/Studio .NET suites, typecheck/lint/build, public consumers, architecture and maps. Compare discovered/executed .NET test coverage. Complete independent correctness/standards/acceptance review, hosted exact-head CI/Studio/full tier, and resulting-main verification. Reintegrate current main if it advances; report failures/skips/quarantines distinctly.

## Review and Complexity

Spec reviewed before planning; research resolved the old Atelier collision, Linen sidebar borrowing, canonical-mode choice, typed metadata roundtrip and preview semantics. Planning review found no gate violation. No new screen framework or duplicated module UI. Retain approved raster references because close visual fidelity is an explicit owner requirement.
