# Implementation Plan: Connection picker load-failure explanation

**Branch**: `agent/codex/3102-connection-picker-failure` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Issue [#3102](https://github.com/elsa-workflows/elsa-foundation/issues/3102) and [feature specification](spec.md).

**Delivery State**: Root reviewed and approved on 2026-10-20. Implementation is authorized; task completion and verification are recorded separately in tasks.md.

## Summary

Give authors a short, accessible generic explanation when Connection metadata loading fails, beside the current Retry action. Reuse the current retry/loading/result path and preserve the stored reference and existing state guards. No backend, permission, credential, API, module, or error-format changes are planned.

Use “Connections could not be loaded.” in the existing status paragraph and make that persistent paragraph a polite status region. Retain the existing selector description link and Retry action. Test pending retry, repeated rejection, context changes and unchanged reference behavior without displaying provider exception details.

## Technical Context

**Language/Version**: TypeScript in the existing React 19 Studio Connections client; no language or runtime upgrade.

**Primary Dependencies**: Existing `ConnectionPickerEditor`, `pickConnections`, Vitest/React DOM test setup, and the Studio Playwright browser fixture. No new dependency.

**Storage**: None. Preserve the existing opaque workflow Connection reference; no credential value is read or stored by this change.

**Testing**: Add direct React DOM/Vitest picker coverage and strengthen the existing Playwright `connection-syntax-layout.spec.ts` browser seam with metadata-only fixtures. Demonstrate failure before the UI change and success afterward. Then run the full Connections client suite/build, canonical Studio typecheck/lint, the browser accessibility/layout proof, the architecture guard, and the generated-maps freshness check. If the new spec inventory/status makes maps stale, refresh only the relevant maps layer, review findings, and stage changed generated paths explicitly, including the manifest if changed. Do not claim these runs from this planning work; no full-solution local build is planned.

**Target Platform**: Existing Elsa Studio browser client.

**Project Type**: Internal Studio extension UI.

**Performance Goals**: No performance claim or behavior change.

**Constraints**: Keep the rejection message generic because the current editor has no verified status to present. Preserve endpoint context, stale-response and unmount guards, disabled selection, the unavailable-reference state, and metadata-only behavior. Do not change permission policy, credential storage, server behavior, network security settings, module activation, or the error format.

**Scale/Scope**: One existing editor, one focused component test file, and the existing browser proof. No new project, public surface, registration, contract, or endpoint.

## Constitution Check

*Gate review before research; re-check after design.*

**Before research**

- Framework §2.1: no dependency or project-layer change; the existing editor continues to call the existing Connections client operation.
- Framework §2.21: this is a behavior fix, not a refactor that removes or weakens valid tests.
- Framework §2.23.2: add behavior-focused proof for the failure and recovery behavior at the cheapest useful level. No registration test is needed because feature registration does not change.
- Elsa constitution: no runtime, persistence, module-boundary, or workflow-value semantics change. No draft/provisional Elsa section is needed for this UI behavior.
- No constitution exception or architecture decision is proposed.

**After design**

- The design changes only the visible failed state and tests it; it adds no dependency, public type, route, data shape, or contract.
- The issue requires red/green regression evidence, so component and browser assertions precede the UI change in the task order. This is scoped to #3102 and is not a new application-wide TDD rule.
- No extension-point catalog or architecture map change is expected. Spec 253 changes the generated spec inventory/status input, so the authoritative maps check is required; refresh the narrow relevant maps layer only if stale and review any generated findings.

## Project Structure

### Documentation (this feature)

```text
specs/253-connection-picker-failure/
├── spec.md
├── checklists/requirements.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── tasks.md
```

No `contracts/` directory is planned: no external interface or error format changes.

### Source Code (repository root)

```text
Extensions/Connections/studio/Elsa.Studio.Connections/Client/
├── src/ConnectionPickerEditor.tsx                         # existing UI; minimal failure-copy change
└── src/__tests__/connectionPickerEditor.test.tsx           # focused component regression

Studio/tests/browser/connection-syntax-layout.spec.ts       # extend current fixture proof
```

**Structure Decision**: Keep the single authoring journey in the Connections module. Reuse the existing client test harness and Studio browser fixture. Do not add a generic error framework or new cross-module contract.

## Complexity Tracking

No constitution violation or exception is proposed.
