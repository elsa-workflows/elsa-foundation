# Groundwork E3 atomic cutover map

This is the dependency order for the clean break tracked by Groundwork #269. The endpoint is one
Groundwork v2 runtime in every shipping process: no v1 package, migration path, dual write, fallback,
assembly alias, or mixed-runtime bridge. Catalogs are fresh.

The effective `0.0.1-preview.131` direct-reference inventory at Elsa commit `de6b0f859` is:

| Package | Total | `src` | `tests` | benchmark/tool |
|---|---:|---:|---:|---:|
| `Groundwork.Core` | 23 | 5 | 17 | 1 |
| `Groundwork.Documents` | 26 | 8 | 17 | 1 |
| `Groundwork.MongoDb` | 9 | 3 | 6 | 0 |
| `Groundwork.PostgreSql` | 8 | 2 | 6 | 0 |
| `Groundwork.SqlServer` | 8 | 2 | 6 | 0 |
| `Groundwork.Sqlite` | 8 | 3 | 4 | 1 |
| `Groundwork.DiagnosticRecords` | 0 | 0 | 0 | 0 |

Projects can appear in more than one row. `VersionOverride="0.2.0-preview.1"` references in the
already-migrated diagnostics proofs are v2 and are excluded from these counts.
The six remaining v1 package identities total 68 references across 36 projects: 17 production
projects, 18 test projects, and one benchmark project. References with an explicit
`0.2.0-preview.1` or `$(GroundworkVersion)` version are v2 and are excluded even though the provider
package IDs intentionally remain unchanged.

## Implementation order

1. **Public v2 composition foundation — complete.**
   `Elsa.Persistence.Groundwork.V2` owns target selection, declared-unit registration, admission,
   scoped session creation, and multi-unit UOW creation using only Kernel/Query.Model/Store.
   Diagnostics, Secrets, and Studio preferences already consume this boundary.
2. **Independent domain leaves — complete.** Distributed runtime and Identity own ordinary v2
   `StorageUnit` declarations, row/document mapping, queries, and CAS behavior. Their production and
   test closures contain no v1 Groundwork package or ProjectReference.
3. **Shared workflow runtime — in progress.** Replace `Elsa.Persistence.Groundwork`, `Querying`,
   `Composition`, `Unified`, and `ReferenceComposition`. This wave owns checkpoint batching, bookmark/queue/outbox
   query routes, access mapping, and common runtime units. It must use exact v2 batch UOWs and the
   public query AST; the 28 v1 bounded-query declarations disappear. The public v2 manifest and the
   checkpoint, bookmark, durable-value, durable-timer, trigger-binding, run-health, and liveness
   verticals are complete; the remaining runtime stores and shipping composition still use v1.
4. **Design and publishing dependants.** Migrate Activities Design, Workflows Design, Elsa3 reusable
   activity import, and Workflows Publishing in that order. Preserve their atomic commands and
   projection/query behavior against the shared v2 runtime from step 3.
5. **Dashboard and provider leaves.** Replace the four old provider projects with v2 provider
   factories and register their real `IStorageProviderConnection` instances through the target-aware
   composition seam. Mongo capability fit remains honest. Keep provider-specific dashboard SQL only
   where it is not a Groundwork storage adapter.
6. **Shipping composition.** Switch `Elsa.Workbench` only after steps 2–5 are complete. Prove shell
   activation resolves the selected real provider connection and exercises at least one declared
   store. A missing connection is a startup failure, never an in-memory fallback.
7. **Repository closure.** Port or remove v1-only tests, process probes, benchmarks, and evidence
   tools; pin all same-ID provider packages to `0.2.0-preview.1`; require zero effective v1 references;
   refresh maps; then run the all-provider suites and the corrected per-workload medium benchmark
   proof with real routed plan evidence.

The same-ID provider packages make the order strict: a project cannot load v1 and v2 provider
assemblies together. Intermediate green slices therefore have to be dependency-closed on one side of
the boundary; they cannot be made green with binding aliases or compatibility shims.

## Public API prerequisite resolved during cutover

The shared-runtime wave required an additive Groundwork v2 capability before it could preserve Elsa's
tenant boundary. Elsa authorizes recovery and management reads with
`PersistenceAccessContext.PrivilegedAcrossScopes`; its named-query path deliberately opens one audited
session over every scope. Groundwork `0.2.0-preview.1`, published only to the Valence Works Feedz
feed, includes explicit audited across-scope access for scoped units and honest provider capability
admission. Its package-only and native-provider conformance is green, so the shared-runtime wave may
consume that public surface without weakening the unit to global storage or duplicating data.

The accepted generic contract carries a named audit purpose, injects/projects the stored scope for
queries, refuses scope-less point reads, preserves ordinary scoped isolation, and advertises or
refuses the capability honestly. Those invariants remain required in Elsa's shared-runtime tests; the
API prerequisite itself no longer blocks that wave.

## Accepted-scan inventory

The current source tree contains one reviewed `ScanAcceptance` and one matching assembly opt-in:

| Acceptance | Route | Bound | Owner / expiry | Review decision |
|---|---|---|---|---|
| `GW-SCAN-ELSA-SECRETS-SUBSTRING` | Secrets list search over normalized name or display name with portable `Contains` semantics | The public request caps each page at 250 rows; the acceptance is attached only when `Search` is present | `elsa-secrets` / 2027-08-16 UTC | Accepted. Portable case-insensitive substring matching has no index shape shared by SQLite, PostgreSQL, SQL Server, and MongoDB. Exact type/store/scope/status predicates remain provider-side and unsearched list requests carry no acceptance. |

Source: [`GroundworkSecretRepository`](../../src/Elsa/Secrets/Persistence/Groundwork/Stores/GroundworkSecretRepository.cs)
and its assembly-level [`GwAllowAcceptedScans`](../../src/Elsa/Secrets/Persistence/Groundwork/AcceptedScans.cs).
Every additional marker must add a separately reviewed row here; the final #269 closure scan must
match this inventory exactly.
