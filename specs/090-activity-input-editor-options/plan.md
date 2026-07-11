# Implementation Plan: Activity Input Editor Options

**Branch**: `codex/090-activity-input-editor-options` | **Date**: 2026-07-11 | **Spec**: [spec.md](spec.md)

**Input**: Approved feature specification in `specs/090-activity-input-editor-options/spec.md`

## Summary

Extend CLR activity-input metadata with UI hints, static string options, repeatable typed/labeled options, and stable dynamic-provider keys. Reconciliation serializes this metadata into the existing opaque `UISpecifications` JSON. A design-side keyed provider contribution and workflow-management endpoint resolve context-dependent options. Studio consumes both forms, renders dropdown/checklist editors according to input cardinality and explicit hints, refreshes declared dependencies, and preserves stale values.

## Technical Context

**Language/Version**: C# 14 / .NET 10; TypeScript 5 and React 19

**Primary Dependencies**: ASP.NET Core minimal APIs, CShells feature composition/DI, System.Text.Json, Vite/Vitest, existing Studio SDK and property-editor registry

**Storage**: Existing activity-definition catalog JSON and workflow draft state; no schema migration

**Testing**: xUnit with project-level `dotnet test`; Vitest, TypeScript `tsc --noEmit`, Vite production builds

**Target Platform**: Elsa design host and browser-based Elsa Studio

**Project Type**: Cross-repository .NET library/web service plus React web application

**Performance Goals**: Static options require no additional request; dynamic options issue one request on open and at most one request per 150 ms dependency-change window; superseded requests are cancelled

**Constraints**: Preserve reflection-only CLR scanning, opaque `JsonElement` UI metadata, enum inference, Design/Runtime deployment shapes, SDK extensibility, and authored values on failures/stale results

**Scale/Scope**: One activity-input contract, one keyed contribution seam, one endpoint, two built-in Studio editor paths, and the `HttpEndpoint.SupportedMethods` reference input

## Constitution Check

*GATE: Passed before and after design. Constitutions remain draft/provisional.*

- **Framework §2.1 / Elsa §E2.2**: Runtime activity packages receive metadata-only attribute additions. Executable providers live in `Elsa.Workflows.Design.Core`/design-side modules, so no new Runtime → Design dependency is introduced.
- **Framework §2.6**: Dynamic providers use the sanctioned keyed multi-contributor pattern. One resolver owns selection and rejects duplicate keys; callers never resolve arbitrary implementations directly.
- **Framework §2.6.4**: Activity metadata stays in the design/catalog contract and does not alter runtime input evaluation.
- **Framework §2.15**: The backend contract and Studio consumption shape remain in their existing repositories; the foundation spec is the cross-repository source of truth.
- **Framework §2.21.1 / §2.23**: Existing behavior is preserved and the change is covered at attribute/scanner, resolver/API, activity metadata, SDK, and rendered-editor levels.
- **ADR 0035**: `InputDefinition.UISpecifications` remains opaque `JsonElement?`; no open CLR object graph is introduced.
- **Source-of-truth rule**: Public behavior is specified here; provider navigation is added to the extension-point catalog; generated maps remain generated facts.

## Project Structure

### Documentation (this feature)

```text
specs/090-activity-input-editor-options/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── activity-input-authoring.md
│   ├── dynamic-options-api.md
│   └── studio-descriptor.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (two repositories)

```text
elsa-foundation/
├── src/Elsa/Activities/Runtime/Core/              # author attributes and UI-hint constants
├── src/Elsa/Activities/Design/Reconciliation/Clr/ # reflection-only metadata projection
├── src/Elsa/Workflows/Design/Core/                # provider contribution contract and context
├── src/Apps/Elsa.Server/                           # keyed resolver registration and HTTP operation
├── src/Elsa/Activities/Http/                       # SupportedMethods reference metadata
└── tests/Elsa/{Activities,Modularity}/             # scanner, API, and activity metadata tests

elsa-foundation-studio/
├── src/Elsa.Studio.Web/Client/src/sdk/             # descriptor/provider public types
├── src/Elsa.Studio.Web/Client/src/app/             # built-in constrained editors
├── src/Elsa.Studio.Workflows/Client/src/           # dynamic loading and property-panel integration
└── src/**/__tests__/                                # registry, rendered editor, and workflow integration tests
```

**Structure Decision**: Reuse existing runtime metadata, activity reconciliation, workflow design, server integration, Studio SDK, and workflow property-editor modules. Do not create a new production project. Provider implementations that need workflow context are registered by design-side modules, never by runtime activity libraries.

## Complexity Tracking

No constitution violations or new project exceptions are required.
