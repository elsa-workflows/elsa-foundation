# Extension points — Diagnostics persistence lifecycle

This is the owning catalog for the provider-neutral diagnostics persistence lifecycle helpers. The
library owns bounded draining, durable acknowledgement, retry, retention, shutdown, and loss
classification. It contains no Groundwork or EF Core types.

## Overridable contracts

`Elsa.Diagnostics.Persistence` is a narrow **provider-neutral helper boundary**, not a third
diagnostics domain and not a substitute for the Structured Logs or OpenTelemetry `.Core` projects.
The two domain cores continue to own their store contracts and live-feed loss signals. This helper
may reference those core models only to bridge their existing loss signals into one lifecycle
classification; neither core references this helper.

Both interfaces below are **Adapter / Bridge** seams with **Replacement** semantics. They are not
additive contributors: exactly one implementation is meaningful for one composed drain. A caller
that needs fan-out must place it behind one implementation instead of resolving an enumerable.

### `IDiagnosticsDrainTarget<TItem, TResult>` *(Feature contract — `Elsa.Diagnostics.Persistence`)*

- **Kind:** Adapter / Bridge; single Replacement.
- **Role:** bridges the Elsa-owned drain policy to one concrete provider adapter's idempotent commit
  and retention operations.
- **Conflict behavior:** one target is required by the `DiagnosticsDrain` constructor. Host DI must
  select one concrete store/provider implementation; two explicit selections are a configuration
  conflict and must be rejected.
- **Dependency rule:** implementations live in concrete persistence projects. Groundwork types never
  cross this contract.

### `IDiagnosticsPersistenceObserver` *(Feature contract — `Elsa.Diagnostics.Persistence`)*

- **Kind:** Adapter / Bridge; single Replacement.
- **Role:** receives low-cardinality pull counters for lifecycle, retry, failure, and classified loss.
- **Default:** omission selects the internal no-op observer; `DiagnosticsPersistenceCounters` is the
  first-party pull-only implementation.
- **Conflict behavior:** one observer may be supplied to a drain. Multiple sinks require one explicit
  aggregate bridge; contribution-style `IEnumerable<T>` resolution is not supported.
- **Data rule:** the contract accepts no diagnostic payload, identifier, tenant, or free-form label.

The adapter extension surface is intentionally limited to the contracts and models required for
concrete adapter assemblies to compose the shared drain. Provider packages stay in the concrete
adapter projects. A separate `.Core` project would add another public package and dependency layer
without creating an independent domain contract, so this narrow helper-boundary exception is
deliberate.

`DiagnosticsPersistenceObserverRegistrationValidator` is the constitution-mandated
first-party implementation required by §2.23.3; it is not an adapter extension contract. Its public
constructor accepts only `IServiceCollection`, and its public validation method owns observer
conflict detection and the actionable result. An internal Options adapter delegates .NET
`ValidateOnStart` into that implementation without creating another public implementation or
extension seam.

## Implementable contributor interfaces

None. Diagnostics drain targets and observers are single-implementation Replacement contracts, not
fan-in surfaces. Multiple observer sinks must be composed behind one aggregate implementation.

## Events

None. The helper publishes no domain events. Domain-owned live feeds retain their existing in-band
loss signals.

## Production bridges and registration

`DiagnosticsSubscriberDeliveryLossBridge` consumes the existing `DroppedEntriesSignal` and
`OpenTelemetryDroppedItemSummary` models and classifies both as `SubscriberDelivery`. It observes
domain fan-out loss; it does not move fan-out into persistence. Structured Logs signals are
cumulative, so the live feed uses a per-subscription delta recorder; OpenTelemetry summaries are
already incremental. OpenTelemetry loss is observed when each raw item is first dropped; evicted
and requeued summaries preserve the in-band total without counting the underlying items again.

`AddDefaultDiagnosticsStore<TContract, TImplementation>` installs a fallback only when no store has
been selected. `ReplaceDiagnosticsStore<TContract, TImplementation>` makes one explicit selection,
removes a tracked default, and rejects a second explicit provider at registration with a diagnostic
naming the contract and both implementations. This is the required default-vs-explicit Replacement
contract behavior; silent last-write-wins is forbidden.

`AddDiagnosticsPersistenceObservability` installs its default observer through the same tracked
Replacement path. Explicit observer selection is order-independent; multiple direct or tracked
explicit observers are rejected with a configuration diagnostic.

Store registrations are scoped by default in accordance with §2.5.1. A caller may explicitly select
another `ServiceLifetime` when the implementation's dependency graph and state ownership justify it.
The live-feed observer, counters, and bridge are explicitly singleton because their totals span
subscriptions and they hold neither payloads nor scoped dependencies.

## Cross-references

- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Structured Logs owner catalog: [`../StructuredLogs/EXTENSION_POINTS.md`](../StructuredLogs/EXTENSION_POINTS.md).
- OpenTelemetry owner catalog: [`../OpenTelemetry/EXTENSION_POINTS.md`](../OpenTelemetry/EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2, §2.7, §2.22.1, and §2.23.2.
