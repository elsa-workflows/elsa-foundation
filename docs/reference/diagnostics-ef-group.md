# Diagnostics EF feature group

`diagnostics-ef@1` is a Foundation-reviewed **selection group** in catalog version 2. It selects exactly four server diagnostic feature IDs:

| Base feature | EF persistence consumer |
|---|---|
| `DiagnosticsOpenTelemetry` | `DiagnosticsOpenTelemetryEntityFrameworkCore` |
| `DiagnosticsStructuredLogs` | `DiagnosticsStructuredLogsEntityFrameworkCore` |

Each EF consumer requires its base feature. The authored composition pins the group, catalog digest, and accepted exact feature set. Existing compositions pinned to catalog version 1 continue to use that bundled snapshot; a later catalog does not change their selection. You can still add or remove individual features. Removing either required base feature leaves it absent and gives an unresolved dependency finding.

To add the group to the Embedded starting profile and inspect the result:

```bash
dotnet elsa composition init --profile embedded-runtime@1 --group diagnostics-ef@1 --output composition.json
dotnet elsa composition plan --composition composition.json --format json
```

The plan lists every exact feature ID, whether it came from the profile or group, and the reviewed required edges. Without a supplied host inventory and persistence evidence, package availability and provider, connection, schema, migration, and physical-layout readiness remain unverified. `composition generate` resolves the composition's bundled catalog pin automatically; an explicit `--catalog` remains a deliberate override and must match that pin.

The group **does not configure a database**. To place both diagnostic stores on a second target, configure an explicit resource binding for each EF consumer. The [two-target acceptance work](https://github.com/elsa-workflows/elsa-foundation/issues/1969) proved that reviewed arrangement on PostgreSQL, with the other enrolled consumers retaining the primary default. The two diagnostic EF features have distinct legacy connection defaults, so group membership must never imply a shared connection or migration authorization. Host/package compatibility, live migration safety, and in-process engine tracing need their own evidence; this group selects neither a tracing bridge nor an executable host.
