# Groundwork E3 atomic cutover map

This is the dependency order for the clean break tracked by Groundwork #269. The endpoint is one
Groundwork v2 runtime in every shipping process: no v1 package, migration path, dual write, fallback,
assembly alias, or mixed-runtime bridge. Catalogs are fresh.

The effective `0.0.1-preview.131` direct-reference inventory at Elsa commit `1dd253511` is:

| Package | Total | `src` | `tests` | benchmark/tool |
|---|---:|---:|---:|---:|
| `Groundwork.Core` | 27 | 8 | 17 | 2 |
| `Groundwork.Documents` | 29 | 11 | 17 | 1 |
| `Groundwork.MongoDb` | 6 | 3 | 2 | 1 |
| `Groundwork.PostgreSql` | 5 | 2 | 2 | 1 |
| `Groundwork.SqlServer` | 5 | 2 | 2 | 1 |
| `Groundwork.Sqlite` | 9 | 1 | 6 | 2 |
| `Groundwork.DiagnosticRecords` | 0 | 0 | 0 | 0 |

Projects can appear in more than one row. `VersionOverride="0.2.0-preview.1"` references in the
already-migrated diagnostics proofs are v2 and are excluded from these counts.
The six remaining v1 package identities total 81 references across 42 projects: 20 production
projects, 20 test projects, and two benchmark/tool projects.

## Implementation order

1. **Public v2 composition foundation — complete.**
   `Elsa.Persistence.Groundwork.V2` owns target selection, declared-unit registration, admission,
   scoped session creation, and multi-unit UOW creation using only Kernel/Query.Model/Store.
   Diagnostics, Secrets, and Studio preferences already consume this boundary.
2. **Independent domain leaves.** Migrate distributed runtime, then Identity. Each leaf owns ordinary
   v2 `StorageUnit` declarations, row/document mapping, queries, and CAS behavior. Each project must
   remove every ProjectReference into the v1 Groundwork graph before its green commit.
3. **Shared workflow runtime.** Replace `Elsa.Persistence.Groundwork`, `Querying`, `Composition`,
   `Unified`, and `ReferenceComposition`. This wave owns checkpoint batching, bookmark/queue/outbox
   query routes, access mapping, and common runtime units. It must use exact v2 batch UOWs and the
   public query AST; the 28 v1 bounded-query declarations disappear.
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
session over every scope. Groundwork candidate `4d2ca32` adds explicit audited across-scope access for
scoped units and honest provider capability admission. Its package-only and native-provider
conformance is green, so the shared-runtime wave may consume that public surface without weakening
the unit to global storage or duplicating data.

The accepted generic contract carries a named audit purpose, injects/projects the stored scope for
queries, refuses scope-less point reads, preserves ordinary scoped isolation, and advertises or
refuses the capability honestly. Those invariants remain required in Elsa's shared-runtime tests; the
API prerequisite itself no longer blocks that wave.
