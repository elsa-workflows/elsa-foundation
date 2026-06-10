# Implementation Plan: Runtime Value Binding Contract

**Branch**: `codex/runtime-value-binding-contract` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

## Summary

Add typed runtime contracts for executable-node input bindings, active activity outputs, and durable output capture declarations. Keep the slice contract-level: no full expression evaluation, scheduler behavior, authored data-link compiler, history projection, or persistence provider.

## Technical Context

**Language/Version**: C# / .NET 10
**Primary Dependencies**: Elsa.Workflows.Runtime.Core, Elsa.Activities.Runtime.Core, xUnit
**Storage**: In-memory active output register for contract tests only; durable values remain checkpoint state changes
**Testing**: `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`; `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
**Constraints**: Runtime execution contracts must not reference `Elsa.Workflows.Design.*`; raw activity outputs are not durable continuation state

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Contracts use runtime executable node/activity execution ids only. |
| Artifact-only runtime | PASS | Data links are represented as compiled `RuntimeInputBinding` contracts on `ExecutableNode`. |
| Durable state boundary | PASS | Output capture targets `DurableValueState`; no durable output store is introduced. |
| Tests for new logic/classes | PASS | Add focused resolver/register/validator tests. |

## Project Structure

```text
specs/011-runtime-value-binding-contract/
src/Elsa/Workflows/Runtime/Core/
tests/Elsa/Workflows/Runtime/Tests/
```

## Complexity Tracking

No constitution gate violation expected.

## Implementation Notes

- Treat `RuntimeInputBinding` as the durable binding declaration.
- Treat resolved input values as execution-local only.
- Use `ActiveActivityOutputRegister` as an active-scope register; clearing it simulates losing active scope across suspension.
- Keep expression bindings as declarations for later expression middleware.
