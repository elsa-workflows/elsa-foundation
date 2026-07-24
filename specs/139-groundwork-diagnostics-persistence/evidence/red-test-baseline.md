# User Story 1 Red-Test Baseline

> Historical provenance only. This red run was captured on the stale pre-replay branch and is not
> evidence for Groundwork preview.81 or current Elsa `main`. T015 remains open as a non-retroactive
> process deviation; this artifact cannot promote or retroactively certify the missing test-first
> baseline.

Recorded at `2026-07-14T00:47:54Z`, before implementing T016-T021.

- Branch head: `9d0218ab8cfa439f390253ad90032a578b34ff67`
- `origin/main`: `b7ad33fcc005c256011e905687f0f31286915f93`
- Merge base: `b7ad33fcc005c256011e905687f0f31286915f93`
- Configuration: `Release`, restored dependency graph reused with `--no-restore`
- Working tree under test: T012-T014 test changes plus the task ledger; no T016-T021 production implementation changes

## T012 — Structured Logs replay conformance

```text
dotnet test tests/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~GroundworkStructuredLogReplayTests --logger 'console;verbosity=minimal'
```

Result: 6 discovered, 5 passed, 1 failed.

The only failure was
`GroundworkStructuredLogReplayTests.Operational_query_failure_is_not_reported_as_cursor_unavailable`.
It expected an exact `StructuredLogsException`, but the current adapter translated the provider
failure to `StructuredLogReplayCursorUnavailableException`. This is the intended T017 red: invalid,
foreign, and trimmed cursors must remain non-disclosing, while operational query failures must remain
distinguishable.

## T013 — OpenTelemetry restart conformance

```text
dotnet test tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~GroundworkOpenTelemetryRestartTests --logger 'console;verbosity=minimal'
```

Result: 1 discovered, 0 passed, 1 failed.

The only failure was
`GroundworkOpenTelemetryRestartTests.Durable_state_survives_store_restart_for_every_signal_kind`
with the explicit message:

```text
Expected T013 red: T020 has not implemented or wired GroundworkOpenTelemetryStore.
```

The test compiles against `IOpenTelemetryStore` and already exercises resources, trace summaries,
spans, instruments, metric points, and log records across a store restart. T020 must replace the
failing factory with the real Groundwork adapter without weakening the contract.

## T014 — Durable operation conformance

```text
dotnet test tests/Elsa/Diagnostics/Persistence/Tests/Elsa.Diagnostics.Persistence.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~DiagnosticsDurableOperationConformanceTests --logger 'console;verbosity=minimal'
```

Result: 5 discovered, 5 passed, 0 failed.

The specialized SQLite Groundwork diagnostic-record primitive already satisfies the prerequisite
append idempotency, operation-identity conflict, acknowledgement-loss, cancellation-boundary,
concurrent-writer, malformed-payload, and oversized-batch rejection contract. This green baseline is
intentional: T016-T021 may build adapters on that primitive, and T022 will generalize the evidence
across all required providers.
