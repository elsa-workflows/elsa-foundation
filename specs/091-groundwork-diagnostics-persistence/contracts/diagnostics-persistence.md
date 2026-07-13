# Contract: Diagnostics Persistence Mapping

This contract maps Elsa-owned operations to Groundwork capabilities. It is a behavioral contract, not a public promise of Groundwork types in Elsa core assemblies.

## Structured Logs

| Elsa operation | Required behavior | Groundwork realization |
|---|---|---|
| Append | Durable, ordered, idempotent batch commit; authoritative cursor | Append diagnostic records under explicit binding and operation identity |
| Read recent | Exact filters, bounded newest selection, oldest-to-newest response | Indexed bounded record query with deterministic tie-breaks |
| Capture after cursor | Binding validation, stable commit order, exact-once page, bounded advancement | Provider position query plus opaque Elsa cursor codec |
| Trim | Keep exact newest count, including zero; high-water never rewinds | Idempotent bounded record retention operation |
| Inspect/readiness | Visible schema/capability/operational failures | Groundwork validation plus adapter binding checks |

Cursor-unavailable is reserved for malformed, trimmed, or foreign cursors. Cancellation, schema, provider, and serialization failures must not be translated to cursor-unavailable or empty results.

## OpenTelemetry

| Elsa operation | Required behavior | Groundwork realization |
|---|---|---|
| Write normalized batch | Nonblocking capture followed by durable, idempotent acknowledgement | Elsa drain plus one or more transactional/bounded Groundwork operations |
| Query resources | Scoped filters and deterministic bounded catalog result | Groundwork document query over resource catalog |
| Query traces | Inclusive ranges, exact filters, stable ordering and limits | Indexed diagnostic-record query over trace summaries |
| Get trace | Exact summary, spans, resources, and related logs | Bounded coordinated queries by scoped trace/resource identities |
| Query metrics | Instrument catalog plus filtered points and latest-per-key semantics | Document catalog query plus indexed metric-point record query |
| Query logs | Exact declared filters, ordering, and limit | Indexed telemetry-log record query |
| Get diagnostics | Exact counts/high-water/drop information | Scoped Groundwork inspection plus Elsa lifecycle counters |
| Catalog retention | Deterministic least-recently-seen capacity | Bounded document mutation ordered by last-seen/tie-breaker |

## Composition and deployment

- The host selects one Groundwork provider and supplies provider-neutral store/session access.
- Each concrete diagnostics Groundwork adapter owns and contributes its schema declaration; the shared Elsa drain/lifecycle project remains Groundwork-free.
- Schema validation and application can run before application startup through the shared Groundwork CLI/tooling.
- Readiness fails on missing schema, drift, capability mismatch, or unavailable storage.
- Registration replaces only `IStructuredLogStore` and `IOpenTelemetryStore`; live feeds, redaction, parsing, authorization, and domain policy stay Elsa-owned.
- Provider choice must not create provider-specific Elsa store classes or EF migration sets.

## Cross-provider conformance

SQLite, SQL Server, PostgreSQL, and MongoDB must execute the same fixture and assertions for ordering, filters, inclusive boundaries, latest selection, exact counts, retention, scope isolation, restart, concurrency, cancellation, acknowledgement loss, operation-identity conflict, readiness, and bounded execution evidence.

## Removal gate

Diagnostics EF projects may be deleted only when all shared conformance tests, provider readiness tests, execution-plan checks, lifecycle tests, and the ratified performance matrix pass. The final architecture test must prove:

1. no diagnostics project references EF Core or an EF diagnostics project;
2. no diagnostics EF context/entity/configuration/migration/registration remains; and
3. diagnostics core projects do not reference Groundwork.
