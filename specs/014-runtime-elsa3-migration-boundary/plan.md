# Implementation Plan: Runtime Elsa 3 Migration Boundary

**Branch**: `codex/runtime-elsa3-migration-boundary` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

## Summary

Add the first explicit Elsa 3 compatibility boundary for runtime execution planning. The slice keeps compatibility import-only: authored Elsa 3 workflow definitions can enter through adapter contracts and diagnostics, while persisted Elsa 3 runtime instance state is rejected as an Elsa 4 runtime continuation source.

## Technical Context

**Language/Version**: C# / .NET 10
**Primary Dependencies**: Elsa3.Models, Elsa3.Mapping, xUnit
**Storage**: No persistence or import tool implementation in this slice
**Testing**: `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
**Constraints**: Runtime must not reference Design-owned authored models or Elsa 3 compatibility modules at execution time

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Elsa 3 compatibility is import-only | PASS | Live instance resume is rejected by contract. |
| Runtime artifact-only execution | PASS | New compatibility contracts live under `Elsa3.*`, not Runtime. |
| Runtime must not depend on Design | PASS | Existing runtime guard remains; add runtime-to-Elsa3 guard. |
| Tests for new logic/classes | PASS | Add focused diagnostics/result and boundary tests. |

## Project Structure

```text
specs/014-runtime-elsa3-migration-boundary/
src/Elsa3/Models/
src/Elsa3/Mapping/
tests/Elsa/Architecture/
```

## Complexity Tracking

No constitution gate violation expected.

## Implementation Notes

- Keep imported authored definitions as Design-side migration output.
- Do not introduce any runtime dependency on Elsa 3 model or mapper packages.
- Return diagnostics for unsupported instance state instead of leaving behavior implicit.
