# Implementation Plan: Dictionary Entry Editor

**Branch**: detached worktree | **Date**: 2026-07-12 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/005-dictionary-editor/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Replace the ambiguous single-line field for string-keyed dictionary activity inputs with a reusable Workflows dictionary editor. The editor uses a compact inline table for common edits and the existing expanded property modal for full Table and strict JSON modes. It delegates typed values to registered property editors, retains invalid table/JSON drafts in a stable session store, and introduces a shared action-notice primitive for precise reversible actions without duplicating workflow history.

## Technical Context

**Language/Version**: TypeScript 5.6, React 19.1

**Primary Dependencies**: `@elsa-workflows/studio-sdk`, `@elsa-workflows/studio-ui`, `@elsa-workflows/studio-code-editor`, lucide-react, Vite

**Storage**: Existing workflow draft JSON object values; invalid editor drafts remain browser-session memory only

**Testing**: Vitest/jsdom component tests, Playwright browser tests, TypeScript typecheck, ESLint, Stylelint

**Target Platform**: Elsa Studio browser SPA at desktop and resizable inspector widths

**Project Type**: Modular React web application and shared UI package

**Performance Goals**: Immediate editing for at least 100 entries; filter results remain perceptibly instant; no virtualization in the first release

**Constraints**: String keys only; strict object-root JSON; preserve insertion order; no invalid draft may overwrite valid workflow state; all CSS uses `--studio-*` tokens or token-backed Workflows aliases; existing expression syntax behavior must not regress

**Scale/Scope**: One shared UI primitive, one Workflows editor/model, SDK metadata typing, focused unit/integration/browser coverage; no backend or persistence migration

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Modular UI contract**: Pass. Workflows reuses `StudioButton`, `StudioSearchInput`, `StudioTabs`, `StudioCodeEditor`, and the existing expanded property surface. The only new shared primitive is a domain-neutral action notice exported from Studio UI; dictionary logic stays module-owned.
- **Workbench pattern fit**: Pass. This is a dense property-inspector/table workflow with an expanded editing workbench, not a new page or decorative card surface.
- **Typography and token discipline**: Pass. Workflows styles extend `styles.css`; shared notice styles extend the host UI stylesheet. All colors, borders, surfaces, focus states, radii, spacing, and statuses use stable `--studio-*` tokens or token-backed Workflows aliases.
- **Accessible interaction**: Pass. Design covers tablist keyboard behavior, focus entry/restoration, modal isolation, row labels, described validation, error focus, read-only inspection, empty states, non-modal live notices, paused timers, and keyboard-only row entry.
- **Real-screen proof**: Pass. The Activity Properties inspector is the proof screen, exercised at wide and narrow widths plus the expanded modal through a dedicated browser fixture.

## Project Structure

### Documentation (this feature)

```text
specs/005-dictionary-editor/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── dictionary-editor-ui.md
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```text
src/Elsa.Studio.Web/Client/src/
├── sdk/index.ts
└── app/ui/
    ├── feedback/ActionNotice.tsx
    ├── shared.ts
    └── __tests__/kit.test.tsx

src/Elsa.Studio.Workflows/Client/src/
├── activityProperties.ts
├── ActivityPropertiesPanel.tsx
├── DictionaryValueEditor.tsx
├── dictionaryEditorModel.ts
├── dictionaryEditorSession.ts
├── objectExpressionEditor.tsx
├── styles.css
└── __tests__/
    ├── activityProperties.test.ts
    ├── activityPropertyGroups.test.tsx
    ├── dictionaryExpressionEditor.test.tsx
    └── dictionaryValueEditor.test.tsx

tests/browser/
├── src.tsx
└── dictionary-editor.spec.ts
```

**Structure Decision**: Dictionary recognition and pure validation/serialization remain separate from React. The module owns dictionary session drafts because their lifecycle is workflow-specific. The reusable action notice lives in Studio UI and accepts caller-controlled placement and inverse callbacks. Existing property/expression contributions remain the integration boundary; no route, manifest, backend, or module-registration change is required unless implementation proves a new contribution is necessary.

## Implementation Approach

1. Extend the SDK UI specification type with optional dictionary labels, placeholders, and key-comparison metadata.
2. Add pure dictionary type parsing, row conversion, key/value validation, strict duplicate-aware JSON parsing, and formatting helpers with table-driven tests.
3. Add a stable session draft store keyed by workflow/activity/property identity and external-value signature. Keep incomplete table rows, invalid raw JSON, per-entry complex JSON, and last-tab state outside the workflow value; clear them on unload or external replacement.
4. Introduce a reusable action notice in Studio UI. The primitive owns presentation, accessibility, timeout, hover/focus pause, replacement, and dismissal; callers own placement and exact inverse operations.
5. Build the inline dictionary table with typed value-editor delegation, stable row identities, five-row cap, responsive stacking, keyboard flow, precise removal undo, and read-only inspection.
6. Extend the existing expanded property modal with accessible focus isolation/restoration and dictionary Table/JSON tabs. Reuse Studio tabs/search/code editor, guard tab changes when private drafts are invalid, support filter/format/copy/error focus, and expand complex values within rows.
7. Integrate dictionary detection ahead of generic collection/text fallbacks for Literal and structured Object modes while leaving other expression syntaxes unchanged.
8. Verify focused helpers/components, full Workflows regressions, shared UI tests, responsive real-browser behavior, typecheck, lint/token lint, and production build.

## Constitution Check Re-evaluation

- **Modular UI contract**: Still pass. Shared behavior is isolated to the generic action notice and existing UI primitives remain the public module contract.
- **Workbench pattern fit**: Still pass. Inline density and expanded table/JSON editing remain appropriate to the property inspector.
- **Typography and token discipline**: Still pass. No raw color literals or foreign module namespaces are planned.
- **Accessible interaction**: Still pass. Focus, timing, validation, tab guards, read-only state, and responsive keyboard behavior are explicit acceptance gates.
- **Real-screen proof**: Still pass. The quickstart requires the real browser fixture at wide and narrow widths.

## Complexity Tracking

No constitution violations require justification.
