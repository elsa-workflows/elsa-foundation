# Implementation Plan: Runtime Diagnostics History And Incidents

**Branch**: `codex/runtime-diagnostics-history-incidents` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

## Summary

Add contract-level observability models for execution history and diagnostics, typed incident continuation state, and a payload capture policy with conservative defaults. Keep the slice free of persistence providers and behavior-heavy incident strategy execution.

## Technical Context

**Language/Version**: C# / .NET 10
**Primary Dependencies**: Elsa.Workflows.Runtime.Core, xUnit
**Storage**: No history store in this slice; incidents are represented in checkpoint state-change envelopes
**Testing**: `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`; `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
**Constraints**: History/audit projections must not become runtime continuation state; sensitive payloads are excluded by default

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | New contracts use runtime execution IDs and executable identities only. |
| Runtime state boundary | PASS | `IncidentState` is checkpoint state; history/diagnostics are projections outside continuation state. |
| Payload safety | PASS | Default policy excludes sensitive payloads and omits input/output snapshots. |
| Tests for new logic/classes | PASS | Add focused history, incident, and payload policy tests. |

## Project Structure

```text
specs/012-runtime-diagnostics-history-incidents/
src/Elsa/Workflows/Runtime/Core/
tests/Elsa/Workflows/Runtime/Tests/
```

## Complexity Tracking

No constitution gate violation expected.

## Implementation Notes

- Treat history records as observation projections.
- Treat incident state as minimal runtime state needed for blocking/failure handling.
- Keep payload capture policy declarative; do not introduce serializers or stores.
