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
