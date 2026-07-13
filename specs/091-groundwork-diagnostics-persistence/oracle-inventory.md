# Temporary EF diagnostics behavior oracle

This inventory is a deletion aid, not a second contract. The provider-neutral spec and conformance tests remain authoritative; EF implementations stay temporarily so each behavior below has a concrete parity oracle until T053–T055.

| Behavior | EF source/oracle | Provider-neutral destination |
|---|---|---|
| Bounded nonblocking queue, drop-oldest overflow, single reader | `ChannelDrainingStoreBase<TItem>` plus both resilience suites | `DiagnosticsDrainLoadTests` |
| Stable retry batch, nine bounded attempts, exponential backoff | `ChannelDrainingStoreBase<TItem>` | `DiagnosticsDrainLifecycleTests` and failure fixtures |
| Retry exhaustion accounts loss and later batches recover | Both EF resilience suites | `DiagnosticsDrainLoadTests` |
| Explicit start, graceful completion, tolerant partial-start shutdown | EF startup tasks, shell terminators, and store tests | `DiagnosticsDrainLifecycleTests` / `DiagnosticsDrainShutdownTests` |
| Final retention after the queued tail | `CompleteDrainingCoreAsync` | `DiagnosticsDrainShutdownTests` |
| Bounded async-disposal fallback | `ChannelDrainingStoreBase<TItem>.DisposeAsync` | `DiagnosticsDrainShutdownTests` |
| Accepted Structured Log acknowledgement completes on commit or explicit failure | `EfCoreStructuredLogStore` pending-completion registry | Shared drain acknowledgement assertions |
| Structured Log committed result carries the authoritative replay cursor | `EfCoreStructuredLogStore.PersistBatchAsync` | Structured Logs Groundwork conformance (T012–T017) |
| OpenTelemetry dropped counts remain queryable | `EfCoreOpenTelemetryStore` counters | Shared observability plus OpenTelemetry adapter mapping (T037/T041) |
| Provider logging cannot recursively enter the captured diagnostics stream | EF feature `NullLoggerFactory` wiring | Pull-only shared counters and adapter-specific instrumentation exclusion |
| Subscriber backpressure is a live-feed loss, not persistence overflow | `InMemoryStructuredLogLiveFeed` and `InMemoryOpenTelemetryLiveFeed` | Existing domain drop signals mapped to `SubscriberDelivery` without moving fan-out |

No current EF test is deleted or weakened in this slice. The provider-specific query, replay, retention, composition, and migration oracles remain mapped by T012–T057 before deletion.

## Exact temporary evidence paths

These paths are the concrete deletion checklist for T010. A later task may add stronger Groundwork
evidence, but it must not remove an EF oracle below until its mapped conformance behavior is green.

| Area | Structured Logs EF evidence | OpenTelemetry EF evidence |
|---|---|---|
| Shared draining implementation | `src/Elsa/Persistence/EFCore/Storage/ChannelDrainingStoreBase.cs` | `src/Elsa/Persistence/EFCore/Storage/ChannelDrainingStoreBase.cs` |
| Drain resilience tests | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/EfCoreStructuredLogStoreResilienceTests.cs` | `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/EfCoreOpenTelemetryStoreResilienceTests.cs` |
| Drain startup task | `src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/Tasks/StartStructuredLogDrainingStartupTask.cs` | `src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/Tasks/StartOpenTelemetryDrainingStartupTask.cs` |
| Drain shell terminator | `src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/Tasks/StopStructuredLogDrainingShellTerminator.cs` | `src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/Tasks/StopOpenTelemetryDrainingShellTerminator.cs` |
| Subscriber-loss live feed | `src/Elsa/Diagnostics/StructuredLogs/Live/InMemoryStructuredLogLiveFeed.cs` | `src/Elsa/Diagnostics/OpenTelemetry/Providers/InMemory/InMemoryOpenTelemetryLiveFeed.cs` |
| Query and replay implementation | `src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/Storage/EfCoreStructuredLogStore.cs` | `src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/Storage/EfCoreOpenTelemetryStore.cs` |
| Query and replay tests | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/EfCoreStructuredLogStoreTests.cs` (`GetRecent*`, `ReplayPages*`, `ReadAfter*`) | `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/EfCoreOpenTelemetryStoreTests.cs` (`QueryTraces*`, `QueryMetricsAndLogs*`) |
| Retention and retry tests | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/EfCoreStructuredLogStoreTests.cs` (`TrimToZero*`, `DrainPrunes*`, `CompleteDrainingPrunes*`) and `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/EfCoreStructuredLogStorePruneRetryTests.cs` | `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/EfCoreOpenTelemetryStoreTests.cs` (`DrainPrunesHighVolumeSignalsToConfiguredCapacities`) |
| Composition implementation | `src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/EFCoreStructuredLogsPersistenceFeatureBase.cs` | `src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/EFCoreOpenTelemetryPersistenceFeatureBase.cs` |
| Composition tests | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/SqliteStructuredLogsPersistenceFeatureTests.cs` | `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/SqliteOpenTelemetryPersistenceFeatureTests.cs` |
| Migration/schema implementation | `src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/Sqlite/Migrations/20260618004216_Initial.cs` and `src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/Sqlite/Migrations/StructuredLogsDbContextModelSnapshot.cs` | `src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/Sqlite/Migrations/20260623005000_Initial.cs` and `src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/Sqlite/Migrations/OpenTelemetryDbContextModelSnapshot.cs` |

The exact feature tests above are the closest composition/migration-startup evidence: each SQLite
feature inherits the EF migration startup path and separately proves the store, lifecycle tasks, and
DbContext factory registrations. There is no dedicated migration-shape test today; the migration and
model-snapshot files therefore remain explicit source oracles until provider schema validation replaces
them.
