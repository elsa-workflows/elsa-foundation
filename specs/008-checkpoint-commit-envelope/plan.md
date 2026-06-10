# Implementation Plan: Checkpoint Commit Envelope And Post-Commit Intent Boundary

**Branch**: `codex/runtime-next-execution-slice` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/008-checkpoint-commit-envelope/spec.md`

## Summary

Extend the first runtime execution contracts with an explicit checkpoint commit envelope. The envelope names the checkpoint boundary, carries atomic state changes across the split runtime-state categories, records post-commit intents, and routes commits through policy, writer, and dispatcher abstractions. The slice deliberately stops before concrete persistence, bookmarks, actors, or outbox processing.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)
**Primary Dependencies**: Existing Runtime.Core dependencies and BCL `System.Text.Json`
**Storage**: No concrete persistence provider; writer and dispatcher contracts only
**Testing**: xUnit contract tests plus existing architecture guards
**Target Platform**: Elsa server/library runtime packages
**Project Type**: Modular multi-project .NET library
**Performance Goals**: N/A for contract slice
**Constraints**: Runtime execution contracts must not reference `Elsa.Workflows.Design.*`; no full scheduler/bookmark/outbox behavior
**Scale/Scope**: Runtime.Core models/contracts/service, Runtime tests, Speckit artifacts, extension-point catalog update

## Constitution Check

| Gate | Status | Note |
|---|---|---|
| Elsa §E2.2 Runtime must not depend on Design | PASS | New contracts live in Runtime.Core and reuse runtime-owned state models only. |
| Elsa §E2.6 artifact-only runtime | PASS | Commit envelope carries execution state pinned to executable artifacts, not authored documents. |
| Elsa §E2.9 triplet separation | PASS | Commit state remains continuation/runtime state, not read projections or authored state. |
| Framework §2.23 tests for new logic/classes | PASS | Commit ordering and policy semantics get focused contract tests. |

No unjustified violations.

## Project Structure

### Documentation

```text
specs/008-checkpoint-commit-envelope/
├── spec.md
├── plan.md
├── tasks.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── runtime-checkpoint-commit.md
└── checklists/
    └── requirements.md
```

### Source Code

```text
src/Elsa/Workflows/Runtime/Core/
├── Contracts/
│   ├── IRuntimeCheckpointPersistencePolicy.cs
│   ├── IRuntimeCheckpointWriter.cs
│   └── IRuntimePostCommitIntentDispatcher.cs
├── Models/
│   ├── RuntimeCheckpoint.cs
│   └── RuntimeCheckpointCommit.cs
└── Services/
    └── RuntimeCheckpointCommitter.cs

tests/Elsa/Workflows/Runtime/Tests/
└── RuntimeCheckpointCommitTests.cs
```

## Implementation Notes

- Treat `RuntimeCheckpointCommit` as the unit handed to persistence providers.
- Keep immediate/deferred/skip policy decisions separate from the checkpoint name and payload.
- Dispatch post-commit intents after successful writer completion only.
- Represent bookmark, incident, and operational state as explicit runtime state-change references until their full models are introduced.

## Validation

Run:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```
