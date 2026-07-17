# Implementation Plan: Contributed Runtime Intent Handlers

**Branch**: `codex/dispatch-workflow-program` | **Date**: 2026-07-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/095-runtime-intent-handlers/spec.md`

## Summary

Replace the scheduler-only post-commit dispatcher with a deterministic composite dispatcher over contributed intent handlers. Register scheduler delivery through the same public contribution mechanism, retain its existing validation and enqueue behavior, and let the global resumption service process every deliverable intent kind. Guard the seam with unit coverage for registration/conflicts/unsupported kinds and an end-to-end checkpoint → outbox → resumption marker test.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, nullable enabled, implicit usings enabled)

**Primary Dependencies**: Elsa.Workflows.Runtime.Core, Elsa.Workflows.Runtime, Microsoft.Extensions.DependencyInjection.Abstractions

**Storage**: Existing `IRuntimeCheckpointCommitStore` and `IRuntimePostCommitOutboxStore`; no new persistence schema

**Testing**: xUnit with the existing Workflows Runtime unit and in-memory integration fixtures

**Target Platform**: Cross-platform .NET hosts

**Project Type**: Multi-project .NET library/runtime framework

**Performance Goals**: One ordinal handler lookup and one handler invocation per delivery; no additional persistence or actor-mailbox hop

**Constraints**: Preserve scheduler payloads and identifiers; unsupported kinds follow the existing policy-selected safe outbox failure path; deterministic composition independent of module load order; no broker or Design dependency

**Scale/Scope**: One runtime composition with a small bounded set of first- and third-party intent kinds; #675 only

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Runtime/Design boundary**: PASS — contracts and delivery operate only on Runtime-owned intent/outbox models and add no `Elsa.Workflows.Design.*` reference.
- **Artifact-only runtime**: PASS — the change does not load design definitions or mutate executable semantics.
- **Three-layer/package shape**: PASS — public contract remains in Runtime.Core; the default composite and scheduler implementation remain in the Runtime implementation package.
- **Extension contribution semantics**: PASS — one public registration surface; identical contributions are idempotent; same-kind conflicts fail deterministically; the extension-point catalog is updated.
- **Checkpoint/post-commit gate (ADR 0020)**: PASS — checkpoint commits only record intents; delivery remains in the separate outbox processor/resumption path.
- **Pipeline/single-writer boundaries (ADRs 0029/0031)**: PASS — delivery occurs outside workflow actor mailboxes and does not alter scheduler pipeline ordering or fenced execution. ADR 0031 is still marked proposed in the checkout, so it is treated as architecture guidance rather than a ratified constitution gate.
- **Testing gate**: PASS — focused unit tests cover the logic-bearing composite/handler behavior and an integration guardrail crosses the real checkpoint/outbox/resumption seam.
- **Constitution status**: The broader constitution is draft/provisional. No deferred wording is promoted or treated as superseding accepted ADR 0020.

Post-design re-check: PASS. The contracts in [contracts/post-commit-intent-handler.md](contracts/post-commit-intent-handler.md) preserve the same boundaries and introduce no exception requiring Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/095-runtime-intent-handlers/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Runtime/
├── Core/Contracts/                         # public handler contract
├── Extensions/                             # public DI contribution mechanism
├── Services/                               # composite dispatcher + scheduler handler
└── EXTENSION_POINTS.md                     # canonical extension-point documentation

tests/Elsa/Workflows/Runtime/Tests/
├── RuntimePostCommitIntentDispatcherTests.cs
├── RuntimePostCommitIntentContributionTests.cs
└── RuntimeResumptionServiceTests.cs
```

**Structure Decision**: Extend the existing Runtime.Core contract / Runtime implementation split. Keep persistence and Resumption projects unchanged because no provider schema or pump contract changes; the global service change lives in the existing Runtime implementation and is exercised from its current test project.

## Complexity Tracking

No constitution violations require justification.
