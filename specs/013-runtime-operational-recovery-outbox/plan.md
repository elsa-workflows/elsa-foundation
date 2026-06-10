# Implementation Plan: Runtime Operational Recovery And Post-Commit Outbox

**Branch**: `codex/runtime-operational-recovery-outbox` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

## Summary

Add contract-level operational recovery and post-commit outbox models. Keep the slice provider-neutral: define state, boundaries, and invariants, but do not implement a durable scanner, outbox processor, or actor provider.

## Technical Context

**Language/Version**: C# / .NET 10
**Primary Dependencies**: Elsa.Workflows.Runtime.Core, xUnit
**Storage**: No provider implementation in this slice; contracts target future checkpoint/outbox stores
**Testing**: `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`; `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
**Constraints**: Operational recovery must stay distinct from domain retry; post-commit effects are delivered only after checkpoint commit

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | New contracts use runtime execution IDs and operational IDs only. |
| Runtime state boundary | PASS | Operational state is continuation/coordination state; outbox delivery state is separate delivery state. |
| Actor/provider neutrality | PASS | Scanner/outbox contracts are provider boundaries; no actor framework dependency. |
| Tests for new logic/classes | PASS | Add focused operational recovery and outbox contract tests. |

## Project Structure

```text
specs/013-runtime-operational-recovery-outbox/
src/Elsa/Workflows/Runtime/Core/
tests/Elsa/Workflows/Runtime/Tests/
```

## Complexity Tracking

No constitution gate violation expected.

## Implementation Notes

- Model execution lease, heartbeat, drain, and interruption as operational state.
- Model post-commit outbox delivery state separately from checkpoint state changes.
- Keep domain retry as a policy boundary, not an operational recovery outcome.
