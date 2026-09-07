# Diagnostics durable-history v1.2 successor

`diagnostics-durable-history-v1.2.json` is an additive successor to the immutable historical
`workloads/diagnostics.json` contract. The historical v1.1 source and digest are retained unchanged;
catalog loading validates the successor's lineage and replaces the historical entry by the exact
workload id.

The public `IOpenTelemetryStore` contract exposes an instrument catalog only as part of metric query
results. It has no standalone instrument-last-seen operation. The v1.2 native-plan contract therefore
admits resource last-seen plus trace-listing through the ordinary `elsa_otel_trace_summaries_v3` unit
and its declared `elsa_otel_trace_summaries_start` index. Each route uses a finite page of 127 rows
against a larger candidate population. Resource status/service queries use their declared compound
indexes. Trace detail is admitted as a composite rather than as a synthetic single route: its summary
and resource reads are bounded primary-key operations (with no secondary-index claim), while spans and
logs use their exact trace-key/order indexes. The public store capacities (`SpanCapacity`,
`LogRecordCapacity`, and `ResourceCapacity`) are the authoritative finite row bounds enforced by
retention; summary resource-key unions add the existing 5,000-element bound. The harness retains each
constituent independently and records the bounded point-read fanout (at most 128 resource keys) and
the ordered signal page bound (at most ceil(100,000 / 127) = 788 pages per signal), so it never
collapses multiple commands into one plan or executes an unbounded/materializing query shape. EF observations remain
correctness-only and are not promoted to provider-native evidence.

Groundwork capture dispatches provider-native retained plans for SQLite, SQL Server, PostgreSQL, and
MongoDB through the existing provider composition. Admission reparses each raw-plan envelope and binds
each indexed constituent to its exact ordinary unit, provider index, predicates, order, and finite page;
primary-key constituents are validated as key reads without inventing an index. Summary predicate/index
flags are not authority. The temporary EF comparator is a separate
SQLite-only adapter over the public Structured Logs and OpenTelemetry stores. It refuses non-SQLite before
opening a provider and is a correctness oracle only while the diagnostics absolute-budget gate remains
blocked.
