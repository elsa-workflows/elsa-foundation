# Implementation Plan: Runtime Bookmark Resume Contract

**Branch**: `codex/runtime-bookmark-resume-contract` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/010-runtime-bookmark-resume-contract/spec.md`

## Summary

Define the durable bookmark/resume contract before implementing the bookmark store or scheduler resume behavior. Add typed bookmark state, a resume-target declaration attribute for activity authors, and a resolver that maps bookmark `ResumeTargetId` through the pinned executable artifact.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)
**Primary Dependencies**: Existing Runtime.Core and Activities.Runtime.Core dependencies only
**Storage**: N/A
**Testing**: xUnit contract tests plus existing architecture guards
**Target Platform**: Elsa server/library runtime packages
**Project Type**: Modular multi-project .NET library
**Performance Goals**: N/A for contract slice
**Constraints**: Runtime execution contracts must not reference `Elsa.Workflows.Design.*`; no bookmark store, handler invocation, or scheduler behavior in this slice
**Scale/Scope**: Runtime.Core bookmark/resume models, resolver service, activity resume-target attribute, runtime tests, Speckit artifacts, extension-point catalog update

## Constitution Check

| Gate | Status | Note |
|---|---|---|
| Elsa §E2.2 Runtime must not depend on Design | PASS | Bookmark contracts use runtime-owned executable node IDs and pinned artifact identity only. |
| Elsa §E2.6 artifact-only runtime | PASS | Resume resolution uses `WorkflowExecutable.ResumeTargets`, not authored workflow documents. |
| Framework §2.23 tests for new logic/classes | PASS | Tests cover bookmark shape, pinned artifact resolution, and missing target failures. |

No unjustified violations.

## Project Structure

```text
src/Elsa/Activities/Runtime/Core/
└── Attributes/

src/Elsa/Workflows/Runtime/Core/
├── Contracts/
├── Exceptions/
├── Models/
└── Resolvers/

tests/Elsa/Workflows/Runtime/Tests/
└── RuntimeBookmarkResumeContractTests.cs
```

## Implementation Notes

- `ResumeTargetId` is durable state; C# method/delegate names remain implementation details.
- The resolver is a pure contract service. It does not load artifacts or invoke handlers.
- Bookmark lookup indexing is deferred; this slice only defines the fields an index would use.
- Typed bookmark checkpoint changes replace the earlier bookmark reference placeholder.

## Validation

Run:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```
