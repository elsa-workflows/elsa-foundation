# Implementation Plan: Runtime Pipeline Slots And Inspectable Plans

**Branch**: `codex/runtime-pipeline-slots` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/009-runtime-pipeline-slots/spec.md`

## Summary

Define the runtime pipeline seam before behavior-heavy middleware exists. Add workflow/activity-specific middleware contracts, stable slot names, slot/order registration models, inspectable plan models, and builders that seed built-in no-op placeholders. This preserves separate workflow and activity pipelines while letting tests and modules inspect the resolved ordering.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)
**Primary Dependencies**: Existing Runtime.Core dependencies only
**Storage**: N/A
**Testing**: xUnit contract tests plus existing architecture guards
**Target Platform**: Elsa server/library runtime packages
**Project Type**: Modular multi-project .NET library
**Performance Goals**: N/A for contract slice
**Constraints**: Runtime execution contracts must not reference `Elsa.Workflows.Design.*`; no execution behavior or before/after graph sorting
**Scale/Scope**: Runtime.Core constants/contracts/models/builders/middleware placeholders, runtime tests, Speckit artifacts, extension-point catalog update

## Constitution Check

| Gate | Status | Note |
|---|---|---|
| Elsa §E2.2 Runtime must not depend on Design | PASS | Contracts live in Runtime.Core and expose only runtime-owned state/context models. |
| Elsa §E2.6 artifact-only runtime | PASS | Workflow context carries runtime execution state pinned to an executable artifact. |
| Framework §2.23 tests for new logic/classes | PASS | Tests cover plan ordering, built-ins, and context separation. |

No unjustified violations.

## Project Structure

```text
src/Elsa/Workflows/Runtime/Core/
├── Builders/
├── Constants/
├── Contracts/
├── Middleware/
└── Models/

tests/Elsa/Workflows/Runtime/Tests/
└── RuntimePipelineContractTests.cs
```

## Implementation Notes

- Use slot plus integer order as the only ordering mechanism in this slice.
- Do not introduce before/after constraints until the runtime has concrete middleware needs.
- Built-in placeholders are no-op middleware classes so the plan shape is inspectable before execution is wired.
- Workflow and activity middleware contracts use distinct context types.

## Validation

Run:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```
