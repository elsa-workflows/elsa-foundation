# Validation Guide: OpenIddict Groundwork Stores

This guide is the completion evidence sequence for Spec 106. It does not authorize EF deletion until every gate below passes on the exact reviewed head.

## Prerequisites

- .NET 10 SDK and Docker-compatible provider runtime.
- Public, mutually compatible Groundwork Core/Documents/provider/Tool packages from the exact reviewed head; record package hashes and `dotnet groundwork --version` output.
- SQLite, SQL Server, PostgreSQL, and MongoDB fixtures. Mongo scenarios requiring a multi-record unit of work run only against a replica set or sharded topology.
- Provider connection values supplied through environment/configuration, never committed or shown in process-visible command arguments.

## 1. Verify the upstream admission gate

Restore and build the new adapter package, then run focused capability probes for codec admission, physical entity definitions, schema operations, typed compound/range routes, multivalue declarations, bounded mutation count/cancellation, native mutation-plan evidence, CAS, and UoW. Record the exact provider/tool versions and outcomes in the feature evidence.

Expected: every required public capability succeeds. A missing or non-public capability blocks the feature and is linked to an upstream Groundwork work item.

Historical preparation status on 2026-07-25: the exact configured package/tool
family then passed 6/6 focused probes. The probe applied the public
parameterless OpenIddict manifest through a real SQLite
`GroundworkProviderDriver`, saved and reloaded a global token document across
distinct clients, and executed the declared token-reference route with
provider-native plan evidence. The adapter test project then passed 14/14 for
its codec/manifest/failure/declaration scaffold, and the focused architecture
boundary passed 4/4.

The current probe additionally exercises naming/fingerprint transformation,
multivalue routes, expected-version CAS, UoW, CLI, and runtime readiness. T005
remains open because no manifest-admitted bounded mutation proves exact matched
count, cancellation, and a provider-native mutation plan. SQL Server,
PostgreSQL, MongoDB, topology, restart, the #141 relationship guards, and full
native-query/mutation evidence remain T006.

## 2. Run direct adapter and registration tests

```bash
dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-build
```

Expected: every existing OpenIddict objective still passes through rewired fixtures; all four replacement registrations resolve; mapper/route/generic-rejection/CAS/exception branches are covered directly.

## 3. Validate schema through the deployment path

```bash
dotnet tool restore
dotnet groundwork --version
dotnet groundwork plan --output json
dotnet groundwork validate --output json
dotnet groundwork status --output json
```

Run the exact host-provided manifest invocation selected during implementation for each provider. Run authorized apply only in an isolated test deployment.

Expected: plan/validate/status do not mutate; apply records deterministic history; runtime admission blocks schema drift, capability gaps, topology gaps, and naming collisions.

## 4. Run the four-provider conformance matrix

Run the shared black-box application/authorization/scope/token suite on each provider. Include independent clients, descriptor round trips, uniqueness, named bounded queries, generic delegate rejection, CAS conflicts, 100 refresh races, dependent cleanup, prune/revoke exact counts, cancellation, all declared failure windows, close/reopen, and process restart.

Expected: identical domain outcomes; zero duplicate refresh successors; no orphaned relationships; no client-side full-collection route; provider-native query and mutation plans identify the declared physical targets.

## 5. Run production-shaped host evidence

Compose the real selected identity features and each Groundwork provider. Seed authorized access; sign in; issue and validate access/refresh tokens; prove refresh replay rejection; revoke access and refresh tokens; restart; then repeat public bearer and protected endpoint calls.

Expected: the caller workflow remains unchanged, state and schema survive restart, and revoked/redeemed/expired/unknown tokens fail closed.

## 6. Submit and consume #646 performance evidence

For token issue/lookup/refresh/revoke/prune, authorization filter/revoke, application client lookup, and scope resource lookup, submit fixed input/result digests, dataset/payload/concurrency/warm-cold declarations, provider identity, storage form, and native-plan artifacts. Run the shared benchmark program and record a pass, redesign, or blocked verdict.

Expected: no workload advances without a reproducible verdict. A blocked/redesign result keeps EF removal blocked.

## 7. Final cleanup audit

After all prior gates pass, remove EF OpenIddict source, migrations, packages, test fixtures, shell settings, host references, and ratchet entries; refresh the authorized maps and update feature/extension documentation. Then inspect all source, project, package, and resolved dependency graphs for `Microsoft.EntityFrameworkCore`.

Expected: no OpenIddict EF artifact remains; retained core identity projects remain free of concrete-provider dependencies; the final architecture guard reports a full path for a deliberate reintroduction.

## Preparation checkpoint — 2026-07-25

This checkpoint was replayed manually from a current-main worktree without
rebasing or merging the stale draft. It freezes the OpenIddict 7.5 145-member denominator,
the 54-objective EF-oracle retention baseline plus one current-main shared-host
addendum (55 retained objectives), architecture-guard source, and partial
public-API probe source. It also separates provider-neutral server/validation/token
behavior from the frozen EF oracle and creates the Groundwork adapter boundary.

The 2026-07-25 root verification passed the real SQLite capability probe 6/6,
the current adapter scaffold 14/14, and the architecture boundary 4/4. Both the
frozen EF oracle and Groundwork boundary projects built in Release with no
errors. The directly affected Identity/OpenIddict selection passed 51/51 and
the complete Identity test project passed 140/140. Root also re-derived the
store-contract denominator from the restored OpenIddict 7.5.0 XML:
Application=42, Authorization=32, Scope=28, Token=43, total 145.

These results prove only the behavior-preservation and
manifest/codec/boundary slice described above. T005 and T006 remain open until
the corrected admitted-mutation probe passes on the exact configured
Groundwork family. There is no replacement registration, complete four-store
implementation, relationship-safe session/UoW/CAS/redeem flow, four-provider
store matrix, unified deployment selection, or performance evidence. The EF
oracle remains load-bearing for #646 and must not be deleted by this
checkpoint.

## Current-main replay checkpoint — 2026-07-29

The replay branch was cut from Elsa `6751087c613b150f4c435d11230dbde00eade37e`
and reconstructs only the provider-neutral foundation without merging or
rebasing the conflicting draft PR. It deliberately excludes the old
preview.88/preview.90 package-family and provider-evidence commits. Those
serialized inputs are stale and must be replaced only by the exact-head
provider-evidence import required by Spec 094.

The replay registers the Behavior, Groundwork adapter, and adapter test projects
in `Elsa.Server.slnx`. Application, authorization, Scope, and token store source
remain outside this foundation checkpoint. The initially replayed Scope and
relationship-free token candidates were reverted because T006 is still open
and the plan forbids later implementation before that admission gate.

Container-free verification on the replay head:

```bash
dotnet test tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests.csproj \
  -c Release --logger 'console;verbosity=minimal'
# 32 passed

dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  -c Release \
  --filter 'FullyQualifiedName~OpenIddictPersistenceArchitectureTests|FullyQualifiedName~ArchitectureGuardTests.Solution_folders_collapse_leaf_project_segments' \
  --logger 'console;verbosity=minimal'
# 5 passed
```

The post-review candidate substantiates T009, T018, and T019 on current
Elsa main.
It does not close T006, T011-T013, T016-T017, T020-T022, any complete token or
registry-store task, provider conformance, host acceptance, performance, or EF
removal.

## Ratified source checkpoint on current main — 2026-07-30

The program owner approved this replay as a narrow, source-only pre-T005/T006
checkpoint in [issue #643](https://github.com/elsa-workflows/elsa-foundation/issues/643#issuecomment-5135949258).
The approval does not authorize public OpenIddict stores, production
registration, provider-conformance claims, or EF removal. Those remain sequenced
behind the exact-family T005/T006 admission work.

To preserve the repository's merge-only history, current
`origin/main` (`1b9c617c1f6c517f1286ce4149eaeeeb28d5b466`) was merged into
the replay branch as `fdd649481a0f6bd3e033b44a0e39adbac7be57e5`; the branch
was not rebased. The merge resolved `Elsa.Server.slnx` automatically and did
not broaden the approved source boundary.

Root re-ran the focused gates on that current-main merge:

```bash
dotnet test tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests.csproj \
  -c Release --logger 'console;verbosity=minimal'
# 32 passed

dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  -c Release \
  --filter 'FullyQualifiedName~OpenIddictPersistenceArchitectureTests|FullyQualifiedName~ArchitectureGuardTests.Solution_folders_collapse_leaf_project_segments' \
  --logger 'console;verbosity=minimal'
# 5 passed

dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  -c Release \
  --filter 'FullyQualifiedName~OpenIddictGroundworkCapabilityProbeTests&FullyQualifiedName!~Four_provider_manifest_level_non_mutation_capabilities_execute_the_same_storage_contract' \
  --logger 'console;verbosity=minimal'
# 12 passed
```

Those counts bind merge head `fdd649481a0f6bd3e033b44a0e39adbac7be57e5`
before the later review remediation. They are retained as historical
current-main integration evidence, not mislabeled as the final candidate run.

`git diff --check origin/main...HEAD` also passed before this evidence-only
update. The candidate remains a draft until three new adversarial reviewers
inspect the final exact range and all confirmed findings are resolved.

## Exact-range review dispositions — 2026-07-29

Three independent read-only reviewers examined the exact initial replay range
`6751087c613b150f4c435d11230dbde00eade37e..901609c23` on correctness and
mechanism, evidence integrity, and scope/test preservation. All three returned
`BLOCK`; the candidate was not pushed as merge-ready.

The correctness review found that the capability probe bypassed manifest
admission for a raw physical mutation, that authorization and token projection
paths did not match the serialized canonical JSON, that package reporting was
hard-coded to an obsolete preview, and that a Scope lease could be disposed
outside the adapter exception boundary. Re-verification then found that the
token query iterator still had the same disposal gap and that Scope readiness
was no longer preserved after moving session acquisition inside its mapped
boundary. The candidate fixes passed their focused tests, but the complete
Scope/token candidates were then reverted to honor T006 sequencing. The
retained foundation remediation:

- removed the false mutation proof and reopened T005/T006;
- corrected the retained canonical records and expanded codec tests across all
  four model descriptors;
- now derives the reported Groundwork version from
  `Directory.Packages.props`; and
- removed the out-of-order Scope/token source rather than waiving its readiness
  and disposal findings.

The evidence review found that T005 was not reproducible, T010 did not exercise
all descriptor fields and corrupt/future envelopes, T035/T040 had advanced
without T006 or durable red-before-green evidence, the claimed 55-objective
inventory command returned only 52 matching objective lines, and the #143
research note was stale.
The remediation reopened T005, T010, T035, and T040; retained the expanded codec
matrix as candidate source without claiming its missing red-before-green
evidence; fixed the inventory command to return exactly 55 matching objective
lines across ten source paths, including the three named shared-host module
tests; and records #143 as delivered in the
configured preview.95 family while keeping exact-family recertification open.

The scope-preservation review additionally found that a cross-provider bounded
mutation router introduced an unconsumed, high-blast prerequisite unrelated to
this checkpoint. That commit was reverted in full. It also confirmed the
canonical JSON, package-version, T005/T006, and evidence inconsistencies above
and found a ten-endpoint inventory mislabeled as nine; all were corrected. Its
sequencing finding was dispositive: Scope and token implementation source was
also reverted because T006 remains open.

Root verification after those dispositions and the router revert:

```bash
dotnet test tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests.csproj \
  -c Release --no-build --no-restore --logger 'console;verbosity=minimal'
# 32 passed

dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  -c Release --no-restore \
  --filter 'FullyQualifiedName~OpenIddictPersistenceArchitectureTests|FullyQualifiedName~ArchitectureGuardTests.Solution_folders_collapse_leaf_project_segments' \
  --logger 'console;verbosity=minimal'
# 5 passed

dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~OpenIddictGroundworkCapabilityProbeTests&FullyQualifiedName!~Four_provider_manifest_level_non_mutation_capabilities_execute_the_same_storage_contract' \
  --logger 'console;verbosity=minimal'
# 12 passed
```

These are container-free checks only. They do not claim mutation admission,
four-provider conformance, T006, or merge approval. The remediated exact head
must return to the same three reviewers, and any remaining sequencing blocker
must stay open rather than being waived by this checkpoint.

## Ratified-checkpoint review cycle — 2026-07-30

The correctness reviewer blocked the ratified checkpoint because the manifest
still advertised two semantically invalid prune mutations before T005/T006 and
because a persisted record could omit its concurrency token and receive a new
random value on every read. Commit
`87e618acd63781cd5e85477acdbda407e252cee8` removed every prune declaration,
made the concurrency token required and nonblank on save and load, and added
missing/null/blank regressions. The same reviewer re-verified both dispositions
and returned `PASS`.

The evidence reviewer blocked stale final-head counts, present-tense capability
nonclaims contradicted by the current probe, and test names that overstated
storage declarations as store registration/public contract evidence. The
scope/test-preservation reviewer confirmed the stale count and also found an
unrelated PostgreSQL fixture change outside the approved checkpoint. Commit
`5d9e26d4e722dac6f10eb15ee9ef3fc2dbab309d`:

- time-scopes the historical 6/14/32 results and records the current probe's
  actual coverage and remaining T005/T006 gaps;
- renames the declaration and four-provider manifest-level tests so they cannot
  be cited as store-registration or public-store-contract proof;
- describes the 55-item ledger correctly as matching objective lines across ten
  source paths; and
- restores the PostgreSQL provider test fixture to current `main`.

Root then ran the exact code/test head:

```bash
dotnet test tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests.csproj \
  -c Release --no-build --no-restore --logger 'console;verbosity=minimal'
# 35 passed

dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~OpenIddictPersistenceArchitectureTests|FullyQualifiedName~ArchitectureGuardTests.Solution_folders_collapse_leaf_project_segments' \
  --logger 'console;verbosity=minimal'
# 5 passed

dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~OpenIddictGroundworkCapabilityProbeTests&FullyQualifiedName!~Four_provider_manifest_level_non_mutation_capabilities_execute_the_same_storage_contract' \
  --logger 'console;verbosity=minimal'
# 12 passed
```

The evidence-integrity and scope/test-preservation reviewers independently
re-verified the dispositions on record-only head
`9583cd3a5bd4a510f7e1b677b6c80a10209293e9` and both returned `PASS`. Together
with the originating correctness reviewer's `PASS` on the remediated code head,
all three axes are green. The nonclaims remain: no admitted mutation, public
OpenIddict store, replacement registration, four-provider store conformance,
performance verdict, or EF removal is present.

## Scope-store vertical slice — 2026-08-03

`IOpenIddictScopeStore` is implemented at
`src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/GroundworkOpenIddictScopeStore.cs`, all 28
members, none stubbed, with 54 passing tests in
`tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/GroundworkOpenIddictScopeStoreTests.cs`.

Deliberately narrow: the scope store is the only one of the four with no relationship cascade and no
atomic redeem/revoke, so it needs none of the idempotency-receipt machinery that T030/T041 must still
build. It was implemented first to prove the pattern against the merged foundations (PR #1093) before
the harder three. **No registration extension was written** — that needs all four stores — and the other
three stores are untouched. This slice does not advance any US1–US4 task to done.

### Three manifest/route gaps found while implementing it

These were surfaced rather than worked around: no route was invented and no filtering was moved
in-process. Each will also affect the application, authorization and token stores, so they are program
findings and not scope-store details.

**Update 2026-08-04 (corrected): gap 3 is closed. Gap 2 was NOT a gap and is withdrawn. Gap 1 remains
open pending a decision.**

Gap 2 was misdiagnosed. `FindScopeByResourceQuery`'s offset-with-no-sort declaration is not an oversight
that blocked pagination — it is **the only shape Groundwork admits for a collection-membership route**.
Declaring cursor paging compiles fine and passes every in-memory test, then fails at real plan
compilation with `GW-QUERY-008: Collection membership query '...' cannot use cursor paging or
latest-per-key selection until a provider certifies those combined element-to-owner shapes`. The change
was reverted; the bounded page and its fail-closed ceiling are correct-by-necessity, not a workaround.

Worth recording how it was caught, because the cheap checks all missed it: the store unit tests, the
full 355-test architecture suite and the maps gate were all green. Only
`OpenIddictGroundworkCapabilityProbeTests` — which builds real provider query plans across all four
providers — rejected it. Any future change to a bounded-query **declaration** needs that probe run, not
just the fast suites, because the fakes never execute the physical planner.

Lifting this needs upstream provider certification of element-to-owner cursor paging, which puts it in
the same territory as Groundwork #141/#143 rather than in this repo. `FindScopeByNameQuery` now admits `In` alongside `Equal`,
so `FindByNamesAsync` resolves a name set in one provider-executed membership query instead of N point
lookups. Manifest and store were changed together — a declaration with no consumer is inert risk.
Gap 1 needs a document-shape decision (see below the list).

1. **No bounded count-all or list-all route exists for scopes.** `CountAsync()` and
   `ListAsync(count, offset)` fall back to `IDocumentStore.QueryAsync(PortableDocumentQuery)`. That is
   genuinely provider-executed (SQL `COUNT` / `SELECT … LIMIT/OFFSET`), but the package marks it
   `[Obsolete(DiagnosticId = "GW0004")]` and there is no declared id index, so paging has **no
   guaranteed deterministic order**. Both facts matter for a store contract that promises stable paging.
2. **`FindScopeByResourceQuery` declares `Offset` paging with `QuerySortSupport.None`.** That combination
   fits neither `BoundedDocumentQueryPager` helper — `QueryAllAsync` needs cursor paging and
   `QueryAllOffsetAsync` throws without a non-empty declared order. `FindByResourceAsync` is therefore a
   single bounded page capped at 10,000 matches that throws above the cap, not true pagination.
3. **`FindScopeByNameQuery` admits only `Equal`.** `FindByNamesAsync` resolves a name set as N sequential
   point lookups rather than one set-membership query.

### Gap 1 — the open decision

Closing it needs the **fixed-value partition** pattern the runtime manifest already uses:
`ElsaRuntimeStorageManifest.CollectionField` (`"collection"`) is a constant-valued keyword-indexed field,
and `ListAllQuery` filters on it to give a properly bounded, ordered list-all route. Applying it here
means adding a constant `collection` field to `OpenIddictGroundworkScope`, keyword-indexing it, and
declaring a `list-all` route over that index with cursor paging and total-count support.

That is a **persisted-document shape change plus a new index**, which is why it was not done alongside
gaps 2 and 3. It is admissible under the pre-release no-back-compat agreement and must not bump
`SchemaVersion` (a frozen legacy stamp here, not a migration knob), but it is a design decision rather
than a mechanical fix, and the same field would want to be added to the application, authorization and
token records at the same time so all four stores get it once.

Until it is decided, `CountAsync()` and `ListAsync(count, offset)` reject with the capability exception
rather than degrade — neither is needed to issue a token; they back administrative listing.

The remaining gap sits in the
same territory as the open upstream Groundwork contracts #141 (fenced cross-unit relationship guards) and
#143 (fixed-value bounded assignment) that T007 already names as blockers. They should be resolved at the
manifest/upstream level before the remaining three stores are written, or the same workarounds will be
duplicated three more times — and the authorization store's compound `FindAsync(subject, client, status,
type, scopes)` has a strictly worse version of gap 2 already recorded against the dropped four-field
index.
