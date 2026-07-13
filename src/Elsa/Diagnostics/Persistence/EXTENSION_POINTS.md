# Extension points — Diagnostics persistence lifecycle

This is the owning catalog for the provider-neutral diagnostics persistence lifecycle helpers. The
library owns bounded draining, durable acknowledgement, retry, retention, shutdown, and loss
classification. It contains no Groundwork or EF Core types.

## Boundary decision

`Elsa.Diagnostics.Persistence` is a narrow **provider-neutral helper boundary**, not a third
diagnostics domain and not a substitute for the Structured Logs or OpenTelemetry `.Core` projects.
The two domain cores continue to own their store contracts and live-feed loss signals. This helper
may reference those core models only to bridge their existing loss signals into one lifecycle
classification; neither core references this helper.

The public surface is intentionally limited to the contracts and models required for concrete
adapter assemblies to compose the shared drain. Provider packages stay in the concrete adapter
projects. A separate `.Core` project would add another public package and dependency layer without
creating an independent domain contract, so this narrow helper-boundary exception is deliberate.

## Contract semantics

Both interfaces below are **Adapter / Bridge** seams with **Replacement** semantics. They are not
additive contributors: exactly one implementation is meaningful for one composed drain. A caller
that needs fan-out must place it behind one implementation instead of resolving an enumerable.

### `IDiagnosticsDrainTarget<TItem, TResult>` *(provider-neutral helper boundary)*

- **Kind:** Adapter / Bridge; single Replacement.
- **Role:** bridges the Elsa-owned drain policy to one concrete provider adapter's idempotent commit
  and retention operations.
- **Conflict behavior:** one target is required by the `DiagnosticsDrain` constructor. Host DI must
  select one concrete store/provider implementation; two explicit selections are a configuration
  conflict and must be rejected.
- **Dependency rule:** implementations live in concrete persistence projects. Groundwork types never
  cross this contract.

### `IDiagnosticsPersistenceObserver` *(provider-neutral helper boundary)*

- **Kind:** Adapter / Bridge; single Replacement.
- **Role:** receives low-cardinality pull counters for lifecycle, retry, failure, and classified loss.
- **Default:** omission selects the internal no-op observer; `DiagnosticsPersistenceCounters` is the
  first-party pull-only implementation.
- **Conflict behavior:** one observer may be supplied to a drain. Multiple sinks require one explicit
  aggregate bridge; contribution-style `IEnumerable<T>` resolution is not supported.
- **Data rule:** the contract accepts no diagnostic payload, identifier, tenant, or free-form label.

## Production bridges and registration

`DiagnosticsSubscriberDeliveryLossBridge` consumes the existing `DroppedEntriesSignal` and
`OpenTelemetryDroppedItemSummary` models and classifies both as `SubscriberDelivery`. It observes
domain fan-out loss; it does not move fan-out into persistence.

`AddDefaultDiagnosticsStore<TContract, TImplementation>` installs a fallback only when no store has
been selected. `ReplaceDiagnosticsStore<TContract, TImplementation>` makes one explicit selection,
removes a tracked default, and rejects a second explicit provider at registration with a diagnostic
naming the contract and both implementations. This is the required default-vs-explicit Replacement
contract behavior; silent last-write-wins is forbidden.

## Cross-references

- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Structured Logs owner catalog: [`../StructuredLogs/EXTENSION_POINTS.md`](../StructuredLogs/EXTENSION_POINTS.md).
- OpenTelemetry owner catalog: [`../OpenTelemetry/EXTENSION_POINTS.md`](../OpenTelemetry/EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2, §2.7, §2.22.1, and §2.23.2.
