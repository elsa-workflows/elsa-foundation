# Implementation Plan: Activity Instance Metadata

**Branch**: `codex/activity-instance-metadata` | **Date**: 2026-07-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/094-activity-instance-metadata/spec.md`

## Summary

Add optional per-occurrence Display Name and Description to Foundation's typed workflow design
metadata, copy them into an immutable Source Reference presentation sidecar keyed by executable node
ID, and keep both outside Execution Material and `ArtifactHash`. Studio will expose the metadata in
the Inspector, resolve labels consistently, introduce a reusable accessible `CopyableIdentifier`,
and remove routine version badges from nodes and unambiguous palette rows.

## Technical Context

**Language/Version**: TypeScript 5 / React 19; C# / .NET 10

**Primary Dependencies**: React, React Flow, Vitest, Playwright; Elsa Foundation design,
publishing, runtime, and Groundwork persistence modules

**Storage**: Foundation workflow draft/version design-metadata documents and Source Reference
documents

**Testing**: Vitest, Playwright, xUnit, `dotnet test`

**Target Platform**: Browser-hosted Studio and cross-platform .NET services

**Project Type**: Cross-repository web application plus backend libraries/APIs

**Performance Goals**: Metadata edits remain within the existing immediate local-edit/autosave
path; identifier rendering and label resolution add no network round trip

**Constraints**: Presentation metadata must not affect runtime execution or behavioral identity;
existing definitions and Source References must deserialize without generated migration values

**Scale/Scope**: One shared Studio primitive, workflow designer/inspector/palette/runtime
projections, Foundation design persistence, publication/Test Run projection, inspection APIs, and
targeted contract/integration/browser tests

## Constitution Check

- **Modular UI contract**: PASS. `CopyableIdentifier` is added to Studio's shared UI surface and
  imported through the public `@elsa-workflows/studio-ui` contract.
- **Workbench pattern fit**: PASS. The existing designer master/detail workbench is extended; no
  new archetype is introduced.
- **Typography and token discipline**: PASS. Changes use the existing `--studio-*` token contract
  and add no raw colors, unmanaged type, radii, shadows, or status styles.
- **Accessible interaction**: PASS. Copy controls are keyboard buttons, full values are available
  without relying on truncation, copied/failure state is locally announced, and read-only states
  retain semantic labels.
- **Real-screen proof**: PASS. The Workflow Designer Inspector, canvas, and activity palette are
  the real-screen proof, backed by component tests and Playwright verification.

## Project Structure

### Documentation

```text
specs/094-activity-instance-metadata/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── activity-presentation-metadata.md
│   └── copyable-identifier.md
├── checklists/requirements.md
└── tasks.md

docs/adr/
└── 0012-keep-activity-presentation-outside-execution-material.md
```

### Studio source

```text
src/Elsa.Studio.Web/Client/src/app/
├── ui/identity/CopyableIdentifier.tsx
├── ui/shared.ts
├── modules/workflows/
│   ├── workflow-editor/
│   ├── WorkflowInstances.tsx
│   └── styles.css
└── styles.css
```

### Foundation source

```text
src/Elsa/Workflows/
├── Design/{Core,Api,Persistence}/
├── Publishing/
└── Runtime/{Core,Api}/

tests/Elsa/Workflows/
├── Design/
├── Publishing/
└── Runtime/
```

**Structure Decision**: Extend Foundation's existing draft/version design-metadata sibling and
Source Reference lifecycle rather than adding metadata to `WorkflowDefinitionState` or executable
nodes. Add the UI primitive to Studio's shared public UI barrel and keep workflow-specific
composition in the Workflows module.

## Cross-Repository Delivery

Foundation work is developed on `codex/activity-instance-metadata` in the dedicated
`elsa-foundation` worktree. Studio consumes the additive contract on its matching branch. Foundation
contract tests land before Studio switches its draft/source projections to the new fields.

## Complexity Tracking

No constitution violations require an exception.
