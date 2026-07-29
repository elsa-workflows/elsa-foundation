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

Partial execution status: the exact configured package/tool family passed 6/6
focused probes. The probe applies the public parameterless OpenIddict manifest
through a real SQLite `GroundworkProviderDriver`, saves and reloads a global
token document across distinct clients, and executes the declared token-reference
route with provider-native plan evidence. The adapter test project passed 14/14
for its current codec/manifest/failure/registration scaffold, and the focused
architecture boundary passed 4/4.

T005 remains open. The current probe does not yet execute naming/fingerprint
transformation, multivalue routes, bounded mutation/count/cancellation/native
plans, CAS, UoW, or CLI readiness. SQL Server, PostgreSQL, MongoDB, topology,
restart, and full native-query/mutation evidence remain T006.

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
inventory command returned only 52 paths, and the #143 research note was stale.
The remediation reopened T005, T010, T035, and T040; retained the expanded codec
matrix as candidate source without claiming its missing red-before-green
evidence; fixed the inventory command to return exactly 55 paths including the
three named shared-host module tests; and records #143 as delivered in the
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
  --filter 'FullyQualifiedName~OpenIddictGroundworkCapabilityProbeTests&FullyQualifiedName!~Four_provider_non_mutation_capabilities_execute_the_same_openiddict_contract' \
  --logger 'console;verbosity=minimal'
# 12 passed
```

These are container-free checks only. They do not claim mutation admission,
four-provider conformance, T006, or merge approval. The remediated exact head
must return to the same three reviewers, and any remaining sequencing blocker
must stay open rather than being waived by this checkpoint.
