# Diagnostics durable-history v1.3 successor

`diagnostics-durable-history-v1.3.json` supersedes the retained v1.2 workload without rewriting it.
The v1.2 runner compared `IStructuredLogStore.GetHighWaterMarkAsync()` with the number of rows appended
inside the primary scope. That comparison was invalid: the public contract defines high-water as the
maximum committed `Sequence`, and a provider may allocate those sequences across storage scopes. The
workload deliberately seeds 200 secondary-scope records before its 101,000 primary-scope records, so
Groundwork correctly reports primary high-water 101,200 while the retained EF comparator, whose scopes
use separate SQLite databases, correctly reports 101,000.

The v1.3 runner retains the same public operations and scale, but records the provider-neutral invariant
`structuredLogHighWaterMatchedMaximumCommittedSequence`. Each append writer retains its maximum
acknowledged primary sequence; the store's high-water must equal the maximum across writers and must
remain unchanged after trim and reopen. Absolute allocator coordinates are intentionally absent from the
cross-adapter observable digest. Scope isolation is still proven independently by the primary queries.

The new seed, input fingerprint, result digest, raw source digest, and explicit v1.2 lineage prevent v1.2
artifacts from being relabelled as v1.3 evidence. Correctness and native-plan capture must be rerun from a
clean exact v1.3 head before any performance verdict can consume the successor.

## Native string-order routes

The diagnostics Groundwork adapter keeps the existing EF persistence bound of 128 UTF-16 code units
for metric-point IDs, log-record IDs, span-record IDs, and the `SpanId` identity on span records.
This is not a blanket span-reference bound: the optional log-record `SpanId` and trace/summary
`RootSpanId` fields retain their existing 256-code-unit v2 declarations. The unintegrated v2 declarations
keep the required 64-code-unit v3 trace-summary `TraceKey` and its raw storage/key/API unchanged, while
adding a provider-owned ordinal identity for its selected start-time index. They use Groundwork's
persisted ordinal identities on exactly five selected indexes: metric timestamp, log timestamp,
trace-summary start, and span/log trace detail. This keeps each route's public logical order:
trace summaries use start time then trace key; signal routes retain their declared timestamp/start-time,
identity and sequence terms. The persisted keys give SQL
Server an exact physical pathkey that fits its 1,700-byte index-key limit. Raw strings retain
Groundwork's length-aware ordering everywhere else.

The ordinal identity is provider-owned: schema apply derives and backfills it from the logical string,
and callers cannot write it. Because this v2 diagnostics schema has not been integrated into `main`,
the 128-code-unit correction is made before its first supported deployment rather than represented as
a migration from the superseded 512/256-code-unit draft declarations. A future change to these bounds
requires explicit compatibility and migration evidence.

Native command admission binds these five route/table/index triples to their exact persisted column
names; the public route metadata remains logical. A physical-name prefix alone is not authority.
PostgreSQL still has to prove its C-collated index path, and MongoDB must use the stored key directly,
without an intervening stage that overwrites it. Renderer/envelope tests use synthetic plans only to
isolate command compatibility; they do not replace native explain or controlled timing evidence.
Caller query options come from the logical declaration used to open the session, not its provider-
expanded `Unit`: physical covering columns may otherwise be retargeted twice. The native provider
retains responsibility for completing the query's physical mappings.

## Public page and native lookahead

The frozen public page remains 127 rows. Groundwork's page executor fetches exactly one additional
row to establish whether a continuation exists, so the retained native MongoDB command limit is 128,
as is a winning-plan limit when present. Admission checks that exact derived limit, not an arbitrary
larger page. The evidence's `FiniteLimit` and returned/materialized public candidate counts remain
127; they do not claim that the provider fetched only 127 rows. Physical table cardinality and
scan/sort restrictions are unchanged, including the separately bounded 128-row resource catalog.
The extra row and its cost are part of the actual provider command and measured public operation.
For the frozen `resources-by-status` route only, the captured MongoDB shape may use the existing
status-only `IXSCAN` named `elsa_otel_resources_status` with the exact `{status: 1}` key pattern.
The aggregate pipeline must still prove 128 physical rows, the complete deterministic
`lastSeen DESC, idOrderKey ASC, id ASC` ordering, native limit 128, public/materialized bound 127,
and zero sort spill. The logical Groundwork envelope remains bound to its declared compound status
index; this captured status-only allowance does not apply to the other resource routes or any
non-resource route. MongoDB `IXSCAN` plans for those routes continue to require their declared
provider-owned index shape.
