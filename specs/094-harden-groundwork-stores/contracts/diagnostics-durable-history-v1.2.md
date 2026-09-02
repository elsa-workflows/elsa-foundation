# Diagnostics durable-history v1.2 successor

`diagnostics-durable-history-v1.2.json` is an additive successor to the immutable historical
`workloads/diagnostics.json` contract. The historical v1.1 source and digest are retained unchanged;
catalog loading validates the successor's lineage and replaces the historical entry by the exact
workload id.

The public `IOpenTelemetryStore` contract exposes an instrument catalog only as part of metric query
results. It has no standalone instrument-last-seen operation. The v1.2 native-plan contract therefore
admits resource last-seen plus trace-listing through the ordinary `elsa_otel_trace_summaries_v3` unit
and its declared `elsa_otel_trace_summaries_start` index. Each route uses a finite page of 127 rows
against a larger candidate population. Resource status/service queries remain blocked because their
single-column predicate indexes cannot also satisfy the required last-seen order. Trace detail, metric
and log listing, and the two Structured Logs routes remain blocked because their current ordinary units
expose no matching provider-neutral secondary index. The harness does not synthesize evidence for, or
execute the unbounded/materializing query shape of, any blocked route; each remains an explicit blocked
identity in the native-plan evidence.

Groundwork capture dispatches provider-native retained plans for SQLite, SQL Server, PostgreSQL, and
MongoDB through the existing provider composition. Admission reparses each raw-plan envelope and binds
the route to its exact ordinary unit, route index, predicates, order, and finite page; summary
predicate/index flags are not authority. The temporary EF comparator is a separate
SQLite-only adapter over the public Structured Logs and OpenTelemetry stores. It refuses non-SQLite before
opening a provider and is a correctness oracle only while the diagnostics absolute-budget gate remains
blocked.
