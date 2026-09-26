# Contract: `diagnostics-ef@1`

The group is published in bundled Foundation catalog version `2`, under the existing [selection-planner v1 schema](../../174-profile-selection-planner/contracts/selection-planner-v1.md). Its exact members are:

```text
DiagnosticsOpenTelemetry
DiagnosticsOpenTelemetryEntityFrameworkCore
DiagnosticsStructuredLogs
DiagnosticsStructuredLogsEntityFrameworkCore
```

The reviewed required edges are `DiagnosticsOpenTelemetryEntityFrameworkCore -> DiagnosticsOpenTelemetry` and `DiagnosticsStructuredLogsEntityFrameworkCore -> DiagnosticsStructuredLogs`. Neither dependency is a database binding. The group is flat and does not contain provider, connection, schema, secret, migration, or engine-tracing values.

`composition init --profile embedded-runtime@1 --group diagnostics-ef@1 --output <new-file>` pins the current catalog, both definitions, and the planner's exact 20-ID union. `--group` is repeatable for future reviewed groups; duplicate references refuse. `composition plan` and `composition generate` without `--catalog` select the bundled catalog by the authored ID/version/digest. An unknown pin remains unresolved in a plan and refuses generation. If `--catalog` is supplied, only that file is assessed; no bundled fallback occurs. An old version-1 authored composition retains its exact 16-ID Embedded selection.

Selection and candidate generation do not establish target-host package, database, migration, or tracing readiness. The [developer reference](../../../docs/reference/diagnostics-ef-group.md) gives the boundary and separate configuration guidance.
