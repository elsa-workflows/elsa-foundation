# Implementation Plan: Expression Code Intelligence

**Branch**: `codex/expression-code-intelligence` | **Date**: 2026-07-28 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/094-expression-code-intelligence/spec.md`

## Summary

Add rich JavaScript and Liquid expression authoring to the activity-properties inspector by extending the existing engine-neutral `Elsa.Studio.CodeEditor` package with compact and expanded CodeMirror 6 profiles, workflow-lifetime editor sessions, and neutral completion/diagnostic inputs. Extend the public Studio expression-editor SDK with stable document identity, language-neutral authoring context, capability/status envelopes, and cancellation/version metadata. JavaScript and Liquid modules remain responsible for language projection and editor adapters; Workflows remains responsible for draft/activity/property scope and consequential-action integration.

Authoritative symbol metadata, source-aware completion/hover, semantic validation, and full-draft execution/publication gates are delivered by the coordinated Elsa Foundation work unit `143-expression-code-intelligence` through capability `expressions.tooling.v1`. Studio discovers those additive API relations, uses them when compatible, and retains syntax-aware or generic editing when they are absent.

## Technical Context

**Language/Version**: TypeScript 5.6, React 19, C# 13 / .NET 10

**Primary Dependencies**: CodeMirror 6 through `@uiw/react-codemirror`; `@codemirror/lang-javascript`; new `@codemirror/lang-liquid`, `@codemirror/autocomplete`, `@codemirror/lint`, `@codemirror/state`, and `@codemirror/view`; existing Studio SDK and hypermedia capability client

**Storage**: Existing persisted workflow drafts only; editor sessions and tooling caches are memory-only and scoped to the browser/workflow lifetime

**Testing**: Vitest + jsdom for package/module/host tests, Playwright for real inspector journeys, xUnit for any Studio server contract tests, TypeScript typecheck, ESLint, Stylelint, Vite builds, .NET solution build/tests

**Target Platform**: Current stable desktop Chromium/Firefox/Safari and supported touch browsers; ASP.NET Core Studio host connected to an Elsa Foundation backend

**Project Type**: Modular web application with separately packaged Studio client modules and an external hypermedia API backend

**Performance Goals**: p95 focused compact activation ≤100 ms warm and ≤500 ms cold; no p95 typing task >50 ms in the defined 50-field fixture; only the focused compact field mounts a rich editor

**Constraints**: Preserve exact source; keep CodeMirror types internal; no project-wide TypeScript service; no evaluation or live values for intelligence; permission/Host Policy filtering is server-authoritative; draft autosave/history stays authoritative; semantic features must degrade independently

**Scale/Scope**: JavaScript and Liquid only; one shared editor substrate; bounded/searchable catalogs; recursive value shapes by reference; workflow drafts with high-density inspectors and expressions up to 2,000 characters in the benchmark

## Constitution Check

*GATE: Passed before Phase 0 and re-checked after Phase 1 design.*

- **Modular UI contract — PASS**: `Elsa.Studio.CodeEditor` owns the reusable editor/session primitive. Workflows contributes language-neutral context, while JavaScript and Liquid modules contribute their adapters. No module consumes another module's private CSS or CodeMirror types through the public SDK.
- **Workbench pattern fit — PASS**: This extends the existing workflow master/detail workbench's activity-properties inspector and modal property editor; no new page archetype is introduced.
- **Typography and token discipline — PASS**: Changed module CSS uses only the stable `--studio-*` token contract. CodeMirror theme projection is centralized in `Elsa.Studio.CodeEditor`.
- **Accessible interaction — PASS**: Compact focus activation, completion acceptance, multiline expansion, diagnostic announcements, loading/degraded states, Tab behavior, Escape handling, and the expanded-editor Tab escape are explicit test scenarios.
- **Real-screen proof — PASS**: Playwright exercises the actual activity-properties inspector fixture in compact, expanded, multiline, invalid, unavailable, and touch viewport states; visual evidence is captured for review.
- **Cross-repository authority — PASS**: Studio consumes additive Elsa API capability links. Runtime-owned JavaScript/Liquid metadata and semantic validation remain in Foundation modules, avoiding a Studio authority inversion.

## Project Structure

### Documentation (this feature)

```text
specs/094-expression-code-intelligence/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── expression-tooling-api.openapi.yaml
│   └── studio-expression-tooling.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Elsa.Studio.CodeEditor/Client/
│   └── src/
│       ├── engines/
│       ├── languages/
│       ├── sessions/
│       └── __tests__/
├── Elsa.Studio.Web/Client/src/sdk/
├── Elsa.Studio.Workflows/Client/src/
│   ├── api/
│   ├── expression-tooling/
│   ├── workflow-editor/
│   └── __tests__/
├── Elsa.Studio.ExpressionEditors.JavaScript/Client/src/
└── Elsa.Studio.ExpressionEditors.Liquid/Client/src/

tests/
└── browser/
    ├── src.tsx
    └── expression-code-intelligence.spec.ts
```

**Structure Decision**: Extend the existing module boundaries. The editor engine/session substrate remains internal to `Elsa.Studio.CodeEditor`; the engine-neutral expression contract remains in the public Studio SDK; workflow-context orchestration remains in `Elsa.Studio.Workflows`; and language-specific projection remains in each expression-editor module. No new Studio server project or proxy is introduced.

## Delivery Dependency

The Studio implementation can be built and tested against fixtures independently, but authoritative semantic capability is available only when Elsa Foundation work unit `143-expression-code-intelligence` is deployed. The Studio PR therefore:

1. consumes optional additive capability relations without changing the existing Expressions capability major;
2. treats missing relations as an explicit unavailable state;
3. ships contract fixtures that match the coordinated Foundation OpenAPI;
4. never substitutes a client-only validator for authoritative publication or Test Run gates.

## Complexity Tracking

No constitution violations require justification.
